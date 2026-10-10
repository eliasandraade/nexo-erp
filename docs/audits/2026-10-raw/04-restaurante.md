# Auditoria — Restaurante (Orken Menu)

Commit auditado: `e8b4873`. Auditoria somente leitura. Não houve execução de testes nem de servidor; as conclusões vêm da leitura do código.

---

## 🚨 Achados de topo (alta severidade — nenhum acesso cross-tenant confirmado)

> Nenhum vazamento entre tenants nem bypass de autenticação foi confirmado nesta área. Os três itens abaixo ficam no topo porque **quebram fluxo de dinheiro**, **deixam receita fora do sistema** ou **expõem o catálogo publicamente**.

### 🚨 1. Comanda com modificador pago NÃO pode ser paga
**Onde:**
- `OrderService.CloseAsync` (`nexo-backend/src/Nexo.Application/Modules/Restaurante/OrderService.cs:298-306`) cria os `SaleItem` com `item.UnitPrice`, que é o preço base do produto.
- `RestOrderItem.ApplyModifier` (`Domain/Modules/Restaurante/RestOrderItem.cs`) soma o ajuste do modificador só em `RestOrderItem.Total`. `UnitPrice` não muda.

**Cenário concreto:**
1. O garçom lança "Hambúrguer R$30 + Bacon extra +R$5".
2. O total da comanda fica R$35. A `Sale` em Draft fica com R$30.
3. O `PaymentDrawer` (`nexo-main/src/modules/restaurante/components/PaymentDrawer.tsx:65,99`) exige pagamento exato de R$35.
4. `PayAsync` repassa os pagamentos para `SaleService.ConfirmAsync`. Ali, `paymentTotal != sale.Total` (`Features/Sales/SaleService.cs:146-149`) gera `DomainException`.
5. Resultado: o pagamento é sempre recusado e a mesa fica presa em `Closed`/`Occupied`.

**Testes:** não cobrem. Todos os modificadores dos testes têm preço `0m` (`DeliveryPortalFlowTests.cs:138`, `RestauranteFlowTests.cs:842`).

**Agravante:** com modificador de preço negativo, o cliente paga a mais e a receita da `Sale` diverge da comanda.

### 🚨 2. Pedido de delivery/retirada nunca vira venda: sem Sale, caixa, baixa de estoque ou CMV
**Como acontece:**
- `DeliveryOrderService.AcceptAsync` (`DeliveryOrderService.cs:387-485`) cria um `RestOrder` do tipo Delivery/Takeaway.
- Esse `RestOrder` sai com `couvertAmount: 0`. **Não leva taxa de entrega nem desconto de cupom.**
- Na UI, o fluxo termina em `Delivered` (`DeliveryCard.tsx`). Nenhum lugar fecha nem paga esse `RestOrder`.
- `FloorPage` só lista mesas (`pages/FloorPage.tsx:59-150`). Não existe lista de comandas sem mesa.

**Consequência:**
- Todo pedido do portal ou do hub de entregas fica fora de `Sale`, `CashMovement`, baixa de estoque (`RecipeOutput`) e dos relatórios. Relatórios e financeiro só contam `RestOrder.Status == Paid` (`FinanceiroController.cs`, `ReportsController.cs`).
- O `RestOrder` vinculado fica eternamente `Ready`. A sincronização não trata `Paid`/`Closed` (`SyncFromRestOrderAsync`, `DeliveryOrderService.cs:587-619`).

**Mesmo problema em comandas de Balcão/Retirada:** se o operador sair de `/restaurante/comanda/:id`, não há como voltar à comanda pela UI.

### 🚨 3. Todo produto ativo da loja (inclusive insumos) aparece e é pedível no portal público
**Causa:**
- `Product.Create` grava `IsMenuVisible = true` (`Domain/Entities/Product.cs:62`).
- `SetMenuVisibility` (`Product.cs:99`) não é chamado por nenhum endpoint nem por nenhuma tela. Só o teste usa (`DeliveryPortalFlowTests.cs:121`).
- `GetAllMenuItemsAsync` (`Infrastructure/Repositories/ProductRepository.cs:28-35`) não exclui `IsIngredient`.

**Cenário:**
- `GET /api/public/menu/{slug}` (anônimo) devolve "Farinha 25kg", "Carne moída kg" e similares, com preço de venda, descrição e imagem.
- `POST /api/public/orders` aceita pedido desses itens.

**Amplificadores:**
- O portal não exige linha de `FoodServiceSettings`. Sem ela, os defaults são `AcceptingOrders=true` e `DeliveryEnabled=true` (`PublicMenuService.cs`, `DeliveryOrderService.cs:263-272`).
- O portal não verifica `Store.Status`, a assinatura do módulo (`ModuleSubscription` é carregada e ignorada, `StoreEntityRepository.cs:31-36`) nem o status do tenant.
- O `PublicSlug` é o mesmo do portal Service. Assim, a loja Service de uma barbearia que publica slug também expõe os `Product` dela como "cardápio" em `/:slug` e recebe pedidos de delivery.

---

## Cardápio / Menu (produtos como itens, grupos de modificadores, disponibilidade)
**Objetivo:** cadastrar os itens do cardápio, opções/adicionais (modificadores) e a disponibilidade para salão e portal.

**Estado:** 🟠 PARCIAL   **Maturidade:** 35%

**Justificativa:**
- Os modificadores existem no backend e são validados no portal.
- Faltam:
  - UI para criar grupos/modificadores;
  - controle de visibilidade no cardápio;
  - disponibilidade ("esgotado", horário).
- Modificador pago quebra o pagamento da comanda (🚨1).

