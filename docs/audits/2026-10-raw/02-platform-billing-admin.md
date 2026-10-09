# Auditoria 02: Plataforma (SuperAdmin), Billing, Módulos/Planos, Flags, Onboarding, Configurações, Lojas, Auditoria, Landing

Commit auditado: `e8b4873`. A auditoria foi somente leitura: nenhum arquivo do repositório foi alterado e nenhum servidor foi iniciado.

---

## 🚨 ACHADO CRÍTICO: escrita cross-tenant no slug público da loja

**Endpoint:** `PATCH /api/stores/{id}/public-slug` (`nexo-backend/src/Nexo.Api/Controllers/StoreController.cs:86-113`).

**Cadeia do problema:**
- O controller carrega a loja com `IStoreRepository.GetByIdTrackedAsync(id)`.
- Esse método usa `IgnoreQueryFilters()` e **não filtra por tenant** (`nexo-backend/src/Nexo.Infrastructure/Repositories/StoreEntityRepository.cs:26-29`).
- O controller não compara `store.TenantId` com o tenant atual. Também não exige papel: basta `[Authorize]`, então qualquer usuário autenticado (vendedor, cozinha, estoquista) passa.
- O `TenantSaveChangesInterceptor` só barra INSERT cross-tenant ou **mudança** do `TenantId` (`nexo-backend/src/Nexo.Infrastructure/MultiTenancy/TenantSaveChangesInterceptor.cs:107-127`). Um UPDATE em uma entidade de outro tenant, que não mexe no `TenantId`, passa sem bloqueio.

**Cenário concreto:**
1. Um usuário do tenant A conhece o GUID de uma loja do tenant B (ex-funcionário, print de suporte, log, ex-impersonação etc.).
2. Ele envia `PATCH /api/stores/{guidB}/public-slug` com `{ "publicSlug": null }`. Resultado: o cardápio público `/:slug` e a agenda `/agendar/:slug` do tenant B saem do ar.
3. Em seguida, ele envia `PATCH /api/stores/{guidA}/public-slug` com o slug antigo de B. Resultado: o tráfego de clientes de B passa a cair na loja de A, que recebe pedidos e agendamentos de clientes de B (sequestro do portal).

**Mitigação parcial:** o GUID da loja não aparece nas respostas públicas. As chaves do R2 usam `tenants/{tenantId}`, e os DTOs `PublicMenuDto` e `PublicServicePortalDto` não trazem `storeId`. Por isso a exploração exige conhecer o GUID.

**Severidade:** **Alta**. É uma escrita cross-tenant confirmada no código, com impacto em receita e possível phishing. Não existe teste cobrindo esse caminho: `DeliveryPortalFlowTests.cs:94` e `ServicePublicPortalTests.cs:437` só testam o caminho feliz dentro do mesmo tenant.

---

## Plataforma (SuperAdmin): tenants, impersonação, usuários, notas e métricas

**Objetivo:** console do operador Orken para criar e gerir tenants, módulos, sessões e senhas, impersonar clientes e ver métricas (MRR, churn, trial) e saúde do sistema.

**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 70% — o fluxo UI → API → EF é real e coerente em quase todos os endpoints, e as ações privilegiadas são auditadas. Faltam papéis entre usuários de plataforma, gestão de platform users, cancelamento no Stripe ao revogar/suspender, invalidação de cache e um refresh funcional na impersonação.

### Backend existente
- `PlatformController` (`[Authorize(Policy="Platform")]`, 1161 linhas). Endpoints:
  - `GET/POST /api/platform/tenants`
  - `GET/PUT /api/platform/tenants/{id}`
  - `PUT .../status`
  - `POST/DELETE .../modules`
  - `POST .../impersonate`
  - `GET /stats`, `/health`, `/system/endpoints`, `/audit`
  - notas (`GET/POST/DELETE/PATCH pin`)
  - `POST .../users/{uid}/reset-password`, `POST .../force-logout`, `GET/DELETE .../sessions`
  - `GET /tenants/trial-expired`, `/tenants/{id}/plan-history`, `/mrr`, `/churn`
- Autenticação da plataforma: `PlatformAuthController` (`POST /api/platform/auth/login`). Existe também um fallback dentro de `AuthController.Login`, que tenta `PlatformUsers` quando o login contém "@" (`nexo-backend/src/Nexo.Api/Controllers/AuthController.cs:110-133`).
- A policy `Platform` só exige a claim `type=platform` (`Program.cs:109-110`).
- Bootstrap do super-admin por variável de ambiente, em todos os ambientes (`Program.cs:305-309`, `DataSeeder.SeedPlatformUserAsync`).

### Frontend existente
- Páginas em `nexo-main/src/modules/platform/pages/*`, rotas `/platform/*` (`AppRouter.tsx:307-322`): Dashboard (MRR/churn), Tenants, TenantDetail (800 linhas), Trial, Activity (audit), System, Flags, além de páginas de IA (fora do escopo).
- `platformApi.ts` consome todos os endpoints acima. A única exceção é `updateFlag`, que não tem consumidor.
- A impersonação abre `/impersonate` em nova aba (`PlatformTenantDetailPage.tsx:127-132`, `pages/ImpersonatePage.tsx`) e exibe o `ImpersonationBanner`.

