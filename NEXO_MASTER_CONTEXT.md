# NEXO ERP — MASTER CONTEXT
> Fonte única de verdade do projeto. Atualizar a cada decisão arquitetural relevante.
> Versão: 2.1 | Última atualização: 2026-04-02

---

## 1. VISÃO DO PRODUTO

**O que é**: NEXO ERP é um SaaS modular e multi-tenant de gestão para pequenas e médias empresas do Brasil. Desenvolvido pela Andrade Systems.

**Público-alvo**: PMEs de múltiplos segmentos (varejo, restaurante, academia, clínica, pet shop, oficina mecânica, pousada, imobiliária, salão de beleza, artes marciais).

**Proposta de valor**: Um único produto que serve todos os segmentos via módulos verticais — cada empresa contrata apenas o que precisa, paga por módulo, sem contratos longos.

**Estratégia de módulos**:
- CORE: entidades e funcionalidades compartilhadas por todos os segmentos (produtos, clientes, estoque, vendas, caixa, financeiro básico)
- Módulos verticais: estendem o CORE com funcionalidades específicas do segmento
- Um tenant pode ter múltiplos módulos ativos simultaneamente
- Acesso ao módulo é controlado por `ModuleSubscription` ativo no banco + claim `module` no JWT

**Módulos disponíveis** (chaves no sistema):
| Chave | Nome |
|---|---|
| `varejo` | Comércio em Geral (Varejo) |
| `restaurante` | Restaurantes e Bares |
| `academia-musculacao` | Academias de Musculação |
| `academia-artes-marciais` | Academias de Artes Marciais |
| `clinica-medica` | Clínicas Médicas e Odontológicas |
| `salao-beleza` | Salões de Beleza |
| `pet-shop` | Pet Shops + Clínicas Veterinárias |
| `oficina-mecanica` | Oficinas Mecânicas |
| `pousada-hotel` | Pousadas e Hotéis |
| `imobiliaria` | Imobiliárias |

---

## 2. ARQUITETURA DEFINIDA (v2.1)

### Stack

| Camada | Tecnologia |
|---|---|
| Backend | .NET 8, C# — Clean Architecture |
| ORM | EF Core 8 + Npgsql (PostgreSQL) |
| Banco | PostgreSQL 16, schema `nexo` |
| Cache / Auth | Redis (StackExchange.Redis) |
| Billing | Stripe (webhooks + Checkout) |
| Frontend | Next.js (migração de React+Vite) |
| Auth | JWT HS256 — access 15min + refresh 7d |
| Testes | xUnit + Testcontainers (PostgreSQL real) |

### Projetos (Solution)

```
Nexo.Domain          → Entidades, enums, exceções, interfaces de domínio
Nexo.Application     → Services, DTOs, interfaces de infra, validators (FluentValidation)
Nexo.Infrastructure  → EF Core, repositórios, JWT, Redis, Audit, Seeder
Nexo.Api             → Controllers, Middleware, Program.cs
Nexo.Shared          → Result<T>, PagedResult<T> — sem dependências externas
Nexo.UnitTests       → Testes unitários
Nexo.IntegrationTests → Testes de integração com banco real (Testcontainers)
```

### Princípios arquiteturais

- **Modular monolith**: Um único deploy, módulos separados por namespace/feature folder
- **Multi-tenant por linha**: Todos os dados em um único banco PostgreSQL com `tenant_id` em cada tabela de negócio
- **Fail-open no cache**: Se Redis estiver indisponível, o sistema continua funcionando (exceto logout/blacklist de token)
- **Audit-by-default**: Toda operação sensível gera `AuditRecord` na mesma transação
- **Result pattern**: Services retornam `Result<T>` para erros esperados; exceções apenas para erros inesperados

---

## 3. MULTI-TENANT E SEGURANÇA

### Isolamento em 5 camadas