### Backend existente
- Produtos do core (`Product`) servem como itens de cardápio. Campos `IsMenuVisible` e `IsIngredient`.
- `ProductModifierGroup` e `ProductModifier` (TenantEntity, preço `PriceAdjustment` com delta fixo). Migrations `20260413060240_AddModifierGroups`, `20260413063408_AddMinSelectionsToModifierGroups`.
- `ModifierGroupsController`: `GET/POST/PUT /api/restaurante/modifier-groups`, `POST/PUT/DELETE /api/restaurante/modifier-groups/{id}/modifiers/{modId}`. Serviço em `ModifierGroupService`.
- Validação no portal: `ValidatePortalItemModifiers` (`DeliveryOrderService.cs:660-698`) cobre pertença ao produto, `IsActive`, min/max.

### Frontend existente
- Leitura apenas: `getModifierGroups` (`restaurante/api/restaurante.api.ts:102`), `ModifierSelector.tsx` (salão) e `portal/components/ModifierPicker.tsx`.

### Funcionalidades concluídas
- Selecionar modificador obrigatório na comanda, com 422 quando falta. Teste `AddItem_WithRequiredModifierGroupMissing_Returns422`.
- Validação completa de modificadores no portal. Teste `CreatePortalOrder_WithRequiredModifier_Succeeds`.

### Funcionalidades parciais
- Snapshot de modificador (`RestOrderItemModifier`) existe. O preço entra no `Total` da comanda, mas não na `Sale` (🚨1).
- No salão, `AddItemAsync` só valida grupos obrigatórios. Não valida `MaxSelections` nem se o modificador pertence ao produto (`OrderService.cs:187-215`).

### Funcionalidades não desenvolvidas
- UI de CRUD de grupos/modificadores: não há chamada POST/PUT/DELETE para `modifier-groups` em `nexo-main/src`.
- Controle de "visível no cardápio": nenhum endpoint nem UI.
- Disponibilidade por horário/esgotado. `BusinessHoursJson` é salvo, mas não é aplicado.
- Categorias e ordenação específicas de cardápio. Hoje usa `Category` do core.

### Problemas
- 🚨1 e 🚨3.
- `ToShort` lança `ArgumentOutOfRangeException` (`ModifierGroupService.cs:83`), que provavelmente vira 500 em vez de 422.
- `CreateGroupAsync` não confere se `ProductId` pertence ao tenant. O impacto é baixo porque as leituras filtram por tenant.

### Dependências
- Produtos/categorias do core; `PublicMenuService`; `OrderService`; `DeliveryOrderService`.

### Próximos passos
1. Corrigir 🚨1: levar o preço dos modificadores para a `Sale`, como `UnitPrice` efetivo ou linha própria.
2. Endpoint e UI de `IsMenuVisible`. Default `false` para `IsIngredient`.
3. UI de grupos/modificadores.
4. Aplicar `MaxSelections` e pertença no salão.
5. Disponibilidade/esgotado.

---

## Áreas e mesas
**Objetivo:** cadastrar o salão e controlar a ocupação das mesas.

**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 75%

**Justificativa:** CRUD completo, com UI e testes de concorrência. Faltam status manual na UI e saída para mesa "presa" em `Closed`.

### Backend existente
- `AreasController` (`GET/POST/PUT /api/restaurante/areas`) e `TablesController` (`GET/POST/PUT /api/restaurante/tables`, `GET by-area/{areaId}`, `PATCH {id}/status`, `GET {id}/orders`).
- Serviços `AreaService` e `TableService`. Entidades `RestArea` e `RestTable` (StoreEntity).

### Frontend existente
- `/restaurante/configurar` (`RestauranteSetupPage.tsx`) para CRUD de áreas e mesas.
- `/restaurante` (`FloorPage.tsx`) com grade de mesas por área (`AreaTabs`, `TableCard`).

### Funcionalidades concluídas
- CRUD de áreas e mesas, com ativar/desativar.
- Abrir comanda ocupa a mesa e pagar/cancelar libera. Testes `OpenOrder_OccupiesTable`, `PayOrder_ReleasesTable`, `CancelOrder_ReleasesTable`.
- Proteção contra dupla abertura: `SELECT FOR UPDATE` mais índice parcial único `ix_rest_orders_one_active_per_table` (`RestOrderConfiguration.cs`). Testes `ConcurrentOrderOpen_OnSameTable_OnlyOneSucceeds`, `SequentialDuplicateOrder_IsRejectedByApplicationGuard`.

### Funcionalidades parciais
- `PATCH /api/restaurante/tables/{id}/status` (Reserved/Maintenance/Available) não tem consumidor no frontend.
- Mesa em `Reserved` não aceita comanda: `SetOccupied` exige `Available`.

### Funcionalidades não desenvolvidas
- Reservas, junção/transferência de mesas e mapa visual.

### Problemas
- **Mesa presa:**
  - `useActiveOrder` (`hooks/useActiveOrder.ts:15-19`) ignora comandas `Closed`.
  - Se o operador fecha a conta e fecha o drawer de pagamento, ao clicar na mesa ocupada vê "Nenhuma comanda aberta" (`OrderPage.tsx:59-67`).
  - O backend proíbe cancelar comanda `Closed` (`RestOrder.Cancel`).
  - Sem UI, a mesa fica `Occupied` para sempre.
- `PATCH status=Available` pode liberar uma mesa com comanda aberta. O guard em `OpenAsync` evita a dupla comanda.

### Dependências
- `OrderService`.

### Próximos passos
1. Incluir `Closed` em `useActiveOrder` e dar acesso ao pagamento pendente.
2. UI de status manual.
3. Bloquear `Available` manual quando houver comanda ativa.

---

## Pedidos de salão (RestOrder: itens, modificadores, status, fechamento, pagamento)
**Objetivo:** comanda de mesa/balcão até o pagamento, integrada a Vendas, Caixa, Estoque e Financeiro.

**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 55%