### Funcionalidades concluídas
- **Criação de tenant.** Cria, na mesma unit of work: loja inicial, usuário Diretoria com `requirePasswordChange`, AdminGrants com eventos, AppSettings e plano de contas padrão, mais auditoria (`PlatformController.cs:227-326`).
- **Edição e mudança de status do tenant** (`:340-386`). A suspensão é aplicada pelo `TenantResolutionMiddleware` com 403, em até 5 minutos por causa do cache (`TenantResolutionMiddleware.cs:157-164`). Testes: `AuthorizationTests.SuspendedTenant_Users_CannotAccessEndpoints` e `InactiveTenant_...`.
- **Concessão e revogação de módulo** (`:394-469`), gravando `ModuleSubscriptionEvent` e audit.
- **Reset de senha, force-logout e revogação de sessões.** Faz bump do SecurityStamp e remove `refresh:valid:*` (`:809-924`, `:1145-1160`). Teste: `PlatformAuditTests.ResetPassword_AuditRecord_ContainsNoSecret`.
- **Impersonação auditada** com severidade `critical` (`:479-552`). Teste: `PlatformAuditTests.Impersonate_WritesPlatformAuditRecord`.
- **Notas internas (CRM),** histórico de plano, trial-expired, MRR/ARR, churn e stats. Tudo lê o banco de verdade.

### Funcionalidades parciais
- **Impersonação.** O endpoint gera um par de tokens, mas **não grava o refresh token em `refresh:valid:{jti}`** nem cria `UserSession`. O `AuthService.RefreshAsync` exige essa entrada (`AuthService.cs:128-130`), então a sessão impersonada morre quando o access token expira (`Jwt:AccessTokenMinutes`, padrão 15 min). O token também não tem claim de "impersonado": tudo o que for feito na sessão é atribuído ao usuário Diretoria real.
- **MRR.** Depende de `ModuleDefinitions`, que existem só via `DataSeeder` (não roda em produção, `Program.cs:311`). Não há definição para o módulo `service`. Na prática, a assinatura `service` e qualquer tenant de produção sem seed somam R$ 0 (`PlatformController.cs:1043-1051`).
- **Health.** Verifica só o banco. A latência da "api" é fixa em `1L` (`:622`), e Redis, Stripe e R2 não são checados.
- **Grant em assinatura existente.** Chama `Renew()`, que reativa sem trocar `PlanType` nem limpar `StripeSubscriptionId` (`ModuleSubscription.cs:120-126`). Uma assinatura Stripe cancelada e reativada pelo admin continua contando como "pagante" no MRR. Além disso, `ExpiresAt=null` vira "+10 anos" ao renovar, mas vira vitalício (null) ao criar: comportamento inconsistente.

### Funcionalidades não desenvolvidas
- Gestão de usuários de plataforma (criar, remover, trocar papel). O campo `PlatformUser.Role` existe, mas nunca é checado: todo platform user é super-admin.
- MFA para platform users, revogação do token de plataforma (8h, sem refresh e sem stamp) e auditoria do login de plataforma.
- Criação, edição ou arquivamento de lojas adicionais de um tenant.
- Exclusão de tenant e exportação de dados (LGPD).
- Cancelamento ou pausa no Stripe ao revogar módulo ou suspender tenant.

### Problemas
- **Seg.** Policy `Platform` sem RBAC. O login de plataforma passa pelo `/api/auth/login` público com o mesmo rate limit do tenant e sem MFA.
- **Bug.** Grant, revoke e status não invalidam `tenant:{id}:info`, `tenant:{id}:modules` nem o `IMemoryCache` do `ModuleAccessService`. O efeito leva até 5 minutos. `ModuleAccessService.InvalidateCache` existe, mas não é chamado.
- **Bug.** `CreateTenant` só checa unicidade de e-mail. Um CNPJ duplicado bate no índice único e vira 500. `AdminPassword` não tem validação de tamanho, e `Modules[]` não é validado contra chaves conhecidas. O mesmo vale para `GrantModule.ModuleKey`.
- **Bug.** A busca em `/platform/audit` usa `Contains` (LIKE case-sensitive no Postgres).
- **Arquitetura.** O controller de 1161 linhas acessa `NexoDbContext` direto, sem camada Application e com DTOs anônimos.
- **UX.** Os tokens de impersonação trafegam por `localStorage` (`PlatformTenantDetailPage.tsx:129-131`), o que os expõe a XSS.

### Dependências
Redis (cache de tenant, stamp e refresh), `IAuditWriter`, `DefaultFinancialAccountProvisioner`, `ModuleDefinitions` (MRR).

### Próximos passos
1. Gravar o refresh da impersonação e adicionar a claim `impersonatedBy`.
2. Invalidar os caches de módulo e tenant em grant, revoke e status.
3. RBAC de plataforma, MFA e auditoria do login.
4. Migration com `ModuleDefinitions` reais (incluindo `service`) e remoção das SKUs mortas.
5. CRUD de lojas.
6. Integração revoke/suspend ↔ Stripe.

