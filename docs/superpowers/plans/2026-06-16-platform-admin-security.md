# Platform Admin Security Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline) or superpowers:subagent-driven-development to implement task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Harden the platform super-admin panel: provision the super-admin in production, rate-limit platform login, audit privileged actions (transactional, secret-safe), and enforce one authorization policy.

**Architecture:** Backend only (.NET 8 / ASP.NET Core). Reuse existing infrastructure — `DataSeeder` (bootstrap), the partitioned `auth-login` rate-limit policy, `IAuditWriter` (transactional audit), ASP.NET authorization policies. No frontend/env/Redis/Stripe changes.

**Tech Stack:** ASP.NET Core, EF Core (Npgsql), xUnit + FluentAssertions + Testcontainers PostgreSQL.

Spec: `docs/superpowers/specs/2026-06-16-platform-admin-security-design.md`.

---

## File structure

- Modify `src/Nexo.Api/Program.cs` — register `"Platform"` authz policy; call `SeedPlatformAdminAsync` for all non-Testing envs.
- Modify `src/Nexo.Infrastructure/Persistence/Seed/DataSeeder.cs` — expose public `SeedPlatformAdminAsync`.
- Modify `src/Nexo.Api/Controllers/PlatformController.cs` — `[Authorize(Policy="Platform")]`, remove manual checks, inject `IAuditWriter`, stage audit + ensure SaveChanges (Impersonate).
- Modify `src/Nexo.Api/Controllers/PlatformFlagsController.cs` + `InterpreterAdminController.cs` — `[Authorize(Policy="Platform")]`, remove manual checks.
- Modify `src/Nexo.Api/Controllers/PlatformAuthController.cs` — `[EnableRateLimiting("auth-login")]` on Login.
- Modify `src/Nexo.Application/Common/Interfaces/IAuditWriter.cs` — add `AuditActions` constants.
- Tests: `tests/Nexo.IntegrationTests/Security/PlatformAuthorizationTests.cs` (new), `Security/PlatformAuditTests.cs` (new), `Auth/RateLimitingTests.cs` (add platform-login case), `Auth/PlatformBootstrapTests.cs` (new).

---

### Task 1: Platform authorization policy

**Files:**
- Modify: `src/Nexo.Api/Program.cs` (around line 103 `AddAuthorization()`)
- Modify: `src/Nexo.Api/Controllers/PlatformController.cs`, `PlatformFlagsController.cs`, `InterpreterAdminController.cs`
- Test: `tests/Nexo.IntegrationTests/Security/PlatformAuthorizationTests.cs`

- [ ] **Step 1: Write failing test** — `PlatformAuthorizationTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

[Collection("Integration")]
public class PlatformAuthorizationTests
{
    private readonly TestWebApplicationFactory _factory;
    public PlatformAuthorizationTests(TestWebApplicationFactory factory) => _factory = factory;

    // One representative GET per platform-only controller.
    public static IEnumerable<object[]> PlatformEndpoints() => new[]
    {
        new object[] { "/api/platform/stats" },            // PlatformController
        new object[] { "/api/platform/flags" },            // PlatformFlagsController
        new object[] { "/api/platform/interpreter/stats" },// InterpreterAdminController (adjust to a real GET)
    };

    [Theory]
    [MemberData(nameof(PlatformEndpoints))]
    public async Task NoToken_Returns401(string path)
    {
        var client = _factory.CreateApiClient();
        (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(PlatformEndpoints))]
    public async Task TenantToken_Returns403(string path)
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory); // tenant (diretoria) token
        (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(PlatformEndpoints))]
    public async Task PlatformToken_Returns200(string path)
    {
        var client = await PlatformClient();
        (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpClient> PlatformClient()
    {
        var client = _factory.CreateApiClient();
        var resp = await client.PostAsJsonAsync("/api/platform/auth/login", new
        {
            email = "platform-test@nexo.test",
            password = "FakePlatformPass!999",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<PlatformLoginResponse>();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return client;
    }

    private record PlatformLoginResponse(string AccessToken);
}
```