| Camada | Onde | O que faz |
|---|---|---|
| 1. JWT claim | `tenantId` no token | Identifica o tenant antes do primeiro acesso ao banco |
| 2. TenantResolutionMiddleware | `Nexo.Api/Middleware` | Valida tenant no Redis/DB, retorna 401/403, popula `ICurrentTenant` |
| 3. Global Query Filter | `NexoDbContext` | `WHERE tenant_id = @currentTenantId` aplicado automaticamente em toda query de `TenantEntity` |
| 4. TenantSaveChangesInterceptor | `Nexo.Infrastructure` | Auto-injeta `TenantId` em INSERT; bloqueia cross-tenant writes com exceção |
| 5. Testes de integração | `Nexo.IntegrationTests` | Dois tenants criados em cada teste que envolva isolamento |

### Regras de entidades

- **`TenantEntity`**: herança obrigatória para toda entidade de negócio (tenants, platform_users e module_definitions são exceções de plataforma)
- **`TenantId` imutável**: definido no construtor via `base(tenantId)`, nunca alterado
- **`IgnoreQueryFilters()`**: proibido em código de aplicação — permitido apenas em `DataSeeder`, `migrations`, e serviços de plataforma (com comentário explicativo)

### ICurrentTenant

```csharp
public interface ICurrentTenant {
    Guid Id { get; }
    string Slug { get; }
    IReadOnlyList<string> ActiveModules { get; }
    bool IsResolved { get; }
    void Set(Guid id, string slug, IReadOnlyList<string> activeModules);
}
```
- Scoped (uma instância por request HTTP)
- Populado pelo middleware após autenticação
- `Set()` pode ser chamado apenas uma vez por request (lança exceção se chamado duas vezes)

### TenantCacheEntry (Redis)

```
Chave: tenant:{tenantId}:info
TTL: 5 minutos
Campos: Slug, Status, ActiveModules[]
```
Status é verificado em TODAS as resoluções (cache hit OU miss). Tenant suspenso retorna 403 em até 5min após suspensão.

---

## 4. AUTH

### Tokens

| | Access Token | Refresh Token |
|---|---|---|
| TTL | 15min (dev: 60min) | 7d (dev: 30d) |
| Audience | `nexo-frontend` | `nexo-refresh` |
| Claims | sub, jti, userId, tenantId, tenantSlug, name, role, module[] | sub, jti, userId, tenantId |
| Algoritmo | HS256 | HS256 |
| Segredo | `Jwt:Secret` (≥ 32 chars) | Mesmo segredo |

### Claims do access token

```json
{
  "sub": "uuid-do-usuario",
  "jti": "uuid-unico-do-token",
  "userId": "uuid-do-usuario",
  "tenantId": "uuid-do-tenant",
  "tenantSlug": "andrade-systems-abc123",
  "name": "João Silva",
  "role": "gerente",
  "module": ["varejo", "restaurante"]
}
```

### Fluxo de refresh (token rotation)

1. `POST /auth/refresh { refreshToken }` — endpoint `[AllowAnonymous]`
2. Valida assinatura e audience `nexo-refresh`
3. Verifica chave `refresh:valid:{jti}` no Redis (se não existir → 401)
4. Verifica usuário e tenant ainda ativos
5. Remove chave antiga do Redis
6. Gera novo par de tokens
7. Armazena novo `refresh:valid:{newJti}` no Redis (TTL = 7d)
8. Retorna: `{ accessToken, accessTokenExpiresAt, refreshToken, refreshTokenExpiresAt }`

### Chaves Redis de auth

```
refresh:valid:{jti}        → RefreshTokenEntry { UserId, TenantId }    TTL: 7d
jwt:blacklist:{jti}        → "1"                                        TTL: até expiração do access token
user:blocked:{userId}      → "1"                                        (reservado para bloqueio imediato)
```

### Endpoints de auth

