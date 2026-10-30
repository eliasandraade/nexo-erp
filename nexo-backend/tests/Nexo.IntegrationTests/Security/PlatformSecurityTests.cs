using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PlatformIsolatedCollection
{
    public const string Name = "PlatformIsolated";
}

/// <summary>
/// The platform administrator as a privileged surface, on a real PostgreSQL (own database: platform users, sessions and
/// impersonations are platform-wide state): every platform route protected, platform tokens confined to the platform API,
/// revocable sessions with rotating refresh, two-step verification with replay protection, and impersonations that name
/// who really acted and can never become a normal session.
/// </summary>
[Collection(PlatformIsolatedCollection.Name)]
public sealed class PlatformSecurityTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public PlatformSecurityTests(TestWebApplicationFactory factory) => _factory = factory;

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(expected, body);
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static HttpClient Bearer(HttpClient c, string token)
    {
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    /// <summary>A fresh platform account (so two-step verification and sessions start clean).</summary>
    private async Task<(string Email, string Password)> NewPlatformUserAsync(string role = "super_admin")
    {
        var email = $"ops-{Guid.NewGuid():N}"[..14] + "@platform.test";
        var password = "Px!" + Guid.NewGuid().ToString("N")[..20];   // generated per run: no credential in the code
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        db.PlatformUsers.Add(PlatformUser.Create(email, scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(password), role));
        await db.SaveChangesAsync();
        return (email, password);
    }

    private static Task<HttpResponseMessage> Login(HttpClient c, string email, string password)
        => c.PostAsJsonAsync("/api/platform/auth/login", new { email, password });

    [Fact]
    public void Every_platform_route_requires_a_platform_policy_or_is_a_deliberate_anonymous_sign_in_step()
    {
        var anonymousByDesign = new HashSet<string> { "PlatformAuth.Login", "PlatformAuth.VerifyMfa", "PlatformAuth.Refresh" };
        var actions = _factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(d => d.AttributeRouteInfo?.Template?.StartsWith("api/platform", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        actions.Should().NotBeEmpty();

        var unprotected = new List<string>();
        foreach (var d in actions)
        {
            var name = $"{d.ControllerName}.{d.ActionName}";
            var attributes = d.ControllerTypeInfo.GetCustomAttributes(true).Concat(d.MethodInfo.GetCustomAttributes(true)).ToList();
            if (attributes.OfType<AllowAnonymousAttribute>().Any())
            {
                if (!anonymousByDesign.Contains(name)) unprotected.Add(name + " (anonymous)");
                continue;
            }
            var policies = attributes.OfType<AuthorizeAttribute>().Select(a => a.Policy).ToList();
            if (!policies.Contains("Platform") && !policies.Contains("PlatformAdmin")) unprotected.Add(name);
        }
        unprotected.Should().BeEmpty("a new platform endpoint must declare the Platform (or PlatformAdmin) policy");
    }

    [Fact]
    public async Task A_tenant_token_never_reaches_the_platform_api_and_a_platform_token_never_reaches_a_tenant_endpoint()
    {
        var tenant = await AuthClientFactory.LoginAsAdminAsync(_factory);
        foreach (var path in new[] { "/api/platform/tenants", "/api/platform/stats", "/api/platform/audit", "/api/platform/interpreter/providers", "/api/platform/auth/security" })
            (await tenant.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden, path);

        var (email, password) = await NewPlatformUserAsync();
        var login = await Json(await Login(_factory.CreateApiClient(), email, password));
        var platform = Bearer(_factory.CreateApiClient(), login.GetProperty("accessToken").GetString()!);
        (await Json(await platform.GetAsync("/api/products"), HttpStatusCode.Forbidden)).GetProperty("code").GetString().Should().Be("platform_token_scope");
        (await platform.GetAsync("/api/platform/stats")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sessions_rotate_their_refresh_token_end_on_logout_and_on_revoke_all_at_once()
    {
        var (email, password) = await NewPlatformUserAsync("support");
        var login = await Json(await Login(_factory.CreateApiClient(), email, password));
        var refresh = login.GetProperty("refreshToken").GetString()!;
        refresh.Should().StartWith("p.");

        var rotated = await Json(await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh }));
        var next = rotated.GetProperty("refreshToken").GetString()!;
        next.Should().NotBe(refresh);
        (await Json(await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh }), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().Should().Be("refresh_already_rotated", "another tab's race is not a theft");

        var platform = Bearer(_factory.CreateApiClient(), rotated.GetProperty("accessToken").GetString()!);
        var security = await Json(await platform.GetAsync("/api/platform/auth/security"));
        security.GetProperty("sessions").EnumerateArray().Should().Contain(s => s.GetProperty("current").GetBoolean());

        (await platform.PostAsync("/api/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Json(await platform.GetAsync("/api/platform/stats"), HttpStatusCode.Unauthorized)).GetProperty("code").GetString().Should().Be("platform_session_ended");
        (await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = next })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var a = Bearer(_factory.CreateApiClient(), (await Json(await Login(_factory.CreateApiClient(), email, password))).GetProperty("accessToken").GetString()!);
        var b = Bearer(_factory.CreateApiClient(), (await Json(await Login(_factory.CreateApiClient(), email, password))).GetProperty("accessToken").GetString()!);
        (await a.PostAsync("/api/platform/auth/sessions/revoke-all", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await b.GetAsync("/api/platform/stats")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "revoke-all ends every session at once");

        using var scope = _factory.Services.CreateScope();
        var actions = await scope.ServiceProvider.GetRequiredService<NexoDbContext>().AuditRecords.IgnoreQueryFilters()
            .Where(r => r.ActorName == email).Select(r => r.ActionType).ToListAsync();
        actions.Should().Contain(["platform_logged_in", "platform_logged_out", "platform_session_revoked"]);
    }

    [Fact]
    public async Task Admin_acts_need_two_step_verification_with_codes_that_work_once()
    {
        var host = _factory.WithWebHostBuilder(b => b.UseSetting("Platform:RequireMfaForAdmin", "true"));
        HttpClient Client() => host.CreateClient();
        var (email, password) = await NewPlatformUserAsync();

        var first = await Json(await Login(Client(), email, password));
        var session = Bearer(Client(), first.GetProperty("accessToken").GetString()!);
        (await session.PatchAsJsonAsync($"/api/platform/interpreter/providers/{Guid.NewGuid()}", new { isEnabled = true }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "a password alone does not open admin acts");

        var setup = await Json(await session.PostAsJsonAsync("/api/platform/auth/mfa/setup", new { password }));
        var secret = FromBase32(setup.GetProperty("secret").GetString()!);
        var step = Totp.StepAt(DateTime.UtcNow);
        var enabled = await Json(await session.PostAsJsonAsync("/api/platform/auth/mfa/enable", new { code = Totp.Code(secret, step) }));
        var recovery = enabled.GetProperty("recoveryCodes").EnumerateArray().Select(x => x.GetString()!).ToList();
        recovery.Should().HaveCount(8);
        (await session.GetAsync("/api/platform/stats")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "turning it on ends every session");

        // Through the shared login form, as the app does.
        var challenge = await Json(await Client().PostAsJsonAsync("/api/auth/login", new { login = email, password }));
        challenge.GetProperty("mfaRequired").GetBoolean().Should().BeTrue();
        var code = Totp.Code(secret, step + 1);
        var verified = await Json(await Client().PostAsJsonAsync("/api/auth/mfa/verify", new { challenge = challenge.GetProperty("mfaChallenge").GetString(), code }));
        var admin = Bearer(Client(), verified.GetProperty("accessToken").GetString()!);
        (await admin.GetAsync("/api/platform/interpreter/providers")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PatchAsJsonAsync($"/api/platform/interpreter/providers/{Guid.NewGuid()}", new { isEnabled = true }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "past the policy: the provider simply doesn't exist");

        // The same code again: refused. A recovery code: works once.
        var again = await Json(await Client().PostAsJsonAsync("/api/auth/login", new { login = email, password }));
        (await Client().PostAsJsonAsync("/api/auth/mfa/verify", new { challenge = again.GetProperty("mfaChallenge").GetString(), code }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var third = await Json(await Login(Client(), email, password));
        (await Client().PostAsJsonAsync("/api/platform/auth/mfa/verify", new { mfaChallenge = third.GetProperty("mfaChallenge").GetString(), recoveryCode = recovery[0] }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var fourth = await Json(await Login(Client(), email, password));
        (await Client().PostAsJsonAsync("/api/platform/auth/mfa/verify", new { mfaChallenge = fourth.GetProperty("mfaChallenge").GetString(), recoveryCode = recovery[0] }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a recovery code works once");
    }

    [Fact]
    public async Task An_impersonation_needs_a_reason_names_who_acted_never_refreshes_and_ends_at_once()
    {
        var (email, password) = await NewPlatformUserAsync();
        var platform = Bearer(_factory.CreateApiClient(), (await Json(await Login(_factory.CreateApiClient(), email, password))).GetProperty("accessToken").GetString()!);
        var tenantAdmin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var me = await Json(await tenantAdmin.GetAsync("/api/auth/me"));
        var tenantId = Guid.Parse(me.GetProperty("tenantId").GetString()!);

        (await Json(await platform.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/impersonate", new { reason = "" }), HttpStatusCode.UnprocessableEntity))
            .GetProperty("code").GetString().Should().Be("impersonation_reason_required");

        var (supportEmail, supportPassword) = await NewPlatformUserAsync("support");
        var support = Bearer(_factory.CreateApiClient(), (await Json(await Login(_factory.CreateApiClient(), supportEmail, supportPassword))).GetProperty("accessToken").GetString()!);
        (await support.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/impersonate", new { reason = "Suporte: verificar cadastro" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "support reads; only a super administrator enters a tenant");

        var granted = await Json(await platform.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/impersonate", new { reason = "Suporte: verificar cadastro de produto" }));
        granted.GetProperty("refreshToken").ValueKind.Should().Be(JsonValueKind.Null, "an impersonation never becomes a normal session");
        var imp = Bearer(_factory.CreateApiClient(), granted.GetProperty("accessToken").GetString()!);
        (await imp.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(await imp.PostAsJsonAsync("/api/auth/switch-store", new { storeId = Guid.NewGuid() }), HttpStatusCode.Forbidden))
            .GetProperty("code").GetString().Should().Be("impersonation_forbidden");
        (await imp.PostAsJsonAsync("/api/auth/sessions/revoke-all", new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Something the impersonated session does is audited with the administrator behind it.
        (await imp.PostAsJsonAsync("/api/auth/impersonation/end", new { })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Json(await imp.GetAsync("/api/auth/me"), HttpStatusCode.Unauthorized)).GetProperty("code").GetString().Should().Be("impersonation_ended");

        using var scope = _factory.Services.CreateScope();
        var records = await scope.ServiceProvider.GetRequiredService<NexoDbContext>().AuditRecords.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && (r.ActionType == "platform_impersonation" || r.ActionType == "platform_impersonation_ended"))
            .OrderByDescending(r => r.CreatedAt).Take(2).ToListAsync();
        records.Select(r => r.ActionType).Should().Contain(["platform_impersonation", "platform_impersonation_ended"]);
        records.Single(r => r.ActionType == "platform_impersonation_ended").ImpersonatorEmail.Should().Be(email,
            "every record written during the impersonation names the administrator who really acted");
        records.Single(r => r.ActionType == "platform_impersonation").Description.Should().Contain("verificar cadastro de produto");
    }

    [Fact]
    public async Task Turning_two_step_off_resetting_it_and_revoking_one_session_are_explicit_and_audited()
    {
        var (email, password) = await NewPlatformUserAsync();
        var first = Bearer(_factory.CreateApiClient(), (await Json(await Login(_factory.CreateApiClient(), email, password))).GetProperty("accessToken").GetString()!);
        var second = Bearer(_factory.CreateApiClient(), (await Json(await Login(_factory.CreateApiClient(), email, password))).GetProperty("accessToken").GetString()!);

        var sessions = (await Json(await first.GetAsync("/api/platform/auth/security"))).GetProperty("sessions").EnumerateArray().ToList();
        var other = sessions.Single(s => !s.GetProperty("current").GetBoolean()).GetProperty("id").GetGuid();
        (await first.DeleteAsync($"/api/platform/auth/sessions/{other}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await second.GetAsync("/api/platform/stats")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "that session ended");
        (await first.DeleteAsync($"/api/platform/auth/sessions/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Wrong password on setup; then on, off (with a recovery code).
        (await Json(await first.PostAsJsonAsync("/api/platform/auth/mfa/setup", new { password = "wrong" }), HttpStatusCode.UnprocessableEntity))
            .GetProperty("code").GetString().Should().Be("invalid_password");
        (await Json(await first.PostAsJsonAsync("/api/platform/auth/mfa/enable", new { code = "123456" }), HttpStatusCode.UnprocessableEntity))
            .GetProperty("code").GetString().Should().Be("mfa_setup_missing");
        var secret = FromBase32((await Json(await first.PostAsJsonAsync("/api/platform/auth/mfa/setup", new { password }))).GetProperty("secret").GetString()!);
        var step = Totp.StepAt(DateTime.UtcNow);
        var codes = (await Json(await first.PostAsJsonAsync("/api/platform/auth/mfa/enable", new { code = Totp.Code(secret, step) })))
            .GetProperty("recoveryCodes").EnumerateArray().Select(x => x.GetString()!).ToList();

        var challenge = (await Json(await Login(_factory.CreateApiClient(), email, password))).GetProperty("mfaChallenge").GetString();
        var verified = await Json(await _factory.CreateApiClient().PostAsJsonAsync("/api/platform/auth/mfa/verify", new { mfaChallenge = challenge, code = Totp.Code(secret, step + 1) }));
        var signedIn = Bearer(_factory.CreateApiClient(), verified.GetProperty("accessToken").GetString()!);
        var refreshed = await Json(await _factory.CreateApiClient().PostAsJsonAsync("/api/platform/auth/refresh", new { refreshToken = verified.GetProperty("refreshToken").GetString() }));
        refreshed.GetProperty("refreshToken").GetString().Should().StartWith("p.");
        (await Json(await signedIn.PostAsJsonAsync("/api/platform/auth/mfa/disable", new { password, recoveryCode = "nope-nope" }), HttpStatusCode.UnprocessableEntity))
            .GetProperty("code").GetString().Should().Be("invalid_code");
        (await signedIn.PostAsJsonAsync("/api/platform/auth/mfa/disable", new { password, recoveryCode = codes[1] })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Another super administrator can reset the two-step verification of a colleague, never their own.
        var (adminEmail, adminPassword) = await NewPlatformUserAsync();
        var admin = Bearer(_factory.CreateApiClient(), (await Json(await Login(_factory.CreateApiClient(), adminEmail, adminPassword))).GetProperty("accessToken").GetString()!);
        Guid adminId, targetId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            adminId = await db.PlatformUsers.Where(u => u.Email == adminEmail).Select(u => u.Id).SingleAsync();
            targetId = await db.PlatformUsers.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        }
        (await Json(await admin.PostAsync($"/api/platform/auth/users/{adminId}/mfa/reset", null), HttpStatusCode.UnprocessableEntity))
            .GetProperty("code").GetString().Should().Be("mfa_self_reset");
        (await admin.PostAsync($"/api/platform/auth/users/{targetId}/mfa/reset", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var check = _factory.Services.CreateScope();
        var actions = await check.ServiceProvider.GetRequiredService<NexoDbContext>().AuditRecords.IgnoreQueryFilters()
            .Where(r => r.EntityId == targetId.ToString()).Select(r => r.ActionType).ToListAsync();
        actions.Should().Contain(["platform_mfa_enabled", "platform_mfa_disabled", "platform_session_revoked"]);
    }

    [Fact]
    public async Task Platform_admin_screens_answer_the_truth_and_refuse_invalid_input_without_a_500()
    {
        var (email, password) = await NewPlatformUserAsync();
        var admin = Bearer(_factory.CreateApiClient(), (await Json(await Login(_factory.CreateApiClient(), email, password))).GetProperty("accessToken").GetString()!);

        var health = await Json(await admin.GetAsync("/api/platform/health"));
        health.GetProperty("checks").EnumerateArray().Select(c => c.GetProperty("name").GetString()).Should().Equal("database", "cache");

        var endpoints = await Json(await admin.GetAsync("/api/platform/system/endpoints"));
        endpoints.EnumerateArray().Select(e => e.GetProperty("path").GetString()).Should().OnlyContain(p => !p!.StartsWith("/api/api"));
        endpoints.EnumerateArray().Should().Contain(e => e.GetProperty("hasParameters").GetBoolean());

        var tenantAdmin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var tenantId = Guid.Parse((await Json(await tenantAdmin.GetAsync("/api/auth/me"))).GetProperty("tenantId").GetString()!);
        (await admin.PutAsJsonAsync($"/api/platform/tenants/{tenantId}/status", new { status = "99" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var note = await Json(await admin.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/notes", new { content = "Nota de teste da plataforma", isPinned = false }), HttpStatusCode.Created);
        var noteId = note.GetProperty("id").GetGuid();
        (await admin.PatchAsync($"/api/platform/tenants/{tenantId}/notes/{noteId}/pin", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.DeleteAsync($"/api/platform/tenants/{tenantId}/notes/{noteId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = await Json(await admin.GetAsync($"/api/platform/tenants/{tenantId}"));
        var taken = await admin.PostAsJsonAsync("/api/platform/tenants", new
        {
            companyName = "Duplicada", tradeName = (string?)null, taxId = detail.GetProperty("taxId").GetString(),
            email = $"dup-{Guid.NewGuid():N}"[..18] + "@nexo.test", phone = (string?)null, businessType = "varejo", modules = Array.Empty<string>(),
            adminName = "Dup", adminLogin = $"dup{Guid.NewGuid():N}"[..14], adminPassword = "Dx!" + Guid.NewGuid().ToString("N")[..16], adminEmail = (string?)null,
        });
        (await Json(taken, HttpStatusCode.Conflict)).GetProperty("code").GetString().Should().Be("tenant_tax_id_taken");

        using var scope = _factory.Services.CreateScope();
        var actions = await scope.ServiceProvider.GetRequiredService<NexoDbContext>().AuditRecords.IgnoreQueryFilters()
            .Where(r => r.EntityId == noteId.ToString()).Select(r => r.ActionType).ToListAsync();
        actions.Should().Contain(["platform_note_created", "platform_note_pinned", "platform_note_deleted"]);
    }

    private static byte[] FromBase32(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var value = 0;
        var output = new List<byte>();
        foreach (var c in base32.TrimEnd('=').ToUpperInvariant())
        {
            value = (value << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((value >> (bits - 8)) & 0xff));
                bits -= 8;
            }
        }
        return output.ToArray();
    }
}
