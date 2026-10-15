# Relatório Geral do Orken — Auditoria Técnica Completa
**Data:** 2026-05-25  
**Auditor:** Claude (automated deep-read audit)  
**Escopo:** Frontend `nexo-main/src` + Backend `nexo-backend/src`

---

## 1. Resumo Executivo

O sistema Orken ERP encontra-se em um estado de **transição parcial**: o backend está significativamente mais maduro e produção-pronto que o frontend. A camada de API possui autenticação JWT robusta, isolamento multi-tenant por Global Query Filters, Redis com fail-open e WaitAsync guard, paginação em todas as entidades críticas, e SignalR para tempo real. O frontend, porém, carrega um peso considerável de dívida técnica: vários módulos ainda dependem de serviços mock em memória (`cashService`, `posService`, `commissionService`, `quotationService`, `reportService`, `insightService`, `profileService`) que nunca comunicam com o backend real.

Os módulos de maior risco imediato em produção são: **Caixa** (100% mock — abre/fecha sessão em memória, não persiste), **Comissões** (100% mock — deriva dados de posService que usa mockSales), **Orçamentos** (100% mock — quotationService usa mockQuotations), e **Relatórios Gerenciais** (hibrido — usa API real para sales/inventory/customers mas reportService ainda agrega de posService mock para operacional/comissões/caixa). O módulo **Perfil** também tem a troca de senha em mock.

A autenticação backend é bem construída: JWT com refresh token rotation, SecurityStamp para revogação imediata, rate limiting em login (5 req / 15 min), cookies HttpOnly com SameSite=None em produção. O TenantResolutionMiddleware valida TenantId + UserId a cada request. As principais ameaças arquiteturais identificadas são: endpoints `GET /api/sales` e `GET /api/audit` retornam listas ilimitadas (sem paginação); o `StockController.GetAll()` também retorna todos os itens sem paginação; e o `SalesByDayAsync` faz `ToListAsync` antes de agrupar por data no backend — potencial de carregar 30 dias de vendas em memória.

Três bugs foram corrigidos nesta sessão antes da auditoria: AvailableQuantity como propriedade C# não traduzível no EF Core (fixado para aritmética raw), Redis WaitAsync guard (200ms), e DashboardService com Task.WhenAll no mesmo DbContext (fixado para sequencial). Essas correções estão nos commits mais recentes.

---

## 2. Mapa da Arquitetura

### Diagrama textual
```
                        ┌─────────────────────────────────────┐
                        │    Browser — nexo-main (Vite/React) │
                        │                                     │
                        │  Auth → API Client (fetch + JWT)    │
                        │  React Query (TanStack v5)          │
                        │  SignalR (@microsoft/signalr)       │
                        │  recharts, radix-ui, shadcn/ui      │
                        └────────────┬────────────────────────┘
                                     │ HTTPS (Railway)
                                     │ Cookie: nexo_access / nexo_refresh
                        ┌────────────▼────────────────────────┐
                        │   nexo-backend (ASP.NET Core 8)     │
                        │                                     │
                        │  Program.cs → CORS, JWT, Serilog    │
                        │  TenantResolutionMiddleware          │
                        │  SecurityStampValidationMiddleware   │
                        │  Rate Limiter (auth: 5/15min)        │
                        │                                     │
                        │  Controllers (REST)                  │
                        │  SignalR Hub: /hubs/restaurant       │
                        │  DashboardService (sequential)       │
                        │  ModuleAccessService (IMemoryCache)  │
                        └──────┬──────────┬───────────────────┘
                               │          │
              ┌────────────────▼─┐  ┌─────▼──────────────┐
              │ PostgreSQL (ERP) │  │ Redis (Railway)     │
              │ nexo schema      │  │ tenant:*:info TTL5m │
              │ 57 migrations    │  │ jwt:blacklist:*     │
              │ GQF tenant+store │  │ refresh:valid:*     │
              │ ~40 entities     │  │ user:stamp:*        │
              └──────────────────┘  └─────────────────────┘
```

### Stack
| Camada | Tecnologia | Versão / Notas |
|--------|-----------|----------------|
| Frontend | React + TypeScript | Vite/SWC, react-router-dom v6 |
| State / Fetch | TanStack React Query v5 | Sem staleTime global padrão em muitos hooks |
| UI | shadcn/ui + Radix UI | Tailwind CSS |
| Charts | recharts + D3 deps | Chunk separado vendor-charts |
| Real-time | @microsoft/signalr | Fallback polling a 10s |
| Backend | ASP.NET Core 8 | C# 12 |
| ORM | Entity Framework Core 8 | PostgreSQL via Npgsql |
| Cache | StackExchange.Redis | fail-open, WaitAsync 200ms |
| Logs | Serilog | Console + File (30d rotation) |
| Infra | Railway | Docker containers, PostgreSQL+Redis managed |
| Auth | JWT Bearer | 15min access, 7d refresh, SecurityStamp |

---

## 3. Mapa de Rotas Frontend