---

## Billing / Stripe (assinaturas)

**Objetivo:** cobrança recorrente por módulo via Stripe Checkout, Customer Portal e webhooks idempotentes.

**Estado:** 🟠 PARCIAL   **Maturidade:** 35% — o esqueleto Stripe está correto (checkout, portal, assinatura de webhook, tabela de idempotência). Porém:
- o feature flag vem `false` por padrão;
- a UI só vende `restaurante` e `build` (não vende `varejo` nem `service`);
- há bugs de sincronização que deixam assinaturas "órfãs" do Stripe;
- não há teste do `StripeWebhookService`;
- não há eventos de histórico nem invoices.

Não está pronto para cobrar clientes reais.

### Backend existente
- `BillingController` (`nexo-backend/src/Nexo.Api/Controllers/BillingController.cs`): `POST /api/billing/checkout`, `POST /api/billing/portal`, `GET /api/billing/subscriptions`, `POST /api/billing/webhook` (anônimo, valida `Stripe-Signature`).
- `StripeProvider`: customer, checkout com metadata `tenantId`/`moduleKey` também em `subscription_data`, e portal.
- `StripeWebhookService` trata `checkout.session.completed`, `customer.subscription.updated`, `customer.subscription.deleted` e `invoice.payment_failed`.
- Idempotência via `stripe_processed_events` com índice único em `stripe_event_id` (`StripeProcessedEventConfiguration.cs:36`).
- Entidade `ModuleSubscription`, com índice único `(TenantId, ModuleKey)` (`ModuleSubscriptionConfiguration.cs:81`).
- Configuração: `Features:StripeEnabled=false` e `Stripe:PriceIds` vazios, só com chaves `restaurante_*` e `build_*` (`appsettings.json:65-82`).

### Frontend existente
- `/assinatura` → `nexo-main/src/modules/billing/pages/AssinaturaPage.tsx`, usando `services/billing.api.ts`.
- O catálogo é um **array fixo** `MODULES = [restaurante, build]` (`AssinaturaPage.tsx:19-30`).

### Funcionalidades concluídas
- **Geração de checkout session** com customer reutilizado e ativação **somente via webhook**. Coberto por testes de unidade do controller com mocks (`tests/Nexo.UnitTests/Integrations/BillingControllerTests.cs`, 15 testes).
- **Validação de assinatura do webhook** e short-circuit para evento já processado (`StripeWebhookService.cs:43-71`).

### Funcionalidades parciais
- **Checkout sobre assinatura existente.** Quando já existe linha para o módulo (Trial, AdminGrant ou cancelada), `HandleCheckoutCompletedAsync` só chama `SyncFromStripe(Active, …)` (`StripeWebhookService.cs:121-124`). Ele **não atualiza `StripeSubscriptionId`, `StripePriceId` nem `PlanType`**. Consequências:
  - os eventos seguintes (`updated`, `deleted`, `payment_failed`) buscam por `GetByStripeSubscriptionIdAsync` e não encontram nada;
  - o cancelamento ou a inadimplência no Stripe **nunca revoga o acesso**;
  - o MRR conta R$ 0.
- **Mapeamento de status.** O padrão é `_ => Active` (`:206`). Os status `incomplete`, `incomplete_expired` e `paused` liberam acesso. Troca de preço no portal (upgrade/downgrade) não atualiza `PlanType`/`StripePriceId`. Só os intervalos `month` e `year` são mapeados; `Quarterly`/`Semiannual`/`Lifetime` existem no enum, mas não no Stripe.
- **PastDue.** A entidade documenta "7 dias de carência e depois Canceled" (`ModuleSubscription.cs:13`), mas nenhum job faz isso. Na prática, `PastDue` corta o acesso na hora (`StoreRepository.GetActiveModuleKeysAsync` aceita só `Active`/`Trialing`).
- **UI:**
  - `GET /subscriptions` responde 404 quando o Stripe está desligado. Nesse caso a página mostra "Billing em configuração" e esconde até os AdminGrants.
  - Para qualquer assinatura existente (inclusive `Canceled`), não há botão de assinar: não dá para reassinar.
  - O botão "Gerenciar assinatura" aparece mesmo sem `StripeCustomerId`, e o endpoint responde 400.
  - O `toast` é disparado durante o render (`AssinaturaPage.tsx:126-128`).

### Funcionalidades não desenvolvidas
- Venda de `varejo` (o módulo do trial) e de `service`. Não há PriceId nem card.
- Plano vitalício (checkout em modo `payment`).
- Faturas e histórico de pagamentos.
- Gravação de `ModuleSubscriptionEvent` a partir dos webhooks (o histórico de plano só reflete ações do admin).
- Endpoint de catálogo e preços: `ModuleDefinitions` não é exposto.
- Bloqueio de checkout duplicado.

