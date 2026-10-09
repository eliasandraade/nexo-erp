# Auditoria 03 — Núcleo comercial / Varejo, Financeiro, Caixa, Relatórios e Dashboard

Commit auditado: `e8b4873` (branch `feature/orken-service-closure-prC-resume`). Auditoria somente leitura.
Caminhos relativos a `nexo-backend/src` (BE) e `nexo-main/src` (FE).

---

## 🚨 Achados críticos (topo)

### 🚨 1. Ajuste manual de estoque soma quando deveria subtrair (corrupção de estoque)
- FE `modules/inventory/components/InventoryAdjustmentForm.tsx:34-41` obriga `qty > 0` e envia `{ quantity: qty, movementType }` para qualquer tipo, inclusive `ManualExit` ("Saída manual") e `Loss` ("Perda / vencimento") (`modules/inventory/types/index.ts:112-117`).
- BE `StockService.AdjustAsync` (`Nexo.Application/Features/Stock/StockService.cs`) faz `stockItem.ApplyMovement(request.Quantity)` sem olhar o tipo. O contrato do DTO diz "positive = entry, negative = exit" (`StockDtos.cs:7`).
- **Cenário:** o estoquista registra "Perda" de 5 unidades em `/estoque/ajustes`. O saldo **sobe** 5 e o movimento fica gravado como `Loss` com `QuantityAfter > QuantityBefore`. Movimentos são imutáveis (`TenantSaveChangesInterceptor.EnforceImmutableEntities`), então a correção exige outro lançamento. Pela UI não existe forma de reduzir estoque manualmente. "Ajuste de inventário" também é delta, não contagem absoluta.

### 🚨 2. Itens de venda sem validação: quantidade, preço e desconto livres (manipulação financeira e de estoque)
- `SaleItem.Create` (`Nexo.Domain/Entities/SaleItem.cs:23-45`) não valida quantidade > 0, preço ≥ 0 nem desconto. `SaleService.AddItemAsync` usa `request.UnitPrice` do cliente sem comparar com `Product.SalePrice` nem com a lista de preço (`SaleService.cs:93-101`).
- `ConfirmAsync` aceita `DiscountAmount`, `TaxAmount` e `SurchargesAmount` arbitrários (`SaleService.cs:143`). Não há validators FluentValidation para Sales, Cash, Stock ou Financial (só existem para Auth, Users, Settings, Interpreter, Build e Service).
- **Cenário:** qualquer usuário autenticado com o módulo varejo (inclusive o papel `vendedor`) faz `POST /api/sales/{id}/items` com `quantity: -3` de um produto caro, junto de um item normal. No confirm, a checagem `AvailableQuantity < -3` passa, o estoque **aumenta** 3 (`ApplyMovement(-(-3))`) e o total da venda cai. O resultado é uma "devolução" fantasma sem autorização, que reduz o faturamento e o valor recebido no caixa.

### 🚨 3. Endpoints financeiros, de caixa e de estoque sem autorização por papel
- `FinancialController`, `CashController`, `StockController`, `ReportsController` e `DashboardController` têm só `[Authorize]`. Não têm `[RequireModule]` nem `Roles`. `SalesController` só tem `[RequireModule("varejo")]`. Os únicos `Roles=` estão em `SettingsController` e `UsersController`.
- **Cenário:** um usuário `vendedor` (ou `cozinha`) chama `POST /api/financial/transactions/{id}/pay` ou `/cancel` para quitar ou cancelar um recebível. Também pode chamar `POST /api/cash/sessions/{id}/close` para fechar o caixa de outro operador com qualquer saldo, `POST /api/sales/{id}/cancel` sem gerente, ou `POST /api/stock/adjust`. O isolamento é por tenant/loja, então não há vazamento cross-tenant, mas não existe segregação de funções. O fluxo `verify-manager` (`AuthController.cs:385`) não é exigido em nenhuma operação sensível do backend.

> Não encontrei acesso cross-tenant nesta área: os filtros globais em `NexoDbContext.ApplyTenantQueryFilters` mais o `TenantSaveChangesInterceptor` cobrem leitura e escrita. O único ponto fraco é a validação de FKs (ver "Achados de segurança").

---

## Produtos e Categorias (SKU, código de barras, imagem, ficha)
**Objetivo:** cadastro de produtos e categorias com SKU, código de barras, preço de custo e venda, controle de estoque e estoque mínimo.
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 65% — o CRUD é real de ponta a ponta, mas excluir categoria quebra, o código de barras não é único e não há variações nem histórico de preço de venda.

### Backend existente
- `ProductsController` (`api/products`): `GET`, `GET paged`, `GET {id}`, `POST`, `PUT {id}`, `PATCH {id}/prices`, `POST {id}/activate|deactivate`, `PATCH {id}/image`, `GET {id}/sheet.pdf`.
- `ProductService`: valida código único (`CodeExistsAsync`) e cria o `StockItem` quando `TrackStock` (`ProductService.cs:72-73`).
- `Product : StoreEntity`, com índice único `(TenantId, StoreId, Code)`. O índice de `Barcode` **não é único** (`ProductConfiguration.cs:100-101`).
- `CategoriesController` (`api/categories`): GET, POST, PUT, activate, deactivate. **Não existe DELETE.** `Category : TenantEntity`.
- `ProductPurchasePricesController` (`api/products/{id}/purchase-prices`): histórico manual dos últimos 5 preços de compra (`ProductPurchasePriceService`).

### Frontend existente
- `/produtos`, `/produtos/novo`, `/produtos/:id` (`ProdutosPage`, `ProductFormPage`), com `api/products.api.ts` real.
- `ManageCategoriesDialog` (criar, editar e excluir).
- Lookup de código de barras via `services/integrations.api.ts` → `GET /api/integrations/barcode/{code}` (`ProductMainDataSection.tsx:63`).
- `IngredientPriceSection`, que consome `purchase-prices`.
- `services/productService.ts` está vazio e deprecated (sem mock ativo).

