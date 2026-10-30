using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Nexo.IntegrationTests.Security;

namespace Nexo.IntegrationTests.Interpreter;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AiIsolatedCollection
{
    public const string Name = "AiIsolated";
}

/// <summary>
/// The SuperAdmin's provider configuration drives the runtime, end to end on a real PostgreSQL (own database: these
/// tests change platform-wide provider state). Claude calls go to the in-process fake Anthropic — never the real one.
/// </summary>
[Collection(AiIsolatedCollection.Name)]
public sealed class AiProviderRuntimeTests : IClassFixture<TestWebApplicationFactory>, IAsyncLifetime
{
    private const string Key = "sk-ant-api03-integration-test-fake-key-9876";
    private readonly TestWebApplicationFactory _factory;

    public AiProviderRuntimeTests(TestWebApplicationFactory factory) => _factory = factory;

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

    private async Task<(HttpClient Admin, Dictionary<string, Guid> Ids)> AdminAsync()
    {
        var admin = await PlatformAuthorizationTests.PlatformClientAsync(_factory);
        var list = await Json(await admin.GetAsync("/api/platform/interpreter/providers"));
        var ids = list.GetProperty("providers").EnumerateArray().ToDictionary(p => p.GetProperty("provider").GetString()!, p => p.GetProperty("id").GetGuid());
        return (admin, ids);
    }

    private static JsonElement ProviderOf(JsonElement list, string provider)
        => list.GetProperty("providers").EnumerateArray().Single(p => p.GetProperty("provider").GetString() == provider);

    private static Task<HttpResponseMessage> Patch(HttpClient c, Guid id, object body)
        => c.PatchAsJsonAsync($"/api/platform/interpreter/providers/{id}", body);

    private static Task<HttpResponseMessage> Playground(HttpClient c, string? force, Guid? tenantId = null)
        => c.PostAsJsonAsync("/api/platform/interpreter/playground", new { text = "Abasteci R$ 87,90 no Posto Avenida em 02/10/2026 no cartão", forceProvider = force, tenantId });

    /// <summary>Claude enabled, with key and model, RuleBased as the default.</summary>
    private async Task<(HttpClient Admin, Dictionary<string, Guid> Ids)> ClaudeReadyAsync(string model = "claude-haiku-5-5")
    {
        var (admin, ids) = await AdminAsync();
        await Json(await admin.PutAsJsonAsync($"/api/platform/interpreter/providers/{ids["Claude"]}/api-key", new { apiKey = Key }));
        await Json(await Patch(admin, ids["Claude"], new { isEnabled = true, modelId = model }));
        await Json(await Patch(admin, ids["RuleBased"], new { isEnabled = true, isDefault = true }));
        return (admin, ids);
    }