### Problemas
- **Corrupção financeira (alta).** Cobrança continua sem controle de acesso, pelo bug do `StripeSubscriptionId` acima.
- **Financeiro (média).** `CreateCheckout` não verifica se já existe assinatura ativa do módulo. Dois checkouts criam duas assinaturas no Stripe, e o webhook sobrescreve a linha local: a primeira assinatura continua cobrando, órfã.
- **Seg. (média).** Checkout e **portal** exigem só `[Authorize]`. Qualquer papel (vendedor, cozinha) pode abrir o Customer Portal e cancelar o plano ou trocar o cartão. A rota de UI é restrita a gestão, mas a API não.
- **Seg. (baixa).** `SuccessUrl`/`CancelUrl` vêm do cliente sem allowlist (`BillingController.cs:95-96`), o que permite open redirect via Stripe.
- **Testes.** Nenhum teste do `StripeWebhookService` (handlers, mapeamentos, idempotência sob concorrência). Os testes do controller usam mocks.

### Dependências
`Features:StripeEnabled`, `Stripe:SecretKey`/`WebhookSecret`/`PriceIds`, `ModuleSubscription`, `TenantResolutionMiddleware` (enforcement).

### Próximos passos
1. No checkout sobre assinatura existente, chamar `UpdateStripeData` e trocar `PlanType`.
2. Mapear status desconhecidos para não-ativo.
3. Restringir checkout e portal a Diretoria.
4. Bloquear assinatura duplicada.
5. Adicionar PriceIds e cards de `varejo`/`service` e um catálogo vindo do backend.
6. Gravar eventos de histórico nos webhooks.
7. Job de carência para PastDue.
8. Testes de integração do webhook com payloads assinados.

---

## Módulos / planos / enforcement de acesso

**Objetivo:** liberar ou bloquear áreas conforme a assinatura ativa.

**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 55% — o enforcement backend existe e respeita status e fim de período. Mas os controllers "core" não são bloqueados, há três mecanismos paralelos com caches distintos e o catálogo de módulos está desatualizado.

### Backend existente
- **Fonte de verdade:** `GetActiveModuleKeysAsync`, que filtra `Status ∈ {Active, Trialing}` e `CurrentPeriodEnd == null || > now` (`nexo-backend/src/Nexo.Infrastructure/Repositories/StoreRepository.cs:35-46`).
- **Três gates diferentes:**
  - `Nexo.Api/Filters/RequireModuleAttribute` + `RequireModuleFilter` → `ModuleAccessService` (IMemoryCache de 5 min);
  - `Nexo.Api/Attributes/RequireModuleAttribute` (Redis `tenant:{id}:modules`);
  - `RequireServiceModuleAttribute`, que lê `ICurrentTenant.ActiveModules` do cache `tenant:{id}:info`.
- **Quem aplica quando a assinatura vence:** os filtros acima, por requisição, recalculando a partir do banco a cada 5 minutos no máximo. Não há job. O vencimento acontece "naturalmente" por `CurrentPeriodEnd`.

### Frontend existente
- `ModuleRoute` e `ServiceModuleRoute` leem `session.modules`, que vem do login ou de `/auth/me`.
- `ProtectedRoute` redireciona para `/assinatura` quando `trialEndsAt` passou e `modules.length === 0` (`app/router/ProtectedRoute.tsx:31-39`).

### Funcionalidades concluídas
- **Bloqueio 403 por módulo** em PDV, PriceLists, Purchases, Sales (`varejo`), Restaurante (maioria), Build e Service. Testes: `AuthorizationTests.ModuleRequirement_*` e `ModuleActivation_EventuallyAllowsAccess`.

### Funcionalidades parciais
- **Controllers core sem gate:** `ProductsController`, `StockController`, `CashController`, `CustomersController`, `SuppliersController`, `FinancialController`, `ReportsController`, `DashboardController`, `CategoriesController`. Também sem gate: `Restaurante/CouponsController` e `DeliveryZonesController`. Um tenant com trial ou assinatura vencida continua usando estoque, caixa e financeiro pela API. O bloqueio pós-trial existe **só no frontend**.
- **Catálogo `ModuleDefinitions`:**
  - só via seeder (não roda em produção);
  - contém SKUs mortas (`academia-*`, `pousada-hotel`, `imobiliaria`, `DataSeeder.cs:159-167`);
  - não tem `service`;
  - não tem endpoint público.

### Funcionalidades não desenvolvidas
Período de carência, aviso de vencimento, downgrade automático e um único serviço de entitlement.

### Problemas
- **Arquitetura.** Três mecanismos de gate, dois deles com o nome `RequireModuleAttribute` em namespaces diferentes (`Filters` em 19 controllers, `Attributes` em 12), com caches distintos e sem invalidação.
- **Bug.** Mudanças de módulo só aparecem no frontend depois de re-login ou `/auth/me`.

### Próximos passos
1. Unificar em um `IModuleAccessService` com invalidação.
2. Decidir quais rotas core exigem algum módulo ativo e aplicar o bloqueio no backend.
3. Migration do catálogo.

---

## Feature flags

**Objetivo:** ligar e desligar funcionalidades por tenant ou globalmente.

**Estado:** 🟠 PARCIAL   **Maturidade:** 30% — a administração (CRUD de flags, overrides por tenant e cache Redis) e o endpoint de resolução funcionam, mas **nenhuma flag é consumida** em lugar nenhum. Ligar ou desligar uma flag não muda nada no produto.

