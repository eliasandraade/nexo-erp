# Limpeza de branches — outubro de 2026

Branches remotas **já incorporadas ao `master`** apagadas no PR 74. Nenhum commit se perde: todos estão no `master`. Para recriar uma, use o SHA abaixo:

```bash
git push origin <sha>:refs/heads/<nome>
```

Ficaram: as `archive/*` (guardadas de propósito), as não incorporadas, as de PR aberto e duas incorporadas que ainda têm worktree local em uso (`codex/orken-experience`, `feature/delivery-zones-and-coupons`).

| Branch | Último commit | Data |
|---|---|---|
| `docs/orken-product-status-2026-10` | `f0cdf01` | 2026-10-09 |
| `feat/auth-hardening` | `35b500f` | 2026-10-15 |
| `feat/auth-mfa` | `8bdc764` | 2026-10-15 |
| `feat/auth-recovery` | `724a927` | 2026-10-15 |
| `feat/build-closure` | `ceb5aaa` | 2026-10-15 |
| `feat/cash-2` | `447573d` | 2026-10-14 |
| `feat/cash-flow` | `8ad657b` | 2026-10-15 |
| `feat/core-reports` | `ed83883` | 2026-10-15 |
| `feat/financial-ledger` | `aac6e74` | 2026-10-14 |
| `feat/fiscal-foundation` | `b7e3763` | 2026-10-15 |
| `feat/frontend-ci` | `855b751` | 2026-10-15 |
| `feat/health-readiness` | `1ebbedd` | 2026-10-15 |
| `feat/menu-closure` | `167a775` | 2026-10-15 |
| `feat/menu-order-sale` | `bec960b` | 2026-10-15 |
| `feat/observability` | `3dcf523` | 2026-10-15 |
| `feat/payables` | `e890f52` | 2026-10-15 |
| `feat/pdv-payment-integrity` | `6d46ded` | 2026-10-14 |
| `feat/price-lists` | `3378bb2` | 2026-10-14 |
| `feat/private-storage` | `fbad2c4` | 2026-10-15 |
| `feat/purchases` | `a9d25ae` | 2026-10-14 |
| `feat/rbac-backend` | `152a22c` | 2026-10-14 |
| `feat/real-settings` | `d324c99` | 2026-10-15 |
| `feat/receivables` | `f4b496e` | 2026-10-15 |
| `feat/reports-hub` | `6f07820` | 2026-10-15 |
| `feat/sale-returns` | `f80c4c0` | 2026-10-14 |
| `feat/service-agenda` | `6b3b330` | 2026-10-15 |
| `feat/service-finance` | `f5e919e` | 2026-10-15 |
| `feat/service-reminders` | `269801e` | 2026-10-15 |
| `feat/service-self-cancel` | `19280c6` | 2026-10-15 |
| `feat/stock-advanced` | `2f42728` | 2026-10-14 |
| `feat/vertical-ledger` | `5144a63` | 2026-10-15 |
| `feature/orken-build-completion` | `627896c` | 2026-06-16 |
| `feature/orken-service-closure-prA-barbearia` | `51aada0` | 2026-08-20 |
| `feature/orken-service-closure-prB-financeiro` | `b0407f2` | 2026-08-20 |
| `feature/orken-service-closure-prC-resume` | `7e78045` | 2026-10-09 |
| `feature/orken-service-portal-pr15-impeccable` | `fdf29a9` | 2026-06-19 |
| `feature/orken-service-portal-pr16-branding` | `2bc355d` | 2026-06-19 |
| `feature/orken-service-public-portal-pr12` | `8c273ff` | 2026-06-18 |
| `feature/orken-service-public-portal-pr13-frontend` | `8105d74` | 2026-06-18 |
| `feature/orken-service-public-portal-pr14-config-ui` | `d9a30e6` | 2026-06-18 |
| `feature/orken-service-v1` | `528d9e1` | 2026-06-17 |
| `feature/orken-service-v1-pr1-foundation` | `135cf15` | 2026-06-17 |
| `feature/orken-service-v1-pr10-os-pacotes-pagamentos` | `dfbe8b9` | 2026-06-18 |
| `feature/orken-service-v1-pr11-dashboard-qa` | `ea5de72` | 2026-06-18 |
| `feature/orken-service-v1-pr2-subjects-records` | `a790cc4` | 2026-06-17 |
| `feature/orken-service-v1-pr3-appointments-agenda` | `ca53046` | 2026-06-17 |
| `feature/orken-service-v1-pr4-orders-os` | `ca052ca` | 2026-06-18 |
| `feature/orken-service-v1-pr5-packages` | `31ad404` | 2026-06-18 |
| `feature/orken-service-v1-pr6-5-single-service-module` | `6602610` | 2026-06-18 |
| `feature/orken-service-v1-pr6-payments` | `62a21a9` | 2026-06-18 |
| `feature/orken-service-v1-pr7-frontend` | `61de176` | 2026-06-18 |
| `feature/orken-service-v1-pr8-cadastros` | `3e45e81` | 2026-06-18 |
| `feature/orken-service-v1-pr9-agenda` | `9045e17` | 2026-06-18 |
| `fix/audit-p0-hardening` | `10cfa9b` | 2026-10-09 |
| `fix/auth-session-hardening` | `0138dae` | 2026-06-16 |
| `fix/billing-integrity` | `545bee6` | 2026-10-15 |
| `fix/orken-build-prod-e2e-blockers` | `82ba786` | 2026-06-17 |
| `fix/platform-admin-security` | `5b06fd9` | 2026-06-16 |
| `fix/portal-chunk-cycle` | `6075eaa` | 2026-06-16 |
| `fix/redis-resilience` | `988bd3d` | 2026-06-15 |
| `fix/restaurante-delivery-validation` | `c96725b` | 2026-06-16 |
| `fix/security-jwt-secret-and-dumps` | `aba9cba` | 2026-10-09 |
| `fix/sensitive-logging-dev-only` | `8ba6daf` | 2026-10-09 |
| `fix/service-backend-roles` | `064f807` | 2026-10-09 |
| `fix/ui-e2e-flow-polish` | `3eb0f0b` | 2026-06-16 |
| `perf/first-load-optimization` | `898d991` | 2026-06-15 |
| `security/verify-manager-tenant-scope` | `65f3a95` | 2026-06-15 |
| `ui/orken-frontend-polish` | `49e8cbb` | 2026-06-15 |
| `ux/orken-menu-complete-review` | `c536fcb` | 2026-06-16 |
| `ux/platform-admin-operator-console` | `d104f44` | 2026-06-16 |

**Apagadas depois desta lista** (PRs da reta final, já no `master`):

| Branch | SHA | Data |
|---|---|---|
| `chore/nexo-orken-debt` | `948a3d9` | 2026-10-15 |
| `feat/flags-cleanup` | `2bf1794` | 2026-10-15 |
| `chore/dead-code` | `803c030` | 2026-10-15 |

**Ficaram:** `archive/*`; `codex/orken-experience` e `feature/delivery-zones-and-coupons` (já no `master`, mas com worktree local); e as que **não** estão no `master` — `feature/orken-service-closure-prC-comissao`, `feature/orken-service-v1-planning`, `fix/redis-connectivity` e `fix/refresh-rotation-race` (PR aberto de outra sessão). Apagar trabalho não incorporado é decisão do dono.
