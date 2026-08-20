# Orken — Snapshot da codebase

> Documento gerado em modo **somente leitura** para alimentar uma LLM Wiki (Obsidian).
> Síntese técnica — nenhum arquivo foi copiado na íntegra; nenhum segredo, token, chave ou valor de variável de ambiente é exposto.

## Data da análise

- **2026-06-24**
- Branch corrente observada durante a análise: `feature/orken-service-portal-pr16-branding` (já mergeada em `master`).
- Último marco de produto: portal público de Serviços (PR #28→#32) concluído e deployado.

## Escopo analisado

Repositório monorepo com dois apps principais e documentação de apoio:

- `nexo-backend/` — API .NET 8 (Clean Architecture).
- `nexo-main/` — frontend React/Vite.
- `docs/` — specs, plans e auditorias (incl. `docs/ORKEN_SYSTEM_AUDIT.md`, `docs/ORKEN_MENU_ARCHITECTURE.md`, `docs/ORKEN_INTEGRATIONS_PLAN.md`, `docs/superpowers/**`).
- `.github/workflows/` — CI.
- `docker-compose.dev.yml` — Postgres de desenvolvimento.

Não foram inspecionados `node_modules/`, `bin/`, `obj/` nem valores de configuração sensíveis.

## Resumo executivo

**Orken** (nome interno legado: **Nexo**) é um **ERP/“sistema operacional empresarial” modular e multi-tenant** para PMEs brasileiras (restaurantes, bares, lojas, prestadores de serviço, obras). Um **Core** (PDV, estoque, caixa, vendas, clientes, financeiro) é complementado por **módulos verticais** ativáveis por assinatura: **Orken Menu** (`restaurante`), **Orken Build** (`build`/obras), **Orken Service** (`service`/serviços) e **Varejo**. Há ainda um **motor de interpretação** (OCR/IA leve para lançamentos) e um **painel de plataforma** (super-admin).

O backend é **.NET 8 em Clean Architecture** (Domain/Application/Infrastructure/Api) sobre **PostgreSQL + EF Core**, com **multi-tenancy por filtro global de query** e isolamento adicional por loja (store). O frontend é **React 18 + TypeScript + Vite + Tailwind + shadcn/ui + TanStack Query**, organizado por módulos (estilo feature-sliced). Deploy em **Railway** (auto-deploy ao mergear em `master`), frontend servido por **Caddy**.

Estado: **maduro e em produção**. O ciclo mais recente — **portal público de agendamento do Orken Service** (backend + frontend + config UI + redesign premium + branding real) — está **mergeado e deployado**, aguardando QA visual final.

## Visão do produto

- **Proposta** (de `nexo-main/CLAUDE.md` › Design Context): “a camada de controle entre o caos da operação e a decisão do dono”. Não é ERP corporativo; é operacional, direto, para quem opera sob pressão sem equipe de TI.
- **Core** = destaque (PDV, estoque, caixa). **Verticais** = “tem mais se precisar”.
- **Diferencial declarado**: inteligência/automação de lançamentos (texto → OCR → interpretação → sugestão → confirmação).
- **Multi-tenant + multi-loja (galpão)**: um tenant pode ter várias lojas; dados operacionais são particionados por `StoreId`.
- **Marca/UX**: dark mode (navy/indigo) no painel; tipografia Inter; o **portal público de serviços** foge disso com tema **claro adaptativo por ramo** (ver Frontend).

## Stack técnica

**Backend** (`nexo-backend/`)
- .NET 8, C#; solução `nexo-backend/Nexo.sln`.
- Projetos: `Nexo.Domain`, `Nexo.Application`, `Nexo.Infrastructure`, `Nexo.Api`, `Nexo.Shared` (+ testes `Nexo.UnitTests`, `Nexo.IntegrationTests`).
- EF Core + Npgsql (PostgreSQL, schema `nexo`); FluentValidation; Serilog; SignalR (Hubs); StackExchange.Redis (cache opcional, fail-open / NoOp).
- Testes: xUnit + FluentAssertions; integração via **Testcontainers PostgreSQL** (exige Docker).

**Frontend** (`nexo-main/`)
- React 18, TypeScript 5, Vite 5, TailwindCSS 3, shadcn/ui (Radix), TanStack Query 5, React Router 6, Recharts, lucide-react, Sonner (toasts).
- Porta dev `8080`; cliente HTTP central `src/services/api-client.ts` (`apiClient`).
- Fontes do portal público: Fraunces + Manrope (carregadas só no escopo do portal).

**Infra/CI**
- Railway (deploy), Caddy (frontend). CI: `.github/workflows/backend-tests.yml` + SonarCloud + GitGuardian (checks de PR).

## Estrutura do repositório

```
NexoERP/
├─ nexo-backend/            # API .NET 8 (Clean Architecture)
│  ├─ src/
│  │  ├─ Nexo.Domain/         # Entidades, value objects, regras de domínio, enums
│  │  ├─ Nexo.Application/    # Casos de uso (Features/ + Modules/), DTOs, validators, interfaces
│  │  ├─ Nexo.Infrastructure/ # EF Core, repositórios, multi-tenancy, integrações, cache, email, SignalR
│  │  ├─ Nexo.Api/            # Controllers, middleware, Program.cs (pipeline)
│  │  └─ Nexo.Shared/         # Tipos compartilhados
│  └─ tests/                  # Nexo.UnitTests, Nexo.IntegrationTests (Testcontainers)
├─ nexo-main/               # Frontend React/Vite
│  └─ src/modules/<modulo>/   # api/ hooks/ pages/ components/ lib/ types/
├─ docs/                    # specs, plans, auditorias, releases
├─ .github/workflows/       # backend-tests.yml
└─ docker-compose.dev.yml   # Postgres de dev (porta 5433)
```

Camadas de domínio/aplicação por módulo: `Nexo.Domain/Modules/{Build,Interpreter,Restaurante,Service,Varejo}` e `Nexo.Application/Modules/{...}`; o Core fica em `Nexo.Application/Features/{Sales,Products,Stock,Customers,Cash,Financial,...}`.

## Backend

- **Padrão**: Clean Architecture. `Nexo.Api/Controllers/` expõe REST; `Nexo.Application` orquestra casos de uso; `Nexo.Domain` concentra invariantes; `Nexo.Infrastructure` implementa persistência/integrações.
- **Pipeline** (`nexo-backend/src/Nexo.Api/Program.cs`): autenticação JWT → `TenantResolutionMiddleware` → autorização; `ExceptionHandlingMiddleware` mapeia exceções de domínio para HTTP (`NotFoundException`→404, `ConflictException`→409, `ForbiddenException`→403, `DomainException`→422, `ValidationException`→400); `RateLimiter` (políticas `auth-login` e `public-booking`); JSON camelCase + enums como string.
- **Controllers (amostra)**: Core em `Controllers/*.cs` (Auth, Sales, Products, Stock, Cash, Customers, Suppliers, Financial, Reports, Dashboard, Settings, Audit, Stores/Store, Billing, Features, ProductPurchasePrices); verticais em `Controllers/Modules/{Restaurante,Build,Service,Interpreter,Varejo}/*`; públicos (sem auth) em `Controllers/Public/` (`PublicOrdersController`, `PublicServiceController`); plataforma em `PlatformController`, `PlatformAuthController`, `PlatformFlagsController`; integrações em `Controllers/Integrations/` (Storage, Barcode, Lookup, Weather).
- **Repositórios**: `Nexo.Infrastructure/Repositories/` (Core + `Repositories/Modules/{...}`); leitura pública usa `IgnoreQueryFilters()` com `tenantId`/`storeId` explícitos.

## Frontend

- **Organização modular** (`nexo-main/src/modules/<nome>/`): `api/`, `hooks/` (TanStack Query), `pages/`, `components/`, `lib/`, `types/`.
- **Roteamento**: `src/app/router/AppRouter.tsx` (lazy chunks; guards `ProtectedRoute`, `ModuleRoute`, `RoleRoute`, `PlatformRoute`, `ServiceModuleRoute`). Layouts em `src/app/layouts/` (`MainAppLayout`, `PosLayout`, `WaiterLayout`, `KitchenLayout`, `PlatformLayout`).
- **Sidebar/nav**: `src/components/shared/AppSidebar.tsx` + `src/app/router/routes.ts` (grupos por workspace; itens do Service são capability-gated).
- **Acesso a dados**: `src/services/api-client.ts` (real, JWT) — **não** mock (apesar do README dizer o contrário; ver Contradições).
- **Rotas públicas (customer-facing)**:
  - Restaurante: `/:slug` (cardápio) e `/rastrear/:token` — `src/modules/portal/`.
  - Serviço: `/agendar/:slug` — `src/modules/service-portal/` (redesign premium com **tema adaptativo por ramo** em `lib/portal-theme.ts`).

## Banco de dados e migrações

- **PostgreSQL**, schema `nexo`, histórico em `__ef_migrations_history`.
- **~40 migrações** em `nexo-backend/src/Nexo.Infrastructure/Persistence/Migrations/` (baseline `InitialBaseline` → … → `AddServicePublicBooking` → `AddServicePortalBranding`).
- **Padrão de migração** observado: aditivo (novas tabelas/colunas nullable ou com default); migrações destrutivas são evitadas. Aplicadas automaticamente no boot fora de produção (e via `Database.MigrateAsync` nos testes).
- **Modelo multi-tenant**: `TenantEntity` (TenantId) e `StoreEntity` (TenantId + StoreId) em `Nexo.Domain/Common/`; filtros globais de query por tenant/store; `TenantSaveChangesInterceptor` injeta TenantId/StoreId no INSERT e bloqueia cross-tenant (no-op quando não há tenant resolvido — caminho público).
- **Entidades-chave**: `Store` (com `PublicSlug` globalmente único), `Customer`, `Sale`/`StockMovement`/`CashMovement` (append-only), `FinancialMovement`; verticais `Rest*` (restaurante), `Build*` (obras), `Svc*` (serviços).

## Autenticação e permissões

- **JWT** (access + refresh) — `Nexo.Infrastructure/Auth/` (`JwtTokenService`, `PasswordHasher`, `SessionStoreService`, `RegistrationService`).
- **Resolução de tenant**: `Nexo.Api/Middleware/TenantResolutionMiddleware.cs` (lê claim, valida usuário↔tenant e status, popula `ICurrentTenant` com cache Redis 5min; pula requisições anônimas e tokens de plataforma).
- **Perfis (roles)**: Diretoria, Gerente, Vendedor, Estoquista (gating de rota no front via `RoleRoute`; no back via `[Authorize]` + `[RequireModule]`/`[RequireServiceModule]`).
- **Módulos como entitlement**: `ModuleSubscription` por tenant; gate de Serviço é “family-aware” (`ServicePresetRegistry`/`RequireServiceModule`).
- **Plataforma (super-admin)**: tokens `type=platform`, políticas dedicadas, auditoria de ações privilegiadas, rate-limit de login. Hardening documentado em `docs/superpowers/specs/2026-06-16-platform-admin-security-design.md`.
- **Segurança de transporte**: cookies SameSite=None (frontend e API em domínios distintos — ver memória de topologia de cookies).

## Integrações externas

Em `nexo-backend/src/Nexo.Infrastructure/Integrations/` (todas **feature-flagged** via `IntegrationFeatureFlags`; `Composite`/`Common` orquestram):

- **Storage** (R2/S3-like, `IStorageProvider`) — upload por contexto via `Controllers/Integrations/StorageController.cs` (`product-image`, `restaurant-logo/cover`, `build-daily-log`, `service-record`, `service-portal-logo/cover`); chave por `tenants/{tenantId}/...`.
- **Stripe** — billing/assinaturas (`modules/billing` no front).
- **Email** — `Nexo.Infrastructure/Email/` (Resend quando há API key; `ConsoleEmailService` como fallback).
- **ViaCep / BrasilApi** — CEP/endereço/CNPJ (lookup).
- **OpenFoodFacts** — dados de produto por código de barras.
- **Weather** — clima (desabilitado por flag por padrão).
- **Pdf** — geração de relatórios (QuestPDF; decisão de licença em `docs/QUESTPDF_LICENSE_DECISION.md`).
- **Barcode** — `Controllers/Integrations/BarcodeController.cs`.

Plano em `docs/ORKEN_INTEGRATIONS_PLAN.md`.

## Módulos e funcionalidades

**Core** (Features em `Nexo.Application/Features/`): PDV/Vendas, Produtos, Estoque (movimentos/ajustes/transferências), Clientes, Fornecedores, Caixa (sessão: abertura/sangria/suprimento/fechamento/divergência), Financeiro, Categorias, Usuários/Permissões, Configurações, Auditoria, Dashboard, Relatórios.

**Orken Menu** (`restaurante`): mesas/áreas, comandas (`RestOrder`), KDS/cozinha (SignalR), Delivery Hub (inbox multicanal `RestDeliveryOrder`), fichas técnicas + CMV, modificadores, cupons, zonas de entrega, **portal público de cardápio/pedido** (`/:slug`, `/api/public/...`). Arquitetura em `docs/ORKEN_MENU_ARCHITECTURE.md`.

**Orken Build** (`build`): projetos/obras, etapas, orçamentos, diário de obra (com fotos), resumo financeiro/dashboard.

**Orken Service** (`service`): **um módulo comercial, 9 presets internos** (clínica, nutri, personal, oficina, programador, autoescola, pet-shop, salão, escola) selecionados no onboarding (`SvcSettings.PresetKey`). Entidades `Svc*`: profissionais, catálogo, subjects (pet/veículo/aluno), agenda (`SvcAppointment` com máquina de estados + overlap), ordens de serviço (`SvcOrder`), pacotes (`SvcPackage`/`SvcCustomerPackage`), pagamentos (`SvcPayment`, registro standalone — **não** integra ao financeiro global no v1), prontuário leve (`SvcRecordEntry`). **Portal público de agendamento** (`/agendar/:slug`, `/api/public/service/{slug}`) + página de configuração `/service/portal` + branding real.

**Interpreter** (motor de interpretação/IA): analisadores (rule-based + stub Claude), extração, sugestões, correções, memória por tenant — `Nexo.*/Modules/Interpreter`.

**Plataforma** (`modules/platform`): tenants, trial, atividade, sistema, flags, operações de IA (dashboard/playground/providers/telemetry/costs/prompts).

## Estado atual

- **Em produção** (Railway). Prod backend: `https://backend-production-b2bc.up.railway.app` (`/health` → "Healthy"); frontend: `https://app.orken.com.br`.
- **Service Public Portal**: backend (#28), frontend público (#29), config UI (#30), redesign Impeccable (#31) e branding real (#32) **mergeados e deployados**.
- **Suíte de testes**: ~**351 unit + ~304 integration** (backend) verdes; frontend com Vitest (módulos service/portal cobertos).
- **CI/Gates**: `backend-tests` (build+unit+integration), SonarCloud e GitGuardian verdes nos últimos PRs.

## O que parece validado

- Núcleo transacional (PDV, Caixa, Estoque, Vendas) — coberto por testes de integração e descrito como operacional.
- Multi-tenancy/isolamento (testes `Security/TenantIsolationTests`, `StoreIsolationTests`).
- Auth (login/refresh/rate-limit/sessões) — `Auth/*Tests`.
- Restaurante (fluxo de portal/delivery/KDS) — `Restaurante/DeliveryPortalFlowTests` (14 E2E) e `RestauranteFlowTests`.
- Orken Service v1 + portal público — validado em prod por sinais HTTP; visual do portal revisado por screenshots locais (harness descartável). Decisões e estados registrados em `docs/superpowers/plans/2026-06-17-orken-service-v1.md`.

## Pendências e próximos passos

- **QA visual final** do portal público de serviços (configurar tenant Service → `/agendar/{slug}` → ponta a ponta) — pendente do dono.
- **Integração de pagamento do Service ao financeiro global** (`FinancialMovement` ContextType=Servico=3) — explicitamente adiada (concern futuro).
- **Reversão/estorno** de consumo de pacote no Service — adiado.
- **Webhooks de canais externos** do restaurante (iFood/Rappi/AnotaAí) — enums existem, receivers não.
- **Taxa de entrega configurável**, enforcement de horário de funcionamento (restaurante) — backlog em `docs/ORKEN_MENU_ARCHITECTURE.md`.
- **Atualizar README/CLAUDE.md** do frontend (descrevem estado mock/aspiracional — ver Contradições).

## Decisões arquiteturais identificadas

- **Clean Architecture** estrita no backend; domínio sem dependência de infraestrutura.
- **Multi-tenancy por filtro global de query** + `StoreEntity` para isolamento por loja; `TenantSaveChangesInterceptor` como guarda de INSERT/UPDATE e ponto de injeção de IDs.
- **Caminho público sem auth** reaproveitado entre restaurante e serviço: resolução por `Store.PublicSlug` (globalmente único) + `IgnoreQueryFilters()` com tenant/store explícitos; preço/slots sempre recalculados no servidor (cliente nunca envia valores confiáveis).
- **Orken Service = 1 módulo, N presets internos** (não N módulos) — correção v1.1; preset adapta labels/capabilities de uma única base de telas.
- **Append-only** para `StockMovement`/`CashMovement` (correções via lançamentos compensatórios).
- **Agregados** (ex.: `SvcOrder`+itens) seguindo padrão BuildBudget; itens persistidos via repositório próprio para evitar bug de tracking de backing-field readonly.
- **Integrações feature-flagged** + fail-open (Redis NoOp; storage 404 quando desabilitado).
- **Portal público com tema isolado** (CSS vars com escopo) para não herdar o dark do painel.
- **Migrações sempre aditivas** + verificação `dotnet ef migrations has-pending-model-changes`.

## Riscos técnicos e pontos de atenção

- **`nexo-main/dist` versionado no git** — builds poluem a árvore; é necessário `git checkout -- dist` após `npm run build` para não commitar artefatos (gotcha recorrente).
- **README/CLAUDE.md do frontend desatualizados** (afirmam “serviços usam mock / backend é futuro”), enquanto o backend está completo e o front usa `apiClient` real.
- **2 testes pré-existentes falhando** em `nexo-main/src/test/auth.*` (fixture JWT base64) — não relacionados, mas ruído na suíte.
- **SonarCloud gate**: `new_security_hotspots_reviewed` exige 100% — **qualquer regex hardcoded** (mesmo seguro) vira “security hotspot” e reprova o PR; padrão adotado = evitar regex (`Uri.IsHexDigit`/char-set).
- **Concorrência de overlap** de agendamento sem constraint de exclusão no DB (aceitável para operação single-operator v1; raro 500 em corrida).
- **Exibição de horário no portal usa fuso do navegador** (o instante UTC enviado é correto; o display pode divergir para visitante em outro fuso até expor o `TimeZoneId` da loja).
- **Pagamento do Service é registro standalone** — não reflete em financeiro/caixa global (decisão consciente; pode confundir quem espera integração).
- **Acoplamento a Railway/Caddy** para deploy (sem IaC versionado evidente no repo).

## Arquivos importantes

- `nexo-backend/Nexo.sln`; `nexo-backend/src/Nexo.Api/Program.cs` (pipeline, rate-limit, DI).
- `nexo-backend/src/Nexo.Api/Middleware/TenantResolutionMiddleware.cs` e `ExceptionHandlingMiddleware.cs`.
- `nexo-backend/src/Nexo.Infrastructure/MultiTenancy/TenantSaveChangesInterceptor.cs`; `Nexo.Domain/Common/{TenantEntity,StoreEntity,BaseEntity}.cs`.
- `nexo-backend/src/Nexo.Infrastructure/DependencyInjection.cs` e `Nexo.Application/DependencyInjection.cs` (mapa de serviços/repos).
- `nexo-backend/src/Nexo.Infrastructure/Persistence/NexoDbContext.cs` + `Persistence/Migrations/`.
- `nexo-backend/src/Nexo.Api/Controllers/Public/PublicServiceController.cs` + `Nexo.Application/Modules/Service/Public/` (portal público de serviços).
- `nexo-main/src/app/router/AppRouter.tsx`, `src/services/api-client.ts`, `src/components/shared/AppSidebar.tsx`.
- `nexo-main/src/modules/service-portal/lib/portal-theme.ts` (temas por ramo) e `modules/portal/` (restaurante).
- Documentação: `docs/ORKEN_SYSTEM_AUDIT.md`, `docs/ORKEN_MENU_ARCHITECTURE.md`, `docs/ORKEN_INTEGRATIONS_PLAN.md`, `docs/superpowers/specs|plans/**`, `NEXO_MASTER_CONTEXT.md`, `TEST_SUITE_REPORT.md`, `SECURITY_CODE_REVIEW_REPORT.md`.

## Como rodar localmente

> Pré-requisitos: .NET 8 SDK, Node 18+, Docker (Postgres + testes de integração). Credenciais de dev ficam em `docker-compose.dev.yml` e `appsettings.Development.json` — **não reproduzidas aqui**.

**Backend**
1. Subir Postgres de dev: `docker compose -f docker-compose.dev.yml up -d` (expõe Postgres em `localhost:5433`, db `nexo_dev`).
2. Rodar a API: a partir de `nexo-backend/src/Nexo.Api`, `dotnet run` (ambiente Development → aplica migrações e roda o `DataSeeder`; Redis é opcional, com fallback NoOp). A API escuta em `http://localhost:5000`.
3. Testes: `dotnet test` em `nexo-backend/` (a suíte de integração sobe Postgres efêmero via Testcontainers — **requer Docker**).

**Frontend** (`nexo-main/`)
1. `npm install`.
2. `npm run dev` (porta **8080**). A base da API vem de `VITE_API_BASE_URL` (em `.env.local`, aponta para `http://localhost:5000/api`).
3. Build: `npm run build`; testes: `npm test` (Vitest). **Lembrete**: `dist/` é versionado — restaurar após build.

## Deploy e produção

- **Railway** com **auto-deploy ao mergear em `master`** (o fluxo do time NÃO usa `railway up`; merge dispara build+deploy).
- **Frontend** servido por **Caddy** (Dockerfile próprio); detecção de deploy do front = mudança do hash `index-<hash>.js` em `index.html`.
- **Backend**: deploy aplica migrações no boot; sinal de deploy = rota nova passando de 404→401 (rota autenticada) ou 404→405 (rota só-PUT) sem token.
- **URLs públicas** (não-secretas): frontend `app.orken.com.br`; backend `backend-production-b2bc.up.railway.app` (`/health` → "Healthy").
- **CI/Gates de PR**: `.github/workflows/backend-tests.yml` (build + unit + integration; só roda quando `nexo-backend/**` muda) + **SonarCloud** (ratings, duplicação ≤3% em código novo, security hotspots 100% revisados) + **GitGuardian**. Migrações EF são excluídas do CPD via UI do SonarCloud (Automatic Analysis ignora `sonar-project.properties` in-repo).

## Contradições ou pontos em aberto

- **Mock vs. backend real**: `nexo-main/README.md` e `nexo-main/CLAUDE.md` afirmam “serviços usam dados mock / backend .NET é futuro”. Na prática há backend .NET completo e o front consome `apiClient` real. Documentação desatualizada.
- **Nome do produto**: “Nexo” (legado, README/namespaces `Nexo.*`) vs. “Orken” (marca atual, domínio `orken.com.br`). Ambos coexistem no código.
- **`master` local pode ficar atrás do remoto** após merges via GitHub (cosmético; os diffs no GitHub são a fonte de verdade).
- **Cobertura de testes do frontend**: forte em alguns módulos (service/portal), ausente/leve em outros; não há gate de cobertura aparente.
- **Sem IaC/observabilidade versionados** no repo (deploy e logs vivem no Railway).

## Recomendações para a wiki

- Criar notas-âncora por **módulo** (Core, Menu, Build, Service, Interpreter, Platform) ligando às specs em `docs/superpowers/`.
- Nota dedicada **“Multi-tenancy & isolamento”** (TenantEntity/StoreEntity, interceptor, query filters, caminho público) — é o conceito transversal mais importante.
- Nota **“Portais públicos”** unificando restaurante (`/:slug`) e serviço (`/agendar/:slug`): padrão de slug, segurança, temas.
- Nota **“CI/Gates & gotchas”**: SonarCloud (regex hotspot, duplicação), GitGuardian, `dist` versionado, deploy Railway (sinais de rollover).
- Manter um **changelog de PRs** (Service #28–#32 e anteriores) — o histórico de decisões está rico em `docs/superpowers/` e nas memórias do projeto.
- Marcar como **TODO de doc**: atualizar README/CLAUDE.md do frontend para refletir o backend real.

---

*Snapshot somente leitura — síntese técnica para LLM Wiki. Sem segredos, sem cópia integral de arquivos. O código é o árbitro final em qualquer divergência.*