### Funcionalidades concluídas
- CRUD, ativação e desativação de produtos, com paginação e busca por nome, código ou barcode (`ProductRepository.cs:70`).
- Preços (`PATCH prices`), imagem por URL e ficha em PDF.
- Criação automática do `StockItem` para produtos com controle de estoque.

### Funcionalidades parciais
- Categorias: criar, editar e ativar funcionam. O botão excluir chama `DELETE /api/categories/{id}` (`products.api.ts:118-119`), que não existe no backend (vai dar 405).
- Histórico de preço de compra: é manual e não é alimentado pela confirmação de compra (`PurchaseService` não grava `ProductPurchasePrice`).

### Funcionalidades não desenvolvidas
- Variações/grade, unidades de conversão, kits, código de barras único, histórico de preço de venda, importação em massa.
- Dados fiscais do produto (NCM, CFOP, CST).

### Problemas
- O barcode duplicado é permitido e `GetByBarcodeAsync` usa `FirstOrDefault`, então a busca no PDV fica ambígua.
- Category é por tenant, mas Product é por loja: categorias são compartilhadas entre lojas e produtos não. Pode ser intencional, mas não está documentado.

### Dependências
Estoque (`StockItem`), Integrações (OpenFoodFacts), Restaurante (`IsIngredient`, `IsMenuVisible`).

### Próximos passos
1. Criar DELETE de categoria (soft) ou trocar o botão por desativar.
2. Índice único parcial para Barcode.
3. Alimentar `ProductPurchasePrice` a partir da confirmação de compras.

---

## Estoque (StockItem, movimentos, ajustes, transferências, alertas)
**Objetivo:** saldo por produto e loja, trilha imutável de movimentos, ajustes, transferências e alertas de mínimo.
**Estado:** 🟠 PARCIAL   **Maturidade:** 35% — a leitura de saldo, a trilha e os alertas são reais, mas o único fluxo de escrita manual corrompe dados (🚨1) e não existe transferência.

### Backend existente
- `StockController` (`api/stock`): `GET`, `GET paged`, `GET product/{id}`, `GET product/{id}/movements`, `POST adjust`.
- `StockItem : StoreEntity`, com `RowVersion` (xmin) como token de concorrência e `ReservedQuantity`. `StockMovement : StoreEntity`, imutável pelo interceptor.
- O enum `StockMovementType` inclui `Transfer`, marcado como "(futuro)" (`StockMovementType.cs:11`).

### Frontend existente
- `/estoque` (`EstoquePage`, KPIs e tabela), `/estoque/movimentacoes` (`MovimentacoesPage`, por produto), `/estoque/ajustes` (`AjustesPage` mais `InventoryAdjustmentForm`). Todos com API real (`api/stock.api.ts`).

### Funcionalidades concluídas
- Consulta de saldo e histórico por produto.
- Baixa automática na venda (`SaleOutput`), entrada na compra (`PurchaseEntry`) e estorno no cancelamento de venda (`ReturnEntry`).
- Alertas zero/baixo no Dashboard (`DashboardService.GetStockAlertsAsync`, linhas 191-225).

### Funcionalidades parciais
- Ajuste manual: entrada funciona; saída, perda e inventário estão invertidos (🚨1).
- No backend, `MovementType` é livre: o cliente pode gravar `SaleOutput` ou `PurchaseEntry` manualmente sem referência. Não há validação de sinal × tipo.
- Reserva: os métodos `Reserve`/`Unreserve` existem, mas não há chamador no fluxo de varejo.

### Funcionalidades não desenvolvidas
- Transferência entre lojas ou locais, inventário por contagem absoluta, lotes e validade, custo médio, estoque negativo bloqueado no ajuste.

### Problemas
- `ApplyMovement` permite saldo negativo no ajuste.
- `PurchaseService.ConfirmAsync` cria `StockItem` até para produto com `TrackStock=false` (`PurchaseService.cs:130-135`).
- Compra (`RetPurchase : TenantEntity`) é por tenant e o estoque é por loja: a entrada cai na loja ativa no momento do confirm.

### Dependências
Produtos, Vendas, Compras, Restaurante (ficha técnica `RecipeOutput`).

### Próximos passos
1. Corrigir o sinal (backend derivar a direção do tipo e rejeitar inconsistência).
2. Whitelist de tipos ajustáveis no backend.
3. Inventário por contagem.
4. Transferência.

---

## Clientes e Fornecedores
**Objetivo:** cadastro de PF/PJ com contato, endereço, documento, limite de crédito e dados comerciais.
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 65% — o CRUD é real com lookup de CEP e CNPJ, mas o cadastro não se conecta ao resto: limite de crédito ignorado, sem extrato, sem vínculo com contas a pagar ou receber.

### Backend existente
- `CustomersController` (`api/customers`) e `SuppliersController` (`api/suppliers`): GET, paged, by id, POST, PUT, activate, deactivate.
- `Customer`/`Supplier : TenantEntity`, com índice único `(TenantId, DocumentNumber)`.

### Frontend existente
- `/clientes`, `/clientes/novo`, `/clientes/:id`, `/fornecedores`, `/fornecedores/novo`, `/fornecedores/:id`, com `api/*.api.ts` reais.
- Lookup de CEP e CNPJ via `services/integrations.api.ts`.

### Funcionalidades concluídas
- CRUD completo, ativação, paginação e filtros.

### Funcionalidades parciais
- `CreditLimit` é persistido, mas não é usado em nenhuma regra (grep só encontra Customer, DTOs, Config e Repo). A venda a prazo não checa limite.
- Lista de preço por cliente: a entidade `RetCustomerPriceList` existe, mas não há endpoint para vincular (ver Listas de Preço).

