using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Cash;
using Nexo.Application.Features.Products;
using Nexo.Application.Features.Sales;
using Nexo.Application.Features.Settings;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Sales;

/// <summary>
/// PR 71 — the PDV discount rule set in Configurações is enforced by the server: above the limit a
/// seller needs a manager's login and password (audited), or cannot at all when the company turned
/// that off; managers have no limit. Settings are restored after each test.
/// </summary>
[Collection("Integration")]
public class PosDiscountRuleTests
{
    private readonly TestWebApplicationFactory _factory;
    public PosDiscountRuleTests(TestWebApplicationFactory factory) => _factory = factory;

    private static UpdateSettingsRequest Rules(bool requireManagerAuth, decimal max = 10m) => new(
        new InventorySettingsDto(EnableLowStockAlerts: true, EnableZeroStockAlerts: true),
        new PosSettingsDto(AllowValueDiscount: true, AllowPercentDiscount: true, RequireManagerAuth: requireManagerAuth, MaxDiscountPercent: max));

    [Fact]
    public async Task Settings_keep_only_what_has_an_effect_and_refuse_a_pdv_without_discount_modes()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var before = await admin.GetFromJsonAsync<SettingsDto>("/api/settings");
        try
        {
            (await admin.PutAsJsonAsync("/api/settings", Rules(true, 12.5m))).EnsureSuccessStatusCode();
            var saved = await admin.GetFromJsonAsync<JsonElement>("/api/settings");
            saved.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["inventory", "pos"]);
            saved.GetProperty("pos").GetProperty("maxDiscountPercent").GetDecimal().Should().Be(12.5m);

            var noModes = Rules(true) with { Pos = Rules(true).Pos with { AllowValueDiscount = false, AllowPercentDiscount = false } };
            (await admin.PutAsJsonAsync("/api/settings", noModes)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            await admin.PutAsJsonAsync("/api/settings", new UpdateSettingsRequest(before!.Inventory, before.Pos));
        }
    }

    [Fact]
    public async Task Above_the_limit_a_seller_needs_a_manager_who_is_recorded()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var before = await admin.GetFromJsonAsync<SettingsDto>("/api/settings");
        (await admin.PutAsJsonAsync("/api/settings", Rules(requireManagerAuth: true))).EnsureSuccessStatusCode();
        try
        {
            var (seller, session) = await SellerWithDrawerAsync("pdv.desconto");
            var noApproval = await ConfirmAsync(seller, session, discount: 15m);
            noApproval.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await noApproval.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Contain("autorização de um gerente");

            var wrong = await ConfirmAsync(seller, session, discount: 15m, approval: new ManagerApprovalRequest(TestCredentials.AdminLogin, "nao-e-a-senha-" + Guid.NewGuid().ToString("N")[..6]));
            wrong.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await wrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Contain("recusada");

            var approved = await ConfirmAsync(seller, session, discount: 15m,
                approval: new ManagerApprovalRequest(TestCredentials.AdminLogin, TestCredentials.AdminPassword));
            approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
            var sale = (await approved.Content.ReadFromJsonAsync<SaleDto>())!;

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            (await db.AuditRecords.IgnoreQueryFilters().AnyAsync(a => a.ActionType == "pos_discount_override" && a.EntityId == sale.Number.ToString()))
                .Should().BeTrue("who authorized the discount is recorded");

            (await ConfirmAsync(seller, session, discount: 10m)).StatusCode.Should().Be(HttpStatusCode.OK, "within the limit");
        }
        finally
        {
            await admin.PutAsJsonAsync("/api/settings", new UpdateSettingsRequest(before!.Inventory, before.Pos));
        }
    }

    [Fact]
    public async Task Without_authorization_at_the_pdv_only_a_manager_gives_more_than_the_limit()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var before = await admin.GetFromJsonAsync<SettingsDto>("/api/settings");
        (await admin.PutAsJsonAsync("/api/settings", Rules(requireManagerAuth: false))).EnsureSuccessStatusCode();
        try
        {
            var (seller, session) = await SellerWithDrawerAsync("pdv.semgerente");
            var refused = await ConfirmAsync(seller, session, discount: 30m,
                approval: new ManagerApprovalRequest(TestCredentials.AdminLogin, TestCredentials.AdminPassword));
            refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Contain("Só um gerente");

            (await ConfirmAsync(admin, session, discount: 30m)).StatusCode.Should().Be(HttpStatusCode.OK, "managers have no limit");
        }
        finally
        {
            await admin.PutAsJsonAsync("/api/settings", new UpdateSettingsRequest(before!.Inventory, before.Pos));
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>A 100,00 sale with a sale-level discount, paid by Pix.</summary>
    private async Task<HttpResponseMessage> ConfirmAsync(HttpClient c, CashSessionDto session, decimal discount, ManagerApprovalRequest? approval = null)
    {
        var code = $"PR71-{Guid.NewGuid():N}"[..20];
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);   // products are created by management
        var product = await (await admin.PostAsJsonAsync("/api/products",
                new CreateProductRequest(Code: code, Name: code, Unit: "Un", SalePrice: 100m, TrackStock: false)))
            .Content.ReadFromJsonAsync<ProductDto>();
        var draft = (await (await c.PostAsJsonAsync("/api/sales", new CreateSaleRequest(CashSessionId: session.Id)))
            .Content.ReadFromJsonAsync<SaleDto>())!;
        (await c.PostAsJsonAsync($"/api/sales/{draft.Id}/items", new AddSaleItemRequest(product!.Id, 1, 100m))).EnsureSuccessStatusCode();
        return await c.PostAsJsonAsync($"/api/sales/{draft.Id}/confirm",
            new ConfirmSaleRequest([new PaymentInput("Pix", "Cash", 100m - discount)], DiscountAmount: discount, ManagerApproval: approval));
    }

    private async Task<(HttpClient Client, CashSessionDto Session)> SellerWithDrawerAsync(string login)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            if (!await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Login == login))
            {
                var adminUser = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Login == TestCredentials.AdminLogin);
                var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
                db.Users.Add(User.Create(
                    tenantId: adminUser.TenantId, fullName: $"Vendedor {login}",
                    email: $"{login}@{TestCredentials.TestLocalDomain}", login: login,
                    passwordHash: hasher.Hash(TestCredentials.SameTenantManagerPassword), role: UserRole.Vendedor));
                await db.SaveChangesAsync();
            }
        }
        var client = await AuthClientFactory.LoginAsync(_factory, login, TestCredentials.SameTenantManagerPassword);
        // sellers do not open the drawer: they sell on the one a manager opened
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var open = await admin.PostAsJsonAsync("/api/cash/sessions/open", new OpenCashSessionRequest(0m, "PR71"));
        var session = open.StatusCode == HttpStatusCode.Created
            ? (await open.Content.ReadFromJsonAsync<CashSessionDto>())!
            : (await admin.GetFromJsonAsync<CashSessionDto>("/api/cash/sessions/open"))!;
        return (client, session);
    }
}
