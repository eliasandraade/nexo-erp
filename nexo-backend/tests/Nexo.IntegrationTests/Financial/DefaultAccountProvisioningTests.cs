using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Financial;

/// <summary>
/// Every tenant must own the four default accounts, because the money paths look them up by type
/// and fail hard when they are missing — a credit sale and a Service payment both resolve the
/// tenant's "Contas a Receber" account before writing anything.
///
/// This used to hold only for the very first tenant: the demo seeder creates the accounts once and
/// aborts as soon as any account row exists, and it does not run in Production at all. So the
/// second tenant onward had none.
/// </summary>
[Collection("Integration")]
public class DefaultAccountProvisioningTests
{
    private readonly TestWebApplicationFactory _factory;
    public DefaultAccountProvisioningTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Self_service_registration_provisions_the_four_default_accounts()
    {
        var client = _factory.CreateApiClient();
        // Registration uses the e-mail as the user's login, and users.login is varchar(50).
        // Keep the unique suffix short enough that the whole address fits.
        var email = $"barb-{Guid.NewGuid():N}"[..16] + "@" + TestCredentials.TestDomain;

        var resp = await client.PostAsJsonAsync("/api/auth/register", new
        {
            name     = "Barbearia do Ze",
            email,
            // Self-service signup makes the new user the Diretoria of its own tenant, so the
            // canonical admin test password is the fitting fixture — and no new literal is minted.
            password = TestCredentials.AdminPassword,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK, "registration must succeed as a precondition");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();

        var tenantId = await db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Email == email)
            .Select(t => t.Id)
            .SingleAsync();

        var types = await db.FinancialAccounts.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId)
            .Select(a => a.AccountType)
            .ToListAsync();

        types.Should().BeEquivalentTo(new[]
        {
            FinancialAccountType.Cash,
            FinancialAccountType.Bank,
            FinancialAccountType.Receivable,
            FinancialAccountType.Payable,
        });
    }
}
