# 01 — Autenticação, usuários, papéis, multi-tenancy e segurança

Auditoria somente leitura do commit `e8b4873` (branch `feature/orken-service-closure-prC-resume`). Caminhos relativos a `nexo-backend/src` (BE) e `nexo-main/src` (FE), salvo indicação.

---

## 🚨 ACHADOS CRÍTICOS / ALTOS NO TOPO

### 🚨 1. Escrita cross-tenant: `PATCH /api/stores/{id}/public-slug` não valida o tenant (IDOR)
- **Onde:** `Nexo.Api/Controllers/StoreController.cs:86-110` → `_stores.GetByIdTrackedAsync(id)`. O método está em `Nexo.Infrastructure/Repositories/StoreEntityRepository.cs:26-29` e usa `.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == id)`, **sem predicado de `TenantId`**.
- **Por que passa:** o `TenantSaveChangesInterceptor` só bloqueia INSERT com outro TenantId e UPDATE que altere o próprio `TenantId`. Alterar outra coluna de um `Store` alheio passa sem bloqueio. O endpoint também não exige papel nenhum (`[Authorize]` puro).
- **Cenário:** um usuário qualquer (até `Vendedor`) do tenant A conhece o GUID de uma loja do tenant B (ex.: ex-funcionário de B, consultor que atende os dois, log, print de tela, sessão de impersonação). Ele envia `PATCH /api/stores/{storeB}/public-slug {"publicSlug":null}` e derruba o cardápio online ou o portal de agendamento de B. Outra opção: renomeia o slug de B e depois grava o slug antigo de B na própria loja. A partir daí, `/{slug}` e `/agendar/{slug}` mandam os clientes de B para a loja de A, e pedidos e agendamentos públicos de B passam a cair em A.
- **Atenuante:** os IDs de loja são GUIDs v4 e não aparecem em DTOs públicos (`PublicMenuDto`, `PublicServicePortalDtos.cs:9`). Por isso classifico como **ALTA**, não crítica. Não há teste de isolamento para esse endpoint. Os únicos usos são caminhos felizes, em `tests/.../Restaurante/DeliveryPortalFlowTests.cs:94` e `Service/ServicePublicPortalTests.cs:437`.

### 🚨 2. RBAC quase inexistente no backend: só 8 endpoints checam papel
- `grep "Authorize(Roles"` acha apenas estes:
  - `UsersController` (GET lista, POST, PUT, admin-reset);
  - `SettingsController.PUT`;
  - 2 ações de `CommissionsController`.
- Não existe nenhuma outra checagem de papel em service: `IsManager()` só aparece em `AuthService.VerifyManagerAsync`.
- **Cenário:** um `vendedor`, `estoquista` ou `cozinha` autenticado chama direto, sem passar pela UI, rotas que o frontend reserva para `diretoria`/`gerente` (`routes.ts`, `MGMT`). Exemplos:
  - `POST /api/financial/transactions/{id}/pay|cancel`;
  - `POST /api/sales/{id}/cancel`;
  - `GET /api/audit` (trilha de auditoria do tenant, que na UI é só `diretoria`);
  - `POST /api/billing/checkout|portal`;
  - `PUT /api/v1/service/settings/branding`;
  - pagamentos e estornos do Service;
  - `PATCH /api/stores/{id}/public-slug`;
  - Build, relatórios e o financeiro do restaurante.
- O "desafio de gerente" para cancelar vendas existe só no cliente. O endpoint de cancelamento não recebe nem valida nenhuma prova de autorização gerencial.
- **Severidade:** ALTA (escalonamento vertical dentro do tenant e fraude interna).

### 🚨 3. Refresh de token volta a sessão para a 1ª loja (integridade multi-loja)
- `Nexo.Application/Features/Auth/AuthService.cs:154`: `RefreshAsync` sempre gera o novo token com `storeId = stores.FirstOrDefault()`. A loja escolhida via `switch-store` é ignorada.
- O access token dura 15 min. No primeiro refresh, o backend volta a filtrar e gravar na loja padrão. Enquanto isso, o FE (`services/api-client.ts:68-70`) só troca os tokens e mantém `nexo:session` com a loja B.
- **Cenário:** o operador troca para a "Filial Sul". Depois de 15 min, vendas, caixa, OS e estoque passam a ser gravados na "Filial Centro" sem aviso.
- Não existe teste de "refresh preserva loja".
- **Severidade:** ALTA (corrupção operacional e financeira entre lojas do mesmo tenant).

### 🚨 4. Rate limiting contornável e, ao mesmo tempo, agressivo demais (login, refresh, booking)
- `Program.cs:192` e `:220`: a partição usa `X-Forwarded-For.Split(',')[0]`, o primeiro valor, que vem do cliente. Basta variar o header para ter tentativas ilimitadas de força bruta em `/api/auth/login`, `/api/platform/auth/login` e `POST /api/public/service/{slug}/appointments`.
- Não há lockout por conta e não há MFA no super-admin (`PlatformAuthController.cs:31-47`).
- No sentido oposto, há usuários legítimos atrás do mesmo NAT (salão de restaurante, loja com vários caixas). O limite de 5 por 15 min é por IP + caminho e vale também para `/api/auth/refresh`. Com mais de 5 usuários ativos, o refresh devolve 429, o FE faz `clearTokens()` (`api-client.ts:62-64`) e derruba a sessão. Na troca de turno, o 6º login seguido também falha.
- **Severidade:** ALTA (brute force no super-admin e indisponibilidade).

### 🚨 5. Revogação de sessão incompleta (platform "revogar sessões", "force-logout" e "reset-password")
- `PlatformController.RevokeUserSessionsAsync` (`:1145-1160`) só remove os `refresh:valid:{jti}` das linhas de `UserSession`.
- Não registram `UserSession`:
  - `SwitchStoreAsync` (`AuthService.cs:226`);
  - `VerifyEmailAsync` (`RegistrationService.cs:197`);
  - a impersonação.
- Esses refresh tokens sobrevivem à revogação. O `RefreshAsync` não compara SecurityStamp e gera o novo access token com o **novo** stamp. Na prática, a sessão do atacante continua por até 7 dias.
- Trocas de senha e bloqueios feitos pelo próprio tenant também não rotacionam o stamp (`UserService.ChangePasswordAsync` e `AdminChangePasswordAsync`, `User.ChangePasswordHash`). Sessões roubadas continuam válidas depois de "trocar a senha".
- **Severidade:** ALTA.