| Rota | Componente | Lazy | Endpoints Chamados | Status | Riscos |
|------|-----------|------|-------------------|--------|--------|
| `/` | LandingPage | Sim | Nenhum | Real | - |
| `/login` | LoginPage | Não | POST /api/auth/login | Real | - |
| `/register` | RegisterPage | Não | POST /api/auth/register | Real | - |
| `/check-email` | CheckEmailPage | Não | POST /api/auth/resend-verification | Real | - |
| `/verify-email` | VerifyEmailPage | Não | GET /api/auth/verify-email?token= | Real | - |
| `/dashboard` | DashboardPage | Sim | GET /api/dashboard/summary | Real | staleTime 60s, sem refetchInterval |
| `/vendas` | VendasPage | Sim | GET /api/sales/paged | Real | Paginado |
| `/vendas/:id` | VendaDetailPage | Sim | GET /api/sales/:id | Real | - |
| `/produtos` | ProdutosPage | Sim | GET /api/products/paged | Real | Paginado |
| `/produtos/novo` | ProductFormPage | Sim | POST /api/products | Real | - |
| `/produtos/:id` | ProductFormPage | Sim | GET+PUT /api/products/:id | Real | - |
| `/produtos/:id/ficha` | RecipeCardPage | Sim | GET+POST /api/restaurante/recipe-cards/:productId | Real | - |
| `/estoque` | EstoquePage | Sim | GET /api/stock/paged | Real | Paginado |
| `/estoque/movimentacoes` | MovimentacoesPage | Sim | GET /api/stock + GET /api/stock/product/:id/movements | Real | GetAll sem paginação |
| `/estoque/ajustes` | AjustesPage | Sim | POST /api/stock/adjust | Real | - |
| `/clientes` | ClientesPage | Sim | GET /api/customers/paged | Real | Paginado |
| `/clientes/novo` | CustomerFormPage | Sim | POST /api/customers | Real | - |
| `/clientes/:id` | CustomerFormPage | Sim | GET+PUT /api/customers/:id | Real | - |
| `/fornecedores` | FornecedoresPage | Sim | GET /api/suppliers/paged | Real | Paginado |
| `/fornecedores/novo` | SupplierFormPage | Sim | POST /api/suppliers | Real | - |
| `/fornecedores/:id` | SupplierFormPage | Sim | GET+PUT /api/suppliers/:id | Real | - |
| `/caixa` | CaixaPage | Sim | GET /api/cash/sessions/open (real), mas cashService usado pelos modais = MOCK | **MISTO/QUEBRADO** | Modais de abrir/fechar/movimento chamam cashService mock |
| `/pdv` | PdvPage | Sim | GET /api/products, GET /api/stock, POST /api/sales/* | Real | posService.completeSale NÃO usado; useCompleteSale usa API real |
| `/usuarios` | UsuariosPage | Sim | GET /api/users | Real | GetAll sem paginação |
| `/usuarios/novo` | UserFormPage | Sim | POST /api/users | Real | - |
| `/usuarios/:id` | UserFormPage | Sim | GET+PUT /api/users/:id | Real | - |
| `/usuarios/permissoes` | PermissoesPage | Sim | Usa mockUsers rolePresets | **MOCK** | PermissoesPage não tem backend |
| `/auditoria` | AuditoriaPage | Sim | GET /api/audit, GET /api/audit/stats | Real | GetAll sem paginação no backend |
| `/configuracoes` | ConfiguracoesPage | Sim | GET+PUT /api/settings | Real | - |
| `/perfil` | PerfilPage | Sim | GET /api/users/:id (via userService); changePassword mock | **MISTO** | changePassword é mock |
| `/build` | BuildProjectsPage | Sim | GET /api/build/projects | Real | Paginado |
| `/build/projetos/:id` | BuildProjectDetailPage | Sim | GET /api/build/projects/:id/details | Real | - |
| `/restaurante` | FloorPage | Sim | GET /api/restaurante/tables, /areas | Real | - |
| `/restaurante/mesa/:tableId` | OrderPage | Sim | GET+POST /api/restaurante/orders | Real | - |
| `/restaurante/delivery` | DeliveryPage | Sim | GET /api/restaurante/delivery-orders | Real | - |
| `/restaurante/cozinha` | KitchenPage | Sim | GET /api/restaurante/kitchen + SignalR | Real | Fallback polling 10s |
| `/restaurante/portal` | PortalSetupPage | Sim | GET+PUT /api/restaurante/settings/portal | Real | - |
| `/restaurante/configurar` | RestauranteSetupPage | Sim | GET+PUT /api/restaurante/settings | Real | - |
| `/restaurante/relatorios` | RelatoriosPage | Sim | GET /api/restaurante/reports/summary | Real | - |
| `/restaurante/financeiro` | FinanceiroPage | Sim | GET /api/restaurante/financeiro/cmv-report, /summary | Real | - |
| `/:slug` | PortalMenuPage | Sim | GET /api/public/menu/:slug | Real (público) | - |
| `/rastrear/:token` | PortalTrackingPage | Sim | GET /api/public/orders/:trackingToken | Real (público) | - |
| `/platform/*` | Platform* | Sim | GET /api/platform/* | Real | - |
| `/impersonate` | ImpersonatePage | Sim | POST /api/platform/tenants/:id/impersonate | Real | - |

---

## 4. Mapa de Endpoints Backend

| Rota HTTP | Controller | Paginado | Auth | RequireModule | Riscos |
|-----------|-----------|---------|------|--------------|--------|
| POST /api/auth/login | AuthController | N | Anon | - | Rate limit 5/15min |
| POST /api/auth/refresh | AuthController | N | Anon | - | Rate limit 5/15min |
| GET /api/auth/me | AuthController | N | JWT | - | - |
| POST /api/auth/switch-store | AuthController | N | JWT | - | - |
| POST /api/auth/logout | AuthController | N | JWT | - | - |
| POST /api/auth/register | AuthController | N | Anon | - | - |
| GET /api/auth/verify-email | AuthController | N | Anon | - | - |
| POST /api/auth/resend-verification | AuthController | N | Anon | - | - |
| POST /api/auth/verify-manager | AuthController | N | JWT | - | - |
| GET /api/dashboard/summary | DashboardController | N | JWT | - | 5 queries sequenciais; sem cache |
| **GET /api/sales** | SalesController | **N** | JWT | varejo | **RISCO: lista ilimitada** |
| GET /api/sales/paged | SalesController | Sim | JWT | varejo | pageSize max 100 |
| GET /api/sales/:id | SalesController | N | JWT | varejo | - |
| POST /api/sales | SalesController | N | JWT | varejo | - |
| POST /api/sales/:id/items | SalesController | N | JWT | varejo | - |
| POST /api/sales/:id/confirm | SalesController | N | JWT | varejo | - |
| POST /api/sales/:id/cancel | SalesController | N | JWT | varejo | - |
| **GET /api/stock** | StockController | **N** | JWT | - | **RISCO: lista ilimitada** |
| GET /api/stock/paged | StockController | Sim | JWT | - | pageSize max 200 |
| GET /api/stock/product/:id | StockController | N | JWT | - | - |
| GET /api/stock/product/:id/movements | StockController | **N** | JWT | - | **RISCO: movimentos ilimitados** |
| POST /api/stock/adjust | StockController | N | JWT | - | - |
| GET /api/products | ProductsController | **N** | JWT | - | **RISCO: lista ilimitada** |
| GET /api/products/paged | ProductsController | Sim | JWT | - | pageSize max 200 |
| GET /api/products/:id | ProductsController | N | JWT | - | - |
| POST /api/products | ProductsController | N | JWT | - | - |
| PUT /api/products/:id | ProductsController | N | JWT | - | - |
| PATCH /api/products/:id/prices | ProductsController | N | JWT | - | - |
| POST /api/products/:id/activate | ProductsController | N | JWT | - | - |
| POST /api/products/:id/deactivate | ProductsController | N | JWT | - | - |
| GET /api/categories | CategoriesController | **N** | JWT | - | Pequena lista, aceitável |
| POST,PUT,DELETE /api/categories/* | CategoriesController | N | JWT | - | - |
| GET /api/customers | CustomersController | **N** | JWT | - | **RISCO: lista ilimitada** |
| GET /api/customers/paged | CustomersController | Sim | JWT | - | pageSize max 100 |
| GET /api/customers/:id | CustomersController | N | JWT | - | - |
| POST /api/customers | CustomersController | N | JWT | - | - |
| PUT /api/customers/:id | CustomersController | N | JWT | - | - |
| GET /api/suppliers | SuppliersController | **N** | JWT | - | **RISCO: lista ilimitada** |
| GET /api/suppliers/paged | SuppliersController | Sim | JWT | - | - |
| GET,POST,PUT /api/suppliers/* | SuppliersController | N | JWT | - | - |
| GET /api/users | UsersController | **N** | JWT Gerente+Diretoria | - | **RISCO: lista ilimitada** |
| GET,POST,PUT /api/users/* | UsersController | N | JWT | - | - |
| GET /api/cash/sessions | CashController | **N** | JWT | - | **RISCO: histórico ilimitado** |
| GET /api/cash/sessions/open | CashController | N | JWT | - | - |
| GET /api/cash/sessions/:id | CashController | N | JWT | - | - |
| POST /api/cash/sessions/open | CashController | N | JWT | - | - |
| POST /api/cash/sessions/:id/close | CashController | N | JWT | - | - |
| POST /api/cash/sessions/:id/movements | CashController | N | JWT | - | - |
| GET /api/audit | AuditController | **N** | JWT | - | **RISCO: log ilimitado, pode ser enorme** |
| GET /api/audit/stats | AuditController | N | JWT | - | - |
| GET /api/settings | SettingsController | N | JWT | - | - |
| PUT /api/settings | SettingsController | N | JWT | - | - |
| GET /api/reports/sales | ReportsController | N | JWT | - | Date range |
| GET /api/reports/inventory | ReportsController | N | JWT | - | - |
| GET /api/reports/customers | ReportsController | N | JWT | - | - |
| GET /api/restaurante/areas | AreasController | **N** | JWT | restaurante | Pequena lista |
| GET /api/restaurante/tables | TablesController | **N** | JWT | restaurante | Pequena lista |
| GET /api/restaurante/orders | OrdersController | **N** | JWT | restaurante | **RISCO: todas as comandas** |
| GET /api/restaurante/kitchen | OrdersController | **N** | JWT | restaurante | Filtrado por status |
| GET /api/restaurante/delivery-orders | DeliveryOrdersController | **N** | JWT | restaurante | Filtrado |
| GET /api/restaurante/reports/summary | ReportsController | N | JWT | restaurante | Date range |
| GET /api/restaurante/financeiro/cmv-report | FinanceiroController | N | JWT | restaurante | Carrega todos os cards e ingredientes |
| GET /api/restaurante/financeiro/summary | FinanceiroController | N | JWT | restaurante | Date range; heavy (COGS calc em memória) |
| GET /api/public/menu/:slug | PublicOrdersController | N | **Anon** | - | Público sem auth |
| GET /api/public/orders/:trackingToken | PublicOrdersController | N | **Anon** | - | Público sem auth |
| POST /api/public/orders | PublicOrdersController | N | **Anon** | - | Público sem auth |
| GET /api/public/delivery-zones/:slug | PublicOrdersController | N | **Anon** | - | Público sem auth |
| POST /api/public/coupons/validate | PublicOrdersController | N | **Anon** | - | Público sem auth |
| GET /api/platform/* | PlatformController | Misto | JWT platform | - | Requer token type=platform |
| GET /api/build/projects | BuildProjectsController | Sim | JWT | build | - |
| GET /api/build/projects/:id/* | Build* | N/Sim | JWT | build | - |

---

## 5. Estado dos Módulos

### Core

| Módulo | Status | Arquivos | Riscos |
|--------|--------|---------|--------|
| Login/Auth | REAL | auth/pages/LoginPage, authService, api-client | - |
| Dashboard | REAL | dashboard/pages/DashboardPage, useDashboardSummary | staleTime 1min, sem refetchInterval |
| Dashboard (Restaurante blocks) | REAL | RestauranteBlocks via summary endpoint | - |

### Inventário / Estoque

| Módulo | Status | Arquivos | Riscos |
|--------|--------|---------|--------|
| Produtos | REAL | products/hooks/use-products, products.api.ts | - |
| Estoque (listagem) | REAL | inventory/hooks/use-stock, stock.api.ts | - |
| Movimentações | REAL | MovimentacoesPage usa useStockItems (sem paginação) | GET /api/stock carrega tudo |
| Ajustes | REAL | AjustesPage usa useAdjustStock | - |
| inventoryService | DEPRECATED (parcial) | inventory/services/inventoryService.ts | applySale/revertSaleItems ainda existem para posService mock |

### Varejo / PDV

| Módulo | Status | Arquivos | Riscos |
|--------|--------|---------|--------|
| Vendas (listagem) | REAL | VendasPage, useSalesList | Paginado OK |
| Venda detalhe | REAL | VendaDetailPage | - |
| PDV (completar venda) | REAL | PdvPage, useCompleteSale | Busca produto cliente-side (carrega todos sem paginação) |
| posService.completeSale | DEAD CODE | sales/services/posService.ts | NÃO chamado pelo PdvPage atual; useCompleteSale usa API real |
| posService.getRecentSales | MOCK ATIVO | Usado por commissionService, reportService | Deriva de mockSales |
| Orçamentos (OrcamentosPage) | MOCK COMPLETO | quotationService, mockQuotations | Sem backend; não há rota no backend |

### Caixa

| Módulo | Status | Arquivos | Riscos |
|--------|--------|---------|--------|
| CaixaPage (leitura sessão aberta) | REAL | useOpenSession → GET /api/cash/sessions/open | OK |
| Abrir sessão (CashOpenModal) | MOCK | cashService.openSession via cashService.ts | NÃO chama /api/cash/sessions/open |
| Fechar sessão (CashCloseModal) | MOCK | cashService.closeSession | NÃO chama /api/cash/sessions/:id/close |
| Movimentos (CashMovementModal) | MOCK | cashService.addMovement | NÃO chama /api/cash/sessions/:id/movements |

**CRÍTICO: CaixaPage consulta a API real para exibir a sessão aberta, mas todos os modais de ação (abrir, fechar, movimentos) ainda usam cashService mock que mantém estado em memória e não persiste no backend.**

### Clientes / Fornecedores

| Módulo | Status | Arquivos | Riscos |
|--------|--------|---------|--------|
| Clientes (lista) | REAL | useCustomersList, customers.api | Paginado |
| Clientes (form) | REAL | CustomerFormPage | - |
| Fornecedores (lista) | REAL | useSuppliersList, suppliers.api | Paginado |
| Fornecedores (form) | REAL | SupplierFormPage | - |

### Usuários / Auditoria

| Módulo | Status | Arquivos | Riscos |
|--------|--------|---------|--------|
| Usuários (lista) | REAL | UsuariosPage, users.api | GetAll sem paginação no backend |
| Usuários (form) | REAL | UserFormPage | - |
| Permissões | MOCK PARCIAL | PermissoesPage usa rolePresets de mockUsers | Não tem endpoint backend |
| Auditoria | REAL | AuditoriaPage, audit.api | GetAll retorna lista ilimitada |
| auditService.addAuditRecord | NO-OP | audit/services/auditService.ts | `@deprecated` explícito, é no-op |

### Relatórios / Comissões / Insights

| Módulo | Status | Arquivos | Riscos |
|--------|--------|---------|--------|
| RelatoriosPage (api-report-sales/inventory/customers) | REAL | reports.api.ts → /api/reports/* | OK |
| RelatoriosPage (operational/byOperator/topProducts) | MOCK via reportService | reportService.ts usa listSales + saleToLegacy + posService | getData de posService que usa mockSales |
| Comissões (ComissoesPage) | MOCK COMPLETO | commissionService → posService.getRecentSales | Sem backend |
| Insights (RecentInsights no Dashboard) | REAL (parcial) | Usa useDashboardSummary, gera insights client-side | Não é mock, mas derivação local |
| insightService | MORTO / NÃO CONECTADO | insight/services/insightService.ts | Usa commissionService + reportService (ambos mock); não é chamado pelo Dashboard |

### Configurações / Perfil

| Módulo | Status | Arquivos | Riscos |
|--------|--------|---------|--------|
| Configurações | REAL | settingsService → /api/settings | - |
| Perfil (visualização) | REAL via userService | profileService.getProfile → listUsers (GET /api/users) carrega todos | Ineficiente: carrega todos os users para obter um |
| Perfil (trocar senha) | MOCK | profileService.changePassword → delay, sem POST /auth/change-password | Senha não muda no backend |

### Orken Menu (Restaurante)

| Módulo | Status | Arquivos | Riscos |
|--------|--------|---------|--------|
| Salão (FloorPage) | REAL | useRestauranteTables, useRestauranteAreas | - |
| Mesa / Comanda | REAL | OrderPage, useOrderMutations | - |
| Cozinha (KitchenPage) | REAL + SignalR | useKitchenSocket, useKitchenItems | Fallback polling 10s |
| Delivery | REAL | DeliveryPage, useDeliveryOrders | - |
| Portal (PortalMenuPage) | REAL | usePublicMenu → /api/public/menu/:slug | Público sem auth |
| Rastreamento | REAL | PortalTrackingPage → /api/public/orders/:token | Público sem auth |
| Configurar Mesas | REAL | RestauranteSetupPage, useFoodSettings | - |
| Portal Setup | REAL | PortalSetupPage, updatePortalInfo | - |
| Relatórios | REAL | RelatoriosPage (restaurante) → /api/restaurante/reports/summary | - |
| Financeiro / CMV | REAL | FinanceiroPage, use-financeiro | Heavy: COGS calculado em memória por período |
| Ficha Técnica | REAL | RecipeCardPage, use-recipe-card | - |

### Orken Build

| Módulo | Status | Arquivos | Riscos |
|--------|--------|---------|--------|
| Projetos (lista) | REAL | useProjects → /api/build/projects | Paginado |
| Projeto detalhe | REAL | BuildProjectDetailPage | - |
| Orçamentos Build | REAL | use-build (fetchBudgets etc.) | - |
| Diário de obras | REAL | fetchDailyLogs, createDailyLog | - |

### Platform / SuperAdmin

| Módulo | Status | Arquivos | Riscos |
|--------|--------|---------|--------|
| Platform Dashboard | REAL | PlatformDashboardPage, usePlatformStats | staleTime 60s |
| Platform Tenants | REAL | PlatformTenantsPage, fetchPlatformTenants | - |
| Platform Tenant Detalhe | REAL | PlatformTenantDetailPage | - |
| Platform System | REAL | PlatformSystemPage, usePlatformHealth | refetchInterval 30s |
| Platform Activity (Audit) | REAL | PlatformActivityPage, useAuditLog | Paginado |
| Platform Flags | REAL | PlatformFlagsPage, useFlags | - |
| Platform Trial | REAL | PlatformTrialPage | - |
| AI Dashboard/Playground/Providers/Telemetry/Costs/Prompts | REAL | platform/pages/ai/* | - |

---

## 6. Performance

### Gargalos identificados

| Problema | Causa | Evidência | Correção | Prioridade |
|---------|-------|----------|---------|-----------|
| GET /api/sales sem paginação | SalesController.GetAll() retorna IReadOnlyList ilimitada | SalesController.cs linha 20-22 | Remover ou deprecar o endpoint; forçar uso de /paged | P2 |
| GET /api/audit sem paginação | AuditController.GetAll() sem Take | AuditController.cs linha 21-29 | Adicionar paginação obrigatória | P2 |
| GET /api/stock sem paginação | StockController.GetAll() retorna todos | StockController.cs linha 17-18 | MovimentacoesPage e PosProductSearch usam isso; adicionar paginação | P2 |
| GET /api/products sem paginação | ProductsController.GetAll() | ProductsController.cs linha 18-23 | Usado por PosProductSearch e RecipeCardPage | P2 |
| GET /api/customers sem paginação | CustomersController.GetAll() | CustomersController.cs | Raro que seja chamado mas existe | P3 |
| SalesByDayAsync ToListAsync antes de GroupBy | 30 dias de vendas carregados em memória antes de agrupar | DashboardService.cs linha 126-133 | GroupBy + Select na query EF antes de ToListAsync | P2 |
| FinanceiroController COGS em memória | Carrega orderItems, recipeCards, ingredientes e faz foreach em memória | FinanceiroController.cs linha 155-210 | Pode ser aceitável para restaurantes pequenos; usar SQL GROUP BY para cálculos escaláveis | P3 |
| PosProductSearch carrega todos os produtos e stock | useProducts() + useStockItems() sem paginação | PosProductSearch.tsx linha 25-26 | Funciona para <500 produtos; implementar busca server-side | P3 |
| Perfil carrega todos os usuários | profileService.getProfile usa listUsers (GET /api/users) | profileService.ts linha 24-26 | GET /api/users/:id direto | P2 |
| DashboardService sem cache | 5 queries sequenciais por cada acesso ao dashboard | DashboardService.cs | Adicionar ICacheService com TTL 1-2min | P2 |
| reportService agrega tudo em memória | Chama listSales() + saleToLegacy() + loops | reportService.ts | Substituir por endpoints reais de reports | P1 |
| commissionService sem backend | Agrega de posService (mock) | commissionService.ts | Criar backend de comissões ou migrar para reports | P1 |

---

## 7. Bugs e Erros Críticos

### P0 (produção em risco)

Nenhum P0 identificado que não tenha sido corrigido nos commits recentes. Os 3 bugs críticos (AvailableQuantity, Redis backlog, DashboardService Task.WhenAll) foram corrigidos.

### P1 (funcionalidade quebrada)

1. **CaixaPage — modais usam cashService mock**: O componente `CaixaPage` exibe a sessão aberta via API real (`useOpenSession`), mas quando o usuário tenta abrir, fechar ou adicionar movimento de caixa, os modais (`CashOpenModal`, `CashCloseModal`, `CashMovementModal`) usam `cashService` que mantém estado em memória e nunca chama o backend. O estado exibido é incoerente: a leitura é real, as mutações são mock.
   - Arquivos: `src/modules/cash/services/cashService.ts`, `src/modules/cash/components/CashOpenModal.tsx`, `CashCloseModal.tsx`, `CashMovementModal.tsx`
   - Backend existe completo: `CashController.cs` tem todos os endpoints.

2. **Perfil — troca de senha não funciona**: `profileService.changePassword` simula sucesso com um `delay()` e não chama `POST /auth/change-password`. O usuário vê "senha alterada" mas nada muda no backend.
   - Arquivo: `src/modules/profile/services/profileService.ts` linha 30-49
   - Backend: `UsersController.cs` tem `POST /api/users/:id/change-password`.

3. **Orçamentos — módulo 100% mock**: `OrcamentosPage` e `OrcamentoFormPage` usam `quotationService` que persiste em memória (`mockQuotations`). Não existe endpoint no backend para orçamentos. Dados desaparecem ao recarregar a página.
   - Arquivo: `src/modules/sales/services/quotationService.ts`

4. **Comissões — módulo 100% mock**: `ComissoesPage` usa `commissionService` que depende de `posService.getRecentSales()` que usa `mockSales`. Nenhuma comissão real é calculada.
   - Arquivo: `src/modules/commissions/services/commissionService.ts`

5. **Permissões — módulo sem backend**: `PermissoesPage` renderiza `rolePresets` de `mockUsers.ts`. Não há endpoint de permissões no backend.
   - Arquivo: `src/modules/users/pages/PermissoesPage.tsx`

### P2 (degradação)

1. **RelatoriosPage mistura mock e real**: A página `RelatoriosPage` (`src/modules/reports/pages/RelatoriosPage.tsx`) faz 3 queries reais (`api-report-sales`, `api-report-inventory`, `api-report-customers`) E 5 queries via `reportService` mock (operational, byOperator, topProducts, cancellations, commission, cash, inventory). O resultado é uma página com dois "mundos" sobrepostos — dados reais ao lado de dados derivados de mock.

2. **GET /api/stock retorna sem paginação**: `MovimentacoesPage` chama `useStockItems()` que usa `GET /api/stock` (sem paginação). Com milhares de produtos no estoque, isso pode ser lento.

3. **Audit log retorna lista ilimitada**: `GET /api/audit` sem paginação. Em produção com meses de uso, pode retornar dezenas de milhares de registros.

4. **DashboardService sem cache Redis**: 5 queries sequenciais por cada hit no `/api/dashboard/summary`. Acesso simultâneo de vários usuários causa N * 5 queries ao banco.

5. **SalesByDayAsync carrega vendas em memória antes de agregar**: `GetSalesByDayAsync` chama `.ToListAsync()` em vendas dos últimos 30 dias, depois agrupa em memória. Com alta volumetria, isso pode ser pesado.

6. **FinanceiroController COGS loop em memória**: O cálculo de COGS em `FinanceiroController.GetSummary()` carrega todos os pedidos, itens, fichas técnicas e ingredientes em memória e faz um `foreach` para calcular custo por item. Funcional para restaurantes pequenos, problemático para operações maiores.

### P3 (cosmético/menor)

1. **posService.completeSale é código morto**: A função `posService.completeSale` em `posService.ts` não é mais chamada pelo `PdvPage` (que agora usa `useCompleteSale`). Código morto que pode causar confusão.

2. **inventoryService.applySale/revertSaleItems são código morto**: Marcados como `@deprecated`; ninguém os chama exceto testes antigos. Remoção limpa.

3. **insightService nunca é chamado**: O arquivo `insight/services/insightService.ts` importa commissionService e reportService (ambos mock) e gera insights, mas o componente `RecentInsights` no dashboard IGNORA esse serviço e deriva insights diretamente do `useDashboardSummary`. Arquivo inútil.

4. **mockAuditRecords, mockCash, mockInventory, mockProducts, mockSales, mockPosProducts, mockSuppliers, mockCustomers, mockUsers**: Todos esses arquivos `data/mock*.ts` ainda existem no projeto. Não são todos usados, mas poluem o bundle com código de dados fictícios.

5. **Swagger desabilitado em produção**: `Program.cs` condiciona Swagger a `!IsProduction()`. Impede que a equipe teste a API de produção sem ambiente local.

6. **CSP bloqueia conexões externas**: O Content-Security-Policy define `connect-src 'self'` mas o frontend em `orken.com.br` chama `backend-production-b2bc.up.railway.app`. Isso vai causar bloqueio de requests nos browsers que respeitam CSP do backend. (O CSP está no backend, não no frontend, mas se o browser receber esse header do backend via CORS, pode ser aplicado.)

---

## 8. Dívida Técnica

### Frontend

| Item | Severidade | Localização |
|------|-----------|-------------|
| cashService 100% mock — modais não conectados ao backend | Alta | `modules/cash/services/cashService.ts` |
| profileService.changePassword mock | Alta | `modules/profile/services/profileService.ts` |
| quotationService mock sem backend | Alta | `modules/sales/services/quotationService.ts` |
| commissionService mock | Alta | `modules/commissions/services/commissionService.ts` |
| reportService agrega de posService mock | Alta | `modules/reports/services/reportService.ts` |
| insightService arquivo morto | Média | `modules/insights/services/insightService.ts` |
| posService.completeSale código morto | Baixa | `modules/sales/services/posService.ts` |
| inventoryService.applySale/revertSaleItems código morto | Baixa | `modules/inventory/services/inventoryService.ts` |
| Muitos arquivos mock/* ainda no projeto | Baixa | `*/data/mock*.ts` (10+ arquivos) |
| PosProductSearch sem paginação server-side | Média | `modules/sales/components/PosProductSearch.tsx` |
| MovimentacoesPage carrega todos os StockItems | Média | `modules/inventory/pages/MovimentacoesPage.tsx` |
| profileService.getProfile carrega todos os usuários | Média | `modules/profile/services/profileService.ts` |
| staleTime ausente em muitas queries (retries desnecessários) | Baixa | Vários hooks sem staleTime explícito |

### Backend

| Item | Severidade | Localização |
|------|-----------|-------------|
| GetAll endpoints sem paginação (sales, stock, products, customers, users, audit, cash) | Alta | Controllers raiz |
| SalesByDayAsync — ToListAsync antes de GroupBy | Média | DashboardService.cs |
| DashboardService sem cache | Média | DashboardService.cs |
| FinanceiroController COGS em memória | Média | FinanceiroController.cs |
| CSP `connect-src 'self'` bloquearia frontend em produção | Alta | Program.cs middleware de security headers |
| GetMovements sem paginação (StockController) | Média | StockController.cs |
| OrderRepository.GetAllAsync carrega todas as comandas | Média | OrderRepository.cs |

### Infra

| Item | Severidade | Localização |
|------|-----------|-------------|
| Swagger desabilitado em produção | Baixa | Program.cs |
| DataSeeder não roda em produção | Informacional | Program.cs (comportamento intencional) |
| Redis failure não gera alerta operacional | Baixa | RedisCacheService falha silenciosa |

---

## 9. Plano de Ação

### Fase 1 — P1 imediatos (semana 1-2)

1. **Conectar CaixaPage ao backend real**:
   - `CashOpenModal` → chamar `useOpenCashSession` mutation (já existe em `use-cash.ts`)
   - `CashCloseModal` → chamar `useCloseCashSession`
   - `CashMovementModal` → chamar `useAddCashMovement`
   - Remover imports de `cashService` dos modais
   
2. **Conectar profileService.changePassword ao backend**:
   - `profileService.changePassword` → `apiClient.post('/users/:id/change-password', {...})`
   - Endpoint existe: `POST /api/users/:id/change-password`

3. **Criar endpoint de orçamentos no backend OU remover o módulo**:
   - Decisão de produto: se orçamentos são necessários, criar `QuotationsController` + serviço + repositório
   - Se não: remover `OrcamentosPage`, `OrcamentoFormPage`, `quotationService`, `mockQuotations` do router e da navegação

### Fase 2 — Performance e Paginação (semana 2-3)

1. **Adicionar paginação forçada em `GET /api/audit`**: aceitar `page`/`pageSize` obrigatórios, remover a versão unbounded
2. **Deprecar `GET /api/sales` (sem paginação)**: frontend migrar para `/paged` em todos os locais
3. **Deprecar `GET /api/stock` (sem paginação)**: `MovimentacoesPage` e `PosProductSearch` migrar
4. **Adicionar cache Redis no DashboardService**: `ICacheService.GetOrSetAsync` com TTL 2min
5. **Corrigir SalesByDayAsync**: usar `GroupBy` + `Select` no EF antes de `ToListAsync`
6. **profileService.getProfile**: usar `GET /api/users/:userId` diretamente

### Fase 3 — Consistência e Limpeza (semana 3-4)

1. **Criar backend de comissões OU migrar relatórios de comissões para usar `/api/reports`**
2. **Conectar RelatoriosPage ao backend completamente**: substituir as 5 queries mock por endpoints reais
3. **Remover arquivos mortos**: `insightService.ts`, `posService.completeSale`, `inventoryService.applySale`, todos os `data/mock*.ts` que não são mais necessários
4. **Corrigir CSP**: `connect-src` deve incluir o domínio do backend: `https://backend-production-b2bc.up.railway.app`
5. **PermissoesPage**: definir funcionalidade (é uma visualização read-only das roles?) e conectar ao backend ou converter em documentação estática

