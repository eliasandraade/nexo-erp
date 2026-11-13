using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;
using Nexo.Infrastructure.Persistence.Seed;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Seed;

/// <summary>The demo data stays on the oldest company, whatever the physical order of the table. Everything here is undone at the end.</summary>
[Collection("Integration")]
public class DataSeederDefaultTenantTests
{
    private readonly TestWebApplicationFactory _factory;

    public DataSeederDefaultTenantTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task A_later_boot_grants_nothing_to_a_company_that_became_the_first_row()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var newest = Tenant.Create($"Empresa {Guid.NewGuid():N}", "00000000000000", $"c{Guid.NewGuid():N}@teste.dev");
        db.Tenants.Add(newest);
        await db.SaveChangesAsync();

        // An update writes the row again further on, as a change of status does: the newest company is now the first one a scan finds.
        await db.Database.ExecuteSqlInterpolatedAsync($"update nexo.tenants set updated_at = updated_at where id <> {newest.Id}");
        db.ChangeTracker.Clear();
        (await db.Tenants.AsNoTracking().FirstAsync()).Id.Should().Be(newest.Id, "otherwise this test proves nothing");

        await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync();

        (await db.ModuleSubscriptions.IgnoreQueryFilters().AnyAsync(s => s.TenantId == newest.Id)).Should().BeFalse();
    }
}