### Funcionalidades não desenvolvidas
- Histórico de compras do cliente, contas a receber por cliente, contas a pagar por fornecedor, fidelidade.

### Problemas
- `SaleService.CreateAsync` não valida `CustomerId`.

### Dependências
Integrações (BrasilAPI e ViaCEP), Vendas, Compras.

### Próximos passos
1. Aplicar `CreditLimit` na venda a prazo.
2. Aba de títulos financeiros no cliente e no fornecedor.

---

## Vendas (SaleService: confirmação, pagamentos, a prazo, cancelamento)
**Objetivo:** venda com máquina de estados Draft → Confirmed → Paid / Cancelled, baixa de estoque, caixa e recebível.
**Estado:** 🟡 FUNCIONAL / INCOMPLETO (núcleo) e 🔴 NÃO FUNCIONAL (cancelamento pela UI)   **Maturidade:** 50% — o confirm atômico é sólido, mas o cancelamento é inviável na prática, o recebível nunca liquida a venda e os itens não são validados.

### Backend existente
- `SalesController` (`api/sales`, `[RequireModule("varejo")]`): `GET`, `GET paged`, `GET {id}`, `POST`, `POST {id}/items`, `POST {id}/confirm`, `POST {id}/cancel`, `GET {id}/receipt.pdf`.
- `SaleService.ConfirmAsync` (`SaleService.cs:125-286`) roda numa transação:
  1. valida soma dos pagamentos = total;
  2. baixa estoque com concorrência otimista;
  3. cria um `SalePayment` por pagamento;
  4. pagamento `Cash` gera `CashMovement(SaleReceipt)` se houver sessão vinculada;
  5. pagamento `Credit` gera `FinancialTransaction(Receivable)` na conta padrão;
  6. tudo à vista vai direto para `Paid`.
- `CancelAsync` (`:293-369`) estorna estoque (`ReturnEntry`), lança um `CashMovement` **do tipo `Withdrawal`** como estorno e cancela os recebíveis Pending/Overdue.
- `Sale.Cancel()` **proíbe cancelar venda `Paid`** ("Create a return sale instead", `Sale.cs:120-121`). Não existe venda de devolução.
- Numeração `MAX(Number)+1` por loja (`SaleRepository.cs:102-104`), com índice único `(TenantId, StoreId, Number)`.

### Frontend existente
- `/vendas` (`VendasPage`, paginada real), `/vendas/:id` (`VendaDetailPage`, `getSale` mais `saleToLegacy`), `SaleCancellationDialog`, recibo em PDF.

### Funcionalidades concluídas
- Criar, adicionar item e confirmar à vista, com baixa de estoque e lançamento no caixa (teste `SaleFlowTests.FullCashSale_DeductsStock_CreatesCashMovement_StatusIsPaid`).
- Validação de estoque insuficiente e de soma de pagamentos (testes `ConfirmSale_*`).
- Venda a prazo pela API gera recebível (`SaleWithCreditPayment_StatusIsConfirmed_NotPaid`).
- Listagem, detalhe e recibo em PDF.

### Funcionalidades parciais
- **Cancelamento pela UI: 🔴.**
  - `SaleCancellationDialog.tsx:61` chama `userService.validateManagerAuthorization(...)`, que é um stub e **sempre** retorna `{ success:false, error:"Use validateManagerAuthorizationAsync." }` (`modules/users/services/userService.ts:73-79`). O diálogo nunca chega a `cancelSale`.
  - Mesmo que chegasse: toda venda do PDV é `Paid` (pagamentos sempre `type:"Cash"`, `use-pos-sale.ts:16-23`), e o backend rejeita cancelar `Paid`.
  - O cancelamento via API só funciona para Draft ou Confirmed (a prazo). O teste `CancelConfirmedSale_RestoresStock` cobre só esse caso.
  - Motivo e autorizador digitados na UI não são enviados: `mutationFn: (_payload) => cancelSale(id!)`, `VendaDetailPage.tsx:47`.
- Mapeamento de status no FE: Draft, Confirmed e Paid viram todos `"completed"` (`utils/saleAdapter.ts:41-42`). O botão cancelar aparece para vendas que o backend recusa. O tipo do pagamento (a prazo) é descartado e o método `Credit` aparece como "card".
- Cancelamento por item: `SaleItemsTable` aceita `onCancelItem`, mas `VendaDetailPage` não passa a prop. Não existe endpoint.
- Recebível quitado não move a venda para `Paid`: `FinancialService.MarkPaidAsync` não toca a venda e `Sale.MarkPaid()` só é chamado em `SaleService.cs:266`. A venda a prazo fica `Confirmed` para sempre e continua cancelável.
- `CancelAsync` cancela só recebíveis Pending/Overdue. Se o recebível já foi pago, a venda é cancelada, o estoque volta e o recebimento pago fica órfão, sem estorno financeiro.

### Funcionalidades não desenvolvidas
- **Devolução/troca** (só existe `StockMovementType.ReturnEntry`).
- Venda de devolução, cancelamento parcial, orçamento ou pedido, comissão de vendedor no varejo.
  - O FE invalida query keys `commissions-overall` e `commission-records` (`VendaDetailPage.tsx:52-54`) que não pertencem a nenhum fluxo real do varejo; são resquício.