- [ ] **Step 2: Verify the chosen GET paths exist.** Confirm `InterpreterAdminController` has a parameterless GET; if `/interpreter/stats` doesn't exist, replace with a real one (e.g. the controller's list endpoint). Run:
  `dotnet test tests/Nexo.IntegrationTests --filter FullyQualifiedName~PlatformAuthorizationTests` → expect compile/pass-or-fail; fix paths until the test compiles and the platform-token case is the only behavioral gap.

- [ ] **Step 3: Register the policy** — `Program.cs`, replace `builder.Services.AddAuthorization();` with:

```csharp
builder.Services.AddAuthorization(options =>
{
    // Platform super-admin gate: must be authenticated AND carry the platform token claim.
    options.AddPolicy("Platform", policy =>
        policy.RequireAuthenticatedUser().RequireClaim("type", "platform"));
});
```

- [ ] **Step 4: Apply policy + remove manual checks** in all three controllers:
  - Change the controller attribute `[Authorize]` → `[Authorize(Policy = "Platform")]`.
  - Delete every `if (!IsPlatformUser()) return Forbid();` line.
  - Delete the now-unused `private bool IsPlatformUser() => ...;` helper. Keep `GetPlatformUserId()` (still used).

- [ ] **Step 5: Run tests** — `dotnet test tests/Nexo.IntegrationTests --filter FullyQualifiedName~PlatformAuthorizationTests` → all PASS (401/403/200 across the three controllers).

- [ ] **Step 6: Commit** — `git add -A && git commit -m "feat(platform): single [Authorize(Policy=Platform)] gate; drop per-action checks"`

---

### Task 2: Rate-limit platform login

**Files:**
- Modify: `src/Nexo.Api/Controllers/PlatformAuthController.cs`
- Test: `tests/Nexo.IntegrationTests/Auth/RateLimitingTests.cs`

- [ ] **Step 1: Write failing test** — add to `RateLimitingTests.cs` (mirrors the existing tenant-login pattern using `WithRateLimitingEnabled`):

```csharp
[Fact]
public async Task PlatformLogin_ExceedsRateLimit_Returns429()
{
    await using var factory = (TestWebApplicationFactory)_factory.WithRateLimitingEnabled(permitLimit: 5, windowSeconds: 900);
    var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    HttpResponseMessage? last = null;
    for (var i = 0; i < 7; i++)
        last = await client.PostAsJsonAsync("/api/platform/auth/login",
            new { email = "nope@nexo.test", password = "wrong" });

    last!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
}

[Fact]
public async Task PlatformLogin_HasSeparateBucketFromTenantLogin()
{
    await using var factory = (TestWebApplicationFactory)_factory.WithRateLimitingEnabled(permitLimit: 5, windowSeconds: 900);
    var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // Exhaust the platform-login bucket
    for (var i = 0; i < 7; i++)
        await client.PostAsJsonAsync("/api/platform/auth/login", new { email = "nope@nexo.test", password = "wrong" });

    // Tenant login (different path → different partition) must NOT be rate-limited yet
    var tenant = await client.PostAsJsonAsync("/api/auth/login",
        new { login = "admin", password = "wrong" });
    tenant.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
}
```

  (Confirm the `WithRateLimitingEnabled` return type cast matches existing tests in that file; reuse their exact factory-creation idiom.)

- [ ] **Step 2: Run to verify FAIL** — `dotnet test tests/Nexo.IntegrationTests --filter FullyQualifiedName~RateLimitingTests` → the two new tests FAIL (platform login currently un-limited → 401 every time, never 429).

- [ ] **Step 3: Add the attribute** — `PlatformAuthController.cs`: add `using Microsoft.AspNetCore.RateLimiting;` and decorate `Login` with `[EnableRateLimiting("auth-login")]`.

- [ ] **Step 4: Run to verify PASS** — same filter → both new tests PASS.

- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat(platform): rate-limit /platform/auth/login (own partition via auth-login)"`

---

### Task 3: Super-admin bootstrap in production

