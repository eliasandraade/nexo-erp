# Orken Build Completion — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Orken Build a sellable, fake-free construction module: fix the financial contract bug, add edit-obra, complete budgets, wire real daily-log photo uploads, add a real Dashboard + Reports, link suppliers to expenses, and cover the module with tests.

**Architecture:** Backend = .NET 8 Clean Architecture (Domain aggregates with state machines, Application services, EF Core repos, thin controllers). Frontend = React + TS + Vite + TanStack Query, modular under `src/modules/build`. Expenses flow through the Core Interpreter → `FinancialMovement (ContextType=Obra)`; Build never owns financial movements. Photos reuse the existing R2 `StorageController`. All migrations additive.

**Tech Stack:** .NET 8, EF Core (Npgsql), xUnit + NSubstitute, FluentValidation; React 18, TypeScript, TanStack Query, shadcn/ui, sonner.

**Validation gates (run per task as noted):**
- Backend: `cd nexo-backend && dotnet build Nexo.sln`
- Backend tests: `dotnet test tests/Nexo.UnitTests` / `dotnet test tests/Nexo.IntegrationTests`
- Frontend: `cd nexo-main && npx tsc --noEmit` then `npm run build` then `npm test`
- **dist gotcha:** `nexo-main/dist` is tracked. After `npm run build`, restore it before committing: `git checkout -- nexo-main/dist` (do not commit build output).

---

## Task 1 — P0.1: Fix financial-summary contract bug

**Files:**
- Modify: `nexo-main/src/modules/build/api/build.api.ts` (interface `BuildProjectFinancialSummaryDto`)
- Modify: `nexo-main/src/modules/build/pages/BuildProjectDetailPage.tsx` (TabFinanceiro reads)
- Test: `nexo-backend/tests/Nexo.IntegrationTests/Build/BuildFinancialSummaryTests.cs` (regression: response includes `estimatedBudget`/`approvedBudget`)

- [ ] **Step 1 (test, backend):** Write `BuildFinancialSummaryTests` that creates a project with `budgetEstimated`+`budgetApproved`, GETs `/api/v1/build/projects/{id}/financial-summary`, and asserts the JSON has properties `estimatedBudget` and `approvedBudget` (camelCase) with the expected values, plus `totalRealizedExpenses=0`.
- [ ] **Step 2:** Run it — expect PASS (backend already emits these names). This locks the contract so the frontend can't drift again.
- [ ] **Step 3 (frontend):** In `build.api.ts`, rename `budgetEstimated`→`estimatedBudget` and `budgetApproved`→`approvedBudget` in `BuildProjectFinancialSummaryDto`.
- [ ] **Step 4:** In `BuildProjectDetailPage.tsx` `TabFinanceiro`, replace every `financial.budgetApproved`→`financial.approvedBudget` and `financial.budgetEstimated`→`financial.estimatedBudget` (KPIs, `isOverBudget`, `coveragePercent`, the "de … utilizados" line).
- [ ] **Step 5:** `npx tsc --noEmit` → 0 errors.
- [ ] **Step 6:** Commit `fix(build): align financial-summary DTO field names so budget figures render`.

---

## Task 2 — P1.1: Editar obra

**Files:**
- Modify: `nexo-main/src/modules/build/api/build.api.ts` (`UpdateBuildProjectRequest`: add `type`, `budgetApproved`; fix `CreateBuildProjectRequest.type` annotation to the `BuildProjectType` string union)
- Create: `nexo-main/src/modules/build/components/EditProjectDialog.tsx`
- Modify: `nexo-main/src/modules/build/pages/BuildProjectDetailPage.tsx` (add "Editar" button in `TabGeral`, mount dialog)
- (Backend already supports `UpdateDetails(type, budgetApproved)`.)

- [ ] **Step 1:** In `build.api.ts`, change `UpdateBuildProjectRequest` to:
```ts
export interface UpdateBuildProjectRequest {
  name: string;
  clientName: string;
  type: BuildProjectType;
  location?: string;
  startDate?: string;
  expectedEndDate?: string;
  budgetEstimated?: number;
  budgetApproved?: number;
}
```
and change `CreateBuildProjectRequest.type: number` → `type: BuildProjectType`.
- [ ] **Step 2:** Create `EditProjectDialog.tsx` — a dialog pre-filled from the project (name, client, type select, location, startDate, expectedEndDate, budgetEstimated, budgetApproved) using `useUpdateProject(id)`. On success: toast + close. Mirror `NewProjectDialog` styling. Disable submit while pending / when required fields empty.
- [ ] **Step 3:** In `TabGeral`, add a "Editar" button (Pencil icon) in the info header that opens the dialog (only when `project.status` is non-terminal — `Planning|InProgress|Paused`).
- [ ] **Step 4:** `npx tsc --noEmit` → 0 errors.
- [ ] **Step 5:** Manual check via preview later. Commit `feat(build): edit-project dialog with full update contract`.