### Problemas
- 🚨2 (itens e descontos sem validação, preço vindo do cliente).
- Cancelamento sem autorização de gerente no backend (qualquer usuário do módulo).
- O estorno de caixa é lançado como `Withdrawal` (sangria), o que mistura relatório de sangria com estorno. Também é lançado na sessão original **mesmo se ela já estiver fechada** (sem checagem `IsOpen`).
- `ConfirmAsync` não valida se a `CashSessionId` (vinda do request em `CreateAsync`) está aberta, nem se pertence à loja ou ao usuário.
- `catch (DbUpdateConcurrencyException)` não chama `RollbackAsync` explicitamente; depende do dispose do scope.
- Numeração `MAX+1` sob concorrência gera violação de índice único (erro em vez de retry).
- `GET /api/sales` (sem paginação) carrega tudo.
- Testes: 6 integrações. Nenhum cobre cancelamento de venda `Paid`, recebível pago seguido de cancelamento, ou quantidade negativa.

### Dependências
Estoque, Caixa, Financeiro, Produtos, Restaurante (`OrderService.PayAsync` reutiliza `ConfirmAsync`).

### Próximos passos
1. Validações de domínio no `SaleItem` e no confirm.
2. Preço resolvido no servidor.
3. Cancelamento ou devolução para venda `Paid` com autorização de gerente no backend (`verify-manager` token) e motivo persistido.
4. Trocar o stub do diálogo por `validateManagerAuthorizationAsync`.
5. Liquidar a venda quando todos os recebíveis forem pagos.
6. Estornar recebível pago no cancelamento.

---

## PDV (Varejo)
**Objetivo:** frente de caixa rápida: busca, carrinho, desconto, pagamento, troco, recibo.
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 45% — o caminho feliz (um pagamento de valor exato) funciona com backend real; troco, venda a prazo, pagamento múltiplo e preço por lista não funcionam.

### Backend existente
- `PdvController` (`api/varejo/pdv/resolve-price`): resolve o preço pela prioridade lista do cliente → lista padrão → `SalePrice`.
- Para criar, adicionar e confirmar, o PDV usa `api/sales`.

### Frontend existente
- `/pdv` (`PdvPage`, `PosProductSearch`, `PosCartTable`, `PosPaymentPanel`, `PosSaleSuccessModal`), `hooks/use-pos-sale.ts`.
- `use-pos-sale.ts` faz três passos HTTP **não atômicos**: `POST /sales`, depois `POST items` N vezes, depois `POST confirm`.

### Funcionalidades concluídas
- Venda à vista com valor exato em dinheiro, PIX ou cartão, com baixa de estoque e lançamento no caixa.
- Exige sessão de caixa aberta (`PdvPage.tsx:49-51,90`).

### Funcionalidades parciais
- **Troco quebra a venda.** `PosPaymentPanel.tsx:41-50` envia `amount: parsedAmount` (ex.: R$ 50 para total de R$ 45). O backend exige soma = total (`SaleService.cs:146-149`) e responde 400. O rascunho e os itens já criados ficam órfãos em `Draft`, e esses rascunhos entram nos KPIs (ver Dashboard).
- **Desconto percentual** gera `discountAmount` com dízima em JS (ex.: 4,6666…). O valor auto-preenchido é `total.toFixed(2)`, então a igualdade falha → 400.
- **PIX e cartão** são enviados como `type:"Cash"` (`use-pos-sale.ts:18-20`) e viram `CashMovement(SaleReceipt)` na gaveta física. Isso infla o saldo esperado do caixa (ver Caixa).
- Um único pagamento por venda (sem split). Sem venda a prazo no PDV. Sem seleção de cliente.
- O preço vem de `product.price` no cliente (`usePosCart.ts:37`). `resolve-price` **não tem consumidor** no FE.
- `useOpenSession` → `GET /api/cash/sessions/open` devolve **qualquer** sessão aberta da loja (`CashRepository.GetOpenSessionAsync`), não a do operador. A venda é vinculada explicitamente a essa sessão.

### Funcionalidades não desenvolvidas
- NFC-e/cupom fiscal, sangria pelo PDV, atalhos de teclado completos, modo offline, leitor de balança.

### Problemas
- Fluxo não atômico com rascunhos órfãos.
- Sem cancelamento de item antes de confirmar no backend: `RemoveItem` não existe em Sales.

### Dependências
Vendas, Caixa, Produtos, Listas de Preço.

### Próximos passos
1. Endpoint único "checkout" atômico.
2. Separar valor recebido de valor pago (troco no backend).
3. Arredondamento do desconto.
4. `PaymentMethod` distinto de dinheiro físico no caixa.
5. Usar `resolve-price`.
6. Split de pagamento e venda a prazo.

---

## Listas de Preço (Varejo)
**Objetivo:** preços diferenciados por lista, lista padrão e lista por cliente.
**Estado:** 🟠 PARCIAL   **Maturidade:** 20% — existe só backend; não há UI e o vínculo cliente↔lista não pode ser criado.

### Backend existente
- `PriceListsController` (`api/varejo/price-lists`): GET, GET {id}, POST, PUT, `set-default`, `PUT {id}/products`, `DELETE {id}/products/{productId}`.
- `PriceListService.ResolvePriceAsync`.
- Entidades `RetPriceList`, `RetPriceListItem`, `RetCustomerPriceList`.

### Frontend existente
Nenhum (grep por `price-lists` e `resolve-price` em `nexo-main/src` não encontra nada).

### Funcionalidades concluídas
Nenhuma ponta a ponta.

### Funcionalidades parciais
- CRUD de lista e itens só via API.
- `RetCustomerPriceList` é lida em `PriceListRepository.cs:29`, mas **nenhum código cria** esse vínculo. A prioridade "lista do cliente" é inalcançável.

### Funcionalidades não desenvolvidas
UI, vigência, regras por quantidade, promoções.

### Problemas
Sem testes.

### Dependências
PDV, Clientes, Produtos.

### Próximos passos
1. Endpoint de vínculo cliente↔lista.
2. UI.
3. PDV usar `resolve-price`.

---