### Backend existente
- `PlatformFlagsController`: `GET/POST /api/platform/flags`, `PUT /flags/{key}`, `PATCH /flags/{key}/toggle`, `GET /flags/{key}/overrides`, `GET/POST/DELETE /api/platform/tenants/{id}/flags/{key}`.
- `FeaturesController`: `GET /api/features`, resolvido com override ?? default e cache de 2 minutos.
- Não confundir com `IIntegrationFeatureFlags`: são flags de appsettings para Stripe, R2 e similares, outro mecanismo.

### Frontend existente
- `PlatformFlagsPage` (CRUD e toggle; não há edição, `updateFlag` não tem consumidor).
- Hook `nexo-main/src/hooks/useFeatureFlags.ts`, **sem nenhum import no app**.

### Funcionalidades concluídas
Administração das flags e dos overrides, persistida, com invalidação de cache (`PlatformFlagsController.cs:255,275,287-298`).

### Funcionalidades parciais e não desenvolvidas
- Nenhum consumo das flags no backend nem no frontend.
- Nenhuma auditoria de alterações de flag.
- `GET /flags/{key}/overrides` não tem consumidor.

### Problemas
- **Performance.** `InvalidateAllFlagCachesAsync` faz um DEL no Redis por tenant a cada toggle global (O(n)).
- **Arquitetura.** `FeaturesController` injeta `NexoDbContext` direto.

### Próximos passos
Consumir as flags (ou remover a feature), auditar as alterações e trocar a invalidação por versionamento de chave.

---

## Registro, trial e onboarding

**Objetivo:** cadastro self-service, verificação de e-mail, trial e primeiro uso.

**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 55% — o caminho registro → e-mail → verificação → login automático é real. Mas a conversão trial → pago está quebrada, o fluxo não é transacional, não há rate limit e o wizard de onboarding está morto.

### Backend existente
- `AuthController`: `POST /api/auth/register`, `GET /api/auth/verify-email`, `POST /api/auth/resend-verification`.
- `RegistrationService.RegisterAsync` cria tenant, trial `varejo` de 7 dias (`ModuleSubscription.CreateTrial`), loja "Loja Principal", usuário `PendingVerification`, AppSettings e contas padrão. Em seguida envia o e-mail (`nexo-backend/src/Nexo.Infrastructure/Auth/RegistrationService.cs:55-140`).

### Frontend existente
`/register`, `/check-email`, `/verify-email` (`modules/auth/pages/*`). Depois do login, o destino é decidido por `resolvePostLogin`.

### Funcionalidades concluídas
- **Registro com provisionamento completo.** Teste: `tests/Nexo.IntegrationTests/Financial/DefaultAccountProvisioningTests.cs`, `Self_service_registration_provisions_the_four_default_accounts`.
- **Verificação com token persistido no Postgres,** TTL de 24 horas e login automático.

### Funcionalidades parciais
- **Fim do trial:**
  - redireciona para `/assinatura` (só no frontend);
  - a página não oferece `varejo`;
  - com o Stripe desligado, mostra "Billing em configuração";
  - com o Stripe ligado, a linha Trial existe, então só aparece "Gerenciar assinatura", que responde 400 sem `StripeCustomerId`.

  **Beco sem saída:** o usuário com trial expirado não consegue pagar.
- **`Tenant.TrialEndsAt` nunca é preenchido** (`SetTrialEnd` sem chamadas). A página de plataforma "trial-expired" só detecta trials pelo ramo de `ModuleSubscription`.

### Funcionalidades não desenvolvidas
- Onboarding de primeiro uso. `components/shared/OnboardingWizard.tsx` não é renderizado em lugar nenhum, e a flag `nexo:onboarding:{userId}` é gravada mas nunca lida.
- Escolha de módulo no cadastro (sempre `varejo`).
- Limpeza de tenants nunca verificados.

### Problemas
- **Seg. (média).** `register` e `resend-verification` não têm `[EnableRateLimiting]`. Isso permite criação em massa de tenants e e-mail bombing para endereços pendentes.
- **Integridade.** São 6 chamadas `SaveChangesAsync` sem transação (`RegistrationService.cs:76-125`). Uma falha no meio deixa tenant ou loja órfãos. A unicidade é checada só em `Users.Email`, então uma nova tentativa cria outro tenant.
- **Dívida.** O `TaxId` é um placeholder GUID e não é sincronizado com o CNPJ preenchido em Configurações.

### Próximos passos
1. Transação única.
2. Rate limit em register e resend.
3. Card e preço de `varejo` e fluxo de conversão do trial.
4. Bloqueio pós-trial também no backend.
5. Remover ou religar o OnboardingWizard.

---

## Auditoria (audit log)

**Objetivo:** trilha imutável de ações sensíveis, visível ao tenant e à plataforma.

**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 45% — a escrita é transacional e a leitura do tenant é corretamente isolada, com UI real. Mas a cobertura é pequena (só usuários e ações de plataforma), e a API fica aberta a qualquer papel.