**Files:**
- Modify: `src/Nexo.Infrastructure/Persistence/Seed/DataSeeder.cs`
- Modify: `src/Nexo.Api/Program.cs` (the startup migrate/seed block)
- Test: `tests/Nexo.IntegrationTests/Auth/PlatformBootstrapTests.cs`

- [ ] **Step 1: Write failing test** — `PlatformBootstrapTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Infrastructure.Persistence.Seed;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Auth;

[Collection("Integration")]
public class PlatformBootstrapTests
{
    private readonly TestWebApplicationFactory _factory;
    public PlatformBootstrapTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task SeedPlatformAdmin_IsIdempotent_AndSyncsLogin()
    {
        using var scope = _factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();

        // Calling twice must not throw and must not duplicate.
        await seeder.SeedPlatformAdminAsync();
        await seeder.SeedPlatformAdminAsync();

        // The synced super-admin can log in with the env-configured credentials.
        var client = _factory.CreateApiClient();
        var resp = await client.PostAsJsonAsync("/api/platform/auth/login",
            new { email = "platform-test@nexo.test", password = "FakePlatformPass!999" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

- [ ] **Step 2: Run to verify FAIL** — `dotnet test tests/Nexo.IntegrationTests --filter FullyQualifiedName~PlatformBootstrapTests` → FAIL to compile (`SeedPlatformAdminAsync` not found).

- [ ] **Step 3: Expose the method** — `DataSeeder.cs`, add a public wrapper next to `SeedPlatformUserAsync`:

```csharp
/// <summary>
/// Idempotently provisions/synchronizes the platform super-admin from Seed:Platform* config.
/// Runs in EVERY environment (incl. Production) — unlike the demo SeedAsync(). Never logs the password.
/// </summary>
public Task SeedPlatformAdminAsync(CancellationToken ct = default) => SeedPlatformUserAsync(ct);
```

- [ ] **Step 4: Wire into Program.cs** — in the startup block (currently `if (!IsEnvironment("Testing")) { Migrate; if (!IsProduction()) Seed; }`), call the platform bootstrap for all non-Testing envs:

```csharp
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();

    Log.Information("Applying database migrations...");
    await db.Database.MigrateAsync();

    // Platform super-admin: idempotent, env-driven, runs in EVERY env incl. Production.
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    await seeder.SeedPlatformAdminAsync();

    if (!app.Environment.IsProduction())
        await seeder.SeedAsync();   // demo tenants — dev only
}
```

  (Preserve the existing log lines/structure; only add the `SeedPlatformAdminAsync` call and keep `SeedAsync()` under `!IsProduction()`.)

- [ ] **Step 5: Run to verify PASS** — same filter → PASS.

- [ ] **Step 6: Commit** — `git add -A && git commit -m "fix(platform): provision super-admin in Production (bootstrap decoupled from demo seed)"`

---

### Task 4: Audit privileged platform actions

**Files:**
- Modify: `src/Nexo.Application/Common/Interfaces/IAuditWriter.cs` (constants)
- Modify: `src/Nexo.Api/Controllers/PlatformController.cs`
- Test: `tests/Nexo.IntegrationTests/Security/PlatformAuditTests.cs`

- [ ] **Step 1: Add audit-action constants** — in `IAuditWriter.cs` `AuditActions`:

```csharp
// Platform admin
public const string PlatformImpersonation = "platform_impersonation";
public const string TenantUpdated         = "tenant_updated";
public const string TenantStatusChanged   = "tenant_status_changed";
```

- [ ] **Step 2: Write failing test** — `PlatformAuditTests.cs`:

```csharp
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

[Collection("Integration")]
public class PlatformAuditTests
{
    private readonly TestWebApplicationFactory _factory;
    public PlatformAuditTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Impersonate_WritesPlatformAuditRecord()
    {
        var client = await PlatformClient();

        // Use the default seeded tenant id (platform tenants list).
        var tenants = await client.GetFromJsonAsync<List<TenantRow>>("/api/platform/tenants");
        var tenantId = tenants!.First().Id;

        var resp = await client.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/impersonate", new { });
        resp.IsSuccessStatusCode.Should().BeTrue();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var audit = await db.AuditRecords.IgnoreQueryFilters()
            .Where(a => a.ActionType == "platform_impersonation" && a.TenantId == tenantId)
            .OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync();

        audit.Should().NotBeNull();
        audit!.ActorType.Should().Be("platform");
    }

