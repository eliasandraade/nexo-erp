# Platform Admin — Operator Console UX — Implementation Plan

**Goal:** Frontend-only UX hardening of the SuperAdmin panel (see spec `2026-06-16-platform-admin-ux-operator-console.md`). Every dangerous action confirmed, impersonation explicit + visible, audit filters fixed, no dead controls/metrics. No backend/env/auth changes.

**Stack:** React + TS + Vite + Tailwind + shadcn, TanStack Query, sonner toasts, react-router.

---

### Task 1 — Audit filter values (P1.3)
`src/modules/platform/pages/PlatformActivityPage.tsx`:
- Replace `SEVERITIES` with real values + labels: `[{v:"",l:"Todas as severidades"},{v:"info",l:"Info"},{v:"warning",l:"Aviso"},{v:"critical",l:"Crítico"}]`.
- Replace `ACTION_TYPES` with `{value,label}` pairs matching backend `AuditActions` snake_case (login, logout, password_changed, session_revoked, user_created/updated, sale_completed/cancelled, stock_adjustment, cash_open/close, tenant_created/updated/status_changed, module_activated/deactivated, platform_impersonation).
- `SEVERITY_STYLES` keyed by lowercase (`info/warning/critical`).

### Task 2 — Dashboard dead metric (P1.4)
`src/modules/platform/pages/PlatformDashboardPage.tsx`:
- In the "Atenção" card, remove the always-0 "Sem módulo" row. Keep "Suspensos" (real `stats.suspendedCount`). Add a real, useful row only if backed by data (e.g. a link to `/platform/trial`). Add a tiny error note if `statsError`.

### Task 3 — Tenant Detail: confirmations + dead hook + impersonation modal (P1.1, P1.2, P1.5)
`src/modules/platform/pages/PlatformTenantDetailPage.tsx`:
- Remove `useForceLogout` import + `forceLogoutMut` declaration (dead).
- Wrap **Suspend** / **Reactivate** in `openConfirm` (warning / default).
- Wrap **Revoke module** in `openConfirm` (danger). Grant stays direct.
- **Impersonation**: gate `handleImpersonate` behind `openConfirm` (danger) with description naming the tenant + the first Diretoria user + "ação auditada". On confirm, run the existing token/localStorage/new-tab flow. Replace `alert()` with `toast.error`.

### Task 4 — Impersonation banner (P1.2)
- `src/pages/ImpersonatePage.tsx`: before redirect, `sessionStorage.setItem("orken:impersonation", JSON.stringify({ tenant: session.companyName, user: session.name }))`.
- New `src/components/ImpersonationBanner.tsx`: reads the marker; if present, renders a fixed top bar (amber/indigo, high z-index) "Você está acessando como **{user}** do cliente **{tenant}**." + "Sair da impersonação" button → clears the marker, calls auth `logout()`, navigates `/login`.
- Mount `<ImpersonationBanner/>` in `src/app/providers/AppProviders.tsx` (inside the router/auth context, once).

### Task 5 — Mutation toasts (P2.6)
`src/modules/platform/hooks/usePlatformTenants.ts`: add `toast.success(...)` in `onSuccess` and `toast.error(e.message)` in `onError` for: setTenantStatus, grantModule, revokeModule, resetUserPassword, forceLogout(if kept—removed), revokeAllSessions, createNote, deleteNote, toggleNotePin. Import `toast` from "sonner".

### Task 6 — Tenant list status filter + error state (P2.7)
`src/modules/platform/pages/PlatformTenantsPage.tsx`: segmented chips (Todos/Ativos/Suspensos) filtering on `t.status`; `isError` → error card with "Tentar novamente" (refetch).

### Task 7 — Small fixes (P2.8, P2.9)
- Tenant detail users `.map` → `React.Fragment key={u.id}` wrapping the row + SessionsRow.
- `PlatformLayout.tsx`: brand `<a href>` → `<Link to="/platform">`.

### Task 8 — Validate + PR
- `cd nexo-main && npx tsc --noEmit && npm run build && npm test`.
- Commit per task or one cohesive commit; push; open PR (base master, no merge).

## Self-review
Spec P1 → Tasks 1-4; P2 → Tasks 5-7; validation → Task 8. No placeholders. Component/prop names: `ConfirmDialog` (open/title/description/variant/onConfirm/onCancel) already used; `openConfirm({title,description,variant,onConfirm})` exists; `toast` from sonner; `useAuth().logout` exists.