| Endpoint | Auth | Descrição |
|---|---|---|
| `POST /auth/login` | Anônimo | Login com login+senha, retorna token pair + session |
| `POST /auth/refresh` | Anônimo | Rotaciona refresh token, retorna novo par |
| `POST /auth/logout` | `[Authorize]` | Revoga refresh token no Redis (idempotente) |
| `GET /auth/me` | `[Authorize]` | Retorna session atual do banco (não do JWT) |
| `POST /auth/verify-manager` | `[Authorize]` | Valida credenciais de gerente sem criar sessão |

### Roles (UserRole enum)

`Diretoria` > `Gerente` > `Vendedor` > `Estoquista`

---

## 5. BILLING

### Planos (PlanType enum)

| Valor | Descrição |
|---|---|
| `Monthly` | Recorrente mensal via Stripe |
| `Quarterly` | Recorrente trimestral via Stripe |
| `Semiannual` | Recorrente semestral via Stripe |
| `Annual` | Recorrente anual via Stripe |
| `Lifetime` | Pagamento único, `CurrentPeriodEnd = null` (nunca expira) |
| `Trial` | Período de avaliação (usa `Tenant.TrialEndsAt`) |
| `AdminGrant` | Concedido manualmente por admin da plataforma (sem Stripe) |

### Regra de ativação de módulo

Um módulo está ativo para um tenant se:
```
ModuleSubscription.Status IN (Active, Trialing)
AND (CurrentPeriodEnd IS NULL OR CurrentPeriodEnd > NOW())
```

Os módulos ativos são carregados uma vez no login e uma vez no refresh, embutidos como claims `module` no JWT. O middleware recarrega do Redis/DB a cada request.

### Entidades de billing (plataforma — sem tenant_id)

- `ModuleDefinition`: catálogo de módulos, IDs de preço Stripe, preços de referência em BRL
- `ModuleSubscription`: um registro por (tenant, module_key), atualizado via webhooks Stripe

---

## 6. CORE — DEFINIÇÃO OFICIAL

O CORE contém tudo que é comum a todos os segmentos. Um tenant sem nenhum módulo vertical ativo ainda tem acesso total ao CORE.

### Entidades já implementadas (Etapa 1)

#### User
Usuário do sistema, pertence a um tenant.
- `Id`, `TenantId`, `FullName`, `Email`, `Login`, `PasswordHash`, `Phone`, `Role` (enum), `Status` (enum), `RequirePasswordChange`, `Notes`, `LastAccessAt`, `PasswordChangedAt`
- Relação: `Tenant` (N:1)
- Herda: `TenantEntity`

#### AppSettings
Configurações da aplicação por tenant, armazenadas como colunas JSONB.
- `Id`, `TenantId`, `CompanySettingsJson`, `OperationSettingsJson`, `InventorySettingsJson`, `CommissionSettingsJson`, `PosSettingsJson`, `SystemSettingsJson`
- Constraint: único por `TenantId` (uma linha por tenant)
- Herda: `TenantEntity`

#### AuditRecord
Log imutável de auditoria. TenantId nullable (ações de plataforma têm TenantId null).
- `Id`, `TenantId?`, `ActionType`, `Severity`, `EntityType`, `EntityId`, `ActorId?`, `ActorName?`, `ActorType`, `Description`, `MetadataJson?`, `IpAddress?`, `CreatedAt`
- Herda: `BaseEntity` (NOT TenantEntity — sem filtro automático)
- IP capturado automaticamente via `IHttpContextAccessor` no `AuditWriterService`

---

### Entidades a implementar (Etapa 2)

#### Customer (Cliente)
Pessoa física ou jurídica que compra do tenant.
- `Id`, `TenantId`
- `PersonType`: `Individual` | `Company`
- `Name` (nome ou razão social), `TradeName?` (nome fantasia)
- `DocumentType`: `Cpf` | `Cnpj`, `DocumentNumber`
- `Email?`, `Phone?`, `WhatsApp?`
- `AddressJson?`: `{ street, number, complement, neighborhood, city, state, zipCode }`
- `CreditLimit?` (decimal), `Notes?`
- `IsActive` (bool, default true)
- Herda: `TenantEntity`
- Relações: `Sales` (1:N)