    [Fact]
    public async Task ResetPassword_AuditRecord_ContainsNoSecret()
    {
        var client = await PlatformClient();
        var tenants = await client.GetFromJsonAsync<List<TenantRow>>("/api/platform/tenants");
        var tenant = tenants!.First();
        var detail = await client.GetFromJsonAsync<TenantDetail>($"/api/platform/tenants/{tenant.Id}");
        var userId = detail!.Users.First().Id;

        const string newPass = "BrandNewPass!2026";
        var resp = await client.PostAsJsonAsync(
            $"/api/platform/tenants/{tenant.Id}/users/{userId}/reset-password", new { newPassword = newPass });
        resp.IsSuccessStatusCode.Should().BeTrue();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var audit = await db.AuditRecords.IgnoreQueryFilters()
            .Where(a => a.ActionType == "user_password_changed" && a.EntityId == userId.ToString())
            .OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync();

        audit.Should().NotBeNull();
        audit!.ActorType.Should().Be("platform");
        var blob = (audit.Description + " " + (audit.MetadataJson ?? ""));
        blob.Should().NotContain(newPass, "audit must never store the new password");
        blob.ToLower().Should().NotContain("hash");
    }

    private async Task<HttpClient> PlatformClient()
    {
        var client = _factory.CreateApiClient();
        var resp = await client.PostAsJsonAsync("/api/platform/auth/login",
            new { email = "platform-test@nexo.test", password = "FakePlatformPass!999" });
        var body = await resp.Content.ReadFromJsonAsync<PlatformLoginResponse>();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return client;
    }

