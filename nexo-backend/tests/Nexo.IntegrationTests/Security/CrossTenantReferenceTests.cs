using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Features.Financial;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// Referências por id a registros de OUTRA empresa num cadastro desta: devem responder como inexistentes (404) e nunca
/// ficar gravadas. A busca de loja por id ignora o filtro de empresa (as lojas são resolvidas antes do contexto), então
/// cada serviço que aceita um storeId confere a posse.
/// </summary>
[Collection("Integration")]
public class CrossTenantReferenceTests
{
    private readonly TestWebApplicationFactory _factory;

    public CrossTenantReferenceTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task A_financial_entry_never_takes_another_tenants_store()
    {
        var foreignStore = await ForeignStoreAsync();
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var account = (await c.GetFromJsonAsync<List<FinancialAccountDto>>("/api/financial/accounts"))!
            .First(a => a.AccountType == "Payable" && a.IsActive).Id;

        var tag = $"loja-alheia-{Guid.NewGuid():N}"[..24];
        (await c.PostAsJsonAsync("/api/financial/transactions", new CreateTransactionRequest(
                account, "Payable", 10m, tag, DateTime.UtcNow, Category: "OperatingExpense", StoreId: foreignStore)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "a loja de outra empresa é inexistente para esta");

        (await c.PostAsJsonAsync("/api/financial/transactions/installments", new CreateInstallmentsRequest(
                "Payable", 30m, tag, DateTime.UtcNow, Installments: 3, Category: "OperatingExpense", StoreId: foreignStore)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await c.PostAsJsonAsync("/api/financial/recurrences", new CreateRecurrenceRequest(
                "Payable", tag, 10m, 5, DateOnly.FromDateTime(DateTime.UtcNow), Category: "OperatingExpense", StoreId: foreignStore)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.FinancialTransactions.IgnoreQueryFilters().AnyAsync(t => t.StoreId == foreignStore))
            .Should().BeFalse("nenhum lançamento desta empresa pode apontar para a loja de outra");
    }

    /// <summary>Uma loja de outra empresa, criada direto no banco.</summary>
    private async Task<Guid> ForeignStoreAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var tag = Guid.NewGuid().ToString("N")[..10];
        // CROSS-TENANT: semeadura, sem requisição HTTP
        var taxId = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var tenant = Tenant.Create($"Outra Empresa {tag}", taxId, $"outra-{tag}@empresa.test");
        db.Tenants.Add(tenant);
        var store = Store.Create(tenant.Id, $"Loja Alheia {tag}", $"loja-alheia-{tag}");
        db.Stores.Add(store);
        await db.SaveChangesAsync();
        return store.Id;
    }
}
