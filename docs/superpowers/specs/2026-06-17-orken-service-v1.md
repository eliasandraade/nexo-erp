# Orken Service v1 — Scope & Design Spec

**Date:** 2026-06-17
**Branch:** `feature/orken-service-v1-planning`
**Author:** Elias (via Claude)
**Status:** SCOPE — awaiting owner review (no implementation)
**Roadmap:** Build (done) → **Service (this)** → Agro → Stay → Rent

---

## 1. Context & goal

Orken Build is concluded and validated in production. The next roadmap vertical is **Orken
Service**: a single **engine** for service businesses, configured by **presets** per segment —
explicitly **not** nine separate systems.

v1 targets 9 segments:

| # | Segmento | SKU já no catálogo? |
|---|---|---|
| 10 | Clínicas Médicas e Odontológicas | ✅ `clinica-medica` |
| 11 | Personal Trainers | ❌ |
| 12 | Nutricionistas | ❌ |
| 13 | Oficinas Mecânicas | ✅ `oficina-mecanica` |
| 18 | Programadores Autônomos | ❌ |
| 20 | Autoescolas | ❌ |
| 27 | Pet Shops + Clínicas Veterinárias | ✅ `pet-shop` |
| 28 | Salões de Beleza | ✅ `salao-beleza` |
| 29 | Escolas de Idiomas | ❌ |

This document delivers: audit, architecture, entities, endpoints, screens, presets, scope
(P0/P1/P2/P3), risks, reuse map, and open questions — **for review before building.**

---

## 2. Product decisions (locked with owner, 2026-06-17)

| # | Decision | Choice |
|---|---|---|
| D1 | Packaging vs existing per-vertical SKUs | **Per-vertical SKUs → 1 engine.** Each vertical key (`clinica-medica`, `salao-beleza`, `pet-shop`, `oficina-mecanica`, + new keys) keeps its own price/marketing, but **all unlock the same Service engine** and auto-select the preset. The module gate accepts **any key in the `service` family**. |
| D2 | Appointment vs Order | **Defined by preset.** The engine ships **both** `SvcAppointment` and `SvcOrder`; each preset's **capability flags** decide which surfaces are active. Agenda-centric verticals (salão, clínica, personal, pet, autoescola, idiomas) expose Appointment; order-centric (oficina, dev) expose Order; clínica/pet may use both. |
| D3 | Preset coverage in v1 | **Engine + all 9 presets.** All nine ship in v1 at the **label + capability** level. *Deepening* each vertical (specialized fields/flows) is P2. |
| D4 | Subject of service (pet/vehicle/student) | **Generic `SvcSubject` + JSONB.** One entity (`kind` enum + `MetadataJson`) linked to `Customer`; null when not applicable (salão/dev). |

---

## 3. Audit summary (Fase 1)

### Does Service already exist? — **No.**
- No `service` module in backend (`Domain/Application/Infrastructure/Api` have `Modules/{Build,Restaurante,Varejo,Interpreter}` only).
- No `service` module in `nexo-main/src/modules/` (audit, auth, billing, build, cash, customers, dashboard, inventory, landing, platform, portal, products, profile, reports, restaurante, sales, settings, stores, suppliers).
- No frontend route, no mock, no entity, no seed. Every "service" hit in code is a generic `*Service` class.

### But the Core was pre-wired for it
- 🔑 `Nexo.Domain/Modules/Interpreter/FinancialContextType.cs` already has **`Servico = 3`** (alongside `Obra`, `Loja`, `Departamento`). Service payments plug straight into `FinancialMovement` (`ContextType=Servico`, `ContextId=<appointment/order id>`) — same pattern Build uses with `Obra`. **No money entity is created inside Service.**
- 🔑 `DataSeeder.cs` (≈ line 157) already registers per-vertical `ModuleDefinition` catalog SKUs: `clinica-medica` (R$97), `salao-beleza` (R$69), `pet-shop` (R$79), `oficina-mecanica` (R$79) — plus `academia-musculacao`, `academia-artes-marciais`, `pousada-hotel` (→ Stay), `imobiliaria` (→ Rent). These are **catalog rows only** (no code behind them), created `IsPublished=false`. **No frontend logic references them** (only platform tenant-admin lists them for granting).