### Fase 4 — Features e Escalabilidade

1. **PosProductSearch server-side**: debounce + `GET /api/products/search?q=` para empresas com >500 produtos
2. **GetMovements paginado**: `GET /api/stock/product/:id/movements?page=&pageSize=`
3. **OrderRepository.GetAllAsync**: adicionar filtros de data para limitar janela de tempo
4. **FinanceiroController COGS**: se o restaurante crescer, mover cálculo para SQL ou cache
5. **Swagger em produção**: proteger com auth básica ou IP whitelist para devs

---

## 10. Checklist de Validação

| Feature | Implementado | Conectado | Testado | Funcionando |
|---------|------------|---------|---------|------------|
| Login (username + senha) | Sim | Sim | Sim | Sim |
| Login (email platform) | Sim | Sim | - | Sim |
| Registro + verificação email | Sim | Sim | - | Sim |
| Dashboard (KPIs + gráficos) | Sim | Sim | Parcial | Sim |
| Produtos (CRUD) | Sim | Sim | - | Sim |
| Estoque (listagem paginada) | Sim | Sim | - | Sim |
| Estoque (ajuste) | Sim | Sim | - | Sim |
| Movimentações de estoque | Sim | Sim | - | Sim (sem paginação) |
| Vendas (listagem paginada) | Sim | Sim | - | Sim |
| Vendas (detalhe) | Sim | Sim | - | Sim |
| PDV (completar venda) | Sim | Sim | - | Sim |
| Orçamentos | Sim | NÃO (mock) | - | NÃO (dados mock) |
| Caixa (visualizar sessão) | Sim | Sim | - | Sim |
| Caixa (abrir/fechar/movimentos) | Sim backend | NÃO (frontend mock) | - | NÃO (não persiste) |
| Clientes (CRUD) | Sim | Sim | - | Sim |
| Fornecedores (CRUD) | Sim | Sim | - | Sim |
| Usuários (CRUD) | Sim | Sim | - | Sim |
| Permissões | Sim frontend | NÃO (mock roles) | - | Parcial (read-only fake) |
| Auditoria | Sim | Sim | - | Sim (sem paginação) |
| Configurações | Sim | Sim | - | Sim |
| Perfil (visualização) | Sim | Sim (ineficiente) | - | Sim |
| Perfil (troca de senha) | Sim backend | NÃO (frontend mock) | - | NÃO |
| Relatórios (sales/inventory/customers API) | Sim | Sim | - | Sim |
| Relatórios (operational/comissões) | Sim frontend | NÃO (mock) | - | NÃO |
| Comissões | Sim frontend | NÃO (mock) | - | NÃO |
| Restaurante (salão/mesas) | Sim | Sim | - | Sim |
| Restaurante (cozinha + SignalR) | Sim | Sim | - | Sim |
| Restaurante (delivery) | Sim | Sim | - | Sim |
| Restaurante (portal público) | Sim | Sim | - | Sim |
| Restaurante (financeiro/CMV) | Sim | Sim | - | Sim |
| Restaurante (relatórios) | Sim | Sim | - | Sim |
| Ficha técnica (recipe card) | Sim | Sim | - | Sim |
| Build (projetos/etapas/orçamentos) | Sim | Sim | - | Sim |
| Platform admin (tenants/flags) | Sim | Sim | - | Sim |
| Platform AI operations | Sim | Sim | - | Sim |
| Impersonation | Sim | Sim | - | Sim |
| Multi-tenancy isolamento | Sim | Sim | Sim (28 testes) | Sim |
| Token refresh automático | Sim | Sim | Sim | Sim |
| SecurityStamp revogação | Sim | Sim | - | Sim |
| Rate limiting login | Sim | Sim | - | Sim |