**Justificativa:**
- O fluxo sem modificador pago funciona e tem cerca de 24 testes.
- Quebra com modificador pago (🚨1).
- Comandas sem mesa ficam inacessíveis.
- Fechamento não transacional.
- Sem desconto nem pagamento parcial por item.

### Backend existente
- `OrdersController` (`api/restaurante/orders`):
  - `GET`, `GET {id}`, `POST`;
  - `POST {id}/items`, `PATCH {id}/items/{itemId}/status`, `DELETE {id}/items/{itemId}`;
  - `POST {id}/close`, `POST {id}/pay`, `POST {id}/cancel`.
- `OrderService` (514 linhas).
- Entidades `RestOrder`, `RestOrderItem` e `RestOrderItemModifier`.
- Máquina de status: Open → InPreparation → Ready → Closed → Paid | Cancelled.
- Migrations `UpdateRestOrderSchema`, `AddRestOrderItemModifiers`, `AddRestauranteStoreIsolation`.

### Frontend existente
- `/restaurante/mesa/:tableId` e `/restaurante/comanda/:orderId`: `OrderPage.tsx`, `AddItemDrawer.tsx`, `PaymentDrawer.tsx` (split por N pessoas, múltiplos meios), `OpenOrderSheet.tsx`.

### Funcionalidades concluídas
- Abrir comanda DineIn/Counter/Takeaway, com couvert automático por pessoa. Teste `OpenOrder_WithCouvertAutomatic_AppliesCouvertAmount`.
- Adicionar item com snapshot de preço.
- Fechar comanda gera `Sale` Draft no core (`CloseAsync`). Teste `CloseOrder_GeneratesDraftSale`.
- Pagar chama `SaleService.ConfirmAsync`, que gera `SalePayment` e:
  - `CashMovement`, **só** se o usuário tiver sessão de caixa aberta (`SaleService.cs:195-236`);
  - `FinancialTransaction` Receivable, se for a prazo.
- A baixa de estoque de produtos sem ficha técnica fica com a `Sale`. Os produtos com ficha são pulados (`SkipStockProductIds`) e baixam via `RecipeOutput`.
- Idempotência: segundo pay retorna 409. Teste `PayOrderTwice_SecondCallReturnsConflict`.
- Rollback em pagamento divergente. Teste `PayOrder_PaymentMismatch_RollsBackTableAndStock`.
- Couvert manual e taxa de serviço, com 4 testes de breakdown.
- Isolamento por loja. Teste `OpenOrder_IsIsolatedToCurrentStore`.

### Funcionalidades parciais
- Cancelar item: `DELETE` existe, mas não há consumidor no frontend (só `cancelOrder` da comanda inteira).
- Comandas Counter/Takeaway: são abertas em `FloorPage`, mas não existe lista para reabri-las.

### Funcionalidades não desenvolvidas
- Desconto na comanda.
- Pagamento parcial por item ou por pessoa com fechamento parcial.
- Transferência de itens entre mesas.
- Reabrir comanda `Closed`.
- Impressão fiscal. Existe só `utils/print.ts` não fiscal.

### Problemas
- **🚨1** (modificador pago impede pagamento).
- **`CloseAsync` não é transacional** (`OrderService.cs:280-317`). Uma falha em `AddItemAsync` no meio (por exemplo, produto inativado depois do lançamento) deixa uma `Sale` Draft órfã. Um novo `close` cria outra.
- **Numeração `RestOrder`:** `GetNextNumberAsync` usa `MAX+1` sem retry (`OrderRepository.cs:58-62`), com índice único `ix_rest_orders_tenant_store_number`. Abertura concorrente de Balcão/aceite de delivery causa violação de unicidade, provavelmente 500. Para `RestDeliveryOrder` existe retry; aqui não.
- **`GET /api/restaurante/orders` sem paginação nem filtro:** retorna todas as comandas da história, com itens e produtos (`OrderRepository.GetAllAsync`). É usado por `useActiveOrder` e pelo KDS a cada 10s, e a performance degrada linearmente.
- A receita só vai ao caixa se o usuário que paga tiver sessão aberta. Caso contrário, a venda é confirmada sem `CashMovement`, em silêncio.
- A taxa de serviço vale para qualquer tipo de comanda, inclusive balcão/delivery.
- `OrderTypesEnabled` e `StoreType` de `FoodServiceSettings` são salvos e nunca aplicados.
- O `SaleItem` grava `CostPrice` do prato, normalmente 0. O CMV visto pelo core diverge da ficha técnica.

### Dependências
- `SaleService` (core), `IStockRepository`, `IRecipeCardRepository`, `IFoodServiceSettingsRepository`, `IRestaurantNotificationService`, `IDeliveryOrderSyncService`.

### Próximos passos
1. Corrigir 🚨1.
2. Tornar `CloseAsync` transacional.
3. Lista de comandas abertas sem mesa e de comandas `Closed`.
4. Retry na numeração.
5. Paginar/filtrar `GET orders` por status ativo.
6. Aviso quando não houver caixa aberto.

---

## Cozinha / KDS (SignalR)
**Objetivo:** fila de produção em tempo real por loja.

**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 55%

**Justificativa:** o hub está corretamente escopado por tenant/loja e há polling de fallback. Mas o KDS não mostra modificadores, não volta ao grupo após reconectar e carrega o histórico inteiro.

### Backend existente
- `RestaurantHub` (`Infrastructure/Hubs/RestaurantHub.cs`), `[Authorize]`, mapeado em `/hubs/restaurant` (`Program.cs:405`).
- `JoinStore(storeId)` valida `_currentUser.StoreIds` (claims JWT). O grupo é `store:{tenantId}:{storeId}`.
- `RestaurantNotificationService` envia `NewItemAdded`, `OrderItemStatusChanged`, `OrderStatusChanged` e `TableStatusChanged` em fire-and-forget após o commit.
- JWT via `access_token` na query para `/hubs` (`Program.cs:82-85`).