## Compras (PurchaseService)
**Objetivo:** entrada de mercadoria por fornecedor, atualizando estoque e custo, e gerando contas a pagar.
**Estado:** 🟠 PARCIAL   **Maturidade:** 25% — o backend tem fluxo coerente de estoque e custo, mas não há UI, não gera contas a pagar e não há testes.

### Backend existente
- `PurchasesController` (`api/varejo/purchases`, `[RequireModule("varejo")]`): GET, GET {id}, POST, `POST {id}/items`, `DELETE {id}/items/{itemId}`, `POST {id}/confirm`, `POST {id}/cancel`.
- `PurchaseService.ConfirmAsync` (`PurchaseService.cs:113-167`) roda numa transação: `PurchaseEntry` mais `product.UpdatePrices(costPrice: item.UnitCost)` (último custo).
- `CancelAsync` estorna o estoque com `ManualExit`.
- `RetPurchaseItem` valida quantidade > 0 e custo ≥ 0.

### Frontend existente
Nenhum (sem rota e sem chamada a `varejo/purchases`).

### Funcionalidades concluídas
Nenhuma ponta a ponta.

### Funcionalidades parciais
- Recebimento em estoque e custo via API.
- `SupplierName`, `ProductName` e `ProductCode` retornam `string.Empty` (`PurchaseService.cs:229,242-243`).

### Funcionalidades não desenvolvidas
- **Contas a pagar** (nenhuma `FinancialTransaction(Payable)` é gerada).
- Custo médio, recebimento parcial, importação de XML de NF-e, pedido de compra.

### Problemas
- O cancelamento não reverte o `CostPrice`.
- O estorno usa `ManualExit` em vez de um tipo próprio.
- O estorno não usa a concorrência de `StockItem` (sem catch específico) e permite estoque negativo.
- `TenantEntity` × estoque `StoreEntity` (ver Estoque).

### Dependências
Fornecedores, Estoque, Produtos, Financeiro.

### Próximos passos
1. UI.
2. Gerar payable no confirm e cancelar o payable no cancel.
3. Popular nomes.
4. Testes.

---

## Caixa (CashController)
**Objetivo:** turnos de caixa com abertura, sangria, suprimento, recebimentos, fechamento com conferência e divergência.
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 45% — abrir, movimentar e fechar funcionam de verdade; a conferência é só no cliente, está errada para PIX/cartão e não é persistida; não há histórico nem controle de papel.

### Backend existente
- `CashController` (`api/cash`): `GET sessions`, `GET sessions/open`, `GET sessions/{id}`, `POST sessions/open`, `POST sessions/{id}/close`, `POST sessions/{id}/movements`, `GET sessions/{id}/close-report.pdf`.
- `CashService`: uma sessão aberta por usuário (`OpenAsync`).
- `CashSession.Close(closedBy, closingBalance)` grava só o saldo informado. **Não calcula esperado nem divergência** (`CashSession.cs:42-52`).
- `CashMovement` é imutável. Tipos: Opening, SaleReceipt, Withdrawal, Deposit, Closing.

### Frontend existente
- `/caixa` (`CaixaPage`, `CashOpenModal`, `CashMovementModal` com Sangria/Suprimento, `CashCloseModal`, `CashKpiCards`, `CashMovementsTable`), API real.

### Funcionalidades concluídas
- Abertura com saldo inicial, sangria e suprimento, fechamento com saldo informado.
- Lista de movimentos da sessão aberta.
- PDF de fechamento.
- Teste: `SaleFlowTests.OpenCashSession_SecondOpenByAdmin_ReturnsConflict`. Isolamento por loja: `StoreIsolationTests.CashSession_OpenedInStoreB_IsNotVisibleFromStoreA`.

### Funcionalidades parciais
- **Saldo esperado e divergência** são calculados só no FE (`modules/cash/types/index.ts:79-92`, `CashCloseModal.tsx:37`):
  - soma `SaleReceipt` de PIX e cartão como dinheiro de gaveta, então a divergência é falsa;
  - o valor esperado e a divergência **não são enviados nem persistidos**;
  - o PDF de fechamento (`CashCloseReportDocument.cs`) também não mostra esperado nem divergência.
- O estorno de venda entra como `Withdrawal` e é subtraído como sangria (coerente para dinheiro, errado para PIX/cartão).
- Histórico de sessões: `useAllSessions` (`hooks/use-cash.ts:33`) existe, mas **não é usado** por nenhuma página. O PDF só é baixável enquanto a sessão está aberta (`CaixaPage.tsx:33-35` depende de `openSession`).
- Venda do restaurante só gera `CashMovement` se o garçom tiver sessão própria aberta (`SaleService.cs:195-200`). Sem sessão, o dinheiro fica sem rastro de caixa.

### Funcionalidades não desenvolvidas
- Fechamento cego, conferência por forma de pagamento, aprovação de divergência por gerente, transferência do saldo de fechamento para Caixa/Banco no financeiro, reabertura.

### Problemas
- O comentário "uma sessão por tenant" (`CashSession.cs:6`) contradiz o código (uma por usuário).
- O índice `ix_cash_sessions_store_user_status` **não é único**: duas aberturas simultâneas passam.
- Qualquer usuário fecha a sessão de qualquer outro.
- `AddMovementAsync` aceita qualquer tipo (inclusive `SaleReceipt`, `Opening` e `Closing` manuais com `ReferenceType` livre).
- Valor 0 é aceito; saldo inicial negativo é aceito.
- `Enum.Parse` com valor inválido vira 500 (`ArgumentException` não é mapeada).

### Dependências
Vendas, PDV, Restaurante, Financeiro (inexistente).

### Próximos passos
1. Calcular esperado por forma de pagamento no backend e persistir a divergência.
2. Restringir tipos manuais a Withdrawal/Deposit.
3. Fechar exige dono ou gerente.
4. Índice único parcial `WHERE status = Open`.
5. Tela de histórico.

