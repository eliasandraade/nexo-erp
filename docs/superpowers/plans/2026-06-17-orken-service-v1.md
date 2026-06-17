# Orken Service v1 — Implementation Plan

**Date:** 2026-06-17
**Branch:** `feature/orken-service-v1-planning`
**Spec:** [`docs/superpowers/specs/2026-06-17-orken-service-v1.md`](../specs/2026-06-17-orken-service-v1.md)
**Status:** PLAN — **awaiting owner approval before implementation.**

> This plan turns the approved scope into sequenced, executable PRs. **No code, no
> migrations, no deploy** until the owner approves the scope. Each PR is small, additive,
> follows the Build module template, and ships with integration tests.

---

## Guardrails (from owner, this round)
- Work only on `feature/orken-service-v1-planning` (branched from `master`). No work on `master`.
- **Do NOT**: merge, deploy, `railway up`, change env, touch Redis / Stripe / Auth / SuperAdmin;
  modify the validated Build module beyond read/audit; **create migrations** (until explicitly approved);
  implement the full module this round.
- Each implementation PR is opened for review; nothing auto-merges.

---

## Sequencing overview

```
PR0  Core enablers (gate + preset registry + SKUs)        [additive, no domain tables]
PR1  Engine foundation: Professionals + Catalog           [+migration*]
PR2  Subjects + Records (notes/attachments)               [+migration*]
PR3  Appointments + Agenda (overlap, status)              [+migration*]
PR4  Orders + Items + Quote/Approval                      [+migration*]
PR5  Packages + session control                           [+migration*]
PR6  Payments → Core + Dashboard                          [no/【thin】 use case]
PR7  Frontend: shell, preset adaptation, screens          [frontend only]
PR8  Presets: all 9 labels/capabilities + per-preset QA   [config + QA]
```
`*` migrations created **only after** the owner approves the data model (gate at end of PR1 design).

---

## PR0 — Core enablers (no new tables)
**Goal:** make the engine gateable, billable, and preset-aware before any domain code.
- [ ] `[RequireServiceModule]` attribute (family-aware variant of `RequireModuleAttribute`); reuses the cached `tenant:{id}:modules`. Does **not** modify the existing attribute.
- [ ] Frontend `useHasServiceModule()` helper + `"service"` `RouteGroup` in `routes.ts` (routes added later, gated on any family key).
- [ ] `ServicePresetRegistry` (code) defining the 9 presets: key, labels, capability flags (Spec §6). Pure data + a `Resolve(moduleKeys)` function.
- [ ] `GET /api/v1/service/preset` returns the resolved preset (labels + capabilities).
- [ ] Add the 5 missing `ModuleDefinition` rows in `DataSeeder` (`nutricionista`, `personal-trainer`, `autoescola`, `escola-idiomas`, `programador-autonomo`), `IsPublished=false`. **No Stripe pricing.**
- [ ] Decide publish state of existing 4 SKUs (owner call — flag).
- [ ] Tests: preset resolution (each key → right capabilities), gate returns 403 without family key, 200 with.

**Confirm-in-P0:** does Core expose a direct create-confirmed-`FinancialMovement`? Record finding here; if no, PR6 adds a thin use case.

---

## PR1 — Engine foundation: Professionals + Catalog
**Goal:** the two simplest aggregates, full vertical slice, establishing the `Svc` template.
- [ ] Domain: `SvcProfessional`, `SvcCatalogItem` (+ `SvcCatalogCategory?`) with factory/validation/active-inactive (mirror `RestEmployee`/`Customer`).
- [ ] Application: services + DTOs + validators + repository interfaces (`ISvc*Repository`).
- [ ] Infrastructure: EF configurations + repositories; **EF model + migration design** (do not run until approved).
- [ ] Api: `ProfessionalsController`, `CatalogController` (`[RequireServiceModule]`).
- [ ] **GATE → owner approves data model.** Then generate the migration.
- [ ] Tests: CRUD + activate/deactivate + tenant/store isolation + module gate.

---

## PR2 — Subjects + Records
- [ ] Domain: `SvcSubject` (`TenantEntity`, `kind` enum + `MetadataJson` JSONB, link to `Customer`); `SvcRecordEntry` (polymorphic `ContextType`/`ContextId`, text + attachment storageKeys).
- [ ] Application/Infra/Api per template; attachments reuse the Storage/R2 + resolve-at-read pattern.
- [ ] LGPD guardrails (Spec §10): notes scoped tenant+store; storage access checks.
- [ ] Tests: subject CRUD by kind; record timeline by context; isolation; attachment access denied cross-tenant.