### Backend existente
- `IAuditWriter` / `AuditWriterService` (stage no mesmo SaveChanges).
- `AuditController`: `GET /api/audit` e `GET /api/audit/stats`, com `[Authorize]` apenas.
- `AuditQueryService` filtra explicitamente `r.TenantId == _currentTenant.Id`, porque `AuditRecord` é `BaseEntity`, sem filtro global (`nexo-backend/src/Nexo.Infrastructure/Audit/AuditQueryService.cs:29-31,74-76`).
- Para a plataforma: `GET /api/platform/audit`.

### Frontend existente
`/auditoria` (rota só para Diretoria, `AppRouter.tsx:267-274`) → `AuditoriaPage`, `AuditTable`, `AuditFilters`, `audit.api.ts` (real; não há mock). Na plataforma, `PlatformActivityPage`.

### Funcionalidades concluídas
- **Isolamento por tenant na leitura.**
- **Auditoria de ações de plataforma:** criar e editar tenant, status, grant e revoke, impersonação, reset de senha, revogação de sessão.
- **Auditoria de usuários:** `UserService` registra created, updated, senha e autorização gerencial.
- Testes: `PlatformAuditTests` (2).

### Funcionalidades parciais
- **Ações definidas em `AuditActions` mas nunca gravadas:** login/logout, `stock_adjustment`, `stock_transfer`, `cash_*`, `sale_completed`/`sale_cancelled`, `subscription_*`, alterações de Configurações, flags e slug público (`Nexo.Application/Common/Interfaces/IAuditWriter.cs:24-56`; únicos chamadores em `PlatformController` e `UserService`).
- **UI:**
  - os filtros de ação listam tipos que nunca ocorrem;
  - o backend aceita `from`/`to`, mas a UI não envia;
  - `fetchAuditActors` baixa 500 registros só para montar a lista de atores;
  - o limite fixo é `Take(500)`, sem paginação.

### Funcionalidades não desenvolvidas
Exportação, retenção e proteção de imutabilidade. `AuditRecord` não está no `EnforceImmutableEntities`.

### Problemas
- **Seg. (média).** `GET /api/audit` aceita qualquer papel autenticado. Um vendedor pode ler IPs, metadados, e-mails e registros de impersonação da plataforma via API, já que a restrição está só na rota de UI.

### Próximos passos
1. `[Authorize(Roles="Diretoria")]`.
2. Instrumentar caixa, vendas, estoque, login, configurações, billing e slug.
3. Paginação e filtro de data.
4. Imutabilidade no interceptor.

---

## Configurações (AppSettings)

**Objetivo:** preferências do tenant: empresa, operação, estoque, comissões, PDV e sistema.

**Estado:** 🟠 PARCIAL   **Maturidade:** 40% — o CRUD é ponta a ponta: `GET/PUT /api/settings` → `SettingsService` → JSONB por seção, com validador e papéis Gerente/Diretoria, e a UI tem 6 abas. Mas **nada no sistema lê essas configurações**: são "write-only".

### Backend existente
- `SettingsController` (`GET /api/settings`; `PUT` com `[Authorize(Roles="Gerente,Diretoria")]`).
- `SettingsService`, `UpdateSettingsRequestValidator`.
- `AppSettingsRepository.GetOrCreateAsync`, com auto-criação e índice único por tenant (`AppSettingsConfiguration.cs:67`).

### Frontend existente
`/configuracoes` → `ConfiguracoesPage.tsx` (578 linhas), `settings.api.ts`. Sem localStorage.

### Funcionalidades concluídas
Leitura e gravação das 6 seções, com fallback para defaults quando o JSON está inválido.

### Funcionalidades parciais e não desenvolvidas
- `pos.maxDiscountPercent`, `requireManagerAuth`, `commission.defaultCommissionRate`, alertas de estoque e `system.dateFormat`/`currencySymbol`/`language` **não são lidos por nenhum serviço backend nem tela** (grep sem consumidores fora de `Features/Settings`).
- Os dados da empresa (nome, CNPJ) não atualizam `Tenant.CompanyName`/`TaxId`, usados no JWT e na plataforma.

### Problemas
- **UX enganosa.** O usuário configura "desconto máximo no PDV", e o PDV ignora.
- **Defaults divergentes.** Há 4 cópias de JSON default, com `requireManagerAuth` `true` em algumas e `false` em outras (`RegistrationService.cs:113` × `PlatformController.cs:310` × `AppSettingsRepository.cs:37` × `DataSeeder.cs:138`).
- **Sem auditoria** de alterações.

### Próximos passos
Ligar as regras de PDV, comissão e estoque às configurações, sincronizar empresa ↔ Tenant, centralizar os defaults e auditar.

---

## Lojas / multi-store / slug público

**Objetivo:** várias lojas por tenant, troca de loja e slug do portal público.

**Estado:** 🟠 PARCIAL   **Maturidade:** 40% — listagem, troca de loja (com teste) e slug funcionam. Mas não existe criação ou edição de lojas, e o endpoint de slug tem a falha cross-tenant descrita no topo.

### Backend existente
- `StoreController` (`/api/stores`): `GET` (lojas do JWT), `GET check-slug` (anônimo), `PATCH {id}/public-slug`.
- `AuthController.switch-store`.
- `TenantsController`: arquivo `StoresController.cs`, `GET /api/tenants/{id}` e `by-slug`.