### Reusable from Core / Build / Store / Menu
| Asset | Location | Reuse for Service |
|---|---|---|
| `FinancialMovement` + `FinancialContextType.Servico` | `Domain/Modules/Interpreter` | All payments/receipts. `Direction=In` for receita. |
| `Customer` (`TenantEntity`, `AddressJson`) | `Domain/Entities/Customer.cs` | cliente/paciente/aluno/tutor. JSON-column precedent. |
| `ModuleDefinition` + `ModuleSubscription` | `Domain/Entities` | Billing/gating per vertical SKU. |
| `[RequireModule("key")]` (cached 5min) | `Api/Attributes/RequireModuleAttribute.cs` | Backend module gate (needs **family** variant — see P0). |
| `ModuleRoute` + `routes.ts moduleKey` | `nexo-main/src/app/router` | Frontend module gate. |
| `TenantEntity` / `StoreEntity` + interceptor + global query filters | `Domain/Common` | Multi-tenant / multi-store isolation. |
| Storage (R2) + `storageKey` + resolve-at-read | `Infrastructure/Integrations/Storage`, `BuildDailyLogPhoto` | Anexos. |
| Interpreter (text → suggestion → confirm) | `Modules/Interpreter` | Optional: "pagou? já registrado" payment capture. |
| `RestEmployee` (staff = domain entity, no login) | `Domain/Modules/Restaurante` | Precedent for `SvcProfessional`. |
| FSD module + `apiClient` + TanStack Query (`KEYS` factory, `staleTime`) | `nexo-main/src/modules/build` | Frontend layer shape. |
| Dashboard query-service pattern | `Infrastructure/Modules/Build/BuildDashboardQueryService.cs` | `SvcDashboardQueryService`. |
| Clean-Architecture module layering + thin controllers + DTO/paged conventions (`BuildPagedResult<T>`) | `Modules/Build` (all layers) | Direct template. |

### What NOT to reuse / copy
- **SKU-as-physical-module**: do not create 9 modules. One engine, family of billable keys.
- **The "services use mock data" note** in `nexo-main/CLAUDE.md` is stale (Build is real API). Service is born API-connected; **no mock layer**.
- **Build's domain entities** (Project/Stage/Budget/DailyLog) — different shape; reuse the *pattern*, not the classes.
- **Restaurante's table/kitchen/delivery** flow — not relevant to Service.

### Patterns to follow (non-negotiable)
- Aggregates: private ctor + static `Create` with `DomainException` validation, private setters, explicit navigation (no lazy load), state-machine methods.
- Store-scoped operational data extends `StoreEntity`; tenant-shared (Customer) stays `TenantEntity`.
- Controllers thin + `[Authorize]` + module gate; status transitions as `POST /{id}/{action}`; enum parse → `BadRequest`.
- Never `IgnoreQueryFilters()`.

---

## 4. Architecture

### 4.1 One engine, presets as capability descriptors
Service is a single Clean-Architecture module `Modules/Service` (mirrored across Domain /
Application / Infrastructure / Api), entity prefix **`Svc`**. A **preset** is not a table of
features — it is a **descriptor** resolved from the tenant's active service-family module key:

```
ServicePreset {
  key                // "clinica-medica", "salao-beleza", ...
  family             // "service"
  labels             // cliente→paciente/aluno/tutor; profissional→médico/instrutor;
                     // serviço→procedimento/aula; appointment→consulta/aula; order→OS ...
  capabilities {     // which engine surfaces are ON for this vertical
    appointments, orders, quotes, parts, packages,
    subjects(kind?), simpleRecord, commissions, recurrence
  }
  defaultCatalogSeed?  // optional starter services/procedures
}
```

- **Backend**: a static **preset registry** (code-defined, P1) is the source of truth for
  capabilities + validation. A small `SvcSettings` row per store holds *tenant-tunable* config
  (business hours, default slot minutes, label overrides), not the preset identity.
- **Frontend**: reads the resolved preset to render labels and **toggle screens/fields** per
  capability — a single set of screens, adapted, not nine UIs.
- **Preset resolution**: from the tenant's active module key in the `service` family. If a
  tenant somehow has more than one service-family key, pick a deterministic primary (configurable
  later) — see open question Q5.

### 4.2 Module family & gating (D1)
- Define a **service family**: `service` is the logical engine; billable keys
  `clinica-medica | salao-beleza | pet-shop | oficina-mecanica | nutricionista | personal-trainer | autoescola | escola-idiomas | programador-autonomo` all map to it.