### Frontend existente
- `/restaurante/cozinha` (`KitchenPage.tsx`, `KitchenBoard.tsx`, `KitchenCard.tsx`).
- `hooks/useKitchenSocket.ts`: retry inicial de 1s, 3s e 5s, depois polling a cada 10s.
- `hooks/useKitchenItems.ts` e `KitchenConnectionBadge.tsx`.

### Funcionalidades concluídas
- Escopo de grupo por tenant+loja com validação de acesso à loja. Não há acesso cross-tenant ao grupo.
- Fluxo de item Pending → Preparing → Ready → Delivered (`UpdateItemStatusAsync`), que propaga o status da comanda e sincroniza o `DeliveryOrder`.
- Pedido aceito do delivery aparece na cozinha. Teste `AcceptDeliveryOrder_LinkedRestOrder_AppearsInKitchen`.

### Funcionalidades parciais
- Notificação de novos pedidos de delivery/portal: não há evento SignalR. O hub de entregas faz polling a cada 15s (`useDeliveryOrders.ts:12`).

### Funcionalidades não desenvolvidas
- Estações de cozinha (bar/cozinha), tempo de preparo/SLA, som e impressão automática.

### Problemas
- **O KDS não mostra modificadores** ("sem cebola", "mal passado"). `OrderRepository.GetAllAsync` (`OrderRepository.cs:40-46`) não faz `Include(Items.Modifiers)` e não há `AutoInclude`/lazy loading. Por isso `item.modifiers` chega sempre vazio em `KitchenCard.tsx:86-88`. O mesmo acontece na `OrderPage` via `/mesa/:tableId`.
- **Sem re-join após reconexão:** `onreconnected` (`useKitchenSocket.ts:136-141`) não chama `JoinStore` de novo. A nova conexão fica fora do grupo, a UI mostra "realtime", os eventos param de chegar e o polling não volta. Os dados ficam velhos até um refresh.
- O KDS faz polling de `GET /orders` completo (ver salão).
- O hub não exige o módulo `restaurante`. Impacto baixo: o usuário só recebe eventos da própria loja.

### Dependências
- `OrderService`, autenticação JWT.

### Próximos passos
1. Incluir modificadores em `GetAllAsync`.
2. Chamar `JoinStore` em `onreconnected`.
3. Criar endpoint de KDS filtrado (itens ativos).
4. Emitir evento em novo `DeliveryOrder`.

---

## Ficha técnica / CMV
**Objetivo:** receita por prato, baixa automática de insumos e custo/margem.

**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 60%

**Justificativa:** CRUD com UI, baixa de insumos testada e relatório de CMV. Porém:
- o CMV usa o custo **atual**, não o snapshot;
- delivery não baixa insumos (🚨2);
- modificadores não afetam a receita.

### Backend existente
- `RecipeCardsController` (`api/restaurante/recipe-cards`): `GET`, `GET {id}`, `GET product/{productId}`, `POST`, `PUT {id}`, `POST/DELETE {id}/ingredients`, `POST {id}/image`.
- `RecipeCardService` (exige `IsIngredient` para insumo e embalagem).
- Entidades `RestRecipeCard` e `RestRecipeIngredient`. Migrations `ExtendRestRecipeCard`, `AddIsIngredientToProducts`.
- Baixa em `OrderService.PayAsync` (`OrderService.cs:404-437`): `StockMovementType.RecipeOutput` com `CostPriceSnapshot`.
- Relatório `GET /api/restaurante/financeiro/cmv-report` (`FinanceiroController.cs:28-103`).

### Frontend existente
- `/produtos/:id/ficha` (`RecipeCardPage.tsx`, 600 linhas; `PrepStepsEditor.tsx`; `CmvBar.tsx`; `api/recipe-card.api.ts`; `hooks/use-recipe-card.ts`).

### Funcionalidades concluídas
- Ficha com rendimento, ingredientes, embalagem, passos, imagem e tempos.
- Baixa proporcional `(qtd/rendimento) × qtd_ingrediente`. Testes `PayOrder_WithRecipe_DeductsIngredientStock`, `..._YieldDivisionIsCorrect`, `..._CostPriceSnapshot_IsPopulated`.
- CMV% e margem por prato, com gás e mão de obra por minuto (`UpdateOperationalCostsRequest`, `OperationalCostsCard.tsx`). Testes `CMV_IncludesGasAndLaborCost_WhenSettingsConfigured`, `CmvReport_ReturnsItemWithCorrectCmvMetrics`.

### Funcionalidades parciais
- `CostPriceSnapshot` é gravado, mas não é lido por nenhum relatório. `cmv-report` e `financeiro/summary` usam `Product.CostPrice` atual (`FinanceiroController.cs:58-62,203-208`). Quando o custo muda, o CMV histórico muda junto.

### Funcionalidades não desenvolvidas
- Impacto de modificador na receita (ex.: "extra bacon" consumir bacon).
- Baixa em delivery (🚨2).
- Alerta de insumo abaixo do mínimo ligado à ficha.

### Problemas
- Insumo sem `StockItem` é ignorado em silêncio (`OrderService.cs:415-416`).
- O estoque de insumo pode ficar negativo: `StockItem.ApplyMovement` não tem guard. Pode ser intencional, mas não está documentado.
- `cmv-report` faz várias queries, mas sem N+1. Aceitável.

### Dependências
- Produtos e estoque do core; `FoodServiceSettings` (custos operacionais).

### Próximos passos
1. Usar `CostPriceSnapshot` dos `StockMovement` `RecipeOutput` para o CMV realizado.
2. Baixa no fluxo de delivery.
3. Modificadores com insumo.

---

## Configurações food service (couvert, taxa de serviço, portal, custos)
**Objetivo:** parâmetros operacionais da loja de alimentação.

