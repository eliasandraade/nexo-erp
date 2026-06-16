# Orken Build — Completion Spec

**Date:** 2026-06-16
**Branch:** `feature/orken-build-completion`
**Author:** Elias (via Claude)
**Status:** Approved scope — aggressive

---

## 1. Context & goal

Orken Build is the construction vertical of the Orken ERP (módulos: Store / Menu / Build).
Goal of this round: turn Build into a **genuinely sellable, usable** module for small
construction companies, mestres de obra, engineers, architects, empreiteiros and reform
companies — **with no fake features**. Anything visible in the UI must have a real backend
or be removed/adapted.

The audit (Phase 1) found Build is already a real, well-architected vertical (Clean
Architecture backend + fully-wired React/TanStack frontend), **not** a mock. The work is to
close gaps, fix one real contract bug, remove one fake, and harden with tests — plus the
aggressive-scope additions the product owner approved.

---

## 2. Audit summary (current state)

### Working (real, end-to-end)
- **Obras**: list / create / detail / status transitions (start/pause/complete/cancel).
- **Etapas**: list / create / progress (slider) / delete; auto-complete at 100%.
- **Orçamento**: create / add item / send / approve / reject (within a project).
- **Diário**: create / list + **real weather lookup** (Open-Meteo via backend `/integrations/weather`).
- **Financeiro**: expense capture via the **Interpreter** (analyze text → confirm → `FinancialMovement` with `ContextType=Obra`, `ContextId=projectId`, `Direction=Out`) + confirmed-expense list. Movements endpoints are `[Authorize]` only (no module gate) → a Build-only tenant can record expenses.
- **Module plumbing**: `build` seeded as `ModuleDefinition` + `ModuleSubscription`; route/role/workspace gating correct.
- **UI states**: PageHeader, loading skeletons, empty states, error states, toasts, destructive confirmations are present on most screens.

### Gaps / half-done
- **Editar obra**: hook exists, **no UI**. `UpdateBuildProjectRequest` (frontend) is missing `type` and `budgetApproved` that the backend requires.
- **Orçamento**: no edit/remove item, no convert, no margin change after creation (hooks exist, UI absent). Approving a budget does **not** propagate `finalPrice` → `project.budgetApproved`.
- **Etapas**: create captures only the name (backend supports description, planned dates, reorder — no UI).
- **Dashboard Build**: no dedicated screen; only a 4-KPI strip atop the list.
- **Relatórios**: none.
- **Fornecedor ↔ obra/despesa**: not supported (the Interpreter extracts a `payee` string but it is dropped on confirm; `FinancialMovement` has no supplier field).
- **Custo por etapa**: `BuildStage` has no cost fields.

### Fakes (must go)
- **Fotos do diário**: the UI asks the user to **type a storage key manually** (`uploads/2026/foto.jpg`). No real upload. This is the only "fake fingindo produção" in the module.

### Confirmed contract bug (P0)
- **Financial summary field-name mismatch**: frontend reads `financial.budgetApproved` / `budgetEstimated`; backend serializes `approvedBudget` / `estimatedBudget` (camelCase confirmed in `Program.cs`). Effect: in the Financeiro tab the "Orçamento aprovado" and "Estimado" cards always show "—", the over-budget alert never fires, and the consumption bar never renders.

### Tests
- **Zero automated tests** for the Build module (Interpreter, Auth, Sales, Restaurante, Stock, Storage are tested; Build is not).

---

## 3. Approved scope decisions

| Decision | Choice |
|---|---|
| PR ambition | **Aggressive** — MVP + Reports + stage costs + supplier-on-expense, including DTO/migration changes |
| Daily-log photos | **Real upload now** (reuse existing R2 `StorageController`; no new env touched by me) |
| Supplier on expense | **Implement now** (add nullable `SupplierId` to Core `FinancialMovement`) |

### Hard constraints (no authorization)
Do **not**: merge, deploy, alter env vars, touch Redis / Stripe / Auth / validated SuperAdmin,
or create destructive migrations. Migrations must be **additive only**. Do not call it "Nexo"
in the UI. Do not mix Build with Store/Menu.

---

## 4. Requirements by priority

### P0 — bugs (ship-blockers)
- **P0.1** Fix the financial-summary contract so budget figures, over-budget alert and consumption bar work. Align frontend DTO to backend (`estimatedBudget`/`approvedBudget`) or rename backend DTO; choose the **frontend-aligns-to-backend** direction (backend is the contract) to avoid breaking other consumers. Add a regression test.

### P1 — essential for a sellable MVP
- **P1.1 Editar obra**: edit dialog on the detail page (name, client, type, location, dates, estimated/approved budget). Complete `UpdateBuildProjectRequest` (add `type`, `budgetApproved`). Block in terminal status (backend already guards).
- **P1.2 Real photos**: replace the manual storage-key input with a real file upload wired to the existing `StorageController` (`/integrations/storage/upload`, new `build-daily-log` context). Photos render as thumbnails via a backend-resolved URL. When storage is disabled, show an honest "armazenamento não habilitado" state — never a fake key field.
- **P1.3 Dashboard Build**: a dedicated `/build` dashboard (real data only): obras em andamento, atrasadas (real overdue = `expectedEndDate < today` and not completed), custo previsto (Σ approved/estimated), custo realizado (Σ confirmed expenses), saldo, progresso médio das etapas, despesas recentes. Backed by one real aggregation endpoint.
- **P1.4 Orçamento completo**: edit item, remove item, change margin, and **convert/approve propagation** (approving the active budget sets `project.budgetApproved = finalPrice`). Wire existing hooks; add the missing UI.
- **P1.5 Tests**: integration tests covering the Build module flows (projects lifecycle, stages, budgets, daily logs, financial summary) + unit/regression for P0.