- Backend gate: `[RequireServiceModule]` (a family-aware variant of `RequireModule`) → 403 unless
  the tenant has an active subscription to **any** family key. Additive; does not change the
  existing `RequireModule`.
- Frontend: `routes.ts` gains a `"service"` group; routes gate on "any service-family key in
  `session.modules`" (small helper alongside `ModuleRoute`).

### 4.3 Financial integration (reuse, no new money entity)
- A "register payment / mark paid" action creates a **Core `FinancialMovement`**
  (`Direction=In`, `Nature=…`, `ContextType=Servico`, `ContextId=<order|appointment id>`,
  `Status=Confirmed`). Dashboard revenue reads these back — identical contract to Build's
  `ContextType=Obra` financial summary.
- **P0 to confirm:** whether the Core exposes a direct create-confirmed-movement path or only the
  Interpreter (analyze→confirm) flow. If only Interpreter, add a thin additive use case
  `RegisterServicePaymentUseCase` that writes a `FinancialMovement` — no schema change to Core.

### 4.4 Tenancy
- All `Svc*` operational entities extend **`StoreEntity`** (a salão/clínica chain has filiais).
- `SvcSubject` and reused `Customer` stay **`TenantEntity`** (shared across stores), `SvcSubject`
  optionally store-scoped later.

---

## 5. Domain model (proposed)

Entity prefix `Svc`. All extend `StoreEntity` unless noted.

| Entity | Purpose | Key fields | Notes |
|---|---|---|---|
| `SvcProfessional` | profissional/médico/instrutor/professor | Name, Role/Specialty, Color, ContactJson?, DefaultCommissionPct?, `UserId?` (nullable, **no login in v1**), IsActive | Mirror of `RestEmployee`. |
| `SvcCatalogItem` | serviço/procedimento/aula/exame | Name, Description, CategoryId?, DurationMinutes, Price, CommissionPct?, RequiresSubject(bool), IsActive | The "serviços/procedimentos". |
| `SvcAppointment` | slot agendado (agenda) | CustomerId, ProfessionalId, CatalogItemId?, SubjectId?, StartsAt, EndsAt, Status, Notes, `OrderId?` | State: `Scheduled → Confirmed → InProgress → Completed` ; `→ NoShow` ; `→ Cancelled`. Overlap rule per professional. |
| `SvcOrder` | Ordem de Serviço / atendimento billable | CustomerId, ProfessionalId?, SubjectId?, Status, Subtotal/Total (derived), `AppointmentId?` | State: `Draft/Quote → Approved → InProgress → Completed` ; `→ Cancelled`. Quote/approval for oficina/dev. |
| `SvcOrderItem` (child) | linha da OS | Kind(`Service\|Part\|Labor`), CatalogItemId?, Description, Quantity, UnitPrice, Total | peças+mão de obra (oficina); tarefas/horas (dev). |
| `SvcPackage` | definição de pacote/mensalidade | Name, Scope(CatalogItemId?/any), TotalSessions, Price, ValidityDays, Recurrence(`None\|Monthly`) | "pacote com X sessões / mensalidade". |
| `SvcCustomerPackage` | pacote vendido a cliente | CustomerId, PackageId, SessionsTotal, SessionsUsed, PurchasedAt, ExpiresAt, Status | decremented on appointment/order consumption. |
| `SvcSubject` (`TenantEntity`) | pet/veículo/aluno-subject | CustomerId(owner), Kind(`Pet\|Vehicle\|Student\|Generic`), Label, MetadataJson (JSONB), Notes, IsActive | D4. JSONB for plate/model/km, species/breed, etc. |
| `SvcRecordEntry` | observações internas + anexos (timeline) | ContextType(`Customer\|Subject\|Appointment\|Order`), ContextId, AuthorId, Text, AttachmentsJson(storageKeys) | "observações clínicas simples / evolução / prontuário simples" — **internal notes only** (see LGPD). |
| `SvcSettings` (one per store) | config tunável | BusinessHoursJson, DefaultSlotMinutes, LabelOverridesJson, … | preset identity comes from module key, not here. |

**Payments:** none here — `FinancialMovement` in Core (`ContextType=Servico`).
**Dashboard:** `SvcDashboardQueryService` (read-only), not an entity.
**Preset:** static registry in code (`ServicePresetRegistry`), not an entity.