#### Supplier (Fornecedor)
Pessoa física ou jurídica que fornece produtos ao tenant.
- `Id`, `TenantId`
- `PersonType`: `Individual` | `Company`
- `Name`, `TradeName?`
- `DocumentType`: `Cpf` | `Cnpj`, `DocumentNumber`
- `Email?`, `Phone?`, `ContactName?`
- `AddressJson?`
- `PaymentTermsDays?` (int — prazo padrão de pagamento)
- `BankInfoJson?`: `{ bank, agency, account, pixKey }`
- `Notes?`, `IsActive`
- Herda: `TenantEntity`

#### Category (Categoria)
Classificação hierárquica de produtos.
- `Id`, `TenantId`
- `Name` (max 100), `Description?`
- `ParentCategoryId?` (FK para si mesmo — subcategorias)
- `IsActive`
- Herda: `TenantEntity`
- Relações: `Products` (1:N), `Parent` (N:1 self), `Children` (1:N self)

#### Product (Produto)
Item vendável ou usado na operação do tenant.
- `Id`, `TenantId`
- `Code` (SKU interno, único por tenant), `Barcode?`
- `Name` (max 200), `Description?`
- `CategoryId?` (FK → Category)
- `Unit`: `Un` | `Kg` | `L` | `M` | `M2` | `M3` | `Cx` | `Pc` — enum ou string
- `CostPrice` (decimal), `SalePrice` (decimal)
- `TrackStock` (bool — false para serviços)
- `MinStockQuantity?` (decimal), `MaxStockQuantity?` (decimal)
- `IsActive`
- Herda: `TenantEntity`
- Relações: `Category` (N:1), `StockItem` (1:1), `SaleItems` (1:N), `StockMovements` (1:N)

#### StockItem (Estoque atual)
Quantidade atual em estoque por produto. Uma linha por produto.
- `Id`, `TenantId`
- `ProductId` (FK → Product, unique por tenant)
- `CurrentQuantity` (decimal)
- `ReservedQuantity` (decimal — reservado para vendas pendentes)
- `LastMovementAt` (DateTime?)
- Herda: `TenantEntity`
- Relações: `Product` (1:1)

#### StockMovement (Movimento de estoque)
Registro imutável de cada alteração no estoque.
- `Id`, `TenantId`
- `ProductId` (FK → Product)
- `MovementType`: `Entry` | `Exit` | `Adjustment` | `ManualEntry` | `ManualExit` | `SaleOutput` | `ReturnEntry`
- `Quantity` (decimal — sempre positivo)
- `QuantityBefore` (decimal — snapshot), `QuantityAfter` (decimal — snapshot)
- `ReferenceType?` (string — `"Sale"`, `"Purchase"`, `"Manual"`)
- `ReferenceId?` (Guid)
- `Notes?`
- `CreatedByUserId` (Guid)
- Herda: `TenantEntity` (CreatedAt do BaseEntity, sem UpdatedAt — imutável)

#### Sale (Venda)
Cabeçalho de uma venda.
- `Id`, `TenantId`
- `Number` (int — sequencial por tenant, gerado no service)
- `Status`: `Draft` | `Open` | `Paid` | `PartiallyPaid` | `Cancelled`
- `CustomerId?` (FK → Customer)
- `SoldByUserId` (FK → User)
- `CashSessionId?` (FK → CashSession)
- `Subtotal` (decimal), `DiscountAmount` (decimal), `TaxAmount` (decimal), `Total` (decimal)
- `PaymentMethod?`: `Cash` | `Card` | `Pix` | `Credit` | `Mixed`
- `Notes?`
- `PaidAt?` (DateTime)
- Herda: `TenantEntity`
- Relações: `Customer` (N:1), `SoldBy` (N:1), `CashSession` (N:1), `Items` (1:N), `StockMovements` (1:N via ReferenceId)

