# Platform Admin — Operator Console UX — Spec

**Date:** 2026-06-16
**Branch:** `ux/platform-admin-operator-console` (off `master`)
**Scope:** Frontend only (`nexo-main`). Turn the SuperAdmin panel from "technically works" into a real operator console: every dangerous action confirmed, impersonation explicit + visible, audit filters that actually work, no dead controls/metrics. No backend/env/Redis/Stripe/auth-rule changes.

## Audit summary (current state)
Solid base: Orken-branded (no visible "Nexo"), real endpoints, no mock/fake data, dashboard with real KPIs, audit already visible (`/platform/activity`), rich tenant detail, `ConfirmDialog` + sonner toasts available. Problems are localized.

## Priorities

### P0 — bug/risk
_None._ Security from the prior round is intact and must not be weakened. (Policy `Platform`, rate-limit, audit stay untouched.)

### P1 — essential flow & safety
1. **Confirm dangerous actions** (Tenant Detail) — reuse the existing `ConfirmDialog`/`openConfirm`:
   - **Suspend tenant** → confirm (warning).
   - **Reactivate tenant** → confirm (default/info).
   - **Revoke module** → confirm (danger).
   - (Grant module: low-risk, no modal; show toast.)
2. **Impersonation: explicit + auditable + visible**
   - **Confirm modal** before impersonating, naming the target tenant + the Diretoria user that will be assumed and stating the risk + that it is audited. Optional free-text "motivo" (NOT persisted yet — backlog; if typed, pass nowhere until backend supports it). Proceed only on confirm.
   - **Impersonation banner**: `ImpersonatePage` sets a tab-scoped marker `sessionStorage["orken:impersonation"] = {tenant, user}` before redirecting. A global `<ImpersonationBanner/>` (mounted in `AppProviders`) shows a fixed top bar: "Você está acessando como **[user]** do cliente **[tenant]**." + **Sair da impersonação** (clears the marker + logs out + → `/login`). Tab-scoped so it never appears in the admin's own tabs.
3. **Fix audit filter values** (`PlatformActivityPage`) — action-type options must match the real backend `AuditActions` (snake_case): `user_logged_in, user_logged_out, user_password_changed, user_session_revoked, user_created, user_updated, sale_completed, sale_cancelled, stock_adjustment, cash_open, cash_close, tenant_created, tenant_updated, tenant_status_changed, module_activated, module_deactivated, platform_impersonation`. Severities → `info, warning, critical` (drop "Error"). Use human labels in the dropdown, real values as the filter value.
4. **Remove dead Dashboard metric** — the always-`0` "Sem módulo" row (no real source). Replace the "Atenção" card content with real signals only (Suspensos from `stats.suspendedCount`; link to Trial page using existing data) or drop the dead row.
5. **Remove dead `forceLogoutMut`** in Tenant Detail (declared, never used).

### P2 — polish
6. **Action feedback** — add `toast.success`/`toast.error` to the tenant mutation hooks (suspend/reactivate, grant, revoke, reset password, revoke sessions, create/delete/pin note) via `onSuccess`/`onError`; replace the impersonation `alert()` with `toast.error`.
7. **Tenant list operability** — status filter (Todos / Ativos / Suspensos) as segmented chips, plus an **error state** with retry (distinguish error from empty).
8. **React key fix** — keyed `React.Fragment` around each user row group.
9. **Brand nav** — replace `<a href="/platform">` with a router `Link` (no full reload). Profile button: keep `/perfil` only if it renders for platform users; otherwise point to a safe target (logout area already exists).
10. **Dashboard resilience** — show a small error note if stats fail (instead of permanent "—").

### P3 — backlog (documented, not implemented now)
- Impersonation **reason persisted** (needs a backend field/claim + audit metadata).
- **Billing/Stripe** real operations (operate subscriptions) — currently view-only of plan/module/status.
- Rename localStorage namespace `nexo:` → `orken:` (coordinate; not user-visible).
- **Command palette** (Cmd-K) for quick tenant jump; per-session force-logout.

## Components / files
- New: `src/components/ImpersonationBanner.tsx`.
- Modify: `PlatformTenantDetailPage.tsx` (confirms, impersonate modal, dead hook), `ImpersonatePage.tsx` (set marker), `AppProviders.tsx` (mount banner), `PlatformActivityPage.tsx` (filter values), `PlatformDashboardPage.tsx` (dead metric), `PlatformTenantsPage.tsx` (status filter + error), `usePlatformTenants.ts` (toasts), `PlatformLayout.tsx` (brand Link).

## Testing
Frontend gates only: `npx tsc --noEmit`, `npm run build`, `npm test`. Add/extend a unit test for the impersonation-marker helper and (if a test setup exists for components) the banner render. Keep existing tests green.

## Risks
All additive/UI. Impersonation banner reads a new sessionStorage key and uses the existing logout — does not change auth rules. localStorage session sharing across tabs is a pre-existing behavior (out of scope).

## Acceptance
No dead button, no fake data/metric, every dangerous action confirmed, no visible "Nexo", every list with loading/empty/error, no broken route, tenant token still blocked on `/platform`, impersonation explicit + visible, audit usable. Build/tests green.
