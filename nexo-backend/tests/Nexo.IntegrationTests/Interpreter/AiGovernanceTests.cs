using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Nexo.IntegrationTests.Security;

namespace Nexo.IntegrationTests.Interpreter;

/// <summary>
/// What the SuperAdmin sets has the effect it shows, on a real PostgreSQL (own database): a hard spend limit blocks paid
/// calls before they happen — even many at once — and automatic mode falls back to the free reader; the active prompt is
/// the one the runtime sends, one per type; a movement can't be confirmed with references of another company.
/// </summary>
[Collection(AiIsolatedCollection.Name)]
public sealed class AiGovernanceTests : IClassFixture<TestWebApplicationFactory>, IAsyncLifetime
{
    private const string Key = "sk-ant-api03-governance-test-fake-key-4321";
    private readonly TestWebApplicationFactory _factory;

    public AiGovernanceTests(TestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync()
    {
        _factory.Anthropic.Reset();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(expected, body);
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<(HttpClient Admin, Dictionary<string, Guid> Ids, Guid TenantId)> ReadyAsync(decimal outputUsdPerMillion = 0.5m)
    {
        var admin = await PlatformAuthorizationTests.PlatformClientAsync(_factory);
        var list = await Json(await admin.GetAsync("/api/platform/interpreter/providers"));
        var ids = list.GetProperty("providers").EnumerateArray().ToDictionary(p => p.GetProperty("provider").GetString()!, p => p.GetProperty("id").GetGuid());
        await Json(await admin.PutAsJsonAsync($"/api/platform/interpreter/providers/{ids["Claude"]}/api-key", new { apiKey = Key }));
        await Json(await admin.PatchAsJsonAsync($"/api/platform/interpreter/providers/{ids["Claude"]}",
            new { isEnabled = true, modelId = "claude-haiku-5-5", inputUsdPerMillionTokens = 0m, outputUsdPerMillionTokens = outputUsdPerMillion }));
        await Json(await admin.PatchAsJsonAsync($"/api/platform/interpreter/providers/{ids["RuleBased"]}", new { isEnabled = true, isDefault = true }));
        var tenant = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var me = await Json(await tenant.GetAsync("/api/auth/me"));
        return (admin, ids, Guid.Parse(me.GetProperty("tenantId").GetString()!));
    }

    private static Task<HttpResponseMessage> Playground(HttpClient c, string? force, Guid tenantId)
        => c.PostAsJsonAsync("/api/platform/interpreter/playground", new { text = "Paguei R$ 50,00 de frete em 01/10/2026", forceProvider = force, tenantId });

    [Fact]
    public async Task A_hard_limit_blocks_paid_calls_before_they_happen_and_automatic_mode_falls_back()
    {
        var (admin, ids, tenantId) = await ReadyAsync();
        await Json(await admin.PutAsJsonAsync($"/api/platform/interpreter/costs/{tenantId}/limits", new { softLimitUsd = (decimal?)null, hardLimitUsd = 0m }));

        var forced = await Json(await Playground(admin, "Claude", tenantId), HttpStatusCode.ServiceUnavailable);
        forced.GetProperty("details")[0].GetString().Should().Be("limit_reached");
        _factory.Anthropic.Requests.Should().BeEmpty("a call over the hard limit never reaches the provider");

        await Json(await admin.PatchAsJsonAsync($"/api/platform/interpreter/providers/{ids["Claude"]}", new { isDefault = true }));
        var auto = await Json(await Playground(admin, null, tenantId));
        auto.GetProperty("provider").GetString().Should().Be("RuleBased");
        auto.GetProperty("attempts")[0].GetProperty("failureReason").GetString().Should().Be("limit_reached");

        var costs = await Json(await admin.GetAsync("/api/platform/interpreter/costs"));
        costs.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("tenantId").GetGuid() == tenantId)
            .GetProperty("hardLimitHit").GetBoolean().Should().BeTrue();

        await Json(await admin.PatchAsJsonAsync($"/api/platform/interpreter/providers/{ids["RuleBased"]}", new { isDefault = true }));
        await Json(await admin.PutAsJsonAsync($"/api/platform/interpreter/costs/{tenantId}/limits", new { softLimitUsd = (decimal?)null, hardLimitUsd = (decimal?)null }));
    }

    [Fact]
    public async Task Concurrent_paid_calls_cannot_all_pass_a_hard_limit_that_fits_one()
    {
        // $5 per million output tokens: one call reserves 1024 × 5 = 5,120 micro-dollars; the limit (1 cent = 10,000) fits one.
        var (admin, _, tenantId) = await ReadyAsync(outputUsdPerMillion: 5m);
        await Json(await admin.PutAsJsonAsync($"/api/platform/interpreter/costs/{tenantId}/limits", new { softLimitUsd = (decimal?)null, hardLimitUsd = 0.01m }));
        _factory.Anthropic.Respond = model => { Thread.Sleep(400); return FakeAnthropicHandler.Success(model); };

        var calls = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Playground(admin, "Claude", tenantId)));