#### SaleItem (Item da venda)
Linha de produto dentro de uma venda.
- `Id`, `TenantId`
- `SaleId` (FK → Sale)
- `ProductId` (FK → Product)
- `Quantity` (decimal)
- `UnitPrice` (decimal — preço no momento da venda, não o atual)
- `DiscountAmount` (decimal)
- `Total` (decimal — calculado: Quantity * UnitPrice - DiscountAmount)
- `Notes?`
- Herda: `TenantEntity`

#### CashSession (Sessão de caixa)
Abertura e fechamento de caixa por turno.
- `Id`, `TenantId`
- `Status`: `Open` | `Closed`
- `OpenedByUserId` (FK → User)
- `ClosedByUserId?` (FK → User)
- `OpeningBalance` (decimal)
- `ClosingBalance?` (decimal)
- `OpenedAt` (DateTime), `ClosedAt?` (DateTime)
- `Notes?`
- Herda: `TenantEntity`
- Relações: `Movements` (1:N CashMovement), `Sales` (1:N)
- Regra: apenas um CashSession Open por tenant por vez

#### CashMovement (Movimento de caixa)
Cada entrada ou saída financeira registrada na sessão.
- `Id`, `TenantId`
- `CashSessionId` (FK → CashSession)
- `MovementType`: `Opening` | `SaleReceipt` | `Withdrawal` | `Deposit` | `Closing`
- `Amount` (decimal — sempre positivo; tipo define direção)
- `Description`
- `ReferenceType?` (string — `"Sale"`)
- `ReferenceId?` (Guid)
- `CreatedByUserId` (Guid)
- Herda: `TenantEntity` (imutável — sem UpdatedAt significativo)

#### FinancialAccount (Conta financeira — Plano de contas)
Estrutura hierárquica do plano de contas simplificado.
- `Id`, `TenantId`
- `Code` (string — ex: `"1.1.1"`)
- `Name`
- `AccountType`: `Asset` | `Liability` | `Revenue` | `Expense` | `Equity`
- `ParentAccountId?` (FK para si mesmo)
- `IsActive`
- Herda: `TenantEntity`

#### FinancialTransaction (Transação financeira — Contas a pagar/receber)
- `Id`, `TenantId`
- `FinancialAccountId` (FK → FinancialAccount)
- `TransactionType`: `Receivable` | `Payable`
- `Amount` (decimal)
- `Description`
- `DueDate` (DateTime)
- `PaidAt?` (DateTime)
- `Status`: `Pending` | `Paid` | `Overdue` | `Cancelled`
- `ReferenceType?` (string — `"Sale"`)
- `ReferenceId?` (Guid)
- `CreatedByUserId` (Guid)
- Herda: `TenantEntity`

---

## 7. O QUE NÃO É CORE

Funcionalidades pertencentes aos módulos verticais, NÃO ao CORE:

| Módulo | O que adiciona (não-CORE) |
|---|---|
| **Varejo** | Emissão NF-e / NF-ce, integração TEF (máquina de cartão), lista de preços avançada, consignação, carnê, PDV otimizado para caixa físico |
| **Restaurante** | Mesa / Comanda, delivery (integração iFood/Rappi), cardápio digital, receitas e fichas técnicas, kitchen display, divisão de conta, gorjeta |
| **Academia-Musculação** | Aluno, plano de academia, matrícula, avaliação física, medidas corporais, frequência, renovação automática |
| **Academia-Artes Marciais** | Graduação (faixas), turmas, chamada, lutas/torneios |
| **Clínica** | Prontuário eletrônico, agenda médica, TISS/TUSS, prescrições, exames, convênios |
| **Salão de Beleza** | Serviços por profissional, agendamento online, comissão por serviço, fidelidade |
| **Pet Shop** | Cadastro de animais, agenda veterinária, vacinação, banho e tosa, hotel pet |
| **Oficina Mecânica** | Veículo (placa/chassi), ordem de serviço, peças, laudos, garantia |
| **Pousada / Hotel** | Quarto, reserva, check-in/check-out, diária, temporada, taxa de turismo |
| **Imobiliária** | Imóvel, proprietário, contrato de locação/venda, comissão de corretagem, reajuste |