### Frontend existente
`modules/stores` (`storesApi.ts`, `useMyStores`), `StoreSwitcher.tsx`, uso de slug em `PortalSetupPage` e `ServicePortalPage`.

### Funcionalidades concluídas
- **Lista de lojas acessíveis,** filtrada por tenant e `store[]` do JWT (`StoreController.cs:34-52`).
- **Troca de loja.** Testes: `StoreIsolationTests.SwitchStore_*` (3).
- **Verificação de disponibilidade e normalização do slug.**

### Funcionalidades parciais
O slug público funciona no mesmo tenant, mas **sem checagem de tenant nem de papel** (ver 🚨).

### Funcionalidades não desenvolvidas
- Criar, renomear e arquivar lojas: só existe a loja criada no registro ou pela plataforma (`Store.Create` só é chamado em `RegistrationService.cs:85` e `PlatformController.cs:256`).
- Atribuir usuários a lojas pela UI.

### Problemas
- 🚨 Escrita cross-tenant (alta).
- Qualquer papel altera ou derruba o slug da própria loja (média).
- `check-slug` anônimo permite enumerar slugs (baixa).
- `TenantsController` não tem consumidor no frontend e está num arquivo com nome errado.

### Próximos passos
1. Corrigir `SetPublicSlug`: exigir `store.TenantId == currentTenant` e papel de gestão, e adicionar teste cross-tenant.
2. CRUD de lojas.

---

## Seleção de workspace (pós-login)

**Objetivo:** escolher a área de trabalho (Store, Menu, Build, Service) conforme os módulos ativos.

**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 75% — a lógica é pura e testada, com tela real.

### Frontend existente
`modules/workspace/*` (`config.ts`, `resolvePostLogin.ts` com 12 testes em `resolvePostLogin.spec.ts`, `ModuleSelectionPage.tsx`, `WorkspaceContext.tsx`), rota `/workspaces`.

### Funcionalidades concluídas
Destino por papel e por módulos, com a tela de seleção e o estado "nenhum módulo" levando a `/assinatura`.

### Problemas
**Bug.** `persistence.ts:4` tem `VALID_IDS = ["store","menu","build"]`, sem `"service"`. A última escolha de workspace Service nunca é lembrada: um tenant com Service e outro módulo cai sempre na tela de seleção.

### Próximos passos
Incluir `service` em `VALID_IDS`, com teste.

---

## Perfil

**Objetivo:** dados do próprio usuário e troca de senha.

**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 70%

**Evidência:** `/perfil` → `PerfilPage.tsx` → `profileService` → `GET /api/users/{id}` e `POST /api/users/{id}/change-password`.

**Faltam:** edição de nome, telefone e e-mail, e a lista de sessões do próprio usuário.

---

## Landing page

**Objetivo:** página de marketing pública em `/`.

**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 65% — é uma página estática real (`modules/landing`, cerca de 1.200 linhas), com CTAs para `/register`.

### Problemas
- O link "Preços" (`#precos`, `LandingNav.tsx:93,153`) não tem âncora correspondente: não existe seção de preços.
- O módulo Service não aparece.
- As URLs são absolutas e fixas (`https://app.orken.com.br/register`).
- `LandingCtaBlock.tsx` e `LandingDifferentials.tsx` não são importados (código morto).
- A promessa "Começar grátis" não informa a duração do trial nem os preços.

---

## Backend sem frontend
- `GET /api/platform/flags/{key}/overrides` e `PUT /api/platform/flags/{key}` (não há UI de edição).
- `GET /api/features`: o hook existe, mas nenhum componente o usa.
- `GET /api/tenants/{id}` e `GET /api/tenants/by-slug/{slug}` (`StoresController.cs`).
- `POST /api/platform/auth/login`: o frontend usa o fallback em `/api/auth/login`.
- `ModuleDefinitions`: preços existem no banco (dev), sem endpoint nem tela.
- Filtros `from`/`to` de `GET /api/audit`.

## Frontend sem backend/fluxo real
- Catálogo fixo em `AssinaturaPage` (`MODULES` com 2 itens), divergente dos módulos reais (`varejo` e `service` ausentes).
- Configurações de PDV, comissão, estoque e sistema: salvas, mas sem efeito.
- Filtro de tipos de ação em `AuditFilters` lista ações que o backend nunca grava.
- Link `#precos` da landing.
- Bloqueio de trial expirado (`ProtectedRoute.tsx:31-39`): só no cliente.

## Código legado/morto
- `components/shared/OnboardingWizard.tsx`: não é renderizado, e a flag `nexo:onboarding:*` é gravada sem leitura.
- `auditService.addAuditRecord` (no-op, deprecated) e `getAuditByEntity`, que baixa tudo.
- `LandingCtaBlock.tsx` e `LandingDifferentials.tsx`.
- `ModuleDefinitions` de verticais extintas (`academia-*`, `pousada-hotel`, `imobiliaria`) no `DataSeeder`.
- `Tenant.TrialEndsAt` / `SetTrialEnd` (nunca preenchido) e o ramo correspondente em `GetTrialExpired`.
- Constantes não usadas em `AuditActions` (login, caixa, venda, estoque, assinatura).
- `ModuleAccessService.InvalidateCache` (sem chamadas).
- `IStoreRepository`/`TenantsController` em `StoresController.cs` (sem consumidor).