---

## Financeiro (FinancialController / FinancialService) — análise de domínio
**Objetivo:** plano de contas, contas a pagar e a receber, liquidação, estorno, fluxo de caixa, conciliação, recorrência, competência × caixa, integração com os módulos.
**Estado:** 🟠 PARCIAL   **Maturidade:** 20% — é um CRUD de títulos sem UI, com invariantes fracas e sem nenhum dos conceitos de gestão financeira (saldo, fluxo, conciliação, recorrência, competência).

### Backend existente
- `FinancialController` (`api/financial`, só `[Authorize]`):
  - contas: `GET accounts`, `GET accounts/{id}`, `POST accounts`, `PUT accounts/{id}`, `POST accounts/{id}/activate|deactivate`;
  - títulos: `GET transactions/pending`, `GET transactions/{id}`, `GET accounts/{id}/transactions`, `POST transactions`, `PUT transactions/{id}`, `POST transactions/{id}/pay`, `POST transactions/{id}/cancel`.
- `FinancialAccount : TenantEntity` (plano simples Code, Name, Type, Parent; índice único `(TenantId, Code)`). `FinancialAccountType { Cash, Bank, Receivable, Payable }`.
- `FinancialTransaction : TenantEntity` (Receivable/Payable; Pending, Paid, Overdue, Cancelled; `ReferenceType`/`ReferenceId`).
- **Contas padrão por tenant:** `DefaultFinancialAccountProvisioner` cria 1.1 Caixa, 1.2 Banco, 2.1 Contas a Receber e 3.1 Contas a Pagar. É chamado em `RegistrationService` e `PlatformController`, com backfill na migration `20260820094836_BackfillDefaultFinancialAccounts`. Teste: `DefaultAccountProvisioningTests` (1).
- `GetDefaultByTypeAsync` pega a primeira conta ativa do tipo, ordenada por `Code`.

### Frontend existente
- **Nenhuma tela de títulos ou contas.** O grep por `/financial` em `nexo-main/src` não encontra nada. As únicas telas "Financeiro" são:
  - `/restaurante/financeiro` → `api/restaurante/financeiro/*` (modelo próprio: `RestExpenses`, `RestEmployees`, CMV);
  - aba Financeiro do Build → `FinancialMovement` (Interpreter).
- Os lançamentos do Service aparecem apenas indiretamente, nas telas de Pagamentos e Comissões do Service.

### Funcionalidades concluídas
- Provisionamento de contas padrão (testado).
- Recebível automático da venda a prazo (`SaleService.cs:243-259`) e cancelamento dele quando a venda é cancelada.
- Espelhamento do Service (`ServiceFinancialPostingService`):
  - `SvcPayment` → Receivable já `Paid`;
  - void → **Payable `Paid`** de mesmo valor como contrapartida;
  - pagamento de comissão → Payable `Paid`;
  - o `FinancialService` bloqueia editar, pagar ou cancelar referências `Svc*` (`FinancialService.cs:114-115,142,153,167,175-180`).

### Funcionalidades parciais
- Títulos manuais via API (sem UI).
- **Invariantes fracas** em `FinancialTransaction`:
  - `MarkPaid` não verifica o status: paga título `Cancelled` e paga duas vezes;
  - `Cancel` não impede cancelar título `Paid` (o service só checa "já cancelado");
  - `Amount` não é validado (aceita negativo ou zero);
  - não há coerência entre tipo de conta e tipo de título (um Payable pode ir para a conta "Contas a Receber");
  - `ReferenceType` e `ReferenceId` são livres no POST: dá para criar título "Sale" falso, que depois é cancelado junto com a venda (`GetTransactionsBySaleAsync`).
- `MarkOverdue` **nunca é chamado** (nenhum job), então o status `Overdue` é inalcançável.
- Liquidação não registra conta de destino (Caixa/Banco), forma de pagamento, juros, multa, desconto nem pagamento parcial.

### Funcionalidades não desenvolvidas
- Saldo por conta, extrato, **fluxo de caixa** (realizado ou projetado), **conciliação bancária**, **recorrência**, **competência × caixa** (só existe `DueDate`/`PaidAt`), categorias de receita e despesa (DRE), centros de custo, parcelamento, anexos, baixa em lote, juros e multa.
- Integração com Compras (payable), com Restaurante (despesas e folha ficam em `RestExpenses`/`RestEmployees`, fora do `FinancialTransaction`) e com o caixa (fechamento não gera lançamento em "Caixa").
- Venda à vista não gera receita no financeiro: só existe no caixa.

### Problemas (arquitetura)
- **Quatro modelos financeiros paralelos sem consolidação:**
  - `FinancialTransaction` (core, Varejo a prazo e Service);
  - `FinancialMovement` (Interpreter/Build, com `FinancialContextType` Obra/Loja/Servico);
  - `RestExpense`/`RestEmployee` mais o resumo calculado do Restaurante;
  - `CashMovement` (caixa).
  
  Nenhum relatório soma tudo, e o "lucro" de cada módulo é ilha.
- O estorno do Service lançado como **Payable** inflaria "despesas" num futuro DRE. O correto seria um estorno de receita.
- A receita do Service fica na conta tipo "Contas a Receber", já paga, em vez de Caixa/Banco.
- Financeiro é `TenantEntity` (não por loja), enquanto Venda e Caixa são por loja: sem dimensão de loja nos títulos.
- Sem role nem módulo (🚨3).
- Testes: 1 de provisionamento. Os de Service (`ServicePaymentsTests`, `ServiceCommissionTests`) tocam lançamentos. **Nenhum teste de `FinancialController`/`FinancialService`.**

### Dependências
Vendas, Service, Compras (não ligado), Restaurante (não ligado), Caixa (não ligado).