---

## PR3 — Appointments + Agenda
- [ ] Domain: `SvcAppointment` + status machine (`Scheduled→Confirmed→InProgress→Completed`, `→NoShow`, `→Cancelled`).
- [ ] Business rule: **per-professional overlap prevention** + store business hours (from `SvcSettings`).
- [ ] Api: list by range/professional/status; create/reschedule; `PATCH /{id}/status`.
- [ ] Tests: overlap rejected; status transitions valid/invalid; cancel releases slot; no-show audited.

---

## PR4 — Orders + Items + Quote/Approval
- [ ] Domain: `SvcOrder` + `SvcOrderItem` (`Kind=Service|Part|Labor`), totals derived; status machine (`Draft/Quote→Approved→InProgress→Completed`, `→Cancelled`).
- [ ] Api: order CRUD, item add/update/remove (only pre-terminal), `PATCH /{id}/status` (quote/approve/reject/start/complete/cancel).
- [ ] Optional link `Appointment ↔ Order`.
- [ ] Tests: item math; quote→approve→complete; edit blocked after terminal; oficina + dev shapes.

---

## PR5 — Packages + session control
- [ ] Domain: `SvcPackage` (definition) + `SvcCustomerPackage` (balance: `SessionsUsed ≤ SessionsTotal`, expiry).
- [ ] Api: create package, sell to customer, consume (guards: positive balance, not expired).
- [ ] Tests: sell, consume to zero, over-consume rejected, expiry rejected.

---

## PR6 — Payments → Core + Dashboard
- [ ] Payment action on order/appointment → Core `FinancialMovement` (`Direction=In`, `ContextType=Servico`, `ContextId`, `Status=Confirmed`). Use the PR0-confirmed path or the thin `RegisterServicePaymentUseCase`.
- [ ] Paid → entity locked from edits; pending/paid derived from movements.
- [ ] `SvcDashboardQueryService` (read-only): atendimentos do dia, agenda de hoje, receita do período (movements `Servico`), clientes ativos, profissionais ativos, serviços mais vendidos, pendências.
- [ ] Tests: payment writes one movement; dashboard revenue matches; **no Service-owned money rows**; isolation.

---

## PR7 — Frontend: shell + preset adaptation + screens
- [ ] `src/modules/service`: `api/` (typed DTOs/requests, `apiClient`), `hooks/` (TanStack Query `SERVICE_KEYS` + `staleTime`), `pages/`, `components/`.
- [ ] Preset adaptation layer: labels + capability-driven screen/field visibility from `GET /preset`.
- [ ] Screens (Spec §8): dashboard, agenda, clientes (reuse Core), profissionais, serviços, atendimentos, os, pacotes, financeiro, configurações — real API, **no mocks**, Build-style loading/empty/error/toast/confirm.
- [ ] Routes + `"service"` group gated on any family key.
- [ ] Tests: route gating; capability hides `/os` for salão; smoke per screen.

---

## PR8 — Presets: all 9 + per-preset QA
- [ ] Finalize labels/capabilities for all 9 (Spec §6 matrix → confirmed flags).
- [ ] Optional starter catalog seed per preset.
- [ ] Per-preset QA pass: salão, clínica/odonto, nutri, personal, oficina, dev, autoescola, pet, idiomas — each opens cleanly with correct labels and only its enabled surfaces.
- [ ] Copy review for LGPD non-promises (Spec §10).

---

## Testing strategy
- Mirror `tests/Nexo.IntegrationTests/Build/BuildFlowTests.cs`: a `Service/` folder with one flow test per PR, using the faithful `InMemoryCacheService` (not NoOp), real `TestWebApplicationFactory`.
- Security: extend `TenantIsolationTests` for `Svc*`; module-gate 403/200.
- Target: every P1 flow covered before PR7 frontend.

---

## Definition of done (v1)
- Engine end-to-end for all P1 capabilities, all 9 presets resolving with correct labels/surfaces.
- Payments via Core only; dashboard reads back real movements.
- No fakes, no mocks, no orphan UI. LGPD non-promises honored in copy.
- Integration tests green; isolation + gate proven.

---

## ⛔ Approval gate
**Aguardar aprovação do owner antes da implementação.**
Nothing in PR0–PR8 starts until the owner approves this scope. First action after approval:
re-enter `writing-plans` only if scope changes; otherwise begin **PR0** on a fresh
`feature/orken-service-v1` branch (this `-planning` branch holds spec + plan only).