---

## Apêndice: Arquivos Notáveis

### Arquivos de alto risco (código vivo com problemas)

- `C:\Users\Elias\Documents\NexoERP\nexo-main\src\modules\cash\services\cashService.ts`  
  — 100% mock, estado em memória. Crítico: modais de Caixa não persistem nada.

- `C:\Users\Elias\Documents\NexoERP\nexo-main\src\modules\profile\services\profileService.ts`  
  — `changePassword` simula sucesso sem chamar o backend.

- `C:\Users\Elias\Documents\NexoERP\nexo-main\src\modules\reports\services\reportService.ts`  
  — Misto: chama `listSales()` (API real) mas toda a agregação é feita em memória sobre `saleToLegacy` que transforma DTOs da API em formato `CompletedSale` e `commissionService` ainda usa `posService.getRecentSales()` (mock).

- `C:\Users\Elias\Documents\NexoERP\nexo-main\src\modules\sales\services\quotationService.ts`  
  — 100% mock, em memória, sem endpoint backend.

- `C:\Users\Elias\Documents\NexoERP\nexo-main\src\modules\commissions\services\commissionService.ts`  
  — 100% mock, sem endpoint backend.

- `C:\Users\Elias\Documents\NexoERP\nexo-backend\src\Nexo.Infrastructure\Dashboard\DashboardService.cs`  
  — Corrigido nesta sessão (sequential, raw SQL). Ainda sem cache.