---

## Task 3 — P1.4: Orçamento completo (edit/remove item, margin, approve→propagate)

**Files:**
- Modify (backend): `nexo-backend/src/Nexo.Application/Modules/Build/BuildBudgetService.cs` — on `ApproveBudgetAsync`, if the budget has a `ProjectId`, call `project.ApproveBudget(budget.FinalPrice)` and save (propagate finalPrice → project.BudgetApproved).
- Test: `nexo-backend/tests/Nexo.IntegrationTests/Build/BuildBudgetTests.cs`
- Modify (frontend): `BuildProjectDetailPage.tsx` `TabOrcamento` — wire `useUpdateBudgetItem`, `useRemoveBudgetItem`, `useSetBudgetMargin`; add inline edit/remove per item row and a margin editor.

- [ ] **Step 1 (test):** In `BuildBudgetTests`, create project + budget(projectId) + item, approve the budget, GET the project, assert `budgetApproved == budget.finalPrice`.
- [ ] **Step 2:** Run → FAIL (approve currently doesn't propagate).
- [ ] **Step 3:** In `BuildBudgetService.ApproveBudgetAsync`, after `budget.Approve()`, if `budget.ProjectId is Guid pid`, load the project, `project.ApproveBudget(budget.FinalPrice)`, save. (Use existing project repo; guard project not-found gracefully.)
- [ ] **Step 4:** Run → PASS. `dotnet build` → 0 errors.
- [ ] **Step 5 (frontend):** In `TabOrcamento`, add per-item edit (inline form reusing the add-item fields) and a remove button (with confirm) gated to `Draft|Sent`; add a "Margem" editable control using `useSetBudgetMargin`. Invalidate budget query on success; toasts on success/error.
- [ ] **Step 6:** `npx tsc --noEmit` → 0. Commit `feat(build): budget item edit/remove, margin editor, approve propagates to project budget`.

---

## Task 4 — P1.2: Real daily-log photos

**Files:**
- Modify (backend): `nexo-backend/src/Nexo.Api/Controllers/Integrations/StorageController.cs` — add `["build-daily-log"] = "build/daily-logs"` to `ContextPaths`.
- Modify (backend): `BuildDtos.cs` `BuildDailyLogPhotoDto` — add `string? Url`; `BuildDailyLogService` resolves `Url` from `StorageOptions.R2.PublicUrl + key` (inject `IOptions<StorageOptions>`); null when storage not configured.
- Test: `nexo-backend/tests/Nexo.UnitTests/Integrations/StorageControllerTests.cs` — add `build-daily-log` context test (key contains `build/daily-logs`).
- Modify (frontend): `build.api.ts` `BuildDailyLogPhotoDto` — add `url: string | null`.
- Modify (frontend): `BuildProjectDetailPage.tsx` `TabDiario` — replace the manual storage-key input with a real uploader using `uploadFile(file, "build-daily-log")` from `@/services/storage.api`; on success call `addDailyLogPhoto(logId, { storageKey: key, caption })`; render thumbnails from `photo.url`; when an upload returns 404 (storage disabled) show "Upload de fotos indisponível neste ambiente."

- [ ] **Step 1 (test):** Add `Upload_BuildDailyLogContext_KeyContainsPath` to `StorageControllerTests` (mirrors existing context tests) asserting `captured.ObjectKey` contains `build/daily-logs`.
- [ ] **Step 2:** Run → FAIL (context unknown → BadRequest). Add the context entry. Run → PASS.
- [ ] **Step 3 (backend DTO):** Add `Url` to `BuildDailyLogPhotoDto`; map it in `BuildDailyLogService` (compose from `StorageOptions.R2.PublicUrl`; if empty → null). `dotnet build` → 0.
- [ ] **Step 4 (frontend types):** Add `url` to the photo DTO in `build.api.ts`.
- [ ] **Step 5 (frontend UI):** Rewrite the photo block in `TabDiario`: a file input → `uploadFile` → `addDailyLogPhoto`; thumbnails `<img src={photo.url}>` with caption fallback; loading + error (incl. disabled-storage) states. Remove the manual storage-key Input entirely.
- [ ] **Step 6:** `npx tsc --noEmit` → 0; run storage tests. Commit `feat(build): real daily-log photo upload via storage service; remove manual storage-key fake`.

---

## Task 5 — P2.2: Supplier on expense (Core additive change)

**Files:**
- Modify (domain): `nexo-backend/src/Nexo.Domain/Modules/Interpreter/FinancialMovement.cs` — add `Guid? SupplierId`; thread through `CreateDraft` and `UpdateFields` (new optional param, default null).
- Modify (config): `FinancialMovementConfiguration.cs` — map `SupplierId` (nullable, indexed).
- Create (migration): `AddSupplierToFinancialMovement` (additive nullable column).
- Modify: `ConfirmMovementUseCase` / `InterpreterCommands` (confirm command) — accept optional `SupplierId`; pass to movement.
- Modify: movements list DTO + query — add `supplierId` + `supplierName`; optional `supplierId` filter. (Join Suppliers for name.)
- Modify (frontend): `interpreter.api.ts` — add `supplierId?` to `ConfirmMovementRequest`; add `supplierId`/`supplierName` to `MovementListItemDto`; add `supplierId` filter param to `fetchProjectMovements`.
- Modify (frontend): `BuildExpenseDialog.tsx` — supplier `Select` (load suppliers via the suppliers module hook), include `supplierId` in confirm.
- Test: extend interpreter confirm tests + a Build integration test asserting a confirmed Obra expense carries `supplierId`.

- [ ] **Step 1 (test):** Unit test on `ConfirmMovementUseCase` (or integration on `/v1/movements/{id}/confirm`) passing `supplierId`; assert the persisted movement has it. Run → FAIL.
- [ ] **Step 2 (domain):** Add `SupplierId` to `FinancialMovement` (private set), add optional `Guid? supplierId = null` to `CreateDraft` and `UpdateFields`, assign it. Keep existing call sites compiling (default null).
- [ ] **Step 3 (config + migration):** Map `SupplierId`; `dotnet ef migrations add AddSupplierToFinancialMovement -p src/Nexo.Infrastructure -s src/Nexo.Api`. Inspect the generated migration → **only** `AddColumn` (no drops). 
- [ ] **Step 4 (command):** Add `SupplierId` to the confirm command/request; thread into the movement. Run Step-1 test → PASS.
- [ ] **Step 5 (list DTO + query):** Add `SupplierId`/`SupplierName` to the movement list item DTO; left-join Suppliers for the name; add optional `supplierId` filter to the query. Build → 0; existing interpreter tests still green.
- [ ] **Step 6 (frontend):** Update `interpreter.api.ts` types/fn; add supplier `Select` to `BuildExpenseDialog` (reuse `use-suppliers`/suppliers list); pass `supplierId` on confirm; show supplier name in the movement list. `npx tsc --noEmit` → 0.
- [ ] **Step 7:** Commit `feat(build): link supplier to obra expenses (additive FinancialMovement.SupplierId)`.

---

## Task 6 — P1.3: Dashboard Build

**Files:**
- Create (backend): `BuildDashboardDto` in `BuildDtos.cs`; `BuildDashboardService` (aggregates projects + financial snapshots); endpoint `GET /api/v1/build/dashboard` on a new `BuildDashboardController` (or add to `BuildProjectsController`).
- Test: `nexo-backend/tests/Nexo.IntegrationTests/Build/BuildDashboardTests.cs`
- Create (frontend): `BuildDashboardPage.tsx` (or a dashboard section rendered at `/build` above the list); `fetchBuildDashboard` + `useBuildDashboard`.
- Modify: route `/build` to show dashboard + obras (keep list reachable).

`BuildDashboardDto` (real fields only):
```
int ActiveCount, PlanningCount, PausedCount, CompletedCount;
int OverdueCount;                 // expectedEndDate < today && status != Completed/Cancelled
decimal TotalEstimated, TotalApproved, TotalRealized, Balance; // Balance = TotalApproved - TotalRealized
double AvgStageProgress;          // mean of per-project stage completion
IReadOnlyList<RecentExpenseDto> RecentExpenses; // last N confirmed Obra movements across projects
```

- [ ] **Step 1 (test):** `BuildDashboardTests` seeds 2 projects (one overdue, one active with expenses) and asserts counts, `OverdueCount`, `TotalRealized`, and `Balance`. Run → FAIL.
- [ ] **Step 2 (backend):** Implement `BuildDashboardService` reading projects + `IBuildFinancialQueryService` aggregates (no parallel financial writes); add DTO + endpoint. Run → PASS; build → 0.
- [ ] **Step 3 (frontend):** Add `fetchBuildDashboard`/`useBuildDashboard`; build `BuildDashboardPage` with KPI cards (real data), overdue highlight, avg-progress bar, recent-expenses list, all with loading/empty/error states. No fake metrics.
- [ ] **Step 4:** Mount it at `/build` (dashboard first, then obras list or a "Ver todas as obras" link). `npx tsc --noEmit` → 0.
- [ ] **Step 5:** Commit `feat(build): real dashboard endpoint + page (active/overdue/cost/progress/recent expenses)`.

---

## Task 7 — P2.1 + P2.3: Reports + stage estimated cost

**Files:**
- Create (backend): report DTOs + `BuildReportsService` + `BuildReportsController`:
  - `GET /api/v1/build/reports/projects-summary` → previsto×realizado por obra
  - `GET /api/v1/build/reports/expenses-by-category?projectId=`
  - `GET /api/v1/build/reports/expenses-by-supplier?projectId=`
  - `GET /api/v1/build/reports/stage-progress?projectId=`
- Modify: `BuildStageDto` + `BuildStageService` — add `estimatedCost` (Σ budget items where `StageId == stage.Id`).
- Test: `nexo-backend/tests/Nexo.IntegrationTests/Build/BuildReportsTests.cs`
- Create (frontend): `BuildReportsPage.tsx` + api/hooks; route `/build/relatorios`; add nav entry.

- [ ] **Step 1 (test):** `BuildReportsTests` seeds project+budget(items by stage)+confirmed expenses(by category+supplier); asserts `expenses-by-category` and `expenses-by-supplier` sums and `projects-summary` previsto/realizado. Run → FAIL.
- [ ] **Step 2 (backend):** Implement reports service/controller + `estimatedCost` rollup on stages. Run → PASS; build → 0.
- [ ] **Step 3 (frontend):** `BuildReportsPage` with the four reports (cards/tables + simple bars), loading/empty/error; reuse existing chart/table primitives. Show `estimatedCost` in the Etapas tab. Add route + nav.
- [ ] **Step 4:** `npx tsc --noEmit` → 0. Commit `feat(build): reports endpoints + page; stage estimated-cost rollup`.

---

## Task 8 — P2.4: Etapas completas (description, dates, reorder)

**Files:**
- Modify (frontend): `BuildProjectDetailPage.tsx` `TabEtapas` — create/edit captures description + planned dates; add move-up/move-down using `useReorderStages`; expose `updateStageProgress` status override if useful.
- (Backend already supports description/dates/reorder.)

- [ ] **Step 1:** Extend the add/edit stage form with description + planned start/end inputs (optional). Use existing `createStage` (already accepts these) and add an edit path (a `useUpdateStage` hook + `updateStage` api fn calling `PUT /v1/build/stages/{id}` — add it; backend `UpdateBuildStageRequest` exists).

> Note: backend has stage `UpdateDetails` but verify a controller route exists; if `PUT /stages/{id}` is missing, add a thin endpoint (additive). Check `BuildStagesController` before implementing.

- [ ] **Step 2:** Add move-up/move-down buttons that compute the new order list and call `useReorderStages`.
- [ ] **Step 3:** `npx tsc --noEmit` → 0. Commit `feat(build): stage description/dates + reorder controls`.

---

## Task 9 — P1.5: Consolidated Build module tests + full validation

**Files:**
- Ensure `nexo-backend/tests/Nexo.IntegrationTests/Build/` covers: project lifecycle (create→start→pause→start→complete; cancel; terminal guard), stages (create/progress/auto-complete/delete/reorder), budgets (create/add/edit/remove item/margin/approve-propagate/convert), daily logs (create/duplicate-date 422/update), financial summary (variance math), dashboard, reports.
- Reuse the existing IntegrationTests fixture pattern (see `Restaurante/FinanceiroReportTests.cs`).

- [ ] **Step 1:** Fill any flow not yet covered by Tasks 1/3/5/6/7 tests.
- [ ] **Step 2:** `dotnet test tests/Nexo.UnitTests` → all pass.
- [ ] **Step 3:** `dotnet test tests/Nexo.IntegrationTests` → all pass (existing 146 + new Build tests).
- [ ] **Step 4:** Frontend: `npx tsc --noEmit` → 0; `npm run build` → success; `npm test` → pass; then `git checkout -- nexo-main/dist`.
- [ ] **Step 5:** Commit `test(build): integration coverage for the Build module`.

---

## Final steps
- [ ] Update the spec/plan checkboxes; write the final report (assumptions, files, endpoints, tests, commands+results, backlog, verdict).
- [ ] Push branch; open PR (no merge, no deploy).
- [ ] Backlog (documented, not implemented): P3.1 realized-cost-per-stage, P3.2 standalone budgets list, P3.3 drag-drop reorder, P3.5 R2 orphan cleanup, remaining latent DTO cosmetics.

---

## Self-review (spec coverage)
- P0.1 → Task 1. P1.1 → Task 2. P1.4 → Task 3. P1.2 → Task 4. P2.2 → Task 5. P1.3 → Task 6. P2.1+P2.3 → Task 7. P2.4 → Task 8. P1.5 → Task 9.
- Backlog items (P3.x) explicitly deferred with rationale.
- All migrations additive; no Core call site broken (optional params); dist gotcha handled.
