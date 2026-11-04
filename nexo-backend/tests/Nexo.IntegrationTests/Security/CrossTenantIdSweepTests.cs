using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit.Abstractions;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// Varredura de isolamento entre empresas: uma empresa B (administradora, com todos os módulos) chama TODA rota GET
/// com id na URL usando ids reais de linhas de outras empresas (um por tabela com tenant_id). Nenhuma resposta pode
/// ser 500, e nenhuma resposta 2xx pode devolver o id pedido — isso seria ler um registro alheio pelo id.
/// </summary>
[Collection("Integration")]
public class CrossTenantIdSweepTests
{
    private readonly TestWebApplicationFactory _factory;
    private readonly ITestOutputHelper _out;

    public CrossTenantIdSweepTests(TestWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    [Fact]
    public async Task Tenant_B_never_reads_another_tenants_row_by_id_on_any_get_route()
    {
        var (tenantB, client) = await TenantWithEveryModuleAsync();
        var pool = await ForeignIdsAsync(tenantB);
        pool.Should().NotBeEmpty();

        var routes = GetRoutesWithIds();
        _out.WriteLine($"{routes.Count} rotas GET com id; {pool.Count} ids alheios");

        var problems = new List<string>();
        foreach (var route in routes)
        foreach (var (table, id) in pool)
        {
            var url = "/" + IdParam.Replace(route, id.ToString());
            using var response = await client.GetAsync(url);
            if ((int)response.StatusCode >= 500)
                problems.Add($"500 {url} ({table})");
            else if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                if (body.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase))
                    problems.Add($"{(int)response.StatusCode} {url} ({table}) devolveu o id alheio");
            }
        }

        foreach (var p in problems) _out.WriteLine(p);
        problems.Should().BeEmpty();
    }

    private static readonly Regex IdParam = new(@"\{[^}]*\}", RegexOptions.Compiled);

    private List<string> GetRoutesWithIds()
    {
        var sources = _factory.Services.GetServices<EndpointDataSource>();
        return sources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true)
            .Select(e => e.RoutePattern)
            .Where(p => p.Parameters.Count > 0
                        && p.Parameters.All(x => x.Name.EndsWith("id", StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.RawText!.TrimStart('/'))
            .Where(r => r.StartsWith("api/", StringComparison.OrdinalIgnoreCase)
                        && !r.StartsWith("api/platform", StringComparison.OrdinalIgnoreCase)
                        && !r.StartsWith("api/public", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .OrderBy(r => r)
            .ToList();
    }

    /// <summary>Um id por tabela com tenant_id, de uma linha que não é da empresa B.</summary>
    private async Task<List<(string Table, Guid Id)>> ForeignIdsAsync(Guid tenantB)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        var tables = new List<string>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT c.table_schema || '.' || c.table_name FROM information_schema.columns c
                JOIN information_schema.columns i ON i.table_name = c.table_name AND i.table_schema = c.table_schema
                WHERE c.table_schema NOT IN ('pg_catalog', 'information_schema')
                  AND c.column_name = 'tenant_id' AND i.column_name = 'id' AND i.data_type = 'uuid'
                """;
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) tables.Add(r.GetString(0));
        }
        var pool = new List<(string, Guid)>();
        foreach (var t in tables.Distinct())
        {
            await using var cmd = conn.CreateCommand();
            var (schema, table) = (t[..t.IndexOf('.')], t[(t.IndexOf('.') + 1)..]);
            cmd.CommandText = $"SELECT id FROM \"{schema}\".\"{table}\" WHERE tenant_id IS DISTINCT FROM @b LIMIT 1";
            var p = cmd.CreateParameter();
            p.ParameterName = "b";
            p.Value = tenantB;
            cmd.Parameters.Add(p);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) pool.Add((t, r.GetGuid(0)));
        }
        return pool;
    }

    private async Task<(Guid TenantId, HttpClient Client)> TenantWithEveryModuleAsync()
    {
        var email = $"sweep-{Guid.NewGuid():N}"[..18] + "@" + TestCredentials.TestDomain;
        var reg = await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/register",
            new { name = "Empresa Varredura", email, password = TestCredentials.AdminPassword });
        reg.StatusCode.Should().Be(HttpStatusCode.OK, await reg.Content.ReadAsStringAsync());
        Guid tenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Login == email);
            user.Activate();
            tenantId = user.TenantId;
            var have = await db.ModuleSubscriptions.IgnoreQueryFilters().Where(s => s.TenantId == tenantId).Select(s => s.ModuleKey).ToListAsync();
            var keys = await db.ModuleDefinitions.IgnoreQueryFilters().Select(m => m.Key).ToListAsync();
            foreach (var key in keys.Concat(["varejo", "restaurante", "build", "service"]).Distinct().Except(have))
                db.ModuleSubscriptions.Add(ModuleSubscription.CreateAdminGrant(tenantId, key));
            await db.SaveChangesAsync();
        }
        return (tenantId, await AuthClientFactory.LoginAsync(_factory, email, TestCredentials.AdminPassword));
    }
}
