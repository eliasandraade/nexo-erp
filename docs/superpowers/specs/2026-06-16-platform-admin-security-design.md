# Platform Admin Security Hardening — Design Spec

**Date:** 2026-06-16
**Branch:** `fix/platform-admin-security` (off `master`)
**Scope:** Backend only. Harden the platform (super-admin) control panel: fix the production login (401), rate-limit platform login, audit privileged actions, and enforce a single authorization policy.

**Out of scope (follow-ups):** 2FA/MFA, platform roles (read-only support), platform UX (command palette, impersonation banner), stats pagination/caching. No frontend, env, Redis, or Stripe changes.

---

## Problem

1. **401 on platform login in production.** The super-admin signs in at `POST /api/platform/auth/login` against `platform_users`. `Program.cs` only runs the seeder when `!IsProduction()`, so `SeedPlatformUserAsync` never runs in production → the super-admin row is missing or its password was never synced from the env. Changing `Seed__PlatformPassword` has no effect (contradicting the seeder's comment).
2. **Platform login is not rate-limited** (`PlatformAuthController.Login` has no `[EnableRateLimiting]`) → brute-force exposure on the highest-value credential.
3. **Privileged platform actions are not audited.** `Impersonate`, `ResetPassword`, `ForceLogout`, `RevokeAllSessions` write no `AuditRecord`. Impersonation with no trail is a compliance gap.
4. **Authorization is duplicated and fragile.** Every `PlatformController` action repeats `if (!IsPlatformUser()) return Forbid();`. A new endpoint that forgets it is an access hole.

---

## Design

### 1. Super-admin bootstrap (fix the 401)

- Expose a public `DataSeeder.SeedPlatformAdminAsync(CancellationToken)` that calls the existing private `SeedPlatformUserAsync` (reuses tested, sync-on-deploy logic).
- In `Program.cs`, after `MigrateAsync()`, call `SeedPlatformAdminAsync` for **all non-Testing environments, including Production**. The full demo `SeedAsync()` stays gated to `!IsProduction()`.

```
if (!app.Environment.IsEnvironment("Testing"))
{
    await db.Database.MigrateAsync();

    // Platform super-admin: idempotent, env-driven, runs in EVERY env incl. Production.
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    await seeder.SeedPlatformAdminAsync();   // default CancellationToken (startup, no ct in scope)

    if (!app.Environment.IsProduction())
        await seeder.SeedAsync();   // demo tenants — dev only
}
```

- **Rules:** runs in Production; does NOT run in Testing (the test factory seeds explicitly); `SeedAsync()` (demo) stays out of Production; idempotent (create-if-missing **+** re-sync password hash from `Seed__PlatformEmail`/`Seed__PlatformPassword` every deploy); **never logs the password**; throws a clear `InvalidOperationException` if a required env is missing (existing behavior, kept).
- **Env is the source of truth** for the super-admin. Rotation = change `Seed__PlatformPassword` + redeploy.

### 2. Rate-limit platform login

- Add `[EnableRateLimiting("auth-login")]` to `PlatformAuthController.Login`.
- The `auth-login` policy is partitioned by `(clientIp | request.Path)`, so `/api/platform/auth/login` gets its **own bucket**, isolated from tenant `/api/auth/login`. Config-driven limits (default 5 / 15 min); 429 on exceed. No new policy.

### 3. Audit privileged actions (transactional, secret-safe)

Inject `IAuditWriter` into `PlatformController`. For each privileged action, `Stage(...)` an audit record **and ensure `SaveChangesAsync` runs** so it persists.

| Action | actionType | severity | Notes |
|---|---|---|---|
| Impersonate | `platform_impersonation` | critical | **Add an explicit `SaveChangesAsync`** (today the action only mints tokens / reads — nothing is saved). |
| ResetPassword | `user_password_changed` | critical | **No secret material** (see below). Already saves. |
| ForceLogout | `user_session_revoked` | warning | Already saves. |
| RevokeAllSessions | `user_session_revoked` | warning | Already saves. |
| CreateTenant | `tenant_created` | info | Already saves. |
| UpdateTenant | `tenant_updated` | info | Already saves. |
| SetTenantStatus | `tenant_status_changed` | warning | old/new status in metadata. Already saves. |
| GrantModule | `module_activated` | info | moduleKey + expiresAt in metadata. Already saves. |
| RevokeModule | `module_deactivated` | warning | moduleKey in metadata. Already saves. |

New `AuditActions` constants: `PlatformImpersonation = "platform_impersonation"`, `TenantUpdated = "tenant_updated"`, `TenantStatusChanged = "tenant_status_changed"`. Reuse existing constants where they fit (`UserPasswordChanged`, `UserSessionRevoked`, `TenantCreated`, `ModuleActivated`, `ModuleDeactivated`).

**Actor + metadata.** `actorType = "platform"`, `actorId = GetPlatformUserId()`, `actorName = email claim`. IP is auto-captured by `AuditWriterService` from the request. `metadata` object includes, when available:
`tenantId, tenantName, targetUserId, targetUserEmail, targetStoreId, moduleKey, oldValue, newValue, reason, userAgent, correlationId (HttpContext.TraceIdentifier), result`.
(`tenantId` is also passed as the top-level `Stage` param; IP is auto-captured.)

**Secret-safety (ResetPassword):** the audit record records only *that* a reset happened — actor, target user, tenant, action, timestamp, ip/userAgent/correlationId, result. It MUST NOT contain the new password, the password hash, or any refresh/access token.

A small private helper builds the common actor + metadata (userAgent, correlationId) so each call site stays terse.

### 4. Platform authorization policy

- Register policy `"Platform"` = `RequireAuthenticatedUser()` + `RequireClaim("type", "platform")` in `AddAuthorization`.
- Apply `[Authorize(Policy = "Platform")]` at the **controller** level on `PlatformController`, `PlatformFlagsController`, `InterpreterAdminController`.
- Remove the duplicated `if (!IsPlatformUser()) return Forbid();` checks (the policy is the single gate; missing/anonymous → 401, non-platform token → 403, platform token → allowed). `GetPlatformUserId()` / claim reads stay.
- `PlatformAuthController` (login) remains anonymous (now rate-limited).

---

## Testing (integration, regression)

- **Platform login:** wrong credentials → 401; exceeding the limit → 429, proving the bucket is **separate by path** from tenant login (tenant login still works in the same window).
- **Authorization** — for at least one endpoint of EACH protected controller (`PlatformController`, `PlatformFlagsController`, `InterpreterAdminController`): no token → **401**; tenant token → **403**; platform token → **200**.
- **Audit:** after `Impersonate` and `ResetPassword`, an `AuditRecord` exists with `actorType = "platform"`; assert the ResetPassword record's description/metadata contain **no** password/hash/token.
- **Bootstrap:** super-admin can log in after seeding (covered by the test factory's explicit seed; add a focused platform-login success test).
- Whole integration suite stays green (current 146 + new).

---

## Risks / rollback

- Small, isolated backend changes. The bootstrap is idempotent; the policy is additive; audit staging commits with existing transactions.
- The `Impersonate` change adds a `SaveChangesAsync` to a previously read-only action — verify it doesn't alter the response contract (it doesn't; only the audit row is written).
- Rollback = revert the commit. Deploy applies the 401 fix automatically and re-syncs the super-admin password from the current env.

## Non-goals / untouched

No frontend, no env/secret changes, no Redis changes, no Stripe changes. 2FA, platform roles, and platform UX are explicit follow-ups.