---

## 8. REGRAS DE OURO DO PROJETO

1. **Toda entidade de negócio herda `TenantEntity`** — nunca `BaseEntity` diretamente (exceções: `AuditRecord`, `Tenant`, `PlatformUser`, `ModuleDefinition`, `ModuleSubscription`)

2. **`IgnoreQueryFilters()` é proibido em código de aplicação** — permitido apenas em `DataSeeder`, EF migrations, e serviços de plataforma. Sempre com comentário `// IgnoreQueryFilters: justificativa`

3. **Todo módulo vertical requer subscription ativa** — usar `[RequireModule("key")]` em controllers de módulo (a implementar no módulo respectivo)

4. **Refresh token rotation é completa** — ao renovar, o token antigo é revogado no Redis E o novo é retornado ao cliente. Nunca retornar só o access token

5. **Tenant suspenso = 403 imediato** — verificado em toda request autenticada, independente do cache

6. **Audit trail em operações sensíveis**: mudança de senha, cancelamento de venda, ajuste de estoque, abertura/fechamento de caixa, ativação/desativação de módulo. Severity `Warning` para alterações, `Critical` para violações

7. **IP sempre capturado** — `AuditWriterService` captura via `IHttpContextAccessor` automaticamente, sem dependência dos callers

8. **`TenantSaveChangesInterceptor` é singleton sem dependências scoped** — acessa `ICurrentTenant` via `(NexoDbContext)eventData.Context.CurrentTenant`, não por injeção de construtor

9. **Preço de venda desnormalizado em `SaleItem.UnitPrice`** — nunca buscar o preço atual do produto ao ler uma venda histórica

10. **`StockMovement` é imutável** — registros de movimentação nunca são editados ou deletados. Correções são feitos por novos registros de ajuste

11. **Senha nunca em texto plano** — sempre BCrypt via `IPasswordHasher`. A entidade `User` recebe apenas o hash

12. **Migrations nunca escritas à mão definitivamente** — usar `dotnet ef migrations add` para gerar o snapshot correto. A migration manual é apenas temporária até o SDK estar disponível

---

## 9. ETAPAS DO PROJETO

### Etapa 1 — Infraestrutura Multi-Tenant ✅ CONCLUÍDA

**O que foi feito:**
- Domain: `BaseEntity`, `TenantEntity`, `TenantIsolationViolationException`
- Domain: `Tenant`, `PlatformUser`, `ModuleDefinition`, `ModuleSubscription`, `User`, `AppSettings`, `AuditRecord`
- Application: `ICurrentTenant`, `ICacheService`, `ITenantRepository`, `IJwtTokenService` (token pair), `IAuditWriter`
- Infrastructure: `CurrentTenantService`, `TenantSaveChangesInterceptor` (singleton, sem captive dependency), `RedisCacheService`, `NoOpCacheService`
- Infrastructure: `NexoDbContext` com Global Query Filters automáticos + `CurrentTenant` internal property
- Infrastructure: `JwtTokenService` (access 15min + refresh 7d, audiences separadas)
- Infrastructure: `TenantRepository`, `UserRepository`, `AppSettingsRepository`
- Infrastructure: `AuditWriterService` com IP automático
- API: `TenantResolutionMiddleware` (status check em cache hit E miss)
- API: `AuthController` (`/login`, `/refresh` com token rotation, `/logout`, `/me`, `/verify-manager`)
- API: `UsersController`, `SettingsController`, `TenantsController` (escopo restrito ao próprio tenant)
- Migration: `20260402000000_InitialMultiTenant` (schema `nexo`, jsonb para app_settings)