**Estado:** 🟠 PARCIAL   **Maturidade:** 45%

**Justificativa:** existem portal info, custos operacionais e flags do portal com UI. Mas couvert e taxa de serviço só são configuráveis por API, e `OrderTypesEnabled`/`StoreType` são config morta.

### Backend existente
- `FoodServiceSettingsController`: `GET /api/restaurante/settings` (get-or-create), `PUT /settings`, `PUT /settings/portal`, `PUT /settings/costs`.
- Entidade `FoodServiceSettings` (StoreEntity). Migrations `AddFoodServiceSettings`, `Phase_C_PortalFlags`, `AddOperationalCostsToFoodServiceSettings`.

### Frontend existente
- `useFoodSettings` (leitura), `useUpdateOperationalCosts`, `PortalSetupPage.tsx` (`updatePortalInfo`).

### Funcionalidades concluídas
- Info do portal (nome, logo/capa com upload, WhatsApp, flags `AcceptingOrders`/`DeliveryEnabled`/`TakeawayEnabled`). Testes `CreatePortalOrder_WhenNotAcceptingOrders_IsRejected` e mais 2.
- Custos operacionais de gás e mão de obra.

### Funcionalidades parciais
- Couvert e taxa de serviço: a regra funciona no backend e tem teste, mas `updateFoodSettings` (`restaurante.api.ts:20`) não tem consumidor na UI.

### Funcionalidades não desenvolvidas
- Horário de funcionamento com bloqueio automático. `BusinessHoursJson` é só exibido.
- Aplicação de `OrderTypesEnabled`.

### Problemas
- Config morta: `OrderTypesEnabled` e `StoreType`.
- `PUT /settings` não exige papel: qualquer usuário do tenant (inclusive "vendedor") pode alterar a taxa de serviço via API.

### Próximos passos
1. UI de couvert e taxa de serviço.
2. Aplicar ou remover `OrderTypesEnabled`.
3. Aplicar horário de funcionamento no portal.

---

## Funcionários, despesas, financeiro e relatórios do restaurante
**Objetivo:** P&L simplificado do restaurante (receita, CMV, pessoal, despesas, ponto de equilíbrio) e relatório operacional.

**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 50%

**Justificativa:** CRUDs e KPIs funcionam, com UI e 7 testes. Porém é um silo separado do financeiro do core, e os números têm distorções relevantes:
- pessoal não proporcional ao período;
- CMV com custo atual;
- receita sem delivery;
- datas em UTC.

### Backend existente
- `EmployeesController` (`GET/POST/PUT /api/restaurante/employees`) e `ExpensesController` (`GET/POST/PUT/DELETE /api/restaurante/expenses`). Lógica direto no controller com `NexoDbContext`, sem camada de Application.
- `FinanceiroController`: `GET financeiro/summary` e `GET financeiro/cmv-report`.
- `ReportsController`: `GET reports/summary`.
- Migration `CreateRestEmployeesAndExpenses`.

### Frontend existente
- `/restaurante/financeiro` (`FinanceiroPage.tsx`, 771 linhas: KPIs, insights, seções de funcionários e despesas com edição inline).
- `/restaurante/relatorios` (`RelatoriosPage.tsx`).
- APIs `employees-expenses.api.ts`, `financeiro.api.ts`; `fetchRestauranteSummary`.

### Funcionalidades concluídas
- CRUD de funcionários (desativação lógica) e despesas (hard delete).
- Resumo com receita, CMG, CMV ponderado, margem, pessoal, despesas, lucro operacional e break-even. Testes `FinanceiroSummary_IncludesPersonnelAndExpenses`, `FinanceiroSummary_ReturnsZeroRevenue_WhenNoPaidOrdersInPeriod`, `ListExpenses_FiltersByPeriod` e outros.
- Relatório operacional (ticket médio, tempo médio de mesa).

### Funcionalidades parciais
- A receita vem de `RestOrders` pagos, não de `Sale`. Não inclui taxa de entrega nem delivery (🚨2), e pode divergir do dashboard do core.

### Funcionalidades não desenvolvidas
- Integração com `FinancialTransaction`/contas a pagar do core. `RestExpense` é um silo paralelo.
- Exportação e comparativos por canal.

### Problemas
- **Pessoal:** soma o `MonthlySalary` de todos os ativos independentemente do período (`FinanceiroController.cs:136-138`). Um período de 7 dias recebe o mês inteiro; um de 3 meses recebe só 1 mês. O lucro operacional e o break-even ficam errados fora do mês cheio. O comportamento está documentado como decisão em `docs/releases/2026-05-06-cmv-financeiro-fase3.md`, mas é incorreto para filtros arbitrários.
- **Períodos em UTC** (`TryParseDate` com `AssumeUniversal`): vendas entre 21h e 24h (BRT) caem no dia seguinte.
- **Sem autorização por papel no backend:** a UI restringe `/restaurante/financeiro` a MGMT (`routes.ts:94`), mas `GET /api/restaurante/employees` (salários) e `financeiro/summary` respondem a qualquer usuário autenticado do tenant com o módulo (ex.: papel "cozinha").
- Controllers com lógica de domínio e DbContext direto: dívida arquitetural, sem testes unitários.

### Próximos passos
1. Proratear o pessoal pelo período.
2. Usar timezone da loja.
3. Políticas de papel no backend.
4. Unificar despesas com o financeiro do core.
5. Receita a partir de `Sale`, incluindo delivery.

---

## Delivery (hub de pedidos, numeração, zonas, cupons, pedido manual)
**Objetivo:** inbox multicanal, aceite → cozinha → entrega, taxa por bairro e cupons.

**Estado:** 🟠 PARCIAL   **Maturidade:** 40%

