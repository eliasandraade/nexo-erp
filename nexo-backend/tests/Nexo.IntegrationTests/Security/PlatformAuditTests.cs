using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// Verifies privileged platform actions write an audit record (transactionally) and that
/// password resets never persist secret material. Uses a throwaway tenant for the reset so
/// the shared seeded admin is never mutated.
/// </summary>
[Collection("Integration")]
public class PlatformAuditTests
{
    private readonly TestWebApplicationFactory _factory;
    public PlatformAuditTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Impersonate_WritesPlatformAuditRecord()
    {
        var client = await PlatformAuthorizationTests.PlatformClientAsync(_factory);

        var tenants = await client.GetFromJsonAsync<List<TenantRow>>("/api/platform/tenants");
        var tenantId = tenants!.First().Id;

        var resp = await client.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/impersonate", new { });
        resp.IsSuccessStatusCode.Should().BeTrue();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var audit = await db.AuditRecords.IgnoreQueryFilters()
            .Where(a => a.ActionType == "platform_impersonation" && a.TenantId == tenantId)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync();

        audit.Should().NotBeNull("impersonation must be audited");
        audit!.ActorType.Should().Be("platform");
    }

    [Fact]
    public async Task ResetPassword_AuditRecord_ContainsNoSecret()
    {
        var client = await PlatformAuthorizationTests.PlatformClientAsync(_factory);

        // Create an isolated throwaway tenant so we never reset the shared admin.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var create = await client.PostAsJsonAsync("/api/platform/tenants", new
        {
            companyName  = $"TESTE_DELETE Audit Corp {suffix}",
            taxId        = $"9{suffix}0001{suffix[..2]}"[..14],
            email        = $"audit-{suffix}@teste.delete",
            tradeName    = (string?)null,
            phone        = (string?)null,
            businessType = "varejo",
            modules      = new[] { "varejo" },
            adminName    = "Audit Admin",
            adminLogin   = $"auditadmin_{suffix}",
            adminPassword = "InitialPass!123",
            adminEmail   = $"auditadmin-{suffix}@teste.delete",
        });
        create.IsSuccessStatusCode.Should().BeTrue();
        var created = await create.Content.ReadFromJsonAsync<CreatedTenant>();

        var detail = await client.GetFromJsonAsync<TenantDetail>($"/api/platform/tenants/{created!.Id}");
        var userId = detail!.Users.First().Id;

        const string newPass = "BrandNewSecret!2026";
        var resp = await client.PostAsJsonAsync(
            $"/api/platform/tenants/{created.Id}/users/{userId}/reset-password",
            new { newPassword = newPass });
        resp.IsSuccessStatusCode.Should().BeTrue();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var audit = await db.AuditRecords.IgnoreQueryFilters()
            .Where(a => a.ActionType == "user_password_changed" && a.EntityId == userId.ToString())
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync();

        audit.Should().NotBeNull("password reset must be audited");
        audit!.ActorType.Should().Be("platform");

        var blob = (audit.Description + " " + (audit.MetadataJson ?? "")).ToLowerInvariant();
        blob.Should().NotContain(newPass.ToLowerInvariant(), "audit must never store the new password");
        blob.Should().NotContain("hash", "audit must not reference the password hash");
    }

    private record TenantRow(Guid Id);
    private record CreatedTenant(Guid Id);
    private record TenantDetail(List<UserRow> Users);
    private record UserRow(Guid Id);
}