### Próximos passos
1. Invariantes de domínio (máquina de estados de `FinancialTransaction`, validação de `Amount` e tipo de conta, `ReferenceType` reservado para "Sale").
2. Role Gerente/Diretoria.
3. UI mínima de contas a receber e a pagar.
4. Liquidação com conta de destino.
5. Job de vencidos.
6. Decidir o modelo financeiro único (ou camada de consolidação) antes de fluxo de caixa e DRE.

---

## Relatórios (core) — ReportsController
**Objetivo:** relatórios de vendas, estoque e clientes.
**Estado:** 🟠 PARCIAL (backend sem consumidor)   **Maturidade:** 15% — três endpoints simples sem UI.

### Backend existente
- `GET /api/reports/sales?from&to`, `GET /api/reports/inventory`, `GET /api/reports/customers` (`ReportsService`).

### Frontend existente
Nenhum (só `/restaurante/relatorios`, que usa `api/restaurante/reports/*`).

### Funcionalidades parciais
- O relatório de vendas filtra por `CreatedAt`, não por `ConfirmedAt`, e **inclui rascunhos** como faturamento (`Status != Cancelled`, `ReportsService.cs:32-34`).
- O intervalo é em UTC (não respeita o fuso BR).
- `DateOnly.Parse` inválido vira 500.

### Funcionalidades não desenvolvidas
Margem/lucro por produto (há `CostPrice` snapshot no `SaleItem`, não usado), vendas por vendedor, curva ABC, exportação.

### Próximos passos
1. Usar `ConfirmedAt` e excluir Draft.
2. Fuso do tenant.
3. Criar UI ou remover.

---

## Dashboard (core)
**Objetivo:** KPIs de vendas, top produtos, vendedores, série diária, alertas de estoque.
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 55% — os dados são reais e o cache é seguro por tenant e loja, mas a semântica dos KPIs está errada.

### Backend existente
- `GET /api/dashboard/summary` (`Nexo.Infrastructure/Dashboard/DashboardService.cs`), com cache de 30 s na chave `dashboard:summary:{tenant}:{store}`.

### Frontend existente
- `/dashboard` (`DashboardPage`, `KpiCards`, `SalesChart`, `TopProducts`, `SellerRanking`, `StockAlerts`, `RecentInsights` derivado do summary, `RestauranteBlocks`).

### Funcionalidades concluídas
Alertas de estoque zero/baixo, série de 30 dias, top 5 produtos e vendedores.

### Funcionalidades parciais
- KPIs são **de todo o histórico**, sem período (`GetSalesKpisAsync`, linhas 80-91), e **incluem `Draft`**. Os rascunhos órfãos do PDV inflam faturamento e contagem.
- A UI diz "venda(s) no período" (`KpiCards.tsx:70`), o que é enganoso.
- Sem seletor de período, sem margem, sem financeiro (a receber, a pagar).

### Próximos passos
1. Filtrar por `Confirmed`/`Paid` e `ConfirmedAt`.
2. Seletor de período.
3. Cards financeiros após o item Financeiro.

---

## Integrações: Barcode / CEP / CNPJ
**Objetivo:** autopreenchimento de produto por GTIN (OpenFoodFacts) e de endereço e empresa (ViaCEP, BrasilAPI).
**Estado:** 🟡 FUNCIONAL / INCOMPLETO   **Maturidade:** 75% — fluxo real com feature flags e testes unitários. A cobertura do OpenFoodFacts é limitada a alimentos.

### Backend existente
- `BarcodeController` (`GET /api/integrations/barcode/{barcode}`, 404 se `OpenFoodFactsEnabled=false`).
- `LookupController` (`GET /api/integrations/cep/{cep}`, `GET /api/integrations/cnpj/{cnpj}`).
- `CompositeCepLookupProvider` (fallback).

### Frontend existente
`services/integrations.api.ts`, consumido em `ProductMainDataSection` e nos formulários de cliente e fornecedor.

### Testes
- `BarcodeControllerTests` (8), `LookupControllerTests` (9), `CompositeCepLookupProviderTests` (6).

### Problemas
Sem rate limit próprio; depende de provedores externos.

---

## Devolução e Fiscal (NF-e / NFC-e)
**Estado:** ⚪ não desenvolvido   **Maturidade:** 0%

**Devolução:**
- Só existe o enum `StockMovementType.ReturnEntry`, usado no cancelamento, e o rótulo "Devolução" no FE.
- O domínio de `Sale` manda "create a return sale instead", mas isso não existe.

**Fiscal:**
- Só a flag `Integrations:FiscalEnabled=false` (`appsettings.json:85`, `IntegrationFeatureFlags.cs:17,32`) e `RetPurchase.InvoiceNumber` como texto livre.
- Sem emissor, certificado, NCM, CFOP ou CST no produto, XML ou SEFAZ.

---

## Integração com Restaurante (pontos financeiros)
- `OrderService.PayAsync` (`Nexo.Application/Modules/Restaurante/OrderService.cs:376-450`) reutiliza `SaleService.ConfirmAsync` dentro de `ExecuteInTransactionAsync`. O tx aninhado vira `NoOpTransactionScope` (`EfUnitOfWork.cs:27-28`), o que é correto.
- Inconsistência: o Restaurante aceita `paymentsTotal >= grandTotal` (bloqueia só `<`), mas `ConfirmAsync` exige igualdade. Pagamento com troco falha no confirm, a mesma falha do PDV.
- Despesas e folha do restaurante (`ExpensesController`, `EmployeesController`, `FinanceiroController`) não geram `FinancialTransaction`.

---

