using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Application.Modules.Build.Interfaces;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Build;

/// <summary>
/// O detalhe da obra carrega etapas e diários em consultas separadas: cada coleção vem completa,
/// sem repetir linha e na ordem pedida na consulta, não na ordem de inserção.
/// </summary>
[Collection("Integration")]
public class BuildProjectDetailsQueryTests
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;

    public BuildProjectDetailsQueryTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateApiClient();
    }

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", TestCredentials.AdminLoginPayload());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    private async Task<Guid> CreateProjectAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/build/projects", new
        {
            name = $"Obra {Guid.NewGuid():N}"[..14],
            clientName = "Cliente Teste",
            type = "House",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> AddStageAsync(Guid projectId, string name)
    {
        var response = await _client.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/stages", new { name });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task AddDailyLogAsync(Guid projectId, DateOnly date)
    {
        var response = await _client.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/daily-logs",
            new { date, notes = $"Diário de {date:yyyy-MM-dd}" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task GetByIdWithDetails_loads_each_collection_once_in_the_requested_order()
    {
        await AuthenticateAsync();
        var id = await CreateProjectAsync();

        // A ordem de inserção é o contrário da ordem final, para o teste não passar por acaso.
        var stageC = await AddStageAsync(id, "Etapa C");
        var stageB = await AddStageAsync(id, "Etapa B");
        var stageA = await AddStageAsync(id, "Etapa A");
        (await _client.PutAsJsonAsync($"/api/v1/build/projects/{id}/stages/reorder", new
        {
            entries = new[]
            {
                new { stageId = stageA, order = 1 },
                new { stageId = stageB, order = 2 },
                new { stageId = stageC, order = 3 },
            },
        })).IsSuccessStatusCode.Should().BeTrue();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dates = new[] { today.AddDays(-3), today.AddDays(-1), today.AddDays(-7), today.AddDays(-2) };
        foreach (var date in dates)
            await AddDailyLogAsync(id, date);
        var newestFirst = dates.OrderByDescending(d => d).ToArray();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var tenantId = await db.BldProjects.IgnoreQueryFilters()
            .Where(p => p.Id == id).Select(p => p.TenantId).SingleAsync();
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Set(tenantId, "teste", []);

        var project = await scope.ServiceProvider.GetRequiredService<IBuildProjectRepository>()
            .GetByIdWithDetailsAsync(id);

        project.Should().NotBeNull();
        project!.Stages.Select(s => s.Name).Should().Equal("Etapa A", "Etapa B", "Etapa C");
        project.DailyLogs.Select(l => l.Date).Should().Equal(newestFirst);

        // O contrato da API segue igual.
        var details = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/build/projects/{id}/details");
        details.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("name").GetString())
            .Should().Equal("Etapa A", "Etapa B", "Etapa C");
        details.GetProperty("recentLogs").EnumerateArray().Select(l => l.GetProperty("date").GetString())
            .Should().Equal(newestFirst.Select(d => d.ToString("yyyy-MM-dd")));
    }

    [Fact]
    public async Task GetByIdWithDetails_of_a_project_without_children_returns_empty_collections()
    {
        await AuthenticateAsync();
        var id = await CreateProjectAsync();

        var details = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/build/projects/{id}/details");

        details.GetProperty("stages").GetArrayLength().Should().Be(0);
        details.GetProperty("recentLogs").GetArrayLength().Should().Be(0);
    }
}
