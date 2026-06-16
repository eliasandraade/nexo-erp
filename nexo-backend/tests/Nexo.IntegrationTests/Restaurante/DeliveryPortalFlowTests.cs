using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Features.Auth;
using Nexo.Application.Features.Cash;
using Nexo.Application.Features.Products;
using Nexo.Application.Modules.Restaurante;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Restaurante;

/// <summary>
/// E2E integration tests for the public restaurant portal:
///   1. Public menu endpoint (no auth)
///   2. Portal order creation → RestDeliveryOrder (not RestOrder)
///   3. Delivery Hub visibility
///   4. Accept → RestOrder created + kitchen visibility
///   5. Status advancement + tracking
///   Security: AcceptingOrders, DeliveryEnabled, TakeawayEnabled, invisible products,
///             tracking payload isolation
/// </summary>
[Collection("Integration")]
public class DeliveryPortalFlowTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;        // authenticated operator
    private readonly HttpClient _publicClient;  // no auth — simulates browser

    private static int _seq = 0;

    public DeliveryPortalFlowTests(TestWebApplicationFactory factory)
    {
        _factory      = factory;
        _client       = factory.CreateApiClient();
        _publicClient = factory.CreateApiClient(); // no auth header
    }

    public async Task InitializeAsync()
    {
        await AuthenticateAsync(_client);
        await EnsureModuleAsync("restaurante");
        await OpenCashSessionAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task AuthenticateAsync(HttpClient client)
    {
        var r = await client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await r.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    private async Task EnsureModuleAsync(string key)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var tenant = await db.Tenants.FirstOrDefaultAsync();
        if (tenant is null) return;
        if (await db.ModuleSubscriptions.AnyAsync(s => s.TenantId == tenant.Id && s.ModuleKey == key)) return;
        db.ModuleSubscriptions.Add(ModuleSubscription.CreateFromStripe(
            tenant.Id, key, $"sub_test_{key}", $"price_test_{key}",
            PlanType.Lifetime, DateTime.UtcNow, null));
        await db.SaveChangesAsync();
    }

    private async Task OpenCashSessionAsync()
    {
        var r = await _client.PostAsJsonAsync("/api/cash/sessions/open",
            new OpenCashSessionRequest(OpeningBalance: 0, Notes: "Portal E2E session"));
        if (r.StatusCode == HttpStatusCode.Conflict) return;
        r.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task<string> SetupPublicSlugAsync(string slugSuffix)
    {
        var stores = await _client.GetFromJsonAsync<List<StoreDto>>("/api/stores");
        stores.Should().NotBeEmpty();
        var storeId = stores![0].Id;
        var slug    = $"test-portal-{slugSuffix}";

        var r = await _client.PatchAsJsonAsync($"/api/stores/{storeId}/public-slug",
            new { publicSlug = slug });
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        return slug;
    }

    private async Task ClearPublicSlugAsync()
    {
        var stores = await _client.GetFromJsonAsync<List<StoreDto>>("/api/stores");
        if (stores is null || stores.Count == 0) return;
        await _client.PatchAsJsonAsync($"/api/stores/{stores[0].Id}/public-slug",
            new { publicSlug = (string?)null });
    }

    private async Task<ProductDto> CreateMenuProductAsync(string codeSuffix, bool menuVisible = true)
    {
        var seq  = Interlocked.Increment(ref _seq);
        var code = $"PORTAL-{codeSuffix}-{seq}";
        var r = await _client.PostAsJsonAsync("/api/products",
            new CreateProductRequest(code, $"Produto Portal {seq}", "Un", SalePrice: 29.90m, CostPrice: 10m));
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = (await r.Content.ReadFromJsonAsync<ProductDto>())!;

        // Set IsMenuVisible via direct DB (no dedicated endpoint exists yet)
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var p  = await db.Products.IgnoreQueryFilters().FirstAsync(x => x.Id == product.Id);
        p.SetMenuVisibility(menuVisible);
        await db.SaveChangesAsync();

        return product;
    }

    private async Task<ModifierGroupDto> CreateRequiredModifierGroupAsync(
        Guid productId, string optionName = "Bem passado")
    {
        var seq = Interlocked.Increment(ref _seq);
        var gr = await _client.PostAsJsonAsync("/api/restaurante/modifier-groups",
            new CreateModifierGroupRequest(productId, $"Ponto {seq}", true, 1, 1, 0));
        gr.StatusCode.Should().Be(HttpStatusCode.OK);
        var group = (await gr.Content.ReadFromJsonAsync<ModifierGroupDto>())!;

        var mr = await _client.PostAsJsonAsync(
            $"/api/restaurante/modifier-groups/{group.Id}/modifiers",
            new CreateModifierRequest(group.Id, optionName, 0m, 0));
        mr.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await mr.Content.ReadFromJsonAsync<ModifierGroupDto>())!;
        return updated;
    }

    private async Task<FoodServiceSettingsDto> SetPortalFlagsAsync(
        bool acceptingOrders = true, bool deliveryEnabled = true, bool takeawayEnabled = true)
    {
        var r = await _client.PutAsJsonAsync("/api/restaurante/settings/portal",
            new UpdatePortalInfoRequest(
                DisplayName:      "Restaurante Teste",
                LogoUrl:          null,
                CoverImageUrl:    null,
                Description:      "Portal E2E test",
                WhatsAppPhone:    null,
                BusinessHoursJson: null,
                AcceptingOrders:  acceptingOrders,
                DeliveryEnabled:  deliveryEnabled,
                TakeawayEnabled:  takeawayEnabled));
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await r.Content.ReadFromJsonAsync<FoodServiceSettingsDto>())!;
    }

    /// <summary>
    /// Creates a delivery zone for the operator's current store and returns its id.
    /// Delivery (not Takeaway) portal orders REQUIRE a DeliveryZoneId so the server can
    /// resolve the delivery fee from the zone (never trusted from the client).
    /// </summary>
    private async Task<Guid> CreateDeliveryZoneAsync(string neighborhood = "Centro", decimal fee = 7.50m)
    {
        var r = await _client.PutAsJsonAsync("/api/restaurante/delivery-zones",
            new { zones = new[] { new { neighborhood, fee } } });
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var zones = (await r.Content.ReadFromJsonAsync<List<DeliveryZoneDto>>())!;
        return zones.First(z => z.Neighborhood == neighborhood).Id;
    }

    private record StoreDto(string Id, string Name, string Slug, string? PublicSlug, string? ModuleKey, string Status);

    // ── 1. GET /api/public/menu/{slug} — public, no auth ─────────────────────

    [Fact]
    public async Task PublicMenu_Returns200_WithVisibleProducts()
    {
        var slug    = await SetupPublicSlugAsync("menu-basic");
        var product = await CreateMenuProductAsync("VISIBLE");
        await SetPortalFlagsAsync();

        var r = await _publicClient.GetAsync($"/api/public/menu/{slug}");
        r.StatusCode.Should().Be(HttpStatusCode.OK);

        var menu = (await r.Content.ReadFromJsonAsync<PublicMenuDto>())!;
        menu.StoreName.Should().NotBeNullOrEmpty();
        menu.AcceptingOrders.Should().BeTrue();

        var allProducts = menu.Categories.SelectMany(c => c.Products).ToList();
        allProducts.Should().Contain(p => p.Id == product.Id,
            "visible product must appear in the public menu");
    }

    // ── 2. Produto invisível não aparece ──────────────────────────────────────

    [Fact]
    public async Task PublicMenu_InvisibleProduct_DoesNotAppear()
    {
        var slug    = await SetupPublicSlugAsync("menu-invisible");
        var visible = await CreateMenuProductAsync("VIS");
        var hidden  = await CreateMenuProductAsync("HID", menuVisible: false);

        var r = await _publicClient.GetAsync($"/api/public/menu/{slug}");
        r.StatusCode.Should().Be(HttpStatusCode.OK);

        var menu = (await r.Content.ReadFromJsonAsync<PublicMenuDto>())!;
        var allIds = menu.Categories.SelectMany(c => c.Products).Select(p => p.Id).ToList();

        allIds.Should().Contain(visible.Id,   "visible product must appear");
        allIds.Should().NotContain(hidden.Id, "invisible product must be hidden");
    }

    // ── 3. Slug inválido retorna 404 ──────────────────────────────────────────

    [Fact]
    public async Task PublicMenu_InvalidSlug_Returns404()
    {
        var r = await _publicClient.GetAsync("/api/public/menu/slug-que-nao-existe-xyz987");
        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── 4. Criar pedido como Takeaway → cria RestDeliveryOrder ───────────────

    [Fact]
    public async Task CreatePortalOrder_Takeaway_CreatesDeliveryOrder_NotRestOrder()
    {
        var slug    = await SetupPublicSlugAsync("takeaway-test");
        var product = await CreateMenuProductAsync("TKW");
        await SetPortalFlagsAsync(acceptingOrders: true, deliveryEnabled: true, takeawayEnabled: true);

        var r = await _publicClient.PostAsJsonAsync("/api/public/orders", new
        {
            publicSlug    = slug,
            orderType     = "Takeaway",
            customerName  = "João Silva",
            customerPhone = "11999999999",
            items = new[] { new { productId = product.Id, quantity = 1 } },
        });

        r.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
        var order = (await r.Content.ReadFromJsonAsync<DeliveryOrderDto>())!;

        // Must be a DeliveryOrder (has TrackingToken)
        order.TrackingToken.Should().NotBeNullOrEmpty();
        order.OrderType.Should().Be("Takeaway");
        order.Status.Should().Be("Received");
        order.Channel.Should().Be("Portal");

        // RestOrder must NOT exist yet — only created on Accept
        order.RestOrderId.Should().BeNull("RestOrder is only created when operator accepts the order");
    }

    // ── 5. Criar pedido como Delivery ─────────────────────────────────────────

    [Fact]
    public async Task CreatePortalOrder_Delivery_CreatesDeliveryOrder()
    {
        var slug    = await SetupPublicSlugAsync("delivery-test");
        var product = await CreateMenuProductAsync("DLV");
        var zoneId  = await CreateDeliveryZoneAsync();   // Delivery requires a zone for the fee
        await SetPortalFlagsAsync();

        var r = await _publicClient.PostAsJsonAsync("/api/public/orders", new
        {
            publicSlug          = slug,
            orderType           = "Delivery",
            customerName        = "Maria Santos",
            customerPhone       = "11988888888",
            deliveryAddressJson = "{\"street\":\"Rua das Flores\",\"number\":\"123\"}",
            deliveryZoneId      = zoneId,
            items               = new[] { new { productId = product.Id, quantity = 2 } },
        });

        r.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
        var order = (await r.Content.ReadFromJsonAsync<DeliveryOrderDto>())!;

        order.OrderType.Should().Be("Delivery");
        order.Status.Should().Be("Received");
        order.DeliveryAddressJson.Should().NotBeNullOrEmpty();
        order.Items.Should().HaveCount(1);
        order.Items[0].Quantity.Should().Be(2);
    }

    // ── 6. Produto + modificador obrigatório ──────────────────────────────────

    [Fact]
    public async Task CreatePortalOrder_WithRequiredModifier_Succeeds()
    {
        var slug    = await SetupPublicSlugAsync("modifier-ok");
        var product = await CreateMenuProductAsync("MOD");
        var group   = await CreateRequiredModifierGroupAsync(product.Id, "Mal passado");
        var modifier = group.Modifiers.First();
        await SetPortalFlagsAsync();

        var r = await _publicClient.PostAsJsonAsync("/api/public/orders", new
        {
            publicSlug    = slug,
            orderType     = "Takeaway",
            customerName  = "Ana Lima",
            customerPhone = "11977777777",
            items = new[]
            {
                new
                {
                    productId = product.Id,
                    quantity  = 1,
                    modifiers = new[] { new { modifierId = modifier.Id } },
                }
            },
        });

        r.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
        var order = (await r.Content.ReadFromJsonAsync<DeliveryOrderDto>())!;
        order.Items[0].Modifiers.Should().HaveCount(1);
    }

    // ── 7. Pedido aparece no Delivery Hub ─────────────────────────────────────

    [Fact]
    public async Task PortalOrder_AppearsInDeliveryHub()
    {
        var slug    = await SetupPublicSlugAsync("hub-visible");
        var product = await CreateMenuProductAsync("HUB");
        await SetPortalFlagsAsync();

        var create = await _publicClient.PostAsJsonAsync("/api/public/orders", new
        {
            publicSlug    = slug,
            orderType     = "Takeaway",
            customerName  = "Carlos",
            customerPhone = "11966666666",
            items         = new[] { new { productId = product.Id, quantity = 1 } },
        });
        create.IsSuccessStatusCode.Should().BeTrue();
        var created = (await create.Content.ReadFromJsonAsync<DeliveryOrderDto>())!;

        var hub = await _client.GetFromJsonAsync<List<DeliveryOrderDto>>("/api/restaurante/delivery-orders");
        hub.Should().Contain(o => o.Id == created.Id,
            "order created via portal must appear in the operator's Delivery Hub");
    }

    // ── 8. Aceitar → cria RestOrder ───────────────────────────────────────────

    [Fact]
    public async Task AcceptDeliveryOrder_CreatesRestOrder()
    {
        var slug    = await SetupPublicSlugAsync("accept-test");
        var product = await CreateMenuProductAsync("ACC");
        await SetPortalFlagsAsync();

        var create = await _publicClient.PostAsJsonAsync("/api/public/orders", new
        {
            publicSlug    = slug,
            orderType     = "Takeaway",
            customerName  = "Pedro",
            customerPhone = "11955555555",
            items         = new[] { new { productId = product.Id, quantity = 1 } },
        });
        var deliveryOrder = (await create.Content.ReadFromJsonAsync<DeliveryOrderDto>())!;
        deliveryOrder.RestOrderId.Should().BeNull("no RestOrder before accept");

        // Accept
        var accept = await _client.PostAsJsonAsync(
            $"/api/restaurante/delivery-orders/{deliveryOrder.Id}/accept",
            new { estimatedMinutes = (int?)15 });
        accept.StatusCode.Should().Be(HttpStatusCode.OK);
        var accepted = (await accept.Content.ReadFromJsonAsync<DeliveryOrderDto>())!;

        accepted.Status.Should().Be("Accepted");
        accepted.RestOrderId.Should().NotBeNull("accept must create a linked RestOrder");
    }

    // ── 9. RestOrder aparece na cozinha/KDS ───────────────────────────────────

    [Fact]
    public async Task AcceptDeliveryOrder_LinkedRestOrder_AppearsInKitchen()
    {
        var slug    = await SetupPublicSlugAsync("kitchen-test");
        var product = await CreateMenuProductAsync("KIT");
        await SetPortalFlagsAsync();

        var create = await _publicClient.PostAsJsonAsync("/api/public/orders", new
        {
            publicSlug    = slug,
            orderType     = "Takeaway",
            customerName  = "Fernanda",
            customerPhone = "11944444444",
            items         = new[] { new { productId = product.Id, quantity = 1 } },
        });
        var deliveryOrder = (await create.Content.ReadFromJsonAsync<DeliveryOrderDto>())!;

        var accept = await _client.PostAsJsonAsync(
            $"/api/restaurante/delivery-orders/{deliveryOrder.Id}/accept",
            new { });
        var accepted = (await accept.Content.ReadFromJsonAsync<DeliveryOrderDto>())!;

        // Kitchen sees RestOrders, not DeliveryOrders
        var kitchen = await _client.GetFromJsonAsync<List<OrderDto>>("/api/restaurante/orders");
        kitchen.Should().Contain(o => o.Id == accepted.RestOrderId,
            "the linked RestOrder must appear in the kitchen list");
    }

    // ── 10. Avançar status: Received → Accepted → InPreparation → Delivered ──

    [Fact]
    public async Task FullStatusFlow_DeliveryOrder_Takeaway()
    {
        var slug    = await SetupPublicSlugAsync("status-flow");
        var product = await CreateMenuProductAsync("STA");
        await SetPortalFlagsAsync();

        // Create
        var create = await _publicClient.PostAsJsonAsync("/api/public/orders", new
        {
            publicSlug    = slug,
            orderType     = "Takeaway",
            customerName  = "Lucas",
            customerPhone = "11933333333",
            items         = new[] { new { productId = product.Id, quantity = 1 } },
        });
        var d = (await create.Content.ReadFromJsonAsync<DeliveryOrderDto>())!;
        d.Status.Should().Be("Received");

        // Accept
        var accept = await _client.PostAsJsonAsync(
            $"/api/restaurante/delivery-orders/{d.Id}/accept", new { });
        var accepted = (await accept.Content.ReadFromJsonAsync<DeliveryOrderDto>())!;
        accepted.Status.Should().Be("Accepted");

        // Progress kitchen items: Pending → Preparing → Ready (triggers DeliveryOrder sync to ReadyForPickup)
        var restOrderId = accepted.RestOrderId!.Value;
        var kitchen = await _client.GetFromJsonAsync<List<OrderDto>>("/api/restaurante/orders");
        var restOrder = kitchen!.First(o => o.Id == restOrderId);

        foreach (var item in restOrder.Items)
        {
            var r1 = await _client.PatchAsJsonAsync(
                $"/api/restaurante/orders/{restOrderId}/items/{item.Id}/status",
                new { status = "Preparing" });
            r1.StatusCode.Should().Be(HttpStatusCode.OK);

            var r2 = await _client.PatchAsJsonAsync(
                $"/api/restaurante/orders/{restOrderId}/items/{item.Id}/status",
                new { status = "Ready" });
            r2.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Delivered (Takeaway → confirm pickup; DeliveryOrder must be ReadyForPickup after kitchen sync)
        var deliver = await _client.PatchAsJsonAsync(
            $"/api/restaurante/delivery-orders/{d.Id}/status",
            new { status = "Delivered" });
        deliver.StatusCode.Should().Be(HttpStatusCode.OK);
        var final = (await deliver.Content.ReadFromJsonAsync<DeliveryOrderDto>())!;
        final.Status.Should().Be("Delivered");
    }

    // ── 11. Rastreamento em /api/public/orders/{token} ────────────────────────

    [Fact]
    public async Task Tracking_Returns_StatusLabel_WithoutSensitiveFields()
    {
        var slug    = await SetupPublicSlugAsync("tracking-test");
        var product = await CreateMenuProductAsync("TRK");
        await SetPortalFlagsAsync();

        var create = await _publicClient.PostAsJsonAsync("/api/public/orders", new
        {
            publicSlug    = slug,
            orderType     = "Takeaway",
            customerName  = "Rastreio",
            customerPhone = "11922222222",
            items         = new[] { new { productId = product.Id, quantity = 1 } },
        });
        var order = (await create.Content.ReadFromJsonAsync<DeliveryOrderDto>())!;

        // Track without auth
        var r = await _publicClient.GetAsync($"/api/public/orders/{order.TrackingToken}");
        r.StatusCode.Should().Be(HttpStatusCode.OK);

        var tracking = (await r.Content.ReadFromJsonAsync<TrackingDto>())!;
        tracking.OrderNumber.Should().Be(order.OrderNumber);
        tracking.Status.Should().Be("Received");
        tracking.StatusLabel.Should().NotBeNullOrEmpty();
        tracking.OrderType.Should().Be("Takeaway");

        // Verify raw JSON does not contain tenantId or storeId
        var json = await _publicClient.GetStringAsync($"/api/public/orders/{order.TrackingToken}");
        json.Should().NotContainAny("tenantId", "storeId", "TenantId", "StoreId");
    }

    // ── Security: AcceptingOrders=false bloqueia POST ─────────────────────────

    [Fact]
    public async Task CreatePortalOrder_WhenNotAcceptingOrders_IsRejected()
    {
        var slug    = await SetupPublicSlugAsync("closed-test");
        var product = await CreateMenuProductAsync("CLS");
        await SetPortalFlagsAsync(acceptingOrders: false);

        var r = await _publicClient.PostAsJsonAsync("/api/public/orders", new
        {
            publicSlug    = slug,
            orderType     = "Takeaway",
            customerName  = "Teste",
            customerPhone = "11911111111",
            items         = new[] { new { productId = product.Id, quantity = 1 } },
        });

        r.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.Conflict,
            HttpStatusCode.UnprocessableEntity,
            HttpStatusCode.ServiceUnavailable);
    }

    // ── Security: DeliveryEnabled=false bloqueia OrderType=Delivery ──────────

    [Fact]
    public async Task CreatePortalOrder_DeliveryDisabled_DeliveryOrderIsRejected()
    {
        var slug    = await SetupPublicSlugAsync("no-delivery");
        var product = await CreateMenuProductAsync("NDL");
        await SetPortalFlagsAsync(deliveryEnabled: false, takeawayEnabled: true);

        var r = await _publicClient.PostAsJsonAsync("/api/public/orders", new
        {
            publicSlug          = slug,
            orderType           = "Delivery",
            customerName        = "Teste",
            customerPhone       = "11900000000",
            deliveryAddressJson = "{\"street\":\"Rua Teste\",\"number\":\"1\"}",
            items               = new[] { new { productId = product.Id, quantity = 1 } },
        });

        r.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.UnprocessableEntity,
            HttpStatusCode.Conflict);
    }

    // ── Security: TakeawayEnabled=false bloqueia OrderType=Takeaway ──────────

    [Fact]
    public async Task CreatePortalOrder_TakeawayDisabled_TakeawayOrderIsRejected()
    {
        var slug    = await SetupPublicSlugAsync("no-takeaway");
        var product = await CreateMenuProductAsync("NTK");
        await SetPortalFlagsAsync(deliveryEnabled: true, takeawayEnabled: false);

        var r = await _publicClient.PostAsJsonAsync("/api/public/orders", new
        {
            publicSlug    = slug,
            orderType     = "Takeaway",
            customerName  = "Teste",
            customerPhone = "11900000001",
            items         = new[] { new { productId = product.Id, quantity = 1 } },
        });

        r.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.UnprocessableEntity,
            HttpStatusCode.Conflict);
    }

    // ── Slim local DTOs for deserialization ──────────────────────────────────

    private record TrackingDto(
        int     OrderNumber,
        string  Status,
        string  StatusLabel,
        int?    EstimatedMinutes,
        string  OrderType);
}