### P2 — aggressive additions
- **P2.1 Relatórios**: a `/build/relatorios` page (or detail-level report) with: previsto × realizado por obra, despesas por categoria, despesas por fornecedor, andamento por etapa. Backed by real report endpoints.
- **P2.2 Fornecedor na despesa**: add nullable `SupplierId` to `FinancialMovement` (additive migration); carry it through the confirm command and the movements list DTO + filter; let `BuildExpenseDialog` pick a supplier (reusing the suppliers module); enable "despesas por fornecedor".
- **P2.3 Custo previsto por etapa**: derive from budget items (`StageId` rollup) — no schema change. Show previsto vs realizado(project-level) context in the Etapas tab.
- **P2.4 Etapas completas**: capture description + planned dates on create/edit; expose reorder (move up/down is acceptable, drag-drop optional).

### P3 — polish / backlog (document, implement only if safe runway remains)
- **P3.1** Custo **realizado** por etapa (requires tagging movements by stage — larger Core change). → backlog.
- **P3.2** Standalone pre-sale budgets list (budgets without a project). → backlog.
- **P3.3** Drag-drop stage reordering. → backlog (move up/down covers the need).
- **P3.4** Latent DTO cleanups: `recentDailyLogs`→`recentLogs`, `BuildProjectDetailsDto` inherited-but-absent fields, `CreateBuildProjectRequest.type` annotation (`number`→string union), `BuildDailyLogDto.createdBy` (not returned). Fix the ones touched by P0/P1; document the rest.
- **P3.5** Photo orphan cleanup on R2 (delete old object when replaced). → backlog (matches the storage-R2 plan's own known gap).

---

## 5. Contracts to add / change

### Backend
- `FinancialMovement`: add `Guid? SupplierId` (additive). Migration `AddSupplierToFinancialMovement` (nullable column, no data loss).
- `BuildProjectFinancialSummaryDto`: unchanged (backend is the source of truth); frontend aligns.
- New **dashboard** endpoint: `GET /api/v1/build/dashboard` → `BuildDashboardDto` (counts, overdue, Σ previsto/realizado, avg progress, recent expenses).
- New **reports** endpoints under `GET /api/v1/build/reports/...`:
  - `projects-summary` (previsto×realizado por obra)
  - `expenses-by-category?projectId=`
  - `expenses-by-supplier?projectId=`
  - `stage-progress?projectId=`
- `BuildStageDto`: add `estimatedCost` (derived from budget items rollup) — read-only.
- `BuildDailyLogPhotoDto`: add `url` (backend-resolved public URL).
- Movements list DTO: add `supplierId` + `supplierName`; movements query: optional `supplierId` filter.
- `ConfirmMovementRequest` / command: add optional `supplierId`.
- `StorageController.ContextPaths`: add `"build-daily-log" → "build/daily-logs"`.

### Frontend
- `build.api.ts`: fix `BuildProjectFinancialSummaryDto` field names; complete `UpdateBuildProjectRequest`; add dashboard + reports types/fns; add `estimatedCost` to stage; add `url` to photo DTO.
- `interpreter.api.ts`: add `supplierId`/`supplierName` to movement list + confirm request.
- New components/pages: `EditProjectDialog`, `BuildDashboardPage` (or dashboard section), `BuildReportsPage`/report tab, supplier picker in `BuildExpenseDialog`, real photo upload in the Diário tab (reuse `ImageUploadButton` pattern).

---

## 6. UX requirements (every Build page)
PageHeader; breadcrumb/voltar; loading; empty state; error state; clear primary action;
toasts; confirmation on destructive actions. Keep it simple and operational (Orken design
principles). Build flow: Dashboard → Obras → Detalhe (Geral/Etapas/Orçamento/Diário/Financeiro)
→ Relatórios.

---

## 7. Risks
- **Core `FinancialMovement` change (P2.2)** affects all modules that read movements. Mitigation: nullable, additive, default-null; all existing call sites keep compiling (new param optional / overload). Regression-test the existing Interpreter confirm tests.
- **Storage disabled in dev** means photo upload returns 404 locally. Mitigation: explicit "armazenamento não habilitado" UI state; the path is production-real.
- **Migration safety**: additive nullable columns only; verify `dotnet ef migrations` produces no drops.
- **dist tracked in git** (`nexo-main/dist`): `npm run build` pollutes the tree — restore/clean `dist` before committing (per project gotcha).

---

## 8. Acceptance criteria
- `dotnet build Nexo.sln` ✅, `dotnet test UnitTests` ✅, `dotnet test IntegrationTests` ✅ (incl. new Build tests).
- `npx tsc --noEmit` ✅, `npm run build` ✅, `npm test` ✅.
- Financeiro tab shows real budget figures; over-budget alert + consumption bar work.
- Obra editable; budget items editable/removable; margin adjustable; approve propagates to project budget.
- Daily-log photos upload for real (or show honest disabled state) — no manual key field.
- Dashboard and Reports show **only real data**.
- Supplier selectable on expense; "despesas por fornecedor" report works.
- No dead buttons, no page without backend, no fake metric, no hidden real error.

## 9. Verdict targets
`ORKEN_BUILD_READY_FOR_REVIEW` once P0+P1 land green; aggressive P2 items as far as safely
completed, remainder documented as backlog. No merge/deploy without owner authorization.