**Justificativa:**
- O ciclo operacional (receber → aceitar → cozinha → entregue) funciona e tem testes.
- O ciclo financeiro não existe (🚨2).
- Totais ignoram modificadores.
- Não há validação de quantidade.
- Pedidos órfãos.
- Cupom de primeiro pedido nunca funciona.
- Zero testes de cupom e zona.

### Backend existente
- `DeliveryOrdersController` (`api/restaurante/delivery-orders`): `GET`, `GET {id}`, `POST` (genérico/"integrações"), `POST manual`, `POST {id}/accept|reject|rider|cancel`, `PATCH {id}/status`.
- `DeliveryOrderService` (769 linhas).
- Entidades `RestDeliveryOrder`, `RestDeliveryOrderItem`, `RestDeliveryOrderItemModifier`, `DeliveryZone`, `Coupon`, `CouponUsage`.
- Numeração por loja com índice único e retry (`SaveOrderWithRetryAsync`; `RestDeliveryOrderConfiguration.cs:97-99`).
- `DeliveryZonesController` (`GET`, `PUT` upsert) e `CouponsController` (`GET/POST/PUT/DELETE`).
- Migrations `Phase_B_RestDeliveryOrder`, `Phase_B_OrderNumber_UniqueIndex`, `AddDeliveryZonesAndCoupons`.

### Frontend existente
- `/restaurante/delivery` (`DeliveryPage.tsx`, `DeliveryKanban.tsx`, `DeliveryCard.tsx`, `ManualOrderSheet.tsx`, `useDeliveryMutations.ts`).
- Zonas e cupons em `/restaurante/portal` (`PortalSetupPage.tsx`).

### Funcionalidades concluídas
- Inbox com filtros, aceite (cria `RestOrder` e copia itens/modificadores), rejeição, cancelamento.
- Status OutForDelivery/Delivered.
- Sincronização de InPreparation/Ready/Cancelled a partir da cozinha.
- Testes `AcceptDeliveryOrder_CreatesRestOrder`, `FullStatusFlow_DeliveryOrder_Takeaway`, `PortalOrder_AppearsInDeliveryHub`.
- Pedido manual com preço vindo do catálogo (canal restrito a PhoneCall/InPerson/WhatsApp/Other).
- Taxa de entrega resolvida no servidor pela zona (`DeliveryOrderService.cs:274-286`).

### Funcionalidades parciais
- Cupons: CRUD, UI e validação existem, mas há bugs (abaixo) e zero testes.
- Entregador: `POST {id}/rider` não tem consumidor na UI. O nome só pode ser passado no PATCH de status pela API.

### Funcionalidades não desenvolvidas
- Pagamento e fechamento financeiro do pedido de delivery (🚨2).
- Cancelamento parcial de item.
- Notificação ao cliente (WhatsApp).
- Pagamento online/Pix.

### Problemas
- **🚨2** — taxa de entrega e desconto de cupom não chegam ao `RestOrder` (`AcceptAsync`, `DeliveryOrderService.cs:410-419`). Nada vira `Sale`.
- **Total do pedido ignora modificadores:**
  - `RestDeliveryOrderItem.LineTotal => UnitPriceSnapshot * Quantity` (`RestDeliveryOrderItem.cs:23`).
  - O portal mostra ao cliente um total **com** modificadores (`portal/components/CartSheet.tsx:161-166`).
  - O hub mostra ao operador o `order.total` **sem** eles (`DeliveryCard.tsx:180`).
  - Cupom percentual e `MinOrderAmount` são calculados sobre bases diferentes no `validate` (cliente) e na criação (servidor).
- **Sem validação de quantidade:**
  - `RestDeliveryOrderItem.Create` aceita `quantity` ≤ 0 ou negativa.
  - Pelo `POST /api/public/orders` anônimo é possível criar pedido com total negativo.
  - Ao aceitar, `RestOrderItem.Create` lança exceção **depois** que o `RestOrder` vazio já foi salvo, o que deixa um `RestOrder` órfão em Open.
- **Pedidos órfãos:**
  - `CreateFromPortalAsync` e `CreateManualAsync` persistem o cabeçalho (`SaveOrderWithRetryAsync`) **antes** de validar produtos, modificadores e cupom (`DeliveryOrderService.cs:203,305-369`).
  - Produto inválido, modificador inválido ou cupom recusado gera um pedido `Received` sem itens no inbox do operador.
- **Cupom "primeiro pedido" nunca funciona na criação:**
  - `CountOrdersByPhonePublicAsync` roda depois que o próprio pedido foi salvo. Ele conta o pedido atual (status Received), então `isFirstOrder` é sempre `false` e o pedido falha.
  - O pedido órfão ainda fica no inbox.
  - O endpoint `validate` diz que o cupom é válido.
- **`AcceptAsync` não é transacional nem tem lock:** usa três `SaveChanges` separados. Dois operadores aceitando ao mesmo tempo criam dois `RestOrder` para o mesmo pedido.
- **Corrida em `MaxUses` do cupom:** `IncrementUsedCount` não tem concorrência otimista.
- **`POST /api/restaurante/delivery-orders` genérico:** aceita `UnitPrice`, `ProductName` e `ProductId` do cliente sem validação. É endpoint autenticado, sem consumidor no frontend, chamado de "iFood webhooks", mas não é um webhook.
- **Doc desatualizado:** `ORKEN_MENU_ARCHITECTURE.md` §9 diz "Taxa de entrega fixa em 0", mas hoje existem zonas.

### Dependências
- `OrderService`, `PublicOrdersController`, `FoodServiceSettings`, catálogo do core.

### Próximos passos
1. Fluxo de pagamento de delivery levando taxa e desconto para a `Sale`.
2. Incluir modificadores no `LineTotal`.
3. Validar quantidade > 0 (com limite).
4. Validar tudo antes de persistir e usar transação única em create/accept.
5. Corrigir a contagem de primeiro pedido.
6. Escrever testes de cupom e zona.