**Bugs corrigidos antes de fechar:**
- Singleton + Scoped DI violation no interceptor
- RefreshAsync não retornava novo refresh token
- Refresh token aceito como Bearer em qualquer endpoint
- Status de tenant suspenso ignorado em cache hits
- Migration com colunas `text` em vez de `jsonb` para settings

---

### Etapa 2 — CORE: Entidades de Negócio 🔄 EM ANDAMENTO

**Escopo** (backend completo: Domain → EF → Repository → Service → Controller → Migration):
- `Customer` (Clientes)
- `Supplier` (Fornecedores)
- `Category` (Categorias de produtos)
- `Product` (Produtos / Catálogo)
- `StockItem` + `StockMovement` (Estoque)
- `Sale` + `SaleItem` (Vendas)
- `CashSession` + `CashMovement` (Caixa)
- `FinancialAccount` + `FinancialTransaction` (Financeiro básico)

**Estrutura de código**: dentro dos projetos existentes, nas pastas de feature correspondentes.

**Entrega esperada**: API completa consumível pelo frontend Next.js.

---

### Etapa 3 — Módulo Varejo (próximo após Etapa 2)

Adicionar sobre o CORE:
- NF-e / NF-ce (fiscal)
- PDV otimizado
- Integração TEF
- Lista de preços
- Relatórios de varejo

### Etapa 4 — Módulo Restaurante

Mesa, Comanda, Delivery, Cardápio, Ficha técnica.

### Backlog (ordem a definir)

- Stripe webhooks (ativação automática de módulos)
- Frontend Next.js (migração de React+Vite)
- Portal do cliente (self-service de assinatura)
- Módulos restantes: Academia, Clínica, Salão, Pet Shop, Oficina, Pousada, Imobiliária
- Relatórios e dashboards
- Notificações (e-mail, WhatsApp via Z-API)

---

## 10. CONFIGURAÇÃO ESSENCIAL

### appsettings.json — chaves obrigatórias

```json
{
  "ConnectionStrings": {
    "Default": "Host=...;Database=nexo;Username=...;Password=...",
    "Redis": "localhost:6379"
  },
  "Jwt": {
    "Secret": "min-32-chars-random-string",
    "Issuer": "nexo-api",
    "Audience": "nexo-frontend",
    "RefreshAudience": "nexo-refresh",
    "AccessTokenMinutes": 15,
    "RefreshTokenDays": 7
  },
  "Cors": {
    "AllowedOrigins": ["https://app.nexoerp.com.br"]
  }
}
```

### Middleware pipeline (ordem obrigatória)

```csharp
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging();
app.UseSwagger();           // non-prod apenas
app.UseCors("NexoFrontend");
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>(); // DEPOIS de UseAuthentication
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
```

### Geração de migration (após qualquer mudança de schema)

```bash
dotnet ef migrations add <NomeDaMigration> \
  --project src/Nexo.Infrastructure \
  --startup-project src/Nexo.Api \
  --output-dir Persistence/Migrations

dotnet ef database update \
  --project src/Nexo.Infrastructure \
  --startup-project src/Nexo.Api
```

---

## 11. COMO USAR ESTE DOCUMENTO

### Em cada nova conversa

Cole no início da conversa:

```
Contexto do projeto em: C:\Users\Elias\Documents\NexoERP\NEXO_MASTER_CONTEXT.md
Leia esse arquivo antes de qualquer implementação.
```

Ou cole o conteúdo diretamente se quiser que o modelo tenha acesso imediato.

### Quando atualizar

- Ao concluir uma etapa (marcar como concluída na seção 9)
- Ao adicionar novas entidades ao CORE ou a módulos
- Ao alterar regras arquiteturais (seção 8)
- Ao tomar decisões de billing ou segurança diferentes das atuais

### O que NÃO colocar aqui

- Código fonte (fica nos arquivos .cs)
- Histórico de bugs corrigidos (fica no git)
- Detalhes de implementação transitórios