        calls.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, "the reservation is taken under a lock: only one fits");
        calls.Count(r => r.StatusCode == HttpStatusCode.ServiceUnavailable).Should().Be(7);
        _factory.Anthropic.Requests.Should().HaveCount(1);

        using var scope = _factory.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<NexoDbContext>().AiUsageReservations.AsNoTracking()
            .Where(r => r.TenantId == tenantId).ToListAsync();
        rows.Should().ContainSingle(r => r.Status == AiUsageStatus.Settled && r.OutputTokens == 50 && r.ActualMicros == 250);
        await Json(await admin.PutAsJsonAsync($"/api/platform/interpreter/costs/{tenantId}/limits", new { softLimitUsd = (decimal?)null, hardLimitUsd = (decimal?)null }));
    }

    [Fact]
    public async Task The_active_prompt_is_the_one_the_runtime_sends_and_only_one_is_active_per_type()
    {
        var (admin, _, tenantId) = await ReadyAsync();
        var a = await Json(await admin.PostAsJsonAsync("/api/platform/interpreter/prompts",
            new { promptType = "extraction", version = "9.1.0", content = "Instrução de teste A: extraia o valor.", description = "teste" }));
        var b = await Json(await admin.PostAsJsonAsync("/api/platform/interpreter/prompts",
            new { promptType = "extraction", version = "9.2.0", content = "Instrução de teste B: extraia a data.", description = "teste" }));
        a.GetProperty("isActive").GetBoolean().Should().BeFalse();
        (await admin.PostAsJsonAsync("/api/platform/interpreter/prompts",
            new { promptType = "extraction", version = "9.1.0", content = "dup", description = "" })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var both = await Task.WhenAll(
            admin.PostAsync($"/api/platform/interpreter/prompts/{a.GetProperty("id").GetGuid()}/activate", null),
            admin.PostAsync($"/api/platform/interpreter/prompts/{b.GetProperty("id").GetGuid()}/activate", null));
        both.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Conflict, "never a 500");

        using (var scope = _factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<NexoDbContext>().StoredPromptVersions
                .CountAsync(p => p.PromptType == "extraction" && p.IsActive)).Should().Be(1);

        await Json(await admin.PostAsync($"/api/platform/interpreter/prompts/{b.GetProperty("id").GetGuid()}/activate", null));
        var run = await Json(await Playground(admin, "Claude", tenantId));
        run.GetProperty("prompt").GetProperty("version").GetString().Should().Be("9.2.0");
        run.GetProperty("prompt").GetProperty("hash").GetString().Should().Be(b.GetProperty("hash").GetString());
        _factory.Anthropic.Requests.Last().Body.Should().Contain("teste B: extraia a data");
    }

    [Fact]
    public async Task A_movement_cannot_be_confirmed_with_a_reference_of_another_company()
    {
        var admin = await PlatformAuthorizationTests.PlatformClientAsync(_factory);
        var other = await Json(await admin.PostAsJsonAsync("/api/platform/tenants", new
        {
            companyName = "Outra Empresa Ltda", tradeName = "Outra", taxId = $"{Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999)}",
            email = $"outra-{Guid.NewGuid():N}"[..20] + "@nexo.test", phone = (string?)null, businessType = "varejo",
            adminName = "Diretora Outra", adminLogin = $"outra{Guid.NewGuid():N}"[..16], adminEmail = $"dir-{Guid.NewGuid():N}"[..18] + "@nexo.test",
            adminPassword = "Ox!" + Guid.NewGuid().ToString("N")[..16], modules = Array.Empty<string>(),
        }), HttpStatusCode.Created);
        var otherTenant = other.GetProperty("id").GetGuid();
        Guid foreignAccount;
        using (var scope = _factory.Services.CreateScope())
            foreignAccount = await scope.ServiceProvider.GetRequiredService<NexoDbContext>().FinancialAccounts.IgnoreQueryFilters()
                .Where(a => a.TenantId == otherTenant).Select(a => a.Id).FirstAsync();

        var tenant = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var analyzed = await Json(await tenant.PostAsJsonAsync("/api/v1/interpreter/analyze", new { text = "Paguei R$ 80,00 de material", inputSource = "Text" }));
        var confirm = await tenant.PostAsJsonAsync($"/api/v1/movements/{analyzed.GetProperty("draftId").GetGuid()}/confirm", new
        {
            amount = 80m, date = "2026-10-01", description = "Material", direction = "Out", nature = "Expense", categoryId = (Guid?)null,
            contextType = "Loja", contextId = (Guid?)null, accountId = foreignAccount, originalSuggestionId = analyzed.GetProperty("suggestionId").GetGuid(),
        });
        // The interpreter endpoints answer domain refusals as 400; the account of another company is simply "not here".
        (await Json(confirm, HttpStatusCode.BadRequest)).GetProperty("error").GetString().Should().Contain("não existe nesta empresa");
    }
}