    [Fact]
    public async Task A_tenant_token_never_reaches_the_ai_admin_and_support_only_reads()
    {
        var (_, ids) = await AdminAsync();
        var tenant = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await tenant.GetAsync("/api/platform/interpreter/providers")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Patch(tenant, ids["Claude"], new { isEnabled = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Playground(tenant, "RuleBased")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var support = await SupportClientAsync();
        (await support.GetAsync("/api/platform/interpreter/providers")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Patch(support, ids["Claude"], new { isEnabled = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await support.PutAsJsonAsync($"/api/platform/interpreter/providers/{ids["Claude"]}/api-key", new { apiKey = Key })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await support.PostAsync($"/api/platform/interpreter/providers/{ids["Claude"]}/test", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Playground(support, "RuleBased")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_key_is_encrypted_at_rest_and_never_comes_back_through_the_api()
    {
        var (admin, ids) = await AdminAsync();
        var saved = await Json(await admin.PutAsJsonAsync($"/api/platform/interpreter/providers/{ids["Claude"]}/api-key", new { apiKey = Key }));

        saved.GetProperty("hasApiKey").GetBoolean().Should().BeTrue();
        saved.GetProperty("apiKeyLastFour").GetString().Should().Be("9876");
        var listed = await (await admin.GetAsync("/api/platform/interpreter/providers")).Content.ReadAsStringAsync();
        listed.Should().NotContain(Key).And.NotContain("s1.", "neither the key nor its envelope is ever returned");

        using var scope = _factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<NexoDbContext>().AiProviders.AsNoTracking()
            .Where(p => p.Id == ids["Claude"]).Select(p => p.ApiKeyEncrypted).SingleAsync();
        stored.Should().StartWith("s1.").And.NotContain(Key).And.NotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(Key)));
        (await Json(await admin.PutAsJsonAsync($"/api/platform/interpreter/providers/{ids["Claude"]}/api-key", new { apiKey = "short" }),
            HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString().Should().Be("ai_key_invalid");
    }

    [Fact]
    public async Task Enabling_disabling_and_changing_the_model_in_the_superadmin_change_the_runtime()
    {
        var (admin, ids) = await AdminAsync();
        await Json(await Patch(admin, ids["Claude"], new { isEnabled = false }));
        var disabled = await Json(await Playground(admin, "Claude"), HttpStatusCode.UnprocessableEntity);
        disabled.GetProperty("code").GetString().Should().Be("ai_provider_unavailable");
        _factory.Anthropic.Requests.Should().BeEmpty("a disabled provider is never called");

        await ClaudeReadyAsync("claude-haiku-5-5");
        var run = await Json(await Playground(admin, "Claude"));
        run.GetProperty("provider").GetString().Should().Be("Claude");
        run.GetProperty("modelId").GetString().Should().Be("claude-haiku-5-5");
        run.GetProperty("extraction").GetProperty("amount").GetProperty("value").GetDecimal().Should().Be(87.9m);
        run.GetProperty("extraction").GetProperty("inputTokens").GetInt32().Should().Be(150);
        run.GetProperty("extraction").GetProperty("costUsd").GetDouble().Should().BeGreaterThan(0);
        var sent = _factory.Anthropic.Requests.Last();
        sent.Model.Should().Be("claude-haiku-5-5");
        sent.Authorization.Should().Be(Key, "the runtime uses exactly the key stored in the SuperAdmin");
        sent.Host.Should().Be("api.anthropic.com");

        await Json(await Patch(admin, ids["Claude"], new { modelId = "claude-sonnet-5-5" }));
        await Json(await Playground(admin, "Claude"));
        _factory.Anthropic.Requests.Last().Model.Should().Be("claude-sonnet-5-5", "changing the model in the SuperAdmin changes the call");

        await Json(await Patch(admin, ids["Claude"], new { isEnabled = false }));
        (await Json(await Playground(admin, "Claude"), HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString().Should().Be("ai_provider_unavailable");

        using var scope = _factory.Services.CreateScope();
        var telemetry = await scope.ServiceProvider.GetRequiredService<NexoDbContext>().InterpreterTelemetry.AsNoTracking()
            .Where(t => t.OperationType == "Playground" && t.Provider == "Claude").ToListAsync();
        telemetry.Should().Contain(t => t.ModelId == "claude-sonnet-5-5" && t.InputTokens == 150 && t.EstimatedCostMicros > 0 && t.Success);
    }

    [Fact]
    public async Task Playground_answers_every_expected_state_without_a_500()
    {
        var (admin, ids) = await ClaudeReadyAsync();

        (await Json(await Playground(admin, "RuleBased"))).GetProperty("provider").GetString().Should().Be("RuleBased");
        (await Json(await Playground(admin, "auto"))).GetProperty("provider").GetString().Should().Be("RuleBased", "the default runs first in automatic mode");
        (await Json(await Playground(admin, "OpenAI"), HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString().Should().Be("ai_provider_unavailable");
        (await Json(await Playground(admin, "Gemini"), HttpStatusCode.BadRequest)).GetProperty("code").GetString().Should().Be("ai_provider_invalid");
        (await Playground(admin, "RuleBased", Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await Json(await admin.DeleteAsync($"/api/platform/interpreter/providers/{ids["Claude"]}/api-key"));
        (await Json(await Playground(admin, "Claude"), HttpStatusCode.UnprocessableEntity)).GetProperty("error").GetString().Should().Contain("chave");

        var list = await Json(await admin.GetAsync("/api/platform/interpreter/providers"));
        ProviderOf(list, "Claude").GetProperty("status").GetString().Should().Be("missing_key");
        ProviderOf(list, "OpenAI").GetProperty("status").GetString().Should().Be("implementation_missing");
        ProviderOf(list, "RuleBased").GetProperty("status").GetString().Should().Be("available");
    }

    [Fact]
    public async Task A_forced_claude_failure_is_sanitized_and_automatic_mode_falls_back()
    {
        var (admin, ids) = await ClaudeReadyAsync();
        _factory.Anthropic.Respond = _ => FakeAnthropicHandler.Error(HttpStatusCode.Unauthorized, "authentication_error");

        var forced = await Json(await Playground(admin, "Claude"), HttpStatusCode.ServiceUnavailable);
        forced.GetProperty("code").GetString().Should().Be("ai_provider_failed");
        forced.GetProperty("details")[0].GetString().Should().Be("invalid_credentials");
        forced.ToString().Should().NotContain("anthropic-internal-detail").And.NotContain(Key);

        // Claude as the default: it fails, the rule-based reader takes over and the answer says so.
        _factory.Anthropic.Respond = _ => FakeAnthropicHandler.Error(HttpStatusCode.InternalServerError, "api_error");
        await Json(await Patch(admin, ids["Claude"], new { isDefault = true }));
        var auto = await Json(await Playground(admin, null));
        auto.GetProperty("provider").GetString().Should().Be("RuleBased");
        auto.GetProperty("fallbackUsed").GetBoolean().Should().BeTrue();
        auto.GetProperty("attempts")[0].GetProperty("failureReason").GetString().Should().Be("provider_error");
        await Json(await Patch(admin, ids["RuleBased"], new { isDefault = true }));
    }

    [Fact]
    public async Task Testing_the_connection_records_a_sanitized_result_and_a_rejected_key_blocks_the_provider()
    {
        var (admin, ids) = await ClaudeReadyAsync();

        var ok = await Json(await admin.PostAsync($"/api/platform/interpreter/providers/{ids["Claude"]}/test", null));
        ok.GetProperty("status").GetString().Should().Be("ok");
        ok.GetProperty("modelId").GetString().Should().Be("claude-haiku-5-5");

        _factory.Anthropic.Respond = _ => FakeAnthropicHandler.Error(HttpStatusCode.Unauthorized, "authentication_error");
        var rejected = await Json(await admin.PostAsync($"/api/platform/interpreter/providers/{ids["Claude"]}/test", null));
        rejected.GetProperty("status").GetString().Should().Be("invalid_credentials");
        rejected.ToString().Should().NotContain("anthropic-internal-detail");
        var list = await Json(await admin.GetAsync("/api/platform/interpreter/providers"));
        ProviderOf(list, "Claude").GetProperty("status").GetString().Should().Be("invalid_credentials");
        list.GetProperty("automaticOrder").EnumerateArray().Select(x => x.GetString()).Should().NotContain("Claude");

        // A new key clears the rejection.
        _factory.Anthropic.Reset();
        await Json(await admin.PutAsJsonAsync($"/api/platform/interpreter/providers/{ids["Claude"]}/api-key", new { apiKey = Key }));
        ProviderOf(await Json(await admin.GetAsync("/api/platform/interpreter/providers")), "Claude").GetProperty("status").GetString().Should().Be("available");

        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<NexoDbContext>().AuditRecords.IgnoreQueryFilters()
            .CountAsync(a => a.ActionType == "ai_provider_tested")).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task A_key_stored_in_the_legacy_base64_form_is_re_sealed_without_being_typed_again()
    {
        var (admin, ids) = await AdminAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE nexo.ai_providers SET \"ApiKeyEncrypted\" = {Convert.ToBase64String(Encoding.UTF8.GetBytes(Key))}, \"ApiKeyLastFour\" = '9876' WHERE \"Id\" = {ids["Claude"]}");
            ProviderOf(await Json(await admin.GetAsync("/api/platform/interpreter/providers")), "Claude").GetProperty("status").GetString()
                .Should().BeOneOf("secret_unavailable", "disabled", "missing_model");

            var changed = await AiProviderRegistry.UpgradeStoredKeysAsync(db, scope.ServiceProvider.GetRequiredService<ISecretProtector>(), NullLogger.Instance, CancellationToken.None);
            changed.Should().BeGreaterThan(0);
        }
        using (var scope = _factory.Services.CreateScope())
        {
            var secrets = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
            var stored = await scope.ServiceProvider.GetRequiredService<NexoDbContext>().AiProviders.AsNoTracking()
                .Where(p => p.Id == ids["Claude"]).Select(p => p.ApiKeyEncrypted!).SingleAsync();
            stored.Should().StartWith("s1.").And.NotContain(Key);
            secrets.Unprotect(stored, AiProviderRegistry.KeyPurpose(ids["Claude"])).Should().Be(Key, "the same key, now really encrypted");
        }
    }

    private async Task<HttpClient> SupportClientAsync()
    {
        var email = $"support-ai-{Guid.NewGuid():N}"[..20] + "@nexo.test";
        var password = "Sx!" + Guid.NewGuid().ToString("N")[..20];   // generated per run: no credential in the code
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            if (!await db.PlatformUsers.AnyAsync(u => u.Email == email))
            {
                db.PlatformUsers.Add(PlatformUser.Create(email, scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(password), "support"));
                await db.SaveChangesAsync();
            }
        }
        var client = _factory.CreateApiClient();
        var body = await Json(await client.PostAsJsonAsync("/api/platform/auth/login", new { email, password }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return client;
    }
}