### 6. Hashes bcrypt e PII versionados no repositório
- `nexo_backup.sql` (commit `8e05975`) tem 6 hashes `$2a$12$`, entre eles o `platform_users` **elias@nexo.com** (super-admin), e usuários demo com senhas triviais documentadas no seeder (`ana@123`, `lucas@123`…).
- `nexo_backup_.sql` é quase idêntico.
- O histórico do git guarda os dois arquivos mesmo que sejam apagados.
- **Severidade:** ALTA se essa conta ou senha ainda existe em algum ambiente; média caso contrário.

---

## Inventário: `IgnoreQueryFilters` e SQL bruto

### `IgnoreQueryFilters()`

| # | Local | Uso | Isolamento preservado? |
|---|---|---|---|
| 1 | `Api/Middleware/TenantResolutionMiddleware.cs:79` | Users por `Id` (antes do tenant estar resolvido) | ✅ compara `TenantId` com o claim |
| 2 | `Api/Middleware/SecurityStampValidationMiddleware.cs:77` | SecurityStamp por `userId` | ✅ só lê o stamp |
| 3 | `Infra/Repositories/UserRepository.GetByIdAcrossTenantsAsync` | refresh anônimo | ✅ confere `user.TenantId == claims.TenantId` (`AuthService.cs:143`) |
| 4 | `UserRepository.GetByLoginAsync` | login global | ⚠️ `Login` é único só por tenant (`UserConfiguration.cs:94`). `FirstOrDefault` sem ordem é ambíguo entre tenants (ver Problemas) |
| 5 | `UserRepository.GetByEmailAsync` | — | ⚫ sem uso (código morto) |
| 6 | `Infra/Auth/SessionStoreService.TouchAsync` | `UserSession` por jti | ✅ `UserSession` nem tem filtro (é `BaseEntity`) |
| 7–12 | `Infra/Auth/RegistrationService.cs` (6×) | registro, verificação e reenvio | ✅ fluxos anônimos escopados por token ou tenantId |
| 13 | `StoreEntityRepository.GetByIdAsync` | `SwitchStoreAsync` | ✅ chamador confere `store.TenantId` (`AuthService.cs:197`) |
| 14 | **`StoreEntityRepository.GetByIdTrackedAsync`** | `PATCH /api/stores/{id}/public-slug` | ❌ **não** (achado 🚨1) |
| 15 | `StoreEntityRepository.GetByPublicSlugAsync` | portais públicos | ✅ por slug; ⚠️ não checa status da loja/tenant nem módulo ativo |
| 16 | `StoreEntityRepository.PublicSlugExistsAsync` | `check-slug` anônimo | ✅ unicidade global (permite enumerar slugs: baixo) |
| 17–18 | `StoreEntityRepository.GetByTenantIdAsync`, `GetByIdsAsync` | sessão e lojas | ✅ `TenantId` explícito |
| 19 | `DeliveryOrderRepository.GetByTrackingTokenPublicAsync` | rastreio público | ✅ token GUID de 128 bits |
| 20–21 | `DeliveryOrderRepository.GetByRestOrderIdAsync`, `GetNextOrderNumberForStoreAsync` | — | ✅ tenant e store explícitos |
| 22 | `ProductRepository.GetActiveMenuItemAsync` | pedido público | ✅ `storeId` resolvido pelo slug (sem `TenantId`, mas `StoreId` é global) |
| 23 | `ProductRepository.GetAllMenuItemsAsync` | cardápio público | ✅ tenant e store; ⚠️ o `Include(Category)` também ignora filtro. Somado ao `CategoryId` não validado (ver Problemas), pode vazar o nome de categoria de outro tenant |
| 24–27 | `SvcProfessional`, `SvcCatalogItem`, `SvcAppointment`, `SvcSettings` `*PublicAsync` | portal de agendamento | ✅ tenant e store explícitos |
| 28–29 | `ModifierGroupRepository` | cardápio | ✅ tenant explícito |
| 30–31 | `CouponRepository` | cupom e pedido público | ✅ tenant e store |
| 32–33 | `CustomerRepository.*PublicAsync` | booking público | ✅ tenant |
| 34 | `FoodServiceSettingsRepository.GetByStoreIdAsync` | público | ✅ tenant e store |
| 35 | `DeliveryZoneRepository.GetAllByStoreIdPublicAsync` | público | ✅ tenant e store |
| 36 | `Persistence/Provisioning/DefaultFinancialAccountProvisioner.cs:50` | provisionamento | ✅ tenant explícito |
| 37 | `Api/Controllers/FeaturesController.cs:60` | overrides de flags | ✅ `tenantId` do `ICurrentTenant` |
| 38–91 | `PlatformController` (39×), `PlatformFlagsController` (9×), `InterpreterAdminController` (6×) | backoffice | ✅ por desenho, atrás de `[Authorize(Policy="Platform")]` |
| — | `Persistence/Seed/DataSeeder.cs` (11×) | startup | ✅ |

### SQL bruto e bulk

| Local | Tipo | Isolamento |
|---|---|---|
| `Repositories/Modules/Restaurante/TableRepository.cs:30` | `FromSqlRaw ... FOR UPDATE` parametrizado | ✅ `tenant_id` e `store_id` explícitos, mais o filtro global (EF compõe o filtro sobre FromSql) |
| `Repositories/Modules/Service/SvcOrderRepository.cs:36` (`LockAsync`) | `ExecuteSqlInterpolatedAsync` (`SELECT 1 … FOR UPDATE`) | ✅ parametrizado, filtra `tenant_id` (não `store_id`, mas só trava a linha; a leitura é feita pelo repositório filtrado) |
| `Repositories/Modules/Service/SvcPaymentRepository.cs:43` | idem | ✅ idem |
| `Repositories/Modules/Service/SvcCommissionRepository.cs:117,142,180` | `ExecuteUpdateAsync` sobre `DbSet` | ✅ o filtro global de tenant e store é aplicado. Obs.: contorna o interceptor, mas não mexe em `TenantId` |
| Dapper / `NpgsqlCommand` | — | não existe |

---