---

## Portal público de pedidos
**Objetivo:** cardápio público por slug, carrinho, pedido anônimo e rastreio.

**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 45%

**Justificativa:** o fluxo ponta a ponta funciona e tem 14 testes E2E. O hardening de segurança e de regra está ausente:
- expõe todos os produtos (🚨3);
- sem rate limit;
- sem checagem de módulo/loja ativa;
- aceita quantidades negativas;
- totais divergentes.

### Backend existente
- `PublicOrdersController` (`[AllowAnonymous]`):
  - `GET /api/public/menu/{slug}`, `GET /api/public/orders/{trackingToken}`, `POST /api/public/orders`;
  - `GET /api/public/delivery-zones/{slug}`, `POST /api/public/coupons/validate`.
- `PublicMenuService` e `DeliveryOrderService.CreateFromPortalAsync` usam `IgnoreQueryFilters` mais filtro explícito por `StoreId`/`TenantId` (`ProductRepository.cs:20-35`, `DeliveryZoneRepository.cs:18-23`, `CouponRepository.cs:21-38`).

### Frontend existente
- `/:slug` (`portal/pages/PortalMenuPage.tsx`, `ProductSheet.tsx`, `ModifierPicker.tsx`, `CartSheet.tsx` com CEP via ViaCEP, zona e cupom).
- `/rastrear/:token` (`PortalTrackingPage.tsx`).
- `portal/api/portal.api.ts` usa `fetch` direto.

### Funcionalidades concluídas
- **Isolamento por slug:**
  - todas as consultas públicas filtram pela loja resolvida do slug;
  - preços vêm sempre do catálogo;
  - a taxa vem da zona.
- **Rastreio mínimo:** o token tem 32 hex (`Guid.NewGuid().ToString("N")`). O DTO de rastreio expõe só número, status, label, ETA e tipo, sem PII. Teste `Tracking_Returns_StatusLabel_WithoutSensitiveFields`.
- Flags de aceitar pedidos, delivery e retirada são aplicadas.
- Testes de produto invisível oculto e slug inválido 404.

### Funcionalidades parciais
- O `POST` devolve o `DeliveryOrderDto` completo (endereço, telefone, `RestOrderId`). Aceitável, porque são dados do próprio cliente.

### Funcionalidades não desenvolvidas
- Pagamento online.
- Histórico do cliente.
- Bloqueio por horário de funcionamento.

### Problemas
- **🚨3:**
  - todo produto ativo, inclusive insumos, aparece no portal;
  - não há como ocultar;
  - lojas sem configuração de restaurante (inclusive lojas Service com `PublicSlug`) viram "restaurantes" públicos que aceitam pedidos.
- **Sem rate limiting** em `POST /api/public/orders` e `POST /api/public/coupons/validate`. O portal Service tem `[EnableRateLimiting("public-booking")]` (`PublicServiceController.cs:68`); o do restaurante não. Isso permite:
  - spam de pedidos no inbox;
  - enumeração de códigos de cupom.
- **Sem checagem** de `Store.Status`, assinatura do módulo `restaurante` ou status do tenant. Um tenant cancelado ou suspenso continua recebendo pedidos.
- Quantidade negativa ou zero, e pedidos órfãos (ver Delivery).
- **N+1 no cardápio:** uma query de modificadores por produto (`PublicMenuService.cs`, laço `foreach (var p in prods)`).
- **Frontend:** zero testes para `modules/portal`.

### Próximos passos
1. Controle de visibilidade e exclusão de `IsIngredient`.
2. Exigir `FoodServiceSettings` mais módulo ativo mais loja ativa (como `PublicServicePortalService.ResolveAsync`).
3. Rate limit por IP.
4. Validação de quantidade.
5. Eliminar o N+1.

---

## Integrações (iFood, Rappi, AnotaAí)
**Objetivo:** receber pedidos de marketplaces.

**Estado:** ⚪ SCAFFOLD   **Maturidade:** 5%

**Justificativa:** só existem os valores no enum `DeliveryChannel` (`Domain/Modules/Restaurante/DeliveryChannel.cs`), os campos `ExternalOrderId`/`ExternalEventType`/`RawPayload`/`ExternalProductId` e o `POST /api/restaurante/delivery-orders` genérico autenticado. Não há receiver de webhook, OAuth, mapeamento de catálogo nem polling. As buscas por "ifood" só acham o enum (as demais ocorrências são `IFoodServiceSettingsRepository`). O roadmap confirma: `ORKEN_MENU_ARCHITECTURE.md` §10 lista "Webhook iFood … falta o receiver".

### Próximos passos
- Definir o receiver com verificação de assinatura, mapeamento `ExternalProductId` → `Product` e idempotência por `ExternalOrderId`.

---

## Comparação com documentos de intenção
**`docs/ORKEN_MENU_ARCHITECTURE.md`:**
- O "Roadmap › Concluído" diz "Sincronização bidirecional RestOrder ↔ DeliveryOrder". O código é **unidirecional**, e o próprio §9 diz unidirecional.
- O fluxo de delivery termina em `Delivered`, sem pagamento. O documento não descreve como o delivery vira venda: é a lacuna 🚨2.
- §9 "Taxa de entrega fixa em 0" está desatualizado (zonas implementadas).
- §5.8 descreve `pay` com `{ paymentMethod, amount }`. O real é `{ payments[], partySize }`.

**`docs/releases/2026-05-06-cmv-financeiro-fase3.md`:** o que foi entregue confere com o código (entidades, endpoints, 7 testes, `FinanceiroPage`). As decisões "pessoal sem filtro de período" e CMV com custo atual estão implementadas como descritas, mas produzem números incorretos (ver Financeiro).

---