### Avoiding overengineering
- Reuse `Customer` (do **not** create `ServiceCustomerProfile`); vertical-specific subject data
  lives in `SvcSubject.MetadataJson`.
- `SvcOrderItem.Kind` covers parts/labor/tasks — no separate parts/labor tables in v1.
- No money entity, no separate per-vertical tables, no professional-login system in v1.

---

## 6. Presets (the 9) — capability matrix (v1)

`A`=appointments, `O`=orders, `Q`=quotes, `Pt`=parts, `Pk`=packages, `Su`=subjects(kind),
`R`=simpleRecord(notes), `Cm`=commissions, `Rc`=recurrence.

| Preset (key) | Labels (cliente / profissional / serviço) | A | O | Q | Pt | Pk | Su | R | Cm | Rc |
|---|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| `salao-beleza` | cliente / profissional / serviço | ✅ | – | – | – | ✅ | – | – | ✅ | – |
| `clinica-medica` | paciente / profissional / procedimento | ✅ | ◐ | – | – | – | – | ✅ | – | – |
| `nutricionista` | paciente / nutricionista / consulta | ✅ | – | – | – | – | – | ✅ | – | – |
| `personal-trainer` | aluno / personal / sessão | ✅ | – | – | – | ✅ | – | ✅ | – | ◐ |
| `oficina-mecanica` | cliente / mecânico / serviço | ◐ | ✅ | ✅ | ✅ | – | Vehicle | – | – | – |
| `programador-autonomo` | cliente / dev / serviço | – | ✅ | ✅ | – | – | – | – | – | – |
| `autoescola` | aluno / instrutor / aula | ✅ | – | – | – | ✅ | – | ✅ | – | – |
| `pet-shop` | tutor / profissional / serviço | ✅ | ◐ | – | – | ✅ | Pet | ✅ | – | – |
| `escola-idiomas` | aluno / professor / aula | ✅ | – | – | – | ✅ | – | ✅ | – | ✅ |

`◐` = optional/secondary surface for that preset. Matrix is the **review artifact** — exact flags
to be confirmed per vertical during P1.

---

## 7. API contract (proposed)

Base `api/v1/service/*`, `[Authorize]` + `[RequireServiceModule]`. Paged via `SvcPagedResult<T>`.

```
# Bootstrap / preset
GET    /api/v1/service/preset                 -> resolved preset (labels + capabilities)
GET    /api/v1/service/settings
PUT    /api/v1/service/settings

# Professionals
GET    /api/v1/service/professionals
POST   /api/v1/service/professionals
PUT    /api/v1/service/professionals/{id}
POST   /api/v1/service/professionals/{id}/activate | /deactivate

# Catalog (serviços/procedimentos)
GET    /api/v1/service/catalog
POST   /api/v1/service/catalog
PUT    /api/v1/service/catalog/{id}
POST   /api/v1/service/catalog/{id}/activate | /deactivate

# Subjects (pet/veículo/aluno) — only when capability.subjects
GET    /api/v1/service/subjects?customerId=&kind=
POST   /api/v1/service/subjects
PUT    /api/v1/service/subjects/{id}

# Appointments (agenda)
GET    /api/v1/service/appointments?from=&to=&professionalId=&status=
POST   /api/v1/service/appointments
PUT    /api/v1/service/appointments/{id}
PATCH  /api/v1/service/appointments/{id}/status   # confirm|start|complete|no-show|cancel

# Orders (OS / atendimento)
GET    /api/v1/service/orders?status=&customerId=
POST   /api/v1/service/orders
PUT    /api/v1/service/orders/{id}
POST   /api/v1/service/orders/{id}/items
PUT    /api/v1/service/order-items/{id}
DELETE /api/v1/service/order-items/{id}
PATCH  /api/v1/service/orders/{id}/status         # quote|approve|reject|start|complete|cancel

# Packages
GET    /api/v1/service/packages
POST   /api/v1/service/packages
POST   /api/v1/service/customer-packages          # sell to customer
GET    /api/v1/service/customer-packages?customerId=
POST   /api/v1/service/customer-packages/{id}/consume

# Records (observações/anexos)
GET    /api/v1/service/records?contextType=&contextId=
POST   /api/v1/service/records
DELETE /api/v1/service/records/{id}

# Payments (writes Core FinancialMovement, ContextType=Servico)
POST   /api/v1/service/orders/{id}/payments
POST   /api/v1/service/appointments/{id}/payments

# Dashboard
GET    /api/v1/service/dashboard
```