    private record PlatformLoginResponse(string AccessToken);
    private record TenantRow(Guid Id);
    private record TenantDetail(List<UserRow> Users);
    private record UserRow(Guid Id);
}
```

  (Confirm `AuditRecord` exposes `MetadataJson`, `Description`, `ActionType`, `ActorType`, `EntityId`, `TenantId`, `CreatedAt`; adjust property names to the entity if different.)

- [ ] **Step 3: Run to verify FAIL** — `dotnet test tests/Nexo.IntegrationTests --filter FullyQualifiedName~PlatformAuditTests` → FAIL (no audit records written yet).

- [ ] **Step 4: Inject IAuditWriter + helper** — `PlatformController.cs`: add `IAuditWriter _audit` to the constructor (and `using Nexo.Application.Common.Interfaces;` if needed). Add a private helper:

```csharp
private void StageAudit(string action, string severity, string entityType, string entityId,
    string description, Guid? tenantId = null, object? metadata = null)
{
    var actorId   = GetPlatformUserId();
    var actorName = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
    var http      = HttpContext;
    var enriched  = new
    {
        userAgent     = http?.Request.Headers.UserAgent.ToString(),
        correlationId = http?.TraceIdentifier,
        detail        = metadata,
    };
    _audit.Stage(action, severity, entityType, entityId, description,
        tenantId: tenantId, actorId: actorId, actorName: actorName,
        actorType: "platform", metadata: enriched);
}
```

- [ ] **Step 5: Stage audit in privileged actions** (each BEFORE its `SaveChangesAsync`):
  - **Impersonate** — after resolving `adminUser`/`primaryStore`, BEFORE returning, add the audit AND a save (this action currently does not save):
    ```csharp
    StageAudit(AuditActions.PlatformImpersonation, AuditSeverity.Critical, "Tenant", tenantId.ToString(),
        $"Platform admin impersonated tenant '{tenant.CompanyName}' as user '{adminUser.Login}'.",
        tenantId: tenantId,
        metadata: new { tenantName = tenant.CompanyName, targetUserId = adminUser.Id, targetUserEmail = adminUser.Email, targetStoreId = primaryStore.Id });
    await _db.SaveChangesAsync(ct);
    ```
  - **ResetPassword** — before `await _db.SaveChangesAsync(ct);` (NO password/hash in audit):
    ```csharp
    StageAudit(AuditActions.UserPasswordChanged, AuditSeverity.Critical, "User", userId.ToString(),
        $"Platform admin reset password for user '{user.Login}'.",
        tenantId: tenantId,
        metadata: new { tenantId, targetUserId = user.Id, targetUserEmail = user.Email, result = "success" });
    ```
  - **ForceLogout** / **RevokeAllSessions** — before save:
    ```csharp
    StageAudit(AuditActions.UserSessionRevoked, AuditSeverity.Warning, "User", userId.ToString(),
        $"Platform admin revoked sessions for user '{user.Login}'.",
        tenantId: tenantId, metadata: new { targetUserId = user.Id, targetUserEmail = user.Email });
    ```
  - **SetTenantStatus** — capture old status before mutation, before save:
    ```csharp
    StageAudit(AuditActions.TenantStatusChanged, AuditSeverity.Warning, "Tenant", tenantId.ToString(),
        $"Platform admin changed tenant status to {newStatus}.",
        tenantId: tenantId, metadata: new { tenantName = tenant.CompanyName, oldValue = oldStatus.ToString(), newValue = newStatus.ToString() });
    ```
  - **GrantModule** / **RevokeModule** — before save:
    ```csharp
    StageAudit(AuditActions.ModuleActivated /* or ModuleDeactivated */, AuditSeverity.Info /* Warning for revoke */, "Tenant", tenantId.ToString(),
        $"Platform admin {(eventType)} module '{req.ModuleKey}'.",
        tenantId: tenantId, metadata: new { moduleKey = req.ModuleKey, newValue = eventType });
    ```
  - **CreateTenant** — before final save:
    ```csharp
    StageAudit(AuditActions.TenantCreated, AuditSeverity.Info, "Tenant", tenant.Id.ToString(),
        $"Platform admin created tenant '{tenant.CompanyName}'.",
        tenantId: tenant.Id, metadata: new { tenantName = tenant.CompanyName, modules = req.Modules });
    ```
  - **UpdateTenant** — before save:
    ```csharp
    StageAudit(AuditActions.TenantUpdated, AuditSeverity.Info, "Tenant", tenantId.ToString(),
        $"Platform admin updated tenant '{req.CompanyName}'.",
        tenantId: tenantId, metadata: new { tenantName = req.CompanyName });
    ```

- [ ] **Step 6: Run to verify PASS** — `dotnet test tests/Nexo.IntegrationTests --filter FullyQualifiedName~PlatformAuditTests` → PASS.

- [ ] **Step 7: Commit** — `git add -A && git commit -m "feat(platform): audit privileged actions (transactional, secret-safe)"`

---

### Task 5: Full verification

- [ ] **Step 1:** `dotnet build Nexo.sln -c Debug` → 0 errors.
- [ ] **Step 2:** `dotnet test tests/Nexo.UnitTests` → all pass.
- [ ] **Step 3:** `dotnet test tests/Nexo.IntegrationTests` → all pass (prior 146 + new).
- [ ] **Step 4:** Confirm no changes under `nexo-main/`, no env/appsettings, no Redis, no Stripe files.
- [ ] **Step 5:** Push branch, open PR (base `master`), no merge.

---

## Self-review

- **Spec coverage:** §1 bootstrap→Task 3; §2 rate-limit→Task 2; §3 audit (transactional + secret-safe + metadata)→Task 4; §4 authz policy→Task 1; testing→Tasks 1-4 + Task 5. All covered.
- **Placeholders:** test paths flagged "confirm/adjust" (InterpreterAdmin GET, AuditRecord property names, WithRateLimitingEnabled idiom) are explicit verification steps, not vague TODOs — resolve against the real code during execution.
- **Type consistency:** `SeedPlatformAdminAsync` (Task 3) used in Tasks 1/4 platform-login helpers; `StageAudit` defined Task 4 Step 4, used Step 5; `AuditActions.*` constants defined Task 4 Step 1.
