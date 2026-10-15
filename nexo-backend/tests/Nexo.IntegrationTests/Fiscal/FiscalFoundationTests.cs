using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Nexo.Application.Features.Cash;
using Nexo.Application.Features.Fiscal;
using Nexo.Application.Features.Products;
using Nexo.Application.Features.Sales;
using Nexo.Application.Features.Stock;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Fiscal;

/// <summary>
/// PR 66 — fiscal foundation: the issuer profile is validated (check digits, IBGE, UF) and stored
/// without masks; product fiscal data and the readiness list; "CPF na nota" on the sale.
/// </summary>
[Collection("Integration")]
public class FiscalFoundationTests
{
    private readonly TestWebApplicationFactory _factory;
    public FiscalFoundationTests(TestWebApplicationFactory factory) => _factory = factory;

    private static SaveFiscalProfileRequest Profile(string cnpj = "11.222.333/0001-81", string regime = "SimplesNacional") => new(
        "Loja Teste Ltda", "Loja Teste", cnpj, "isento", null, regime,
        "Rua das Flores", "100", null, "Centro", "São Paulo", "3550308", "sp", "01001-000");

    [Fact]
    public async Task The_issuer_profile_is_validated_and_stored_without_masks()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);

        var bad = await admin.PutAsJsonAsync("/api/fiscal/profile", Profile(cnpj: "11.222.333/0001-80") with { CityIbgeCode = "123", State = "XX" });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var details = (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("details").EnumerateArray().Select(x => x.GetString()).ToList();
        details.Should().Contain(d => d!.Contains("CNPJ")).And.Contain(d => d!.Contains("IBGE")).And.Contain(d => d!.Contains("UF"));

        var saved = await admin.PutAsJsonAsync("/api/fiscal/profile", Profile());
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var profile = await admin.GetFromJsonAsync<FiscalProfileDto>("/api/fiscal/profile");
        profile!.Cnpj.Should().Be("11222333000181");
        profile.StateRegistration.Should().Be("ISENTO");
        profile.State.Should().Be("SP");
        profile.ZipCode.Should().Be("01001000");
        profile.Crt.Should().Be(1);

        (await admin.PutAsJsonAsync("/api/fiscal/profile", Profile(regime: "LucroPresumido"))).EnsureSuccessStatusCode();
        (await admin.GetFromJsonAsync<FiscalProfileDto>("/api/fiscal/profile"))!.Crt.Should().Be(3, "regime normal");
        (await admin.GetFromJsonAsync<FiscalReadinessDto>("/api/fiscal/readiness"))!.ProfileComplete.Should().BeTrue();
    }

    [Fact]
    public async Task Products_without_ncm_or_with_a_non_gtin_barcode_are_listed_until_fixed()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var code = $"PR66-{Guid.NewGuid():N}"[..20];
        var product = (await (await admin.PostAsJsonAsync("/api/products",
                new CreateProductRequest(Code: code, Name: "AAA Fiscal " + code, Unit: "Un", SalePrice: 10m, Barcode: "123")))
            .Content.ReadFromJsonAsync<ProductDto>())!;

        var before = await admin.GetFromJsonAsync<FiscalReadinessDto>("/api/fiscal/readiness");
        before!.EmissionAvailable.Should().BeFalse();
        before.ProductsToFix.Should().Contain(p => p.ProductId == product.Id)
            .Which.Problems.Should().HaveCount(2);

        (await admin.PutAsJsonAsync($"/api/fiscal/products/{product.Id}", new UpdateProductFiscalRequest("123", null, 9, "1102", "1")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var ok = await admin.PutAsJsonAsync($"/api/fiscal/products/{product.Id}", new UpdateProductFiscalRequest("6109.10.00", "28.038.00", 0, "5102", "102"));
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var fiscal = await admin.GetFromJsonAsync<ProductFiscalDto>($"/api/fiscal/products/{product.Id}");
        fiscal.Should().BeEquivalentTo(new { Ncm = "61091000", Cest = "2803800", Origin = 0, Cfop = "5102", TaxSituation = "102" });

        var after = await admin.GetFromJsonAsync<FiscalReadinessDto>("/api/fiscal/readiness");
        after!.ProductsToFix.Should().Contain(p => p.ProductId == product.Id)
            .Which.Problems.Should().ContainSingle().Which.Should().Contain("GTIN");
    }

    [Fact]
    public async Task The_cpf_on_the_nota_is_checked_and_kept_on_the_sale()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var open = await admin.PostAsJsonAsync("/api/cash/sessions/open", new OpenCashSessionRequest(0m, "PR66"));
        var session = open.StatusCode == HttpStatusCode.Created
            ? (await open.Content.ReadFromJsonAsync<CashSessionDto>())!
            : (await admin.GetFromJsonAsync<CashSessionDto>("/api/cash/sessions/open"))!;
        var code = $"PR66-{Guid.NewGuid():N}"[..20];
        var product = (await (await admin.PostAsJsonAsync("/api/products",
                new CreateProductRequest(Code: code, Name: code, Unit: "Un", SalePrice: 10m, TrackStock: false)))
            .Content.ReadFromJsonAsync<ProductDto>())!;
        var draft = (await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(CashSessionId: session.Id)))
            .Content.ReadFromJsonAsync<SaleDto>())!;
        (await admin.PostAsJsonAsync($"/api/sales/{draft.Id}/items", new AddSaleItemRequest(product.Id, 1, 10m))).EnsureSuccessStatusCode();

        var wrong = await admin.PostAsJsonAsync($"/api/sales/{draft.Id}/confirm",
            new ConfirmSaleRequest([new PaymentInput("Pix", "Cash", 10m)], ConsumerDocument: "529.982.247-24"));
        wrong.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await wrong.Content.ReadAsStringAsync()).Should().Contain("CPF/CNPJ na nota");
        (await admin.GetFromJsonAsync<SaleDto>($"/api/sales/{draft.Id}"))!.Status.Should().Be("Draft");

        var right = await admin.PostAsJsonAsync($"/api/sales/{draft.Id}/confirm",
            new ConfirmSaleRequest([new PaymentInput("Pix", "Cash", 10m)], ConsumerDocument: "529.982.247-25"));
        right.StatusCode.Should().Be(HttpStatusCode.OK, await right.Content.ReadAsStringAsync());
        (await right.Content.ReadFromJsonAsync<SaleDto>())!.ConsumerDocument.Should().Be("52998224725");
    }
}