`api/v1/service/customers/*` is **not** created — reuse the existing Core customers API.

---

## 8. Screens (frontend `src/modules/service`)

Route group `"service"`, gated on any service-family key. Labels come from the resolved preset.

| Route | Objetivo | Endpoint | Estados | Ações | Perigosas |
|---|---|---|---|---|---|
| `/service` | Dashboard | `GET /dashboard` | loading/empty/error | — | — |
| `/service/agenda` | Calendário (dia/semana) | `appointments` | loading/empty/error | novo agendamento, mudar status | cancelar/no-show (confirm) |
| `/service/clientes` | Clientes (reuse Core) | customers API | … | criar/editar | desativar |
| `/service/profissionais` | Equipe | `professionals` | … | criar/editar | desativar |
| `/service/servicos` | Catálogo | `catalog` | … | criar/editar | desativar |
| `/service/atendimentos` | Lista de atendimentos | `appointments`/`orders` | … | abrir, finalizar | cancelar |
| `/service/os` | Ordens de serviço (preset O) | `orders` | … | criar, item, orçar, aprovar | cancelar |
| `/service/pacotes` | Pacotes + saldo | `packages`,`customer-packages` | … | criar, vender, consumir | — |
| `/service/financeiro` | Receita do período | `dashboard` + Core movements | … | registrar pagamento | estornar (P2) |
| `/service/configuracoes` | Preset, horários, labels | `settings` | … | salvar | — |

Each screen reuses Build's PageHeader / skeleton / empty / error / toast / destructive-confirm
conventions. Screens for a disabled capability are hidden by the preset (e.g. `/service/os`
hidden for salão).

---

## 9. Business rules

- **Multi-tenant / multi-store isolation** via `StoreEntity` + global query filters (never bypass).
- **Module gate**: every Service endpoint requires an active service-family subscription.
- **Professional / catalog item** active|inactive; inactive items cannot be booked/added.
- **Agenda overlap**: a professional cannot have two overlapping appointments unless marked
  available; enforced server-side on create/reschedule.
- **Appointment status**: `Scheduled → Confirmed → InProgress → Completed`; `→ NoShow`; `→ Cancelled` (terminal). No-show and cancel are terminal and audited.
- **Order status**: `Draft/Quote → Approved → InProgress → Completed`; `→ Cancelled`. Items editable only before `Completed`/`Cancelled`.
- **Cancellation**: terminal; releases the agenda slot; does not delete history.
- **Payment**: pending vs paid derived from linked confirmed `FinancialMovement(In)`; a paid
  order/appointment cannot be edited.
- **Package balance**: `SessionsUsed ≤ SessionsTotal`; consumption requires positive remaining
  balance and a non-expired package.

---

## 10. LGPD & sensitive data (clínicas, nutri, vet)

**MVP scope (what we build):** internal observations (`SvcRecordEntry`), attendance history,
attachments, per-tenant access control (existing isolation), audit trail on privileged actions
where applicable.

**Explicit NON-promises (must not be sold/implied as such in v1):** prontuário médico completo,
sistema hospitalar, conformidade regulatória CFM/CFO, prescrição médica eletrônica. Copy and
marketing must reflect "observações internas / histórico", not "prontuário regulatório".

**P0 guardrails:** sensitive notes scoped to tenant+store; no cross-tenant exposure; attachments
behind the same storage access checks as Build; deletion/retention behavior documented.

---

## 11. Scope — P0 / P1 / P2 / P3

### P0 — riscos / bloqueios (resolve before/with build)
1. **Family-aware module gate** (`[RequireServiceModule]` + frontend helper) — additive; central.
2. **Payment path into Core** — confirm direct create-confirmed-`FinancialMovement` exists; else add thin `RegisterServicePaymentUseCase` (no Core schema change).
3. **Preset registry source-of-truth** (code registry + resolution from module key) decided & stubbed.
4. **Module SKUs**: which keys to **publish**; create 5 missing keys (`nutricionista`, `personal-trainer`, `autoescola`, `escola-idiomas`, `programador-autonomo`) as `ModuleDefinition` rows. **Stripe pricing is owner-owned — not touched here.**
5. **LGPD guardrails & non-promises** locked (Section 10).
6. **Migrations are gated** — none created until owner approves the data model.