## Backend sem frontend
- `GET /api/varejo/pdv/resolve-price`
- `api/varejo/price-lists/*` (7 endpoints)
- `api/varejo/purchases/*` (7 endpoints)
- `api/financial/*` (13 endpoints): contas e títulos
- `api/reports/sales|inventory|customers`
- `GET /api/cash/sessions` (histórico; hook `useAllSessions` existe mas está sem uso)
- `GET /api/sales` (não paginado; o FE usa `paged`)
- `PATCH /api/products/{id}/prices`: confirmar uso; o form usa `PUT`.

## Frontend sem backend/fluxo real
- Excluir categoria (`deleteCategory` → `DELETE /api/categories/{id}`): não existe no backend.
- `SaleCancellationDialog`: o stub `validateManagerAuthorization` sempre falha, então o cancelamento é impossível pela UI.
- Cancelamento de item (`SaleItemsTable.onCancelItem`, `cancellationRecords`): sem endpoint e sem dado.
- Divergência de caixa: só visual, não persistida.
- Invalidações de query keys inexistentes no varejo (`commissions-*`, `insights-stats`, `dashboard-operational`, etc.).

## Código legado/morto
- `modules/products/services/productService.ts` e `modules/suppliers/services/supplierService.ts`: arquivos vazios (deprecated).
- `modules/inventory/types/index.ts:135-171`: tipos `@deprecated` "Used only by mockInventory.ts" (o mock não existe mais).
- `userService.validateManagerAuthorization` (stub síncrono) ainda referenciado pelo diálogo de cancelamento.
- `StockItem.Reserve/Unreserve` e `ReservedQuantity`: sem chamador no varejo.
- `FinancialTransaction.MarkOverdue`: sem chamador.
- `CashMovementType.Opening` e `Closing`: nenhum fluxo gera esses tipos.

## Achados de segurança
| # | Severidade | Achado |
|---|---|---|
| S1 | **Alta** | Sem autorização por papel em `api/financial`, `api/cash`, `api/stock`, `api/sales/{id}/cancel`, `api/reports` e `api/dashboard`; `api/financial`, `api/cash` e `api/stock` também sem `RequireModule`. Qualquer usuário do tenant e da loja (vendedor, cozinha, estoquista) liquida ou cancela títulos, fecha caixa alheio, ajusta estoque e cancela vendas. O `verify-manager` não é exigido no backend. |
| S2 | **Alta** | `SaleItem` aceita quantidade, preço e desconto negativos e preço livre do cliente. O confirm aceita desconto, imposto e acréscimo arbitrários. Isso permite manipulação de faturamento e entrada de estoque fantasma (🚨2). |
| S3 | **Alta (integridade)** | Ajuste de saída e perda incrementa estoque (🚨1). |
| S4 | Média | `FinancialTransaction` sem máquina de estados (pagar cancelado, cancelar pago, valor negativo, `ReferenceType="Sale"` forjável no POST, que acopla o título ao cancelamento de uma venda real). |
| S5 | Média | `CreateSaleRequest.CashSessionId` não é validado (loja, dono, aberta). Lançamentos de venda podem cair em sessão fechada ou de outro operador/loja do mesmo tenant. FKs de outro tenant não são checadas pelo interceptor (só `TenantId` da própria entidade); o risco é baixo porque exige GUID alheio e o filtro de leitura esconde. |
| S6 | Baixa | Índice de sessão de caixa aberta não é único (corrida). A numeração `MAX+1` de venda e compra gera erro 500 em concorrência. |
| S7 | Baixa | `Enum.Parse` e `DateOnly.Parse` sem validação viram 500 (`PaymentMethod`, `PaymentType`, `CashMovementType`, `StockMovementType`, `TransactionType`, datas de relatórios). |

Cross-tenant: os filtros globais (`NexoDbContext.cs:175-208`) e o interceptor estão ativos para todas as entidades desta área. Os testes `TenantIsolationTests` (6) e `StoreIsolationTests` (4) cobrem produto, usuários e caixa.

## Testes existentes para esta área
| Arquivo | Testes | Cobertura |
|---|---|---|
| `tests/Nexo.IntegrationTests/Sales/SaleFlowTests.cs` | 6 | à vista, a prazo, cancelamento de Confirmed, soma divergente, estoque insuficiente, 2ª abertura de caixa |
| `tests/Nexo.IntegrationTests/Stock/StockPagedTests.cs` | 2 | listagem paginada (ajuste só como setup, com quantidade positiva) |
| `tests/Nexo.IntegrationTests/Financial/DefaultAccountProvisioningTests.cs` | 1 | provisionamento das 4 contas |
| `tests/Nexo.IntegrationTests/Security/TenantIsolationTests.cs` | 6 | produto, usuários, verify-manager |
| `tests/Nexo.IntegrationTests/Security/StoreIsolationTests.cs` | 4 | caixa entre lojas e switch-store |
| `tests/Nexo.IntegrationTests/Restaurante/RestauranteFlowTests.cs` | 24 | pagamento de comanda via `SaleService` |
| `FinanceiroReportTests` | 2 | resumo do restaurante (modelo próprio) |
| `EmployeeExpenseTests` | 5 | resumo do restaurante (modelo próprio) |
| `tests/Nexo.IntegrationTests/Service/ServicePaymentsTests.cs` e `ServiceCommissionTests.cs` | — | lançamentos `Svc*` no `FinancialTransaction` |
| `tests/Nexo.UnitTests/Integrations/*` | 23 | Barcode 8, Lookup 9, CEP composite 6 |

Sem testes para: `FinancialController`/`FinancialService`, `CashService` (sangria, suprimento, fechamento), `PurchaseService`, `PriceListService`, `ReportsService`, `DashboardService`, ajuste de saída de estoque, quantidade negativa em venda, cancelamento de venda `Paid`.

**Frontend:** nenhum teste unitário para `products`, `inventory`, `sales`, `cash`, `customers`, `suppliers` ou `dashboard`. Playwright só tem `e2e/auth.e2e.spec.ts`.