## Autenticação de usuários do tenant (login, JWT, refresh, sessão, logout)
**Objetivo:** login por login/senha, JWT HS256 com access de 15 min e refresh de 7 dias com rotação, revogação e sessão rastreável.
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 60%. O fluxo funciona ponta a ponta e tem bons testes de integração. Mas o refresh reseta a loja, não há detecção de reuso, a revogação é incompleta, os tokens ficam em localStorage e o rate limit é contornável e também derruba sessões atrás de NAT.

### Backend existente
- `POST /api/auth/login`, `/refresh`, `/logout`, `/switch-store`; `GET /api/auth/me` (`Api/Controllers/AuthController.cs`).
- `AuthService` (`Application/Features/Auth/AuthService.cs`) e `JwtTokenService` (`Infra/Auth/JwtTokenService.cs`). O access token tem claims `tenantId`, `storeId`, `store[]`, `module[]`, `role`, `security_stamp`. O refresh usa audiência `nexo-refresh`. O secret exige no mínimo 32 caracteres (`:181`).
- O estado do refresh vive **só no cache**: `refresh:valid:{jti}`, via `RedisCacheService`, ou `NoOpCacheService` quando não há Redis.
- `UserSession` (BaseEntity, sem filtro) é gravada no login (`SessionStoreService`) e atualizada no refresh.
- Middlewares:
  - `TenantResolutionMiddleware`: confere usuário × tenant, status do usuário e status do tenant, com cache de 5 min;
  - `SecurityStampValidationMiddleware`: cache de 60 s.
- Cookies `nexo_access` e `nexo_refresh`: httpOnly, `SameSite=None; Secure` fora de Development. O JwtBearer aceita o cookie como fallback (`Program.cs:89-95`).

### Frontend existente
- `services/api-client.ts`: guarda access e refresh em **localStorage** (`:30-33`), refresh transparente em 401 com single-flight, e **não usa `credentials:'include'`**. Na prática, os cookies nunca são enviados nem gravados pelo navegador (cross-site), então são código morto.
- `modules/auth/services/authService.ts` e `context/AuthContext.tsx`: revalidam com `/auth/me`.
- `components/shared/StoreSwitcher.tsx` chama `switch-store`.

### Funcionalidades concluídas
- Login, refresh com rotação (o jti antigo é removido) e logout. Testes: `Security/AuthSessionSecurityTests.cs` (rotação, token fabricado, refresh antigo depois do switch) e `Auth/AuthenticationLifecycleTests.cs` (18).
- Bloqueio de usuário inativo, bloqueado ou de tenant suspenso em ≤5 min (`TenantResolutionMiddleware.cs:96-117,155-163`). Testes em `AuthorizationTests`.
- Adulteração de token e claims rejeitada (`TamperingWithToken_ChangeRole_FailsValidation`, `TenantIdClaim_CannotBeChangedByUser`).
- `switch-store` valida que a loja pertence ao tenant e está ativa (`AuthService.cs:196-201`). Teste: `StoreIsolationTests.SwitchStore_CrossTenantStore_IsForbidden`.

### Funcionalidades parciais
- **Refresh:**
  - reseta a loja (🚨3);
  - não compara SecurityStamp;
  - o par Get/Remove não é atômico, então dois refreshes concorrentes geram duas famílias válidas;
  - não há detecção de reuso: um token antigo é só rejeitado, sem revogar a família;
  - `RemoveAsync` engole erros do Redis (`RedisCacheService` linhas ~83-87), então uma revogação pode falhar em silêncio.
- **Sessões (`UserSession`):** criadas só no login. Switch-store, verify-email e impersonação não registram sessão. O logout não marca `IsRevoked` (só remove do cache), então o backoffice continua mostrando a sessão como ativa.
- **Login:** não checa `tenant.Status` (o tenant suspenso recebe token e toma 403 depois). Responde `email_not_verified` ou `account_blocked` **antes** de verificar a senha (`AuthService.cs:50-57`), o que enumera contas e revela o status sem precisar da senha.
- **Dependência total de Redis:** com Redis degradado (problema já diagnosticado em produção, na memória do projeto), todo refresh falha e o usuário é deslogado a cada 15 min.

### Funcionalidades não desenvolvidas
- Detecção de reuso ou revogação de família de refresh.
- Rotação de SecurityStamp em trocas feitas pelo próprio tenant.
- Listagem e revogação de sessões pelo próprio usuário.
- MFA.

### Problemas
- **Login ambíguo entre tenants:** `Login` é único por `(TenantId, Login)` (`UserConfiguration.cs:94`). `CreateAsync` checa duplicidade só no tenant (`UserRepository.LoginExistsAsync`, com filtro). `GetByLoginAsync` faz `FirstOrDefault` global sem ordenação. Dois tenants com um usuário `caixa` cada fazem um dos dois nunca conseguir logar (dá "credenciais inválidas" ou status do outro usuário). Isso também permite DoS deliberado de um tenant contra logins "comuns" de outro. Severidade média-alta (disponibilidade).
- **Maiúsculas no login:** `CreateUserRequestValidator` aceita maiúsculas no `Login`, mas o login faz `ToLowerInvariant()` no input (`AuthService.cs:47`). Um usuário criado como `Joao` nunca consegue logar. Bug.
- **Tokens em localStorage:** o refresh de 7 dias pode ser exfiltrado por XSS. O frontend (Caddy, `nexo-main/Caddyfile`) não envia **nenhum** header de segurança (CSP, XFO, HSTS).
- **Cookies:** a configuração de cookies, com SameSite=None e o fallback de cookie no JwtBearer, é superfície morta e confusa. Ou se adota cookie com CSRF token, ou se remove.
- **Platform login embutido no login de tenant:** se o login do tenant falha e o input tem `@`, `/api/auth/login` tenta `PlatformUsers` (`AuthController.cs:110-135`). Isso dobra o orçamento de brute force do super-admin (partição por caminho) e não audita.

### Dependências
Redis (refresh e caches), `IJwtTokenService`, `Jwt:Secret` em env var do Railway.