### P1 — MVP vendável (the common engine, end-to-end)
- Domain + Application + Infrastructure + Api for: `SvcProfessional`, `SvcCatalogItem`,
  `SvcAppointment` (+agenda+overlap), `SvcOrder`+`SvcOrderItem` (+quote/approval),
  `SvcPackage`+`SvcCustomerPackage`, `SvcSubject`, `SvcRecordEntry`, `SvcSettings`.
- Payments via Core (`ContextType=Servico`), `SvcDashboardQueryService`.
- Module-family gating + seeding + publish of the chosen SKUs.
- **Preset registry + all 9 presets at label+capability level** (D3) + frontend label/capability adaptation.
- Frontend screens (Section 8), wired to real API, no mocks.
- Integration tests per flow (mirror `BuildFlowTests`): agenda, order/quote, package consumption, payment→dashboard, isolation, module gate.

### P2 — presets específicos (deepen each vertical)
- Clínica: retorno/recall; Nutri: plano alimentar simples; Personal: avaliação física + evolução;
  Autoescola: categorias + aulas práticas/teóricas; Idiomas: turmas + presença;
  Pet: vacina/carteira simples; Oficina: histórico por veículo + km; Dev: horas/entregas/board de tarefas.
- Commission **reports**; recurrence/mensalidade automation.

### P3 — backlog
- Lembretes/WhatsApp, portal de auto-agendamento do cliente, no-show automation,
  prontuário evoluído, relatórios avançados, comissão→folha, recursos (salas/equipamentos),
  integração fiscal.

---

## 12. Risks

| Risk | Severity | Mitigation |
|---|---|---|
| **9 presets in v1 → shallow/fake verticals** | High | Engine deep + presets = labels/capabilities only in P1; depth is P2. No fake screens. |
| **LGPD overreach** (clinics/vets/nutri) | High | Section 10 non-promises; internal-notes only; access control + audit. |
| **Family gate touches shared `RequireModule`** | Medium | Add a *new* attribute, don't modify the existing one. |
| **Payment coupling to Core/Interpreter** | Medium | Reuse `FinancialMovement`; additive use case only; confirm path in P0. |
| **Agenda complexity** (overlap, business hours, TZ) | Medium | v1 = single-resource overlap + store business hours; multi-resource/TZ deferred. |
| **Capability-matrix complexity** | Medium | One screen set toggled by capability; registry in code; avoid per-vertical branches in domain. |
| **Scope creep into PDV/stock** | Low | Service has no inventory in v1; parts are free-text `SvcOrderItem`. |

---

## 13. Open questions (for owner)

- **Q1 — Agenda granularity:** v1 = list + day/week calendar with per-professional overlap and store business hours; is multi-resource (salas/equipamentos) or timezone handling needed in v1? *(default: no.)*
- **Q2 — Commissions:** compute-and-report only, or also payable tracking? *(default: report only in P2; capability flag present in P1.)*
- **Q3 — Professional logins:** v1 keeps professionals as domain records (no login, Auth untouched). Confirm. *(default: yes, no logins.)*
- **Q4 — Payment capture UX:** direct "marcar pago" only, or also Interpreter text-capture ("pix recebido do cliente X")? *(default: direct in P1, Interpreter in P2.)*
- **Q5 — Multi-SKU tenant:** if a tenant holds >1 service-family key, how is the primary preset chosen? *(default: deterministic first; configurable later.)*
- **Q6 — Workspace placement:** Service lives as a `"service"` route group inside the existing app shell (like Build), not a separate workspace. Confirm.
- **Q7 — Stripe SKUs:** owner publishes/prices the 5 new keys; this round only adds `ModuleDefinition` rows (unpublished). Confirm Stripe stays out of scope.

---

## 14. Out of scope (v1)
Inventory/stock for parts; fiscal/NF-e; electronic prescriptions; full medical records; client
self-service portal; reminders/WhatsApp; payroll; multi-resource scheduling; Stripe price
creation. (Tracked in P2/P3.)

---

## 15. Verdict
Engine-first, preset-driven, reuse-heavy, no fakes, LGPD-bounded. Ready for owner review →
then `writing-plans` → PR sequence in the companion plan.
