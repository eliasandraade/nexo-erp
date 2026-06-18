# Orken Service v1 — Frontend Stack (PR7 → PR11)

**Mode:** autonomous, hard locks. Build + open stacked PRs only. **No merge, no deploy**, no
env/Redis/Stripe/Auth/SuperAdmin/Build changes, no `dist` commit, no mock/fake UI.

Final validation is done by Elias (ChatGPT) before any merge. Do **not** declare validated.

## Stacking

```
master
 └─ PR7  feature/orken-service-v1-pr7-frontend     (foundation: shell, preset, guards, nav)
     └─ PR8  feature/orken-service-v1-pr8-cadastros   (professionals, catalog, subjects, records)
         └─ PR9  feature/orken-service-v1-pr9-agenda     (appointments / agenda)
             └─ PR10 feature/orken-service-v1-pr10-os-pacotes-pagamentos
                 └─ PR11 feature/orken-service-v1-pr11-dashboard-qa
```

Recommended merge order = PR7, PR8, PR9, PR10, PR11 (bottom-up).

## Architecture decisions (grounded in the backend)

- **Service is a family of 9 vertical module keys**, not a single `"service"` key. `session.modules`
  contains one of: `clinica-medica`, `personal-trainer`, `nutricionista`, `oficina-mecanica`,
  `programador-autonomo`, `autoescola`, `pet-shop`, `salao-beleza`, `escola-idiomas`. Frontend mirrors
  `ServicePresetRegistry.cs` via `SERVICE_FAMILY_KEYS` (`modules/service/preset/serviceFamily.ts`).
  The synchronous module gate uses `hasServiceAccess(session.modules)`.
- **One set of screens, adapted by preset** (decision D2). `GET /v1/service/preset` returns
  `displayName`, `labels`, `capabilities`. Surfaces (Agenda / OS / Pacotes / Pagamentos / Cadastros)
  are shown only when the matching capability is on — single source of truth in
  `serviceSurfaces.ts`, consumed by both the overview page and the sidebar nav.
- **Management-only** (`diretoria` / `gerente`). `canAccessPath` already returns `true` for those
  roles, so **no auth-module file is touched** (respects the AUTH lock). Mirrors the Build precedent.
- **Capability → surface map** (from preset): professionals/catalog always on; subjects iff
  `subjectKind != null`; agenda iff `appointments`; OS iff `orders`; packages iff `packages`;
  payments iff `orders || packages`. Records are contextual (inside subject/customer/order detail),
  not a top-nav item — no list-all endpoint exists.

## Known backend gaps to document (not invent UI for)

- A pure-appointment preset (e.g. `clinica-medica`) has neither orders nor packages, so `SvcPayment`
  (which targets order XOR customer-package) has no attach target → **no Payments surface** for it.
- Quotes/parts capabilities are order-level (rendered inside the OS screen), not separate nav.

## PR7 scope (this PR)

New (`src/modules/service/`): `preset/serviceFamily.ts`, `preset/serviceSurfaces.ts`,
`preset/useServicePreset.ts` (TanStack query + `serviceKeys`), `preset/ServicePresetContext.tsx`
(provider + hooks), `routing/ServiceModuleRoute.tsx` (family guard), `pages/ServiceOverviewPage.tsx`
(real preset-driven landing). Tests: `serviceFamily.spec.ts`, `serviceSurfaces.spec.ts`.

Modified (additive): `app/router/routes.ts` (service nav + `capability?` field),
`app/router/AppRouter.tsx` (register `/service` subtree), `modules/workspace/{types,config}.ts`
(service workspace, family-aware availability), `components/shared/AppSidebar.tsx` (family gate +
capability filter via optional preset context).

Verification per PR: `npx tsc --noEmit`, `npm run build`, `npm test`, then `git checkout -- dist`.