### Próximos passos
1. Preservar a loja no refresh: claim `storeId` no refresh token ou validar o `storeId` atual contra `store[]`. Criar teste "refresh preserva loja".
2. Rate limit por `RemoteIpAddress` com `ForwardedHeaders`/`KnownProxies` (último hop confiável), mais lockout por conta; aumentar o limite do refresh ou partir por usuário.
3. Registrar `UserSession` em switch-store, verify-email e impersonação; checar stamp ou `IsRevoked` no refresh; revogar a família no reuso; usar `GETDEL` atômico.
4. Bump de SecurityStamp mais revogação de sessões em troca de senha, reset, bloqueio e mudança de papel no tenant.
5. Tornar o login único globalmente (índice único em `Login`) ou exigir tenant/slug no login.
6. Tirar o refresh do localStorage (cookie httpOnly real com `credentials:'include'` mais CSRF) e adicionar headers de segurança no Caddy.

---

## Autenticação e backoffice da plataforma (super-admin, impersonação, revogação)
**Objetivo:** login do operador ORKEN, gestão de tenants e usuários, impersonação auditada, revogação de sessões.
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 60%. A política `Platform` está aplicada no nível do controller e tem testes. Faltam MFA, revogação do token de plataforma e lockout. A revogação de sessões de tenant é incompleta. A impersonação não deixa marca no token.

### Backend existente
- `POST /api/platform/auth/login` (`PlatformAuthController.cs`), com rate limit `auth-login`.
- `PlatformController`, `PlatformFlagsController` e `InterpreterAdminController`, todos com `[Authorize(Policy = "Platform")]` (exige claim `type=platform`, `Program.cs:103-110`).
- Token de plataforma: 8 h, audiência `nexo-platform`, sem refresh e sem stamp (`JwtTokenService.cs:189-205`).
- Bootstrap idempotente do super-admin via `Seed:PlatformEmail` e `Seed:PlatformPassword` em todos os ambientes (`DataSeeder.cs:321-350`). Ressincroniza a senha a cada deploy.
- Endpoints de usuário do tenant: reset de senha, force-logout, sessões e revoke-all (`PlatformController.cs:809-930`), todos com `BumpSecurityStamp` e auditoria.
- `POST /api/platform/tenants/{id}/impersonate` (`:479-545`): gera um par de tokens do primeiro usuário `Diretoria` e audita como `Critical`.

### Frontend existente
- Páginas `/platform*` (`AppRouter.tsx:309-317`) atrás de `PlatformRoute` (`session.type === "platform"`).
- Impersonação passa os tokens por localStorage e abre `/impersonate` (`PlatformTenantDetailPage.tsx:125-132`, `pages/ImpersonatePage.tsx`).

### Funcionalidades concluídas
- Login e política de plataforma (`Security/PlatformAuthorizationTests.cs`, 3; `Auth/PlatformBootstrapTests.cs`, 2).
- Auditoria de ações privilegiadas (`Security/PlatformAuditTests.cs`, 2).
- Reset, force-logout e revoke-all funcionam para sessões criadas por login.

### Funcionalidades parciais
- **Revogação:** não pega sessões de switch-store e verify-email (🚨5).
- **Impersonação:**
  - o token é **idêntico** ao do usuário real (sem claim `impersonatedBy`/`act`), então toda ação no tenant é auditada como se fosse da Diretoria;
  - o refresh devolvido não é registrado no cache, então morre em 15 min (ok);
  - mas o FE grava os tokens em localStorage compartilhado entre abas.
- **Login de plataforma:**
  - não há auditoria de login com sucesso ou falha;
  - se o e-mail não existe, o bcrypt é pulado (diferença de tempo permite enumerar e-mails);
  - o token de 8 h não é revogável.

### Funcionalidades não desenvolvidas
- MFA.
- Papéis granulares de plataforma (todo `PlatformUser` é `super_admin`).
- Revogação do token de plataforma.

### Problemas
- **Chaves de provedores de IA:** ficam em `AiProvider.ApiKeyEncrypted` como **Base64**, não criptografadas (`InterpreterAdminController.cs`, por volta de `:391`; `// TODO: replace with real AES-256`). Média.
- **`RequirePasswordChange`:** o admin criado pela plataforma nasce com `requirePasswordChange: true` (`PlatformController.cs`, por volta de `:270`), mas **nada** aplica isso (o backend não bloqueia e o FE não redireciona). A senha inicial definida pelo operador fica para sempre.

### Dependências
Seed env vars, Redis.

### Próximos passos
MFA (TOTP) no super-admin; claim de impersonação e trilha `actor≠subject`; auditoria de login de plataforma; criptografia real (Data Protection/KMS) para as chaves de IA; aplicar `RequirePasswordChange`.

---

## Autorregistro, verificação de e-mail, provisionamento, recuperação de senha
**Objetivo:** cadastro self-service que cria tenant, loja, usuário, trial e configurações; verificação de e-mail com auto-login; recuperação de senha.
**Estado:** 🟠 PARCIAL   **Maturidade:** 45%. O registro e a verificação funcionam. Mas não há recuperação de senha (o link da UI está quebrado), o fluxo não é transacional, não tem rate limit e enumera e-mails.

### Backend existente
- `POST /api/auth/register`, `GET /api/auth/verify-email?token=`, `POST /api/auth/resend-verification` (`AuthController.cs:325-380`).
- `RegistrationService` (`Infra/Auth/RegistrationService.cs`):
  - cria `Tenant`, `ModuleSubscription.CreateTrial("varejo")`, `Store "Loja Principal"`, `User` `Diretoria` em `PendingVerification` e `AppSettings`;
  - roda `DefaultFinancialAccountProvisioner.EnsureAsync`;
  - gera token de verificação de 24 h, persistido no Postgres.
- Provisionamento pela plataforma: `POST /api/platform/tenants`.

### Frontend existente
- `/register`, `/check-email`, `/verify-email` (`AppRouter.tsx:159-164`).
- `OnboardingWizard` via `isNewAccount` (flag em localStorage).

### Funcionalidades concluídas
- Registro e verificação com auto-login (tokens emitidos em `VerifyEmailAsync`).
- O reenvio não vaza se o e-mail existe.

