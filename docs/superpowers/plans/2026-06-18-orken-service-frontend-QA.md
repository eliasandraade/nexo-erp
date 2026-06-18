# Orken Service v1 — Frontend Stack QA / Smoke Checklist (PR11)

Final documentation for the autonomous frontend stack. **Nothing merged or deployed** — this is
for Elias's validation before any merge.

## Stack (bottom-up merge order)

| PR | Branch | Base | Scope |
|----|--------|------|-------|
| #22 | `feature/orken-service-v1-pr7-frontend` | `master` | Foundation: family gate, preset, capability nav, overview |
| #23 | `feature/orken-service-v1-pr8-cadastros` | PR7 | Professionals, catalog, subjects, records |
| #24 | `feature/orken-service-v1-pr9-agenda` | PR8 | Appointments, status machine, overlap |
| #25 | `feature/orken-service-v1-pr10-os-pacotes-pagamentos` | PR9 | OS, packages, customer packages, payments |
| #26 | `feature/orken-service-v1-pr11-dashboard-qa` | PR10 | Dashboard + QA + docs |

Each PR is stacked on the previous; review/merge in order PR7 → PR11.

## Manual smoke (run after the stack is merged to a test env)

Pre-req: a tenant with an active **service-family** module key (e.g. `pet-shop`, `salao-beleza`,
`oficina-mecanica`) and a `diretoria`/`gerente` login.

1. **Workspace** — "Orken Service" appears in the workspace switcher; sidebar shows only the
   surfaces the vertical's preset enables.
2. **Dashboard** (`/service`) — KPIs load real numbers; cards link to their surfaces.
3. **Cadastros** — create/edit/deactivate a professional and a catalog item; create a subject +
   add a text record (if the vertical uses subjects).
4. **Agenda** — create an appointment; try an overlapping one for the same professional → expect
   the overlap toast; walk a booking through Confirm → Iniciar → Concluir; cancel another.
5. **OS** — create an order, add catalog items (total recomputes), advance status; record a
   payment (summary updates), then void it.
6. **Pacotes** — create a package with items; assign it to a customer; consume balance (history
   updates); record + void a payment on the customer package.
7. **Pagamentos** — the global list shows both payments; void works; "Origem" links to the
   order/package.

## Capability matrix (which surfaces a preset shows)

- professionals, catalog → always
- subjects → `subjectKind != null`
- agenda → `appointments`
- OS → `orders`
- packages / customer packages → `packages`
- payments → `orders || packages`

## Known gaps / deferrals (documented, not faked)

- **No aggregate endpoints** — dashboard KPIs are derived client-side from list endpoints
  (`orders` is fetched unfiltered then grouped). A backend summary endpoint would make this
  cheaper; out of scope here (would be a separate backend PR).
- **Pure-appointment verticals** (e.g. `clinica-medica`) have no order/package, so **no Payments
  surface** — `SvcPayment` needs an order/package target (real backend constraint).
- **Record attachments** — backend supports attachment refs; only text records are wired (upload
  UX deferred).
- **Subject `metadataJson`** — structured editor deferred; freeform Notes used.
- **Order**: per-item professional + quantity-edit, and a cancellation-reason prompt, are deferred
  (add/remove + status cover the core loop).
- **Catalog price/commission** are create-only (backend `Update` omits them) — read-only on edit.

## Per-PR verification (all green)

```
cd nexo-main
npx tsc --noEmit      # clean
npm run build         # clean
npm test              # service suites green; only the pre-existing auth.unit/auth.e2e specs fail
git checkout -- dist  # + git clean -fd dist  (dist never committed)
```

The two failing `src/test/auth.*.spec.ts` were confirmed failing on the clean PR7 baseline
(`3061bff`) — pre-existing, unrelated to this stack, and untouchable under the auth lock.

## Locks honored across the whole stack

No merge · no deploy · no `railway up` · no env/Redis/Stripe/Auth/SuperAdmin/Build changes ·
no `dist` commit · no mock/fake UI.