## Achados de segurança

| # | Severidade | Achado | Evidência |
|---|---|---|---|
| 1 | **Alta** | Escrita cross-tenant: `PATCH /api/stores/{id}/public-slug` altera ou derruba o slug de loja de outro tenant (sequestro do portal público) | `StoreController.cs:86-113`; `StoreEntityRepository.cs:26-29`; o interceptor não bloqueia UPDATE (`TenantSaveChangesInterceptor.cs:107-127`) |
| 2 | **Alta** (financeira) | Checkout sobre uma linha existente não grava `StripeSubscriptionId`. Cancelamento e inadimplência no Stripe nunca revogam o acesso | `StripeWebhookService.cs:121-124`, `:143-150` |
| 3 | Média | Customer Portal e checkout acessíveis a qualquer papel: um vendedor pode cancelar o plano ou trocar o cartão | `BillingController.cs:62-64,114-116` |
| 4 | Média | `GET /api/audit` acessível a qualquer papel: expõe IPs, e-mails e metadados de impersonação | `AuditController.cs:10` |
| 5 | Média | `register` e `resend-verification` sem rate limit: criação em massa de tenants e e-mail bombing | `AuthController.cs:329-383` |
| 6 | Média | Status Stripe desconhecido (`incomplete`, `paused`) mapeado para `Active` | `StripeWebhookService.cs:199-207` |
| 7 | Média | Checkout duplicado gera duas assinaturas no Stripe, e a primeira fica órfã cobrando | `BillingController.cs:62-107` |
| 8 | Média | Platform users sem RBAC nem MFA; token de 8h sem revogação; login de plataforma pelo endpoint público do tenant e não auditado | `Program.cs:109-110`; `AuthController.cs:110-133` |
| 9 | Média | Impersonação sem claim de origem: ações aparecem como do usuário Diretoria real | `PlatformController.cs:517-523` |
| 10 | Baixa | Qualquer papel do tenant altera ou desativa o slug da própria loja | `StoreController.cs:86` |
| 11 | Baixa | Open redirect: `SuccessUrl`/`CancelUrl` do cliente sem allowlist | `BillingController.cs:95-96` |
| 12 | Baixa | Tokens de impersonação passam por `localStorage` | `PlatformTenantDetailPage.tsx:129-131` |
| 13 | Baixa | `check-slug` anônimo permite enumerar slugs | `StoreController.cs:59-79` |
| 14 | Baixa | Grant, revoke e suspensão levam até 5 minutos para valer (caches não invalidados) | `TenantResolutionMiddleware.cs:140-155`; `ModuleAccessService.cs:46` |

## Testes existentes para esta área

| Arquivo | Testes | Cobertura |
|---|---|---|
| `nexo-backend/tests/Nexo.UnitTests/Integrations/BillingControllerTests.cs` | 15 | Só o controller, com mocks; **não** cobre `StripeWebhookService` |
| `nexo-backend/tests/Nexo.UnitTests/Integrations/IntegrationFeatureFlagsTests.cs` | 4 | Flags de integração (appsettings), não as feature flags de tenant |
| `nexo-backend/tests/Nexo.IntegrationTests/Security/PlatformAuditTests.cs` | 2 | Impersonação e reset de senha |
| `nexo-backend/tests/Nexo.IntegrationTests/Security/PlatformAuthorizationTests.cs` | 3 Theories × 3 rotas (~9 casos) | 401/403/200 em `/platform/stats`, `/platform/flags`, `/platform/interpreter/dashboard` |
| `nexo-backend/tests/Nexo.IntegrationTests/Auth/PlatformBootstrapTests.cs` | 2 | Bootstrap do super-admin |
| `nexo-backend/tests/Nexo.IntegrationTests/Security/AuthorizationTests.cs` | 14 | Inclui gate de módulo, tenant suspenso/inativo, ativação de módulo |
| `nexo-backend/tests/Nexo.IntegrationTests/Security/StoreIsolationTests.cs` | 4 | Switch-store, inclusive cross-tenant |
| `nexo-backend/tests/Nexo.IntegrationTests/Financial/DefaultAccountProvisioningTests.cs` | 1 | Registro self-service |
| `nexo-main/src/modules/workspace/resolvePostLogin.spec.ts` | 12 | Destino pós-login |
| `nexo-main/e2e/auth.e2e.spec.ts` | ~10 | Login, logout, cookies |

**Sem nenhum teste:**
- `StripeWebhookService`;
- `SettingsController`/`SettingsService`;
- `AuditController` (tenant);
- `FeaturesController`/`PlatformFlagsController` (CRUD e overrides);
- `PATCH public-slug` cross-tenant;
- grant/revoke/MRR/churn da plataforma;
- `AssinaturaPage` e páginas de plataforma no frontend.