- `C:\Users\Elias\Documents\NexoERP\nexo-backend\src\Nexo.Api\Controllers\Modules\Restaurante\FinanceiroController.cs`  
  — `GetSummary` faz COGS em memória. OK para escala atual.

- `C:\Users\Elias\Documents\NexoERP\nexo-backend\src\Nexo.Api\Program.cs` linha 267  
  — `connect-src 'self'` no CSP. Se o browser receber esse header do backend via CORS preflight, requests do frontend para o backend serão bloqueados.

### Arquivos mortos (podem ser removidos com segurança)

- `src/modules/insights/services/insightService.ts` — nunca chamado
- `src/modules/inventory/services/inventoryService.ts` — `applySale`/`revertSaleItems` não são chamados
- `src/modules/sales/services/posService.ts` — `completeSale` não é chamado pelo PdvPage real
- `src/modules/products/services/productService.ts` — já é arquivo vazio (`export {}`)
- Todos os arquivos `*/data/mock*.ts` que não são mais importados por código de produção

### Arquivos de referência (bem implementados, boa prática)

- `C:\Users\Elias\Documents\NexoERP\nexo-backend\src\Nexo.Infrastructure\Cache\RedisCacheService.cs`  
  — fail-open com WaitAsync(200ms) por call. Bom padrão.

- `C:\Users\Elias\Documents\NexoERP\nexo-backend\src\Nexo.Api\Middleware\TenantResolutionMiddleware.cs`  
  — Validação robusta: TenantId + UserId + Status + cache de 5min. Bom.

- `C:\Users\Elias\Documents\NexoERP\nexo-backend\src\Nexo.Api\Middleware\SecurityStampValidationMiddleware.cs`  
  — Revogação de sessão imediata via SecurityStamp com cache 60s. Elegante.

- `C:\Users\Elias\Documents\NexoERP\nexo-main\src\modules\restaurante\hooks\useKitchenSocket.ts`  
  — SignalR com retry backoff + fallback automático para polling 10s. Bem implementado.

- `C:\Users\Elias\Documents\NexoERP\nexo-main\src\services\api-client.ts`  
  — Token em memória + localStorage fallback, retry automático em 401 com refresh. Bom.

- `C:\Users\Elias\Documents\NexoERP\nexo-main\vite.config.ts`  
  — Manual chunks por vendor e por módulo app. Bom split de bundle.