### Funcionalidades parciais
- **Registro em 7 `SaveChangesAsync` separados, sem transação** (`RegistrationService.cs:58-115`). Uma falha no meio deixa tenant, loja ou usuário órfãos.
- **`POST /register` responde 409 `email_already_registered`**: enumeração, que contradiz a postura "silenciosa" do resend.
- **Sem rate limit em `register` e `resend-verification`**: permite criar tenants em massa e bombardear e-mails.
- **JSON de `AppSettings` montado por concatenação**, escapando só `"` (`:103-104`). Um nome com `\` gera JSON inválido ou injetado (só no próprio tenant, baixo).
- **Senha mínima de 6 caracteres** sem nenhuma outra regra (`AuthController.cs:333`, `CreateUserRequestValidator.cs:30`).

### Funcionalidades não desenvolvidas
- **Recuperação de senha ("esqueci a senha")**: o FE tem link para `/forgot-password` (`modules/auth/pages/LoginPage.tsx:147`), mas **não existe rota no FE nem endpoint no BE**. O usuário que esquece a senha depende da Diretoria ou do operador da plataforma.
- **Convite de usuário por e-mail**: o admin cria o usuário com senha definida e a comunica fora do sistema.

### Próximos passos
Transação única no registro; reset de senha com token de uso único e expiração curta; rate limit em register/resend; resposta neutra no register.

---

## Multi-tenancy (Tenant/Store, ICurrentTenant/ICurrentStore, filtros, interceptor)
**Objetivo:** isolamento automático por tenant e por loja em toda leitura e escrita.
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 70%. O desenho é sólido: filtro global gerado por reflexão para **todo** `TenantEntity`/`StoreEntity`, interceptor e escopo explícito na maioria dos usos de `IgnoreQueryFilters`. Ficam um IDOR real, a falta de validação de FKs de entrada e a falta de proteção de `StoreId` no interceptor.

### Backend existente
- `NexoDbContext.ApplyTenantQueryFilters` (`Infra/Persistence/NexoDbContext.cs:174-210`): `StoreEntity` → `TenantId && StoreId`; `TenantEntity` → `TenantId`. Sem tenant resolvido, filtra por `Guid.Empty` (fail-closed).
- `CurrentTenantService` é preenchido pelo middleware; `CurrentStoreService` lê o claim `storeId` assinado.
- `TenantSaveChangesInterceptor` (`Infra/MultiTenancy/TenantSaveChangesInterceptor.cs`):
  - injeta `TenantId` e `StoreId` no INSERT;
  - bloqueia INSERT de outro tenant;
  - bloqueia mudança de `TenantId`;
  - torna `StockMovement` e `CashMovement` imutáveis.
- **Cobertura:** todas as entidades com `tenant_id` herdam `TenantEntity`/`StoreEntity`, exceto as abaixo, que não têm filtro:
  - `AuditRecord`: filtrada manualmente em `Infra/Audit/AuditQueryService.cs:30,78` (ok);
  - `UserSession`, `TenantNote`, `TenantFeatureOverride`, `ModuleSubscription`, `TenantAiLimit`, `InterpreterTelemetry`: usadas só por plataforma, auth ou repositórios com `TenantId` explícito (ok).

### Funcionalidades concluídas (com testes)
- `Security/TenantIsolationTests.cs` (6): produto e usuários cross-tenant, verify-manager cross-tenant.
- `Security/StoreIsolationTests.cs` (4): caixa invisível entre lojas, switch-store.
- Testes de isolamento por módulo em `IntegrationTests/Service/*`.

### Funcionalidades parciais e problemas
- **IDOR** em `PATCH /api/stores/{id}/public-slug` (🚨1).
- **Interceptor:**
  - não valida que `StoreId` informado explicitamente pertence ao tenant ou à loja atual;
  - não impede UPDATE de `StoreId`;
  - pula **toda** validação quando o tenant não está resolvido (fluxos públicos e tokens de plataforma em endpoints de tenant). Nos fluxos públicos, a correção depende de cada service passar `TenantId`/`StoreId` certos (hoje passa).
- **FKs de entrada não validadas por tenant:** ex.: `ProductService.CreateAsync`/`UpdateAsync` aceitam `request.CategoryId` sem checar existência no tenant (`Application/Features/Products/ProductService.cs:61,91`). A FK permite referenciar a categoria de outro tenant, e o cardápio público (`GetAllMenuItemsAsync`, que ignora filtros) carrega o nome dessa categoria. Baixo, mas é um padrão a varrer.
- **Fluxos públicos:** `GetByPublicSlugAsync` não checa `Store.Status`, `Tenant.Status` nem assinatura do módulo. Um tenant suspenso continua recebendo pedidos e agendamentos públicos.
- **Troca de loja:** não existe atribuição usuário→loja. Todo usuário pode trocar para qualquer loja ativa do tenant (`store[]` = todas as lojas). O teste `UserCanOnlyAccessAssignedStores` só testa um GUID aleatório.

### Próximos passos
Corrigir `GetByIdTrackedAsync` (tenant explícito ou filtro); validar `StoreId` no interceptor; varrer FKs de entrada; checar status e módulo nos portais públicos; avaliar ACL usuário→loja.

---

## Autorização (papéis, módulos, usuários, gerente)
**Objetivo:** controlar o que cada papel (`diretoria`, `gerente`, `vendedor`, `estoquista`, `cozinha`) pode fazer e quais módulos o tenant pode usar.
**Estado:** 🟠 PARCIAL   **Maturidade:** 35%. Os gates de módulo funcionam no backend. O RBAC existe de forma consistente só no frontend. O backend deixa quase tudo aberto a qualquer papel, e o fluxo de autorização gerencial é só cliente (e está quebrado na UI de vendas).

### Backend existente
- Papéis: `Domain/Enums/UserRole.cs`, emitidos como `ClaimTypes.Role`.
- `[Authorize(Roles=…)]` só em 8 ações (lista no 🚨2).
- Gates de módulo:
  - `RequireModuleAttribute` (cache `tenant:{id}:modules`, 5 min) em Varejo, Restaurante e Build;
  - `RequireServiceModuleAttribute` (lê `ICurrentTenant.ActiveModules`);
  - `RequireModuleFilter` existe mas não é usado.
- **Sem gate de módulo:**
  - `CouponsController` e `DeliveryZonesController` (restaurante);
  - Interpreter (`MovementsController`, `AttachmentsController`, `TenantInterpreterController`);
  - core (produtos, estoque, clientes, financeiro, caixa, relatórios).
- Usuários: `UsersController` CRUD; `change-password`, `admin-reset-password` e `validate-manager` (`UserService`).
- Gerente: `POST /api/auth/verify-manager`, com escopo de tenant corrigido (`AuthService.cs:280`).

### Frontend existente
- `app/router/routes.ts`: papéis por rota (MGMT = diretoria e gerente).
- `RoleRoute`, `ModuleRoute`, `ServiceModuleRoute`, `PlatformRoute` (`AppRouter.tsx`).
- `modules/users/*` com API real.
- `SaleCancellationDialog`.

### Matriz FE × BE (amostra)

| Rota FE | Papéis FE | Endpoint BE | Papel exigido no BE |
|---|---|---|---|
| `/auditoria` | diretoria | `GET /api/audit` | qualquer autenticado |
| `/usuarios` | diretoria | `GET /api/users` | gerente/diretoria; `GET /api/users/{id}`: qualquer |
| `/configuracoes` | MGMT | `PUT /api/settings` | gerente/diretoria ✅ |
| `/assinatura` | MGMT | `POST /api/billing/checkout\|portal` | qualquer |
| `/caixa`, `/vendas` | MGMT | `/api/cash/*`, `/api/sales/{id}/cancel` | qualquer |
| `/restaurante/financeiro` | MGMT | `/api/restaurante/financeiro/*` | qualquer (com módulo) |
| `/service/*` | MGMT | `/api/v1/service/*` | qualquer (com módulo); comissões: só 2 ações MGMT |
| `/restaurante/portal`, `/service/portal` | MGMT | `PATCH /api/stores/{id}/public-slug` | qualquer (e cross-tenant, 🚨1) |

### Funcionalidades concluídas
- Gates de módulo (`AuthorizationTests.ModuleRequirement_*`, `ModuleActivation_EventuallyAllowsAccess`).
- CRUD de usuários pela Diretoria.
- `verify-manager` isolado por tenant (`TenantIsolationTests.VerifyManager_CrossTenant_*`).

### Funcionalidades parciais
- **RBAC no backend:** ver 🚨2.
- **Autorização gerencial:** existem dois endpoints duplicados (`/auth/verify-manager`, sem consumidor no FE, e `/users/validate-manager`). O resultado nunca é exigido pelo endpoint protegido.
- **Cancelamento de venda quebrado na UI:** `SaleCancellationDialog.tsx:61` chama a versão **síncrona** `userService.validateManagerAuthorization`, que **sempre** retorna `{success:false, "Use validateManagerAuthorizationAsync."}` (`modules/users/services/userService.ts:73-79`). Resultado: 🔴 o fluxo de cancelamento com senha de gerente não funciona na UI (embora a API de cancelamento esteja aberta a qualquer papel).
- **Oráculos de senha sem rate limit,** disponíveis a qualquer autenticado:
  - `POST /api/users/{id}/change-password`: não confere `id == usuário atual`; com a "senha atual" de outro usuário, troca a senha dele; o 403/204 funciona como oráculo;
  - `POST /api/users/validate-manager`: as mensagens distinguem "Usuário não encontrado", "Senha incorreta" e "sem autorização";
  - `POST /api/auth/verify-manager`.
  - Um vendedor pode fazer força bruta na senha do gerente ou da diretoria.
- **Papel da Diretoria:** a Diretoria pode rebaixar ou bloquear a si mesma ou o último diretor (sem proteção). Mudança de papel e bloqueio não invalidam tokens (até 15 min com o papel antigo).

### Funcionalidades não desenvolvidas
- Políticas de autorização por recurso no backend.
- ACL de lojas por usuário.
- Prova de autorização gerencial vinculada à operação (ex.: token de uso único do gerente exigido em `cancel`).

### Próximos passos
1. Matriz de permissões central (policies por papel) aplicada em todos os controllers, começando por financeiro, caixa e vendas (cancelar), auditoria, billing, settings do Service e stores.
2. Exigir prova gerencial no servidor.
3. Rate limit e lockout nos oráculos; `change-password` só para o próprio usuário.
4. Corrigir `SaleCancellationDialog` para a versão async.
5. `RequireModule("restaurante")` em Coupons e DeliveryZones.

---

## Endpoints públicos, uploads, SignalR e cache
**Objetivo:** expor portais públicos (cardápio, pedidos, agendamento) e assets sem vazar dados; tempo real por loja; cache escopado.
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 60%.

### Backend existente
- `Controllers/Public/PublicOrdersController.cs`:
  - `GET /api/public/menu/{slug}`;
  - `GET /api/public/orders/{trackingToken}`;
  - `POST /api/public/orders`;
  - `GET /api/public/delivery-zones/{slug}`;
  - `POST /api/public/coupons/validate`.
- `PublicServiceController.cs`: `GET /api/public/service/{slug}[/catalog|/professionals|/availability]` e `POST …/appointments` (único com `public-booking`).
- `Integrations/StorageController.cs`: `POST /api/integrations/storage/upload`, `DELETE /{*key}`.
- `Infra/Hubs/RestaurantHub.cs`.

### Funcionalidades concluídas
- **DTOs públicos** sem `tenantId`/`storeId`. Preço, taxa e cupom são sempre recalculados no servidor (`DeliveryOrderService.CreateFromPortalAsync`, `:255-300`). O tracking token é um GUID de 128 bits.
- **Upload:**
  - whitelist de contexto e de content-type (config: jpeg/png/webp);
  - tamanho máximo (`MaxFileSizeMb=10`, mais `RequestSizeLimit 20MB`);
  - chave gerada no servidor `tenants/{tenantId}/{ctx}/{guid}{ext}`, que nunca usa o nome original;
  - DELETE restrito ao prefixo do tenant;
  - testes em `UnitTests/Integrations/StorageControllerTests.cs`.
- **SignalR:** o `JoinStore` exige `storeId ∈ store[]` do token; grupo `store:{tenant}:{store}` (`RestaurantHub.cs:21-31`).
- **Cache:** chaves escopadas, como `dashboard:summary:{tenant}:{store}`, `tenant:{id}:info`, `features:{tenantId}`, `user:{id}:info`. Chaves globais só para dados públicos (CEP, barcode, clima). Nenhum vazamento entre tenants encontrado.

### Problemas
- **Rate limit nos endpoints públicos:** `POST /api/public/orders` e `/coupons/validate` não têm. Isso permite spam de pedidos na cozinha e força bruta de códigos de cupom. O limite do booking usa XFF falsificável.
- **Uploads:**
  - o content-type só é validado pelo header do cliente (sem magic bytes);
  - o bucket é **público**: fotos de `service-record` (prontuários de clínica ou pet, fotos de OS) ficam acessíveis sem autenticação a quem tiver a URL (LGPD, médio);
  - nenhum contexto é condicionado ao módulo ativo.
- **SignalR:** o token vai na query string (`Program.cs:82-85`, item pendente do relatório anterior). Conexões longas não são reavaliadas depois de revogação ou bloqueio.
- **Invalidação de cache:** suspender um tenant (`PlatformController.SetTenantStatus`) ou bloquear um usuário não invalida `tenant:{id}:info` nem `user:{id}:info`. O efeito leva até 5 min (aceitável, mas não documentado na UI).

---

## Hardening transversal (rate limit, CORS, headers, segredos, logs)
**Estado:** 🟠 PARCIAL   **Maturidade:** 45%.

- **Rate limiting:** ver 🚨4. Só existem as políticas `auth-login` (login, refresh, platform login) e `public-booking`. Não há limite global.
- **CORS** (`Program.cs:113-145`):
  - lista fixa de origens com `AllowCredentials`;
  - **inclui `http://localhost:3000|8080|5173` em produção**;
  - `AllowAnyHeader()`, uma regressão do item 4 do relatório anterior, que dizia ter restringido os headers.
- **Headers:**
  - no **backend**, há XFO, nosniff, Referrer-Policy, Permissions-Policy e CSP (`Program.cs:368-390`). Mas o middleware fica **depois** de `UseAuthorization` e dos middlewares de tenant, então respostas 401, 403 e 429 geradas antes não recebem os headers. Não há HSTS. Testes: `Security/SecurityHeadersTests.cs` (13);
  - no **frontend**, o Caddy não envia nenhum header de segurança (`nexo-main/Caddyfile`), e é no frontend que o token mora.
- **Segredos:**
  - `appsettings.json` versionado tem `Jwt:Secret` placeholder "CHANGE_THIS…" (68 chars). Não há guarda de startup que rejeite o placeholder em produção: se a env var faltar no Railway, o segredo público assina tokens válidos. Média (não verificável aqui);
  - `appsettings.Development.json` está no `.gitignore` (ok);
  - `nexo-main/.env.production` só tem `VITE_API_BASE_URL` (ok);
  - dumps SQL com hashes (achado 6);
  - nenhum `sk_live`, `whsec_`, `AKIA` ou URL com credencial encontrado via `git grep`.
- **Logs:** o `RequestLoggingRedactionMiddleware` é praticamente um no-op (lê o body e loga só o *nome* do campo em Debug). Mesmo assim, não há vazamento de senha, porque o Serilog request logging não registra corpo nem query. O `ExceptionHandlingMiddleware` devolve uma mensagem genérica no 500 (ok). O `Login` usa `RemoteIpAddress` (IP do proxy) na `UserSession`, então o IP registrado é inútil.
- **Migrations:** `Program.cs:296-312` roda `MigrateAsync()` no startup em produção (pendente do relatório anterior).

---

## Comparação com `SECURITY_CODE_REVIEW_REPORT.md` (11/05/2026)

| Item do relatório | Status atual | Evidência |
|---|---|---|
| 1. Tokens no JSON de login/refresh | ✅ mantido (e o FE usa **só** o JSON e localStorage) | `AuthController.cs:102`, `api-client.ts:30-33` |
| 2. Delete de cookie com options | ✅ | `AuthController.cs` (Logout) |
| 3. Rate limit no refresh | ✅ existe, mas ⚠️ contornável por XFF e derruba sessões atrás de NAT | `AuthController.cs:148`, `Program.cs:192` |
| 4. CORS restrito (headers) | ❌ **regrediu**: `AllowAnyHeader()` mais origens localhost em produção | `Program.cs:135-138` |
| 5. N+1 no middleware de tenant | ✅ cache `user:{id}:info` | `TenantResolutionMiddleware.cs:74` |
| 6. Secure=IsHttps | 🔁 substituído por "Secure fora de Development" (correto atrás do proxy) | `AuthController.cs:76` |
| 7. SameSite=Strict consistente | 🔁 mudou para `None` (justificado cross-site), mas os cookies são ignorados pelo FE | — |
| 8. Expiração do refresh alinhada | ✅ | `AuthController.cs:180` |
| 9. Status do usuário no middleware | ✅ | `TenantResolutionMiddleware.cs:96-117` |
| 10. N+1 no `RequireModule` | ✅ cache | `RequireModuleAttribute.cs:36` |
| 11. Logout com cookie fallback | ✅ | `AuthController.cs` |
| 12. CSP | ✅ (API); ❌ inexistente no FE (Caddy) | — |
| 13. Redação de senha em log | ⚠️ "corrigido" por um middleware que não faz nada; o risco real é baixo porque bodies não são logados | `RequestLoggingRedactionMiddleware.cs` |
| Pendente: SignalR por query string | ❌ aberto | `Program.cs:82` |
| Pendente: `IgnoreQueryFilters` em `ProductRepository` | ✅ auditado (ok). Mas **novo** IDOR em `StoreEntityRepository` | tabela acima |
| Pendente: credenciais em `appsettings.Development` | ✅ gitignored | `.gitignore:34` |
| Pendente: migrations em produção | ❌ aberto | `Program.cs:296` |
| Problemas novos não citados no relatório | RBAC backend, refresh reseta loja, revogação incompleta, login ambíguo, XFF, dumps SQL, Base64 nas chaves de IA, sem forgot-password | ver acima |

---

## Backend sem frontend
- `POST /api/auth/verify-manager` (o FE usa `/users/validate-manager`).
- `GET /api/stores/check-slug` anônimo (o FE chama autenticado; ok).
- Cookies `nexo_access`/`nexo_refresh` e o fallback de cookie no JwtBearer: o FE nunca envia `credentials`.
- `UserRepository.GetByEmailAsync`: sem chamador.
- `RequireModuleFilter`: classe sem uso.
- Endpoints de sessões de usuário da plataforma existem; o FE de plataforma consome parte deles (não verificado em detalhe).

## Frontend sem backend / fluxo real
- `/forgot-password` (link em `LoginPage.tsx:147`): sem rota e sem endpoint.
- `requirePasswordChange` (switch em `UserFormSections.tsx:125`): gravado, mas nunca aplicado no login.
- `SaleCancellationDialog`: autorização gerencial sempre falha (usa um stub síncrono).
- Papéis por rota (`routes.ts`): sem contrapartida no backend para a maioria das rotas.

## Código legado/morto
- `userService.validateManagerAuthorization` (stub síncrono que sempre falha), `modules/users/services/userService.ts:73-79`.
- `RequestLoggingRedactionMiddleware` (efeito nulo).
- Cookies de auth (efetivamente mortos por causa do FE).
- `UserRepository.GetByEmailAsync`, `RequireModuleFilter`.
- Teste com comentário obsoleto: `AuthSessionSecurityTests.cs:24-33` descreve um gap de switch-store que o teste `SwitchStore_OldRefreshToken_IsRejected` hoje dá como corrigido.

## Achados de segurança

| # | Achado | Severidade | Evidência |
|---|---|---|---|
| 1 | IDOR cross-tenant em `PATCH /api/stores/{id}/public-slug` (sequestro ou desativação do portal de outro tenant) | **Alta** (crítica se os IDs vazarem) | `StoreController.cs:86-110`, `StoreEntityRepository.cs:26-29` |
| 2 | RBAC ausente no backend; papéis inferiores acessam financeiro, auditoria, billing, cancelamentos e settings | **Alta** | só 8 `Authorize(Roles)` |
| 3 | Rate limit contornável via `X-Forwarded-For`; sem lockout nem MFA no super-admin | **Alta** | `Program.cs:192,220`; `PlatformAuthController.cs` |
| 4 | Revogação de sessão (reset, force-logout, revoke-all) não cobre switch-store e verify-email; refresh gera token com o stamp novo; troca de senha no tenant não revoga nada | **Alta** | `PlatformController.cs:1145`; `AuthService.cs:226`; `UserService.cs:112-150` |
| 5 | Hashes bcrypt (inclui platform user) e PII em `nexo_backup*.sql` versionados | **Alta/Média** | raiz do repo, commit `8e05975` |
| 6 | Refresh reseta a loja ativa, com gravação em loja errada (integridade) | **Alta** (integridade) | `AuthService.cs:154` |
| 7 | Oráculos de senha sem limite (`change-password` de outro id, `validate-manager`, `verify-manager`) | **Média** | `UsersController.cs:72-104`, `UserService.cs:160-200` |
| 8 | Tokens (refresh de 7 dias) em localStorage, sem CSP ou headers no frontend | **Média** | `api-client.ts:30-33`, `Caddyfile` |
| 9 | Refresh sem detecção de reuso e com rotação não atômica; revogação engole erro do Redis | **Média** | `AuthService.cs:128-161`, `RedisCacheService` |
| 10 | Impersonação sem marca no token (auditoria atribui as ações à Diretoria) | **Média** | `PlatformController.cs:515-521` |
| 11 | Chaves de API de IA em Base64 ("ApiKeyEncrypted") | **Média** | `InterpreterAdminController.cs` (~`:391`) |
| 12 | Bucket público para `service-record` (dados sensíveis de clientes) | **Média** | `StorageController.cs:21-27` |
| 13 | `Jwt:Secret` placeholder versionado sem guarda de produção | **Média** | `appsettings.json` |
| 14 | Login ambíguo entre tenants (`Login` único só por tenant), com DoS de login | **Média** | `UserConfiguration.cs:94`, `UserRepository.cs:38-40` |
| 15 | Enumeração: `register` 409; status da conta revelado antes da senha; timing no platform login | **Baixa/Média** | `AuthController.cs:340`, `AuthService.cs:50-57` |
| 16 | Endpoints públicos `orders` e `coupons/validate` sem rate limit | **Média** | `PublicOrdersController.cs:56,73` |
| 17 | Portais públicos não checam status do tenant ou loja nem o módulo | **Baixa** | `StoreEntityRepository.GetByPublicSlugAsync` |
| 18 | CORS com origens localhost em produção mais `AllowAnyHeader` | **Baixa** | `Program.cs:118-138` |
| 19 | Headers de segurança não aplicados em 401/403/429 do pipeline; sem HSTS | **Baixa** | `Program.cs:368` |
| 20 | FKs de entrada não validadas por tenant (ex.: `CategoryId`) | **Baixa** | `ProductService.cs:61,91` |
| 21 | Registro sem transação e sem rate limit (tenants órfãos, spam de e-mail) | **Baixa/Média** | `RegistrationService.cs:58-115` |
| 22 | Senha mínima de 6 caracteres; `RequirePasswordChange` não aplicado | **Baixa** | validators; `PlatformController.cs` (~`:270`) |

## Testes existentes para esta área

| Arquivo | Testes |
|---|---|
| `tests/Nexo.IntegrationTests/Security/AuthorizationTests.cs` | 14 (alguns frouxos: `LowerRole_CannotAccessAdminOnlyEndpoints` tem asserções condicionais com `if (Created)`) |
| `Security/TenantIsolationTests.cs` | 6 |
| `Security/StoreIsolationTests.cs` | 4 |
| `Security/AuthSessionSecurityTests.cs` | 4 |
| `Security/SecurityHeadersTests.cs` | 13 |
| `Security/PlatformAuthorizationTests.cs` | 3 |
| `Security/PlatformAuditTests.cs` | 2 |
| `Auth/AuthenticationLifecycleTests.cs` | 18 |
| `Auth/CookieSecurityTests.cs` | 10 |
| `Auth/RateLimitingTests.cs` | 11 |
| `Auth/ConcurrencyTests.cs` | 8 |
| `Auth/AuthEndpointsTests.cs` | 7 |
| `Auth/PlatformBootstrapTests.cs` | 2 |
| `tests/Nexo.UnitTests/Auth/AuthServiceTests.cs` mais `Users/UserServiceTests.cs` | 16 |
| `UnitTests/Integrations/StorageControllerTests.cs` | upload/delete |
| Isolamento por módulo em `IntegrationTests/Service/*` | vários |

São cerca de 130 testes diretamente ligados a auth, tenancy e segurança.

**Lacunas:**
- nenhum teste de isolamento em `PATCH /api/stores/{id}/public-slug`;
- nenhum teste de "refresh preserva loja";
- nenhum teste de RBAC por papel fora de `/api/users`;
- nenhum teste de XFF;
- nenhum teste de revogação depois de switch-store;
- nenhum teste de colisão de login entre tenants.