## Backend sem frontend
- `POST/PUT /api/restaurante/modifier-groups`, `POST/PUT/DELETE .../modifiers/{modId}` (CRUD de modificadores).
- `PUT /api/restaurante/settings` (couvert, taxa de serviço, `OrderTypesEnabled`).
- `PATCH /api/restaurante/tables/{id}/status`, `GET /api/restaurante/tables/{id}/orders`, `GET /api/restaurante/tables/by-area/{areaId}`.
- `DELETE /api/restaurante/orders/{id}/items/{itemId}` (cancelar item).
- `POST /api/restaurante/delivery-orders` (genérico/"integrações") e `POST /api/restaurante/delivery-orders/{id}/rider`.
- `Product.SetMenuVisibility`: método de domínio sem endpoint.
- Hub: `LeaveStore` nunca é chamado.

## Frontend sem backend/fluxo real
- Nenhum mock ou array fixo de dados encontrado em `modules/restaurante` e `modules/portal`. Todas as chamadas vão à API real.
- **Fluxos interrompidos:**
  - comanda Balcão/Retirada/Delivery sem tela para reabrir, fechar ou pagar;
  - comanda `Closed` inacessível pela mesa (`useActiveOrder`);
  - KDS exibe `item.modifiers`, mas o endpoint nunca os envia.

## Código legado/morto
- `FoodServiceSettings.OrderTypesEnabled` e `StoreType`: persistidos, nunca lidos por regra.
- `RestOrder.Subtotal`, "kept for backward compatibility" (`RestOrder.cs`).
- `RestOrderType.Delivery` com comentário "(v2)". Hoje é usado por `AcceptAsync`, mas sem fluxo de pagamento.
- `RestStepsEditor`/`print.ts`: em uso, não são mortos.
- `nexo-main/CLAUDE.md` ainda diz "Currently services use mock data". Está desatualizado e não reflete o código.

## Achados de segurança
| # | Severidade | Achado | Evidência |
|---|---|---|---|
| S1 | **Alta** | Exposição pública e possibilidade de pedido de todo produto ativo da loja (insumos inclusive). Sem controle de visibilidade. Qualquer loja com `PublicSlug`, inclusive de outro módulo (Service), vira cardápio público aceitando pedidos. | `Product.cs:62,99`; `ProductRepository.cs:28-35`; `PublicMenuService.GetMenuAsync`; `DeliveryOrderService.cs:257-272` |
| S2 | **Média** | Endpoints públicos de pedido e cupom sem rate limiting: spam de pedidos no inbox e força bruta de códigos de cupom. | `PublicOrdersController.cs` (sem `[EnableRateLimiting]`) vs `PublicServiceController.cs:68` |
| S3 | **Média** | Pedido anônimo com quantidade negativa/zero: total negativo, item quebrado e `RestOrder` órfão no aceite. | `RestDeliveryOrderItem.Create` sem validação; `RestDeliveryOrderItem.cs:23` |
| S4 | **Média** | Portal não verifica assinatura do módulo, status da loja nem do tenant. Tenant suspenso ou cancelado continua recebendo pedidos públicos. | `StoreEntityRepository.GetByPublicSlugAsync` (carrega `ModuleSubscription`, ninguém usa) |
| S5 | **Média** | Sem autorização por papel no backend do restaurante. Qualquer usuário do tenant com o módulo (ex.: cozinha/vendedor) lê salários (`GET /api/restaurante/employees`) e P&L, altera taxa de serviço, cria cupons e cancela pedidos. A restrição existe só na UI (`RoleRoute`). | `Controllers/Modules/Restaurante/*` com apenas `[Authorize]` + `[RequireModule]` |
| S6 | Baixa | `POST /api/restaurante/delivery-orders` aceita preço, nome e `ProductId` arbitrários (sem validar tenant). Abuso interno. | `DeliveryOrderService.CreateAsync` (`:112-163`) |
| S7 | Baixa | Hub SignalR não exige módulo `restaurante`. O escopo de grupo é correto (tenant+loja validados por claims), sem vazamento cross-tenant. | `RestaurantHub.JoinStore` |
| S8 | Baixa | `CreateGroupAsync` aceita `ProductId` de qualquer tenant. As leituras filtram por tenant, então sem impacto prático. | `ModifierGroupService.cs:25-37` |

**Integridade financeira (não é segurança, mas é severidade alta):**
- 🚨1: modificador pago bloqueia pagamento.
- 🚨2: delivery fora de Venda/Caixa/Estoque.
- Venda confirmada sem `CashMovement` quando o operador não tem caixa aberto.

## Testes existentes para esta área
Integração, com Testcontainers (`nexo-backend/tests/Nexo.IntegrationTests/Restaurante/`):

| Arquivo | Testes | O que cobre |
|---|---|---|
| `RestauranteFlowTests.cs` | 24 | Mesa/comanda, close/pay, idempotência, ficha técnica, couvert/taxa, concorrência, isolamento por loja |
| `DeliveryPortalFlowTests.cs` | 14 | Cardápio público, pedido do portal, aceite, KDS, fluxo takeaway, rastreio sem PII, flags |
| `EmployeeExpenseTests.cs` | 5 | Funcionários, despesas, summary |
| `FinanceiroReportTests.cs` | 2 | `cmv-report`, summary vazio |
| `RecipeCardCmvTests.cs` | 2 | CMV com gás/mão de obra, filtro `IsIngredient` |

- **Total:** cerca de 47 testes de integração.
- **Unitários:** nenhum para o restaurante em `Nexo.UnitTests`.
- **Frontend:** nenhum teste em `modules/restaurante` ou `modules/portal`.

**Lacunas de teste mais relevantes:**
- modificador com preço > 0 no pagamento;
- cupons (todas as regras);
- zonas de entrega;
- quantidade inválida no portal;
- SignalR (join/escopo/reconexão);
- relatórios com períodos parciais;
- autorização por papel;
- tenant ou módulo inativo no portal.
