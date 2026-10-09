# 07 — Saúde de engenharia (testes, CI/CD, infra, schema, código morto, naming, docs, matriz BE×FE)

Commit auditado: `e8b4873` (branch `feature/orken-service-closure-prC-resume` = master `8f5c6ab` + PR #35). Auditoria somente leitura. Nenhum servidor foi iniciado e nenhum teste foi executado aqui. Os números de execução vêm do briefing: backend 369 unit / 355 integração verdes, vitest 18 arquivos / 178 testes verdes, e2e 17 testes não executados.

> ⚠️ **Verificação urgente (risco ALTO, ainda não confirmado em produção).** O arquivo `nexo-backend/src/Nexo.Api/appsettings.json` está versionado com `Jwt:Secret = "CHANGE_THIS_IN_PRODUCTION_USE_A_LONG_RANDOM_STRING_AT_LEAST_32_CHARS"`. Esse valor tem 66 caracteres, então passa na única validação existente: `JwtTokenService.cs:165` só verifica `secret.Length < 32`. `Program.cs:51` também aceita qualquer valor não nulo.
> **Cenário:** se a variável `Jwt__Secret` não estiver definida no Railway, a API sobe com esse segredo público. Quem tiver o repositório consegue forjar um JWT de qualquer tenant, ou um token `type=platform` (super-admin).
> Não encontrei um guard que recuse o placeholder em Production. Confirmar a variável no Railway e adicionar um fail-fast.

---

## 1. Inventário de testes

**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 50% — a cobertura é forte em Service, Auth e Segurança, mas quase nula nos módulos Store/Varejo, Financeiro global, Platform e em todo o frontend fora do Service. O e2e cobre só autenticação e não roda em CI.

### Backend — contagem por área

A contagem considera métodos `[Fact]`/`[Theory]`. Com as expansões de `InlineData`, o total fecha com 369/355.

| Projeto / pasta | Arquivos | Métodos | Observação |
|---|---|---|---|
| **Unit / Service** | 17 | ~149 (+~80 InlineData) | Domínio Svc*, AvailabilityCalculator, PresetRegistry, comissões |
| Unit / Interpreter | 5 | 61 | RuleBasedAnalyzer, use cases |
| Unit / Integrations | 11 | 84 | Controllers de integração (Barcode, Billing, Storage, Weather, Pdf, Lookup) testados com mocks |
| Unit / Auth, Users | 2 | 16 | `AuthServiceTests`, `UserServiceTests` |
| **Integ / Service** | 12 | 181 | ~52% de toda a integração. `ServiceCommissionTests.cs` sozinho tem 46 |
| Integ / Auth | 6 | 56 | Ciclo de vida, cookies, concorrência, rate limit, bootstrap da plataforma |
| Integ / Security | 7 | 46 | Isolamento de tenant (6) e de loja (4), autorização (14), headers (13), plataforma |
| Integ / Restaurante | 5 | 47 | `RestauranteFlowTests` (24), `DeliveryPortalFlowTests` (14), despesas, CMV |
| Integ / Sales | 1 | 6 | `SaleFlowTests.cs` |
| Integ / Build | 1 | 4 | `BuildFlowTests.cs` |
| Integ / Stock, Storage, Financial, Interpreter | 4 | 5 | 1–2 testes cada |

- **Testes pulados:** nenhum. `grep "Skip ="` não retorna resultado.
- **Infra de teste:** Testcontainers PostgreSQL e um `InMemoryCacheService` fiel (lição registrada: o NoOp mascarava bug de refresh).

### Backend — o que NÃO tem teste de integração

Rotas cobertas foram contadas por `grep "api/..."` em `tests/Nexo.IntegrationTests`. Estas não aparecem:
- `api/varejo/purchases`, `api/varejo/price-lists`, `api/varejo/pdv` → **Compras e listas de preço sem nenhum teste** (unit ou integração).
- `api/reports/*` (sales/inventory/customers), `api/dashboard/summary`.
- `api/categories`, `api/suppliers`, `api/settings`, `api/audit`.
- `api/financial/*`: 3 chamadas só no teste de provisionamento de contas (`DefaultAccountProvisioningTests.cs`, 1 teste). Não há fluxo de pagar/cancelar transação.
- `api/billing/*`: só unit de controller com mocks. O webhook Stripe não tem teste de integração.
- `api/platform/*`: CRUD de tenants, módulos, notas, sessões, MRR/churn, flags e o InterpreterAdmin só aparecem em testes de autorização (403/401) e auditoria. Não há teste funcional.
- `api/restaurante/coupons` (só via `public/coupons/validate`), stages e daily-logs do Build (4 testes cobrem o Build inteiro), `api/v1/movements` (confirm/void/reprocess).

### Frontend — Vitest (`nexo-main/src`)

| Área | Arquivos | Casos |
|---|---|---|
| `modules/service/**` | 12 | ~72 (lib puras + 3 componentes RTL) |
| `modules/service-portal/lib` | 3 | 17 |
| `modules/workspace/resolvePostLogin.spec.ts` | 1 | 12 |
| `src/test/auth.unit.spec.ts` | 1 | 25 |
| `src/test/example.test.ts` | 1 | 1 (placeholder trivial, **obsoleto**) |

- **Zero testes** em `restaurante`, `build`, `sales`/PDV, `products`, `inventory`, `cash`, `customers`, `suppliers`, `users`, `platform`, `portal` (cardápio público) e `billing`.

### E2E — Playwright (`nexo-main/e2e`)

- Único arquivo: `e2e/auth.e2e.spec.ts`, com 17 testes de login/logout.
- Precisa de API e frontend rodando, com credencial padrão `IntegrationTestOnly!123` (linha 29).
- Não roda em CI, não faz parte do `npm test` (`playwright.config.ts`) e não existe e2e de nenhum fluxo de negócio.
- `nexo-main/playwright-fixture.ts` importa `lovable-agent-playwright-config/fixture`, pacote que não está no `package.json`. É um arquivo **morto e quebrado**, herança do Lovable.

### Testes/relatórios obsoletos versionados

- `TEST_SUITE_REPORT.md` (maio/2026, "140+ testes").
- `nexo-backend/test-results.txt`.
- `nexo-main/frontend-test-results.txt`.
- `nexo-backend/test_dashboard.py` e `test_dash_prod.py`. Este último lê variáveis de produção via `railway variables --json`, incluindo `Seed__AdminPassword`, e chama `https://backend-production-b2bc.up.railway.app`.

---

## 2. CI/CD e infraestrutura

**Estado:** 🟠 PARCIAL   **Maturidade:** 40% — o deploy funciona (auto-deploy Railway a partir de `master`), mas o único gate em CI é o backend. Não há CI de frontend, nem IaC, nem health check real, e a observabilidade é mínima.

### Workflows

- `.github/workflows/backend-tests.yml` é o **único** workflow. Faz restore, build, unit e integração (Testcontainers) e roda só em mudanças de `nexo-backend/**`.
- **Não há CI de frontend.** `npm run typecheck`, `lint`, `test` e `build` (todos existem no `package.json`) só rodam localmente. Um PR só de frontend vai para produção sem nenhum gate automatizado do repositório.
- SonarCloud (Automatic Analysis) e GitGuardian rodam como GitHub Apps externos, sem configuração versionada (não há `sonar-project.properties` ativo).
- Não há Dependabot/Renovate nem CodeQL.

### Build e deploy

- **Backend** (`nexo-backend/Dockerfile`):
  - sdk:8.0 → aspnet:8.0, roda como usuário `nexo` não-root, porta 8080;
  - `COPY . .` também leva `_migrations_backup_*`, `*.py` e `test-results.txt` para o stage de build (inofensivo, mas sujo);
  - não há `.dockerignore` no backend.
- **Frontend** (`nexo-main/Dockerfile` + `Caddyfile`):
  - node:20 build → caddy:2-alpine, com SPA fallback e cache imutável de `/assets`;
  - o `npm install` roda **sem lockfile**: `nexo-main/package-lock.json` está no `.gitignore`, então os builds não são reproduzíveis;
  - o comentário do próprio Dockerfile fala em "committed lockfile", o que contradiz o `.gitignore`.
- `nexo-main/railpack.json` (install + build) coexiste com o Dockerfile. É configuração **redundante e ambígua**: qual builder o Railway usa não está documentado.
- `nexo-main/dist/` é **versionado** (57 arquivos, 2,6 MB, último commit em 2026-05-25), mas o Dockerfile reconstrói tudo. O `dist` commitado é **lixo desatualizado** (anterior a todo o frontend do Service) e polui diffs. Ver o gotcha na memória do projeto.
- **Não há `railway.json`/`railway.toml`.** Serviços, variáveis, health check path, réplicas e restart policy vivem só no painel do Railway, sem IaC nem revisão.

### Startup (`Program.cs`)

- **Migrações:** `db.Database.MigrateAsync()` em **todo** boot fora de `Testing` (`Program.cs:~296`).
  - Não há lock nem `--idempotent`, e não há etapa de migração separada do deploy.
  - Uma migração quebrada derruba o boot, e o Railway entra em loop de restart.
- **Seed:**
  - `SeedPlatformAdminAsync()` roda em todos os ambientes, inclusive Production. Ele reaplica o hash da senha a cada boot (`DataSeeder.cs:337`) a partir de `Seed:PlatformEmail/PlatformPassword`.
  - `SeedAsync()` (tenants e usuários demo com senhas fixas no código, `DataSeeder.cs:268/287/305`) só roda fora de Production.
  - Consequência registrada: dados de referência de produção precisam vir de migration (`ConvertLegacyServiceSubscriptions`, `BackfillDefaultFinancialAccounts`).
  - O catálogo `module_definitions` só é populado pelo seeder. Não há InsertData em migration. A verificar como o catálogo de produção é mantido.
- **Rollback:**
  - inexistente como processo;
  - as migrations de dados `20260820093038_ConvertLegacyServiceSubscriptions` e `20260820094836_BackfillDefaultFinancialAccounts` têm `Down()` **vazio de propósito** ("Rolling back means restoring from backup");
  - não há backup automatizado documentado no repositório;
  - o rollback de código depende do redeploy de uma imagem anterior no Railway, sem reverter o schema.
- **Health check:**
  - `app.MapHealthChecks("/health")` registra só `InterpreterStorageHealthCheck` (`DependencyInjection.cs:215`), que verifica um diretório local;
  - **não verifica PostgreSQL nem Redis**, então `/health` responde 200 com o banco fora.

### Variáveis de ambiente lidas (grep de `configuration[...]`, `GetValue`, `GetSection` e `GetConnectionString`)

`ConnectionStrings:Default`, `ConnectionStrings:Redis`, `Jwt:Secret`, `Jwt:Issuer`, `Jwt:Audience`, `Jwt:PlatformAudience`, `Jwt:RefreshAudience`, `Jwt:AccessTokenMinutes`, `Jwt:RefreshTokenDays`, `Cors:AllowedOrigins`, `App:FrontendUrl`, `Resend:ApiKey`, `Seed:AdminPassword`, `Seed:PlatformEmail`, `Seed:PlatformPassword`, `RateLimiting:AuthLogin:*`, `RateLimiting:PublicBooking:*`, `Interpreter:AttachmentsDir`, `Interpreter:Features`, `Integrations:*` (Stripe SecretKey/WebhookSecret/PriceIds, R2 AccountId/AccessKeyId/SecretAccessKey/Bucket/PublicUrl, OpenMeteo, Features.*), `ASPNETCORE_ENVIRONMENT`.

- Não há documentação central dessas variáveis (nem `.env.example` no backend).
- `Integrations:Stripe:PriceIds` só tem chaves `restaurante_*` e `build_*`. Não há price id para `service` nem `varejo` (`appsettings.json`).
- `DependencyInjection.IsProduction()` (linha 268) lê `config["ASPNETCORE_ENVIRONMENT"] == "Production"`. Se a variável não estiver definida, o host assume Production por padrão, mas esse método retorna `false` e liga `EnableSensitiveDataLogging()`, o que loga valores de parâmetros (PII) em produção. Inconsistente com `app.Environment`.

### Logging e observabilidade

- Serilog com Console e File (`logs/nexo-.log`, retenção de 30 dias) fora de Development (`Program.cs:36-43`). No container o arquivo é **efêmero**: some a cada deploy e restart.
- Não há sink externo, APM, Sentry, OpenTelemetry nem métricas. Só os logs de console do Railway.
- `RequestLoggingRedactionMiddleware` existe e é um ponto positivo.
- Anexos do Interpreter usam `LocalAttachmentStorage` (`DependencyInjection.cs:210`), filesystem do container, também **efêmero** no Railway.

### Redis

- É opcional: com `ConnectionStrings:Redis` vazio, o container registra `NoOpCacheService`. Usa fail-open com `AbortOnConnectFail=false` e warmup em background.
- **A correção de auth depende do Redis.** O refresh só é aceito se `refresh:valid:{jti}` existir no cache (`AuthService.cs:129-130`).
  - Com NoOp (Redis não configurado) ou com Redis indisponível ou com eviction, **todo refresh falha** e os usuários são deslogados ao expirar o access token (15 min).
  - A memória do projeto registra timeout de rede privada do Redis no Railway.
  - A branch `origin/fix/redis-connectivity` continua **não mergeada**.
- Outros usos de cache: `DashboardService`, middlewares de tenant e security-stamp, `RequireModuleAttribute`, feature flags, CEP e OpenFoodFacts/OpenMeteo.

### Dependências

- `Nexo.Application.csproj` referencia `Microsoft.EntityFrameworkCore 8.0.8`, enquanto Infrastructure usa 8.0.11. A versão diverge e há vazamento de camada (Application depende de EF).
- `Microsoft.Extensions.Http 10.0.5` e `Http.Resilience 10.7.0` rodam em projeto `net8.0`, misturando a linha 10.x com a 8.x.
- `Dapper` é referenciado em Infrastructure sem nenhum `using Dapper` no código. É **dependência morta**.

### Gaps (ordem de prioridade)

1. Guard de segredos em Production: recusar o placeholder de `Jwt:Secret` e exigir a variável.
2. CI de frontend (typecheck, lint, vitest, build) e, de preferência, o mesmo gate para o PR de backend.
3. Health check com PostgreSQL e Redis (`AddNpgSql`/`AddRedis`) e configuração do health check path no Railway.
4. Lockfile versionado e `npm ci`. Remover `dist/` e `railpack.json`.
5. Sink de logs persistente ou APM. Storage de anexos do Interpreter em R2.
6. Plano de backup/restore documentado, já que as migrations de dados são irreversíveis.
7. Resolver a resiliência do Redis ou tornar o refresh independente do cache.

---

## 3. Revisão de schema (44 migrations + snapshot)

**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 65% — tenant isolation consistente, todas as FKs indexadas e store isolation aplicada nos módulos novos. Há dívidas de modelagem: múltiplos ledgers financeiros, enums mistos, uniques que forçam dados falsos e resíduos legados.

Fonte: `Persistence/Migrations/NexoDbContextModelSnapshot.cs` (86 tabelas, schema `nexo`, histórico em `nexo.__ef_migrations_history`), analisado por script.

### tenant_id / store_id

- **Todas as tabelas de negócio têm `TenantId` com índice.** As 7 sem `TenantId` são globais de plataforma, por design: `tenants`, `platform_users`, `module_definitions`, `feature_flags`, `ai_providers`, `stored_prompt_versions`, `stripe_processed_events`.
- O filtro global vem de `NexoDbContext` (linhas 162-208), aplicado por herança (`TenantEntity`/`StoreEntity`).
- **Entidades com `TenantId` declarado à mão, sem herdar `TenantEntity`**, portanto **sem query filter automático:** `ModuleSubscription`, `ModuleSubscriptionEvent`, `TenantFeatureOverride`, `TenantNote`, `UserSession`. Hoje são acessadas por código de plataforma ou auth, mas qualquer consulta nova a elas precisa filtrar manualmente.
- **`TenantId` sem FK para `tenants`:** `bld_stages`, `bld_daily_logs`, `bld_daily_log_photos`, `bld_budget_items`, `interpreter_telemetry`, `tenant_ai_limits`. A integridade depende do pai.
- `store_id` está presente nas tabelas de Service (exceto `svc_subjects`, que é TenantEntity por decisão), Restaurante (raízes), `products`, `stock_*`, `sales`, `cash_sessions`, `customers` e `food_service_settings`.
  - **Não está presente** em `ret_purchases`, `ret_price_lists`, `financial_*`, `int_movements` (FinancialMovement), `bld_*`, `categories` e `suppliers`. Esses dados são por tenant, não por loja.
  - Para Compras isso gera inconsistência: `products` e `stock_items` são por loja, mas a compra que dá entrada de estoque não registra a loja.
- `customers.store_id` é `Guid?` com `SetNull`, mas `Customer : TenantEntity` não tem filtro de loja. A coluna fica semimorta e ambígua.

### FKs e cascade

- 189 relacionamentos: 101 Cascade, 78 Restrict, 7 SetNull, 3 default.
- Todos os FKs têm índice com o FK como primeira coluna. Nenhum FK ficou sem índice.
- **72 de 75 FKs para `tenants` são CASCADE.** Apagar um tenant apaga vendas, transações financeiras, pagamentos e comissões. Não existe endpoint de delete de tenant hoje, mas um `DELETE` manual no banco é destrutivo e não deixa trilha.
- FKs para `stores`: 28 Restrict, 3 Cascade (`rest_coupons`, `rest_coupon_usages`, `rest_delivery_zones`) e 1 SetNull.
- Cascade em pai→filho de agregados é adequado: sale_items/payments, order_items, package_items, budget_items, recipe_ingredients.
- `RetCustomerPriceList → Customer` em Cascade apaga o vínculo de lista de preço ao apagar cliente. É aceitável.

### Unicidade

Acertos:
- `sales (TenantId,StoreId,Number)`, `rest_orders (TenantId,StoreId,OrderNumber)`, `svc_commission_entries (TenantId,Source,SourceId)` e `ReversalOfEntryId` para idempotência de comissão, `svc_orders.AppointmentId`, `svc_package_usages.AppointmentId`, `stores.PublicSlug`, `stripe_processed_events.StripeEventId`.

Problemas:
- **`customers (TenantId, DocumentNumber)` e `suppliers (TenantId, DocumentNumber)` são UNIQUE com `DocumentNumber` NOT NULL (default `""`).** Isso impede dois clientes sem documento no mesmo tenant.
  - O portal público contorna gravando o telefone como CPF falso: `PublicServicePortalService.cs:285-293` usa `BuildUniqueDocumentAsync(tenantId, phone)` com `DocumentType.Cpf`.
  - Isso **polui o cadastro fiscal** e vai quebrar NF-e e validação de CPF no futuro.
- `stock_items` tem UNIQUE em `ProductId` e também em `(TenantId,StoreId,ProductId)`. O primeiro vem do 1:1 `Product.StockItem` e é coerente porque `Product : StoreEntity`, mas o comentário em `StockItemConfiguration.cs:60` ("por loja, não por tenant") é enganoso. O índice composto é redundante.
- `ret_purchases (TenantId,PurchaseNumber)` é por tenant enquanto vendas são por loja. A numeração é inconsistente entre módulos.

### Enums

- 39 `HasConversion<string>` contra 26 `HasConversion<int>`.
- Gravados como **int**: Build (`bld_projects.status/type`, `bld_budgets.status`, `bld_stages.status`) e Interpreter (`int_movements.direction/nature/context_type/status`, `int_extraction_results.*`, `int_interpretation_suggestions.*`).
- O restante (Sales, Restaurante, Service, Users, Customers…) usa string.
- Enum como int é frágil a reordenação e ilegível em SQL. A API serializa tudo como string (`JsonStringEnumConverter`, `Program.cs:~232`), então a inconsistência é só de persistência.

### Nullable

- Excesso pontual: `rest_delivery_orders` (18/33 colunas nulas: snapshot do cliente e endereço), `module_definitions` (12/19), `svc_professionals` (8/15), `customers` e `suppliers` (8/17).
- Em geral as colunas nulas são opcionais legítimas. Não é um problema sistêmico.

### Restos de modelagem antiga e duplicação

- **Ledgers financeiros paralelos**, sem um razão único:
  1. `financial_transactions` + `financial_accounts` (FinancialTransaction): usados por `SaleService` e `ServiceFinancialPostingService`; o controller `api/financial/*` **não tem UI**.
  2. `int_movements` (FinancialMovement, do Interpreter/IA): é o que o `BuildFinancialSummaryService` lê para o resumo financeiro de obra (`SupplierId` adicionado em `20260616183140_AddSupplierToFinancialMovement`).
  3. `cash_movements` (caixa do PDV).
  4. `rest_expenses` (despesas do restaurante, só no `ExpensesController`).
  5. `svc_payments` + `svc_commission_*` (Service, com posting para FinancialTransaction).

  Os pedidos do Restaurante (`rest_orders` pay) não aparecem em nenhum desses arquivos de posting. O "financeiro" é fragmentado por módulo. Ver `project_orken_service_closure` na memória: "financeiro é FinancialTransaction".
- **Chaves de módulo legadas:**
  - as internas são `varejo`/`restaurante`/`build`/`service`, enquanto o produto se chama Orken Store/Menu/Build/Service;
  - SKUs por vertical (`clinica-medica`, `salao-beleza`, …) foram convertidos por `ConvertLegacyServiceSubscriptions`;
  - ainda há literais `"barbearia"`, `"academia-musculacao"` e `"restaurante-taxa-servico"` no Domain/Application, como presets/flags.
- **Migrations abandonadas:** `nexo-backend/_migrations_backup_20260406_203531/` (5 migrations + snapshot antigos, substituídos por `20260406233622_InitialBaseline`) está **versionado** e fora de qualquer csproj. É lixo.
- **Migration pendente de deploy:** `20261009201827_AddServiceCommissions` (13 FKs, 2 tabelas) existe só nesta branch (PR #35).

---

## 4. Código morto e lixo

**Estado:** ⚫ (itens abaixo)   **Maturidade:** n/a

### Frontend — páginas

- Todos os `*Page.tsx` de `nexo-main/src/modules/**` e `src/pages` estão roteados em `app/router/AppRouter.tsx`.
- A exceção é `service/pages/ServiceOnboardingPage.tsx`, que é renderizado via `ServicePresetContext` e não está morto.

### Frontend — arquivos sem nenhum import (grep de `from '.../<nome>'`)

- **Componentes e hooks de negócio:**
  - `components/shared/OnboardingWizard.tsx`, que chamava `POST /products` e `POST /stock/adjust`;
  - `hooks/useFeatureFlags.ts`, o único consumidor de `GET /api/features`;
  - `modules/inventory/components/InventoryAlertCard.tsx`;
  - `modules/products/components/ProductRulesSection.tsx`;
  - `modules/users/components/PermissionGroupCard.tsx`;
  - `modules/landing/components/LandingCtaBlock.tsx` e `LandingDifferentials.tsx`.
- **Services vazios ("deprecated", `export {}`):** `modules/products/services/productService.ts` e `modules/suppliers/services/supplierService.ts`.
- **Shared não usados:** `components/shared/{ActionBar,FiltersBar,FormShell,MetricBlock,SectionHeader,TableShell}.tsx`.
- **shadcn/ui não usados (19):** accordion, aspect-ratio, avatar, breadcrumb, calendar, carousel, chart, collapsible, command, context-menu, drawer, form, hover-card, input-otp, menubar, navigation-menu, progress, radio-group, resizable, scroll-area, sidebar, slider, toaster, toggle-group. Custo baixo, mas é ruído.
- **Tipos legados:** `modules/inventory/types/index.ts:135-171` ("Legacy types — kept for mock data + deprecated POS service", referenciando um `mockInventory.ts` que não existe mais).
- **Mock em produção:** não encontrei arrays de mock servidos como dado. `LandingHero.tsx` tem um `DashboardMockup` visual, que é legítimo. `modules/settings/data/defaultSettings.ts` é default de formulário.

### Backend — endpoints sem consumidor

Ver a matriz da seção 7. Os principais:
- `api/financial/*` (13);
- `api/varejo/purchases|price-lists|pdv` (15);
- `api/reports/*` (3);
- `api/tenants/*` (2);
- `api/v1/tenants/stopwords|memory-profile` (5);
- `api/v1/interpreter/attachments`;
- `api/v1/movements/{id}`, `/reprocess` e `/void`;
- `api/restaurante/modifier-groups` (5 mutações);
- `PATCH api/products/{id}/prices`;
- `POST api/auth/verify-manager`;
- `POST api/platform/auth/login`, duplicado: o `/api/auth/login` já tem fallback para `PlatformUsers` (`AuthController.cs:111-132`).

### Backend — outros

- `TODO` em `InterpreterAdminController.cs:390`: a chave de API do provedor de IA é gravada só em Base64 ("TODO: replace with real AES-256").
- `Dapper` sem uso.

### Lixo no repositório (raiz)

| Item | Versionado? | Observação |
|---|---|---|
| `nexo_backup.sql`, `nexo_backup_.sql` | **SIM** (commit `8e05975`) | pg_dump 16.11 com 37 tabelas: **6 hashes bcrypt**, 10 e-mails (inclusive `@andradesystems.com.br`) e dados de tenants. Os dois arquivos são quase idênticos. As senhas demo estão em texto puro no `DataSeeder.cs`, o que torna os hashes demo triviais. **Remover e purgar do histórico** |
| `nexo-main.zip` (397 KB) | não (`*.zip` ignorado) | Snapshot antigo do frontend (mar/2026). Apagar localmente |
| `logs/` | não | `nexo-20260407.log` |
| `NEXO_MASTER_CONTEXT.md` | sim | v2.1 de 2026-04-02, obsoleto |
| `TEST_SUITE_REPORT.md`, `SECURITY_CODE_REVIEW_REPORT.md` | sim | 2026-05-11, obsoletos |
| `docs/ORKEN_SYSTEM_AUDIT.md` | sim | 2026-05-25, anterior a todo o Service |
| `docs/llm-wiki-export/` | não (untracked) | Snapshot de 2026-06-24, anterior ao fechamento do Service e às comissões |
| `nexo-backend/_migrations_backup_20260406_203531/` | sim | Migrations mortas |
| `nexo-backend/test-results.txt`, `test_dashboard.py`, `test_dash_prod.py` | sim | Scripts ad-hoc. `test_dash_prod.py` lê segredos do Railway prod |
| `nexo-main/frontend-test-results.txt`, `nexo-main/playwright-fixture.ts` | sim | Resultado velho; fixture Lovable quebrada |
| `nexo-main/dist/` | sim | Build de 2026-05-25, não usado pelo Dockerfile |
| `nexo-main/railpack.json` | sim | Redundante com o Dockerfile |
| `.worktrees/`, `.gstack/`, `.cache_ggshield` | não | Ferramentas locais |

### Branches remotas (`git branch -r`: 55)

- **Mergeadas em `origin/master` (34, podem ser apagadas):**
  - todas as `feature/orken-service-v1-pr*`, `feature/orken-service-public-portal-pr12..14`, `feature/orken-service-portal-pr15/16` e `feature/orken-service-closure-prA/prB`;
  - `feature/delivery-zones-and-coupons`, `feature/orken-build-completion`;
  - `fix/auth-session-hardening`, `fix/orken-build-prod-e2e-blockers`, `fix/platform-admin-security`, `fix/portal-chunk-cycle`, `fix/redis-resilience`, `fix/restaurante-delivery-validation`, `fix/ui-e2e-flow-polish`;
  - `perf/first-load-optimization`, `security/verify-manager-tenant-scope`, `ui/orken-frontend-polish`, `ux/orken-menu-complete-review`, `ux/platform-admin-operator-console`.
- **Não mergeadas:**
  - 15 `archive/*` de 2026-04-13 a 2026-08-20. São WIP arquivados e devem ser avaliados e apagados.
  - `feature/orken-service-closure-prC-comissao` (2026-08-20, 3 commits à frente), provavelmente substituída por `prC-resume`.
  - `feature/orken-service-closure-prC-resume`, a branch atual (8 commits à frente).
  - `feature/orken-service-v1-planning`, só docs.
  - **`fix/redis-connectivity`** (2026-06-16, 2 commits): correção de infra pendente, relevante para o problema de Redis.

---

## 5. Mapa de naming Nexo × Orken

| Ocorrência | Onde | Visível ao usuário? | Risco de mudar | Facilidade |
|---|---|---|---|---|
| Projetos e namespaces `Nexo.Api/Application/Domain/Infrastructure/Shared`, `Nexo.sln`, pastas `nexo-backend`/`nexo-main` | todo o backend e o repositório | não | médio (renomeia tudo e quebra paths de CI/Railway) | difícil. Dívida interna, **não mexer agora** |
| Schema PostgreSQL `nexo` e `nexo.__ef_migrations_history` | `DependencyInjection.cs:59`, todas as migrations | não | **alto** (migração de schema em produção) | difícil. **Manter** |
| JWT `Issuer "nexo-api"`, audiences `nexo-frontend`/`nexo-platform`/`nexo-refresh` | `appsettings.json`, `Program.cs:66-73` | não | alto (invalida todas as sessões) | fácil tecnicamente, mas só em janela planejada |
| Cookies `nexo_access`, `nexo_refresh` | `AuthController.cs:101/157/190`, `Program.cs:92` | só no DevTools | médio (desloga todo mundo) | fácil |
| Chaves de localStorage `nexo:access_token`, `nexo:session`, `nexo:impersonate:*`, `nexo:onboarding:*`, `nexo:setup-dismissed:*`, `nexo:pending_email` | `services/api-client.ts`, `authService.ts`, `ImpersonatePage.tsx` etc. | só no DevTools | médio (desloga, perde preferências) | fácil com migração no client |
| `CompanyName "NexoERP Platform"` / `"NexoERP"` na sessão de plataforma | `AuthController.cs:132, 211` | **sim** (nome exibido no painel da plataforma) | baixo | **fácil, corrigir já** |
| Senha temporária `"nexo@temp"` | `modules/users/services/userService.ts:47` | sim (o usuário recebe) | baixo | fácil. Também é **achado de segurança** |
| Swagger `"Nexo API"` / `"Nexo ERP — Gestão inteligente…"`, logs "Nexo API starting", política CORS `NexoFrontend` | `Program.cs:116, 261-263, 366, 409` | só em dev (Swagger fica desligado em Production) | baixo | fácil |
| Arquivo de log `logs/nexo-.log`, usuário do container `nexo` | `Program.cs:40`, Dockerfile | não | baixo | fácil |
| `.nexo-spinner` / `@keyframes nexo-spin` | `nexo-main/index.html:36-47` | não (só classe CSS) | nenhum | fácil |
| `package.json` `"name": "vite_react_shadcn_ts"` | `nexo-main/package.json` | não | nenhum | fácil (nem é "Nexo", é herança Lovable) |
| Chaves internas de módulo `varejo`/`restaurante` (produto: Orken Store/Menu) | DB `module_subscriptions.module_key`, `ModuleKeys`, frontend `workspace/config.ts` | URLs `/restaurante/*` **são visíveis** | alto (dados de assinatura, price ids Stripe `restaurante_*`) | difícil. Manter como chave interna |
| README `# Nexo — ERP…`, `nexo-main/CLAUDE.md` ("NEXO — Development Guide"), `NEXO_MASTER_CONTEXT.md`, `CORE_DOMAIN_RULES.md` ("NexoERP") | docs | sim, para devs | nenhum | fácil |
| Domínio da API `backend-production-b2bc.up.railway.app` | `nexo-main/.env.production` | sim (Network tab) | médio (cookies SameSite=None, CORS) | moderado. Migrar para `api.orken.com.br` permite apertar o SameSite (ver memória `project_cookie_samesite_topology`) |
| Textos de UI | `nexo-main/src` | — | — | **não encontrei "Nexo" em texto visível do frontend.** O `<title>` é "Orken" e o remetente de e-mail é `"Orken <noreply@orken.com.br>"` (`ResendEmailService.cs:17`) |

O backlog existente `nexo-main/docs/backlog-technical-rebrand-nexo-to-orken.md` cobre localStorage, cookies, spinner, `nexo@temp` e namespaces, e continua válido. Ele **não cobre** o `CompanyName "NexoERP Platform"`, o JWT issuer/audiences nem o Swagger.

---

## 6. Obsolescência da documentação

| Documento | Data | Situação |
|---|---|---|
| `nexo-main/README.md` | abr/2026 | **Obsoleto.** Diz "ERP desktop", marca Nexo e lista rotas que não existem (`/comissoes`, `/relatorios`, `/insights`) e perfis antigos. Não menciona Service, Build, Menu nem o deploy |
| `nexo-main/CLAUDE.md` | mai/2026 | **Obsoleto em pontos críticos.** A linha 28 diz "Currently services use mock data", o que é falso hoje |
| `NEXO_MASTER_CONTEXT.md` | v2.1, 2026-04-02 | Obsoleto (anterior a Build, Service, Platform e rebrand) |
| `nexo-backend/CORE_DOMAIN_RULES.md` | abr/2026 | Parcialmente válido: as regras de Sale ainda batem com `SaleService`. Não cobre os módulos novos |
| `TEST_SUITE_REPORT.md` | 2026-05-11 | Obsoleto ("140+ testes"; hoje são ~724) |
| `SECURITY_CODE_REVIEW_REPORT.md` | 2026-05-11 | Histórico. Os achados foram corrigidos e não serve como estado atual |
| `docs/ORKEN_SYSTEM_AUDIT.md` | 2026-05-25 | Obsoleto (sem Service, sem as correções de auth de junho) |
| `docs/ORKEN_MENU_ARCHITECTURE.md` | 2026-04-26 | Em grande parte válido para o Restaurante/Menu. Não reflete coupons e zones posteriores |
| `docs/ORKEN_INTEGRATIONS_PLAN.md` | 2026-06-15 | Válido como plano. Várias flags seguem `false` (`appsettings.json` Features) |
| `docs/QUESTPDF_LICENSE_DECISION.md` | 2026-06-15 | Válido |
| `docs/releases/2026-05-06-cmv-financeiro-fase3.md` | mai/2026 | Histórico, válido |
| `docs/superpowers/specs/*` e `plans/*` (6 specs, 18 planos) | abr–ago/2026 | Registros de execução. Os de Service v1/portal/closure batem com o código. O plano `2026-06-15-phase3-storage-r2.md` está untracked. Os de food-service phase 1–3 (abr) estão superados |
| `nexo-main/docs/backlog-*.md` (3) | jun/2026 | Ainda válidos como backlog (rebrand, UX do Menu, fluxo de UI) |
| `docs/llm-wiki-export/2026-06-24-orken-codebase-snapshot.md` | jun/2026 (untracked) | Defasado: anterior ao closure do Service, às comissões e ao financeiro do Service |

**Lacunas:**
- não há README de raiz;
- não há runbook de deploy, rollback e backup;
- não há lista de variáveis de ambiente;
- não há documentação da arquitetura atual de módulos e chaves.

---

## 7. Matriz BACKEND × FRONTEND

Método: rotas extraídas de `[Route]`/`[Http*]` em `nexo-backend/src/Nexo.Api/Controllers/**`, cruzadas com chamadas `apiClient.*`, `fetch` e os helpers em `nexo-main/src/**`.

Legenda de status:
- **E2E?** existe/existe → verificar E2E
- **BsU** backend sem UI
- **UsB** UI sem backend/fake
- **Parcial**

| Prefixo / controller | Ações principais | Consumidor frontend | Status |
|---|---|---|---|
| `api/auth` (AuthController) | login, refresh, me, switch-store, logout, register, verify-email, resend-verification, **verify-manager** | `modules/auth/services/authService.ts`, `services/api-client.ts` (refresh) | E2E? (o verify-manager não tem consumidor: o FE usa `/users/validate-manager`) |
| `api/platform/auth` | POST login | nenhum (o `/auth/login` cobre plataforma via fallback) | BsU (duplicado) |
| `api/billing` | checkout, portal, subscriptions, webhook | `services/billing.api.ts` → `modules/billing/pages/AssinaturaPage.tsx` | E2E? (webhook é Stripe; price ids só para restaurante/build) |
| `api/cash` | sessions list/open/get, open, close, movements, close-report.pdf | `modules/cash/api/cash.api.ts`, `CaixaPage.tsx`, `services/pdf.api.ts` | E2E? |
| `api/categories` | GET, GET {id}, POST, PUT, activate, deactivate | `modules/products/api/products.api.ts` (GET/POST/PUT) | **Parcial / UsB:** o FE chama `DELETE /categories/{id}` (`products.api.ts:118`, usado em `ManageCategoriesDialog.tsx:91`), mas **não há `[HttpDelete]` no backend → 405**. activate/deactivate não têm UI |
| `api/customers` | list, paged, get, create, update, activate, deactivate | `modules/customers/api/customers.api.ts` | E2E? |
| `api/dashboard` | summary | `modules/dashboard/api/dashboard.api.ts` | E2E? (sem teste) |
| `api/features` | GET | só `hooks/useFeatureFlags.ts`, que **nenhum arquivo importa** | BsU: as feature flags da plataforma (`PlatformFlagsPage`) **não têm efeito em lugar nenhum** |
| `api/financial` | accounts CRUD + activate/deactivate; transactions pending/get/by-account, create, update, pay, cancel | **nenhum** | **BsU (13 ações).** O "financeiro global" não tem tela |
| `api/integrations` (barcode, cep, cnpj) | GET | `services/integrations.api.ts` | E2E? |
| `api/integrations/storage` | upload, delete | `services/storage.api.ts` | E2E? (StorageEnabled=false por padrão) |
| `api/integrations/weather` | current, history | `services/weather.api.ts` | E2E? (WeatherEnabled=false) |
| `api/platform` (PlatformController) | tenants CRUD/status/modules/impersonate, notes, users reset/force-logout/sessions, stats, health, system/endpoints, audit, trial-expired, plan-history, mrr, churn | `modules/platform/services/platformApi.ts`, `PlatformDashboardPage.tsx` | E2E? (só testes de autorização) |
| `api/platform` (PlatformFlagsController) | flags CRUD/toggle, overrides, tenant flags | `platformApi.ts` (sem `flags/{key}/overrides`) | Parcial (as flags existem, mas nada as lê; ver `api/features`) |
| `api/platform/interpreter` | dashboard, telemetry, costs, providers, rotate-key, prompts, activate, playground | `modules/platform/services/interpreterAdminApi.ts` → `pages/ai/*` | E2E? (chave gravada só em Base64) |
| `api/audit` | GET, stats | `modules/audit/api/audit.api.ts` | E2E? |
| `api/products` | list, paged, get, create, update, **prices (PATCH)**, activate, deactivate, image, sheet.pdf | `modules/products/api/products.api.ts`, `ProductFormPage.tsx`, `pdf.api.ts` | Parcial (o PATCH prices não tem UI) |
| `api/products/{id}/purchase-prices` | GET, POST | `products/components/IngredientPriceSection.tsx` | E2E? |
| `api/sales` | list, paged, get, create, items, confirm, cancel, receipt.pdf | `modules/sales/api/sales.api.ts`, `VendaDetailPage.tsx` | E2E? (6 testes de integração) |
| `api/reports` | sales, inventory, customers | **nenhum** | **BsU** |
| `api/settings` | GET, PUT | `modules/settings/api/settings.api.ts` | E2E? |
| `api/stock` | list, paged, product, movements, adjust | `modules/inventory/api/stock.api.ts` | E2E? |
| `api/stores` | GET, check-slug, public-slug | `modules/stores/services/storesApi.ts`, `users.api.ts` | E2E? |
| `api/tenants` (StoresController) | GET {id}, by-slug | **nenhum** | BsU |
| `api/suppliers` | list, paged, get, create, update, activate, deactivate | `modules/suppliers/api/suppliers.api.ts` | E2E? (sem teste de integração) |
| `api/users` | list, get, create, update, change-password, admin-reset-password, validate-manager | `modules/users/api/users.api.ts`, `profileService.ts` | E2E? |
| `api/varejo/pdv` | resolve-price | **nenhum** (o PDV usa preço do produto) | BsU |
| `api/varejo/price-lists` | CRUD, set-default, products | **nenhum** | **BsU (7)** |
| `api/varejo/purchases` | CRUD, items, confirm, cancel | **nenhum** | **BsU (7), sem testes** |
| `api/v1/build/projects` | CRUD, details, start/pause/complete/cancel, financial-summary | `modules/build/api/build.api.ts` | E2E? |
| `api/v1/build` budgets/budget-items | list, get, create, send, approve, reject, convert, margin, items | `build.api.ts` | E2E? |
| `api/v1/build` stages | list, create, progress, reorder, delete | `build.api.ts` | E2E? |
| `api/v1/build` daily-logs/photos | list, get, create, update, photos, delete photo | `build.api.ts` | E2E? |
| `api/v1/build/dashboard` | GET | `build.api.ts` | E2E? |
| `api/v1/interpreter/analyze` + `api/v1/movements` | analyze, confirm, list, **get, reprocess, void** | `modules/build/api/interpreter.api.ts` (analyze, confirm, list) | Parcial |
| `api/v1/interpreter/attachments` | POST | nenhum | BsU |
| `api/v1/tenants` (TenantInterpreter) | stopwords, memory-profile, rebuild | nenhum | BsU |
| `api/restaurante/areas` | list, get, create, update | `modules/restaurante/api/restaurante.api.ts` | E2E? (o GET {id} não é usado) |
| `api/restaurante/tables` | list, by-area, get, create, update, status, orders | `restaurante.api.ts` | Parcial (by-area, get e PATCH status sem uso) |
| `api/restaurante/orders` | list, get, create, items, item status, **delete item**, close, pay, cancel | `restaurante.api.ts` | Parcial (o DELETE item não tem UI) |
| `api/restaurante/delivery-orders` | list, get, create, manual, accept, reject, status, rider, cancel | `restaurante.api.ts`, `hooks/useDeliveryOrders.ts` | E2E? (o POST interno e o GET {id} não são usados; o público cobre a criação) |
| `api/restaurante/delivery-zones` | GET, PUT | `restaurante.api.ts` | E2E? |
| `api/restaurante/coupons` | list, create, update, delete | `restaurante.api.ts` | E2E? (sem teste direto) |
| `api/restaurante/modifier-groups` | GET, **POST, PUT, modifiers POST/PUT/DELETE** | `restaurante.api.ts:104` (só GET por produto) | **Parcial:** não há tela para criar ou editar adicionais |
| `api/restaurante/recipe-cards` | list, get, by-product, create, update, ingredients, image | `modules/restaurante/api/recipe-card.api.ts` | E2E? |
| `api/restaurante/settings` | GET, PUT, portal, costs | `restaurante.api.ts` | E2E? |
| `api/restaurante/employees` | list, get, create, update | `employees-expenses.api.ts` | E2E? |
| `api/restaurante/expenses` | list, get, create, update, delete | `employees-expenses.api.ts` | E2E? |
| `api/restaurante/financeiro` | cmv-report, summary | `restaurante/api/financeiro.api.ts` | E2E? |
| `api/restaurante/reports` | summary | `restaurante.api.ts` | E2E? |
| `api/public/menu`, `orders`, `delivery-zones`, `coupons/validate` | GET/POST | `modules/portal/api/portal.api.ts` | E2E? (14 testes de integração) |
| `api/public/service/{slug}` (+catalog, professionals, availability, appointments) | GET/POST | `modules/service-portal/api/booking.api.ts` | E2E? (18 testes de integração) |
| `api/v1/service` | preset, settings, settings/preset, public-booking, branding | `modules/service/api/service.api.ts` | E2E? |
| `api/v1/service/professionals`, `catalog`, `subjects` | CRUD + activate/deactivate | `service.api.ts` | E2E? |
| `api/v1/service/appointments` | list, get, create, update, status | `service.api.ts` | E2E? |
| `api/v1/service/orders` | CRUD, from-appointment, status, items | `service.api.ts` | E2E? |
| `api/v1/service/packages`, `customer-packages` | CRUD, price, items, consume, cancel, usages | `service.api.ts` | E2E? |
| `api/v1/service/payments` | list, get, create, void, summaries | `service.api.ts` | E2E? |
| `api/v1/service/records` | list, get, create, delete | `service.api.ts` | E2E? |
| `api/v1/service/commissions` | entries, summary, payouts, pay | `service.api.ts`, `components/CommissionPanel.tsx` | E2E? (PR #35, ainda não está em produção) |
| SignalR `/hubs/restaurant` | KDS | `modules/restaurante/hooks/useKitchenSocket.ts` | E2E? |

---

## Backend sem frontend

- `api/financial/*` (13 ações): contas e transações. É o razão usado por Vendas e Service e não tem tela.
- `api/varejo/purchases/*` (7) e `api/varejo/price-lists/*` (7): Compras e Listas de Preço do Orken Store sem UI e sem testes.
- `GET api/varejo/pdv/resolve-price`.
- `api/reports/sales|inventory|customers`.
- `api/tenants/{id}` e `by-slug`.
- `api/v1/tenants/stopwords|memory-profile`, `api/v1/interpreter/attachments`, `api/v1/movements/{id}`, `/reprocess` e `/void`.
- Mutações de `api/restaurante/modifier-groups`, `DELETE api/restaurante/orders/{id}/items/{itemId}`, `PATCH api/restaurante/tables/{id}/status`, `GET tables/by-area`.
- `PATCH api/products/{id}/prices`, `api/categories/{id}/activate|deactivate`.
- `POST api/auth/verify-manager`, `POST api/platform/auth/login` (duplicado), `GET api/platform/flags/{key}/overrides`.
- `GET api/features`: o único consumidor (`useFeatureFlags`) está morto.

## Frontend sem backend/fluxo real

- `ManageCategoriesDialog.tsx` → `DELETE /api/categories/{id}` **não existe no backend** (405). Excluir categoria quebra.
- `PlatformFlagsPage` gerencia flags que nenhum código lê. Do ponto de vista do produto, a UI não tem efeito.
- `components/shared/OnboardingWizard.tsx` tem chamadas reais, mas o componente nunca é renderizado.
- Não encontrei serviços de frontend retornando mock. `productService.ts` e `supplierService.ts` estão vazios ("deprecated").

## Código legado/morto

- **Backend:**
  - `nexo-backend/_migrations_backup_20260406_203531/`;
  - `Dapper` sem uso;
  - `PlatformAuthController` duplicado;
  - `customers.store_id` semimorta;
  - `test_dashboard.py`, `test_dash_prod.py`, `test-results.txt`.
- **Frontend:**
  - os arquivos listados na seção 4 (8 componentes de negócio, 6 shared, 19 de shadcn/ui, 2 services vazios, tipos legados de inventory);
  - `playwright-fixture.ts` (Lovable);
  - `src/test/example.test.ts`;
  - `dist/` desatualizado;
  - `railpack.json`.
- **Raiz:** `nexo_backup.sql`, `nexo_backup_.sql`, `nexo-main.zip`, `logs/`, `NEXO_MASTER_CONTEXT.md`, `TEST_SUITE_REPORT.md`, `SECURITY_CODE_REVIEW_REPORT.md`, `docs/ORKEN_SYSTEM_AUDIT.md`, `docs/llm-wiki-export/`.
- **Git:** 34 branches remotas mergeadas e 15 `archive/*`.

## Achados de segurança

| # | Severidade | Achado | Evidência |
|---|---|---|---|
| S1 | **Alta (latente, verificar produção)** | Placeholder de `Jwt:Secret` versionado e aceito (66 chars > 32). Sem a variável no Railway, os tokens (inclusive `type=platform`) ficam forjáveis | `appsettings.json` linha 7; `JwtTokenService.cs:165`; `Program.cs:51` |
| S2 | Média | Dumps `nexo_backup.sql`/`nexo_backup_.sql` versionados com 6 hashes bcrypt, e-mails e dados de tenants. As senhas demo estão em claro em `DataSeeder.cs:268/287/305`. Ficam no histórico do Git mesmo após remoção | `git log -- nexo_backup.sql` → `8e05975` |
| S3 | Média | A chave de API de provedor de IA é gravada só em Base64 (reversível) | `InterpreterAdminController.cs:390-392` (TODO) |
| S4 | Média | O rate limit de login e do booking público particiona pelo primeiro IP de `X-Forwarded-For`, que é falsificável. O atacante rotaciona o header e burla o limite | `Program.cs` (policies `auth-login` e `public-booking`), comentário no próprio código |
| S5 | Média (disponibilidade) | O refresh token depende de Redis. Com NoOp ou Redis fora, todos os refresh falham (logout forçado). A branch `fix/redis-connectivity` não está mergeada | `AuthService.cs:129-130`; `DependencyInjection.cs:77-105` |
| S6 | Baixa | `EnableSensitiveDataLogging` é ligado se `ASPNETCORE_ENVIRONMENT` não estiver explicitamente em config, embora o host assuma Production. Pode logar PII | `DependencyInjection.cs:69, 268` |
| S7 | Baixa | Senha padrão previsível `"nexo@temp"` quando o formulário não envia senha | `modules/users/services/userService.ts:47` |
| S8 | Baixa | O script versionado `test_dash_prod.py` extrai `Seed__AdminPassword` de produção via Railway CLI e chama a API prod | `nexo-backend/test_dash_prod.py` |
| S9 | Baixa | CORS de produção sempre inclui `http://localhost:3000/8080/5173` com `AllowCredentials` | `Program.cs:118-124` |
| S10 | Baixa | Tabelas tenant-owned sem query filter (`TenantNote`, `UserSession`, `ModuleSubscription*`, `TenantFeatureOverride`): o isolamento depende de cada query | `Nexo.Domain/Entities/*.cs` (não herdam `TenantEntity`) |

## Testes existentes para esta área

- **Backend:** 36 arquivos de integração (~345 métodos; 355 casos) e 37 arquivos unit (~299 métodos; 369 casos), detalhados na seção 1. CI: `.github/workflows/backend-tests.yml`.
- **Frontend:** 18 arquivos vitest (~127 `it`/`test`; 178 casos com `.each`), concentrados em `modules/service*`, `workspace` e `src/test/auth.unit.spec.ts`. Não há CI.
- **E2E:** `nexo-main/e2e/auth.e2e.spec.ts` (17 testes), manual, precisa da stack rodando.
- **Infra/schema:** não há teste de migrations (up/down), de health check nem de build do Docker.
