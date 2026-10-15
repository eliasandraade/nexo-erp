# ORKEN 1.0 — Progresso

> Registro contínuo da missão ORKEN 1.0, um PR por linha.
> - Linha de base: `docs/audits/ORKEN_PRODUCT_STATUS_2026-10.md` (~45% de maturidade comercial).
> - Ordem planejada: PRs 42 → 74. A ordem pode mudar quando aparecer uma dependência real.

| PR | Tema | Merge | Migration | Estado |
|---|---|---|---|---|
| #42 | RBAC backend completo | `8022737` | — | Em produção |
| #43 | PDV: integridade de pagamentos | `4724495` | `AddSalePaymentTenderedAndChange` (aditiva) | Em produção |
| #44 | Caixa 2.0 | `b6f55c6` | `AddCashSessionClosingDifference` (aditiva + backfill do histórico) | Em produção |
| #45 | Cancelamento, estorno e devolução | `093a482` | `AddSaleReturns` (aditiva) | Em produção |
| #46 | Estoque avançado | `c3f25e7` | `StockPerStoreCountsAndTransfers` (aditiva + troca de índice único por comum) | Em produção |
| #47 | Compras completas | `da3ba8b` | `PurchasesPerStoreOrderedAndCancellation` (store_id com backfill, índice por loja) | Em produção |
| #48 | Listas de preço | `024b1cd` | `PriceListValidityAndPriority` (aditiva + índice único parcial) | Em produção |
| #49 | Modelo financeiro unificado (livro) | `d6634a5` | `UnifiedFinancialLedger` (aditiva + classificação e backfill do histórico à vista) | Em produção |
| #50 | Contas a receber | `645fb56` | `LedgerCustomerAndSettlement` (aditiva + cliente do título + reexecução idempotente do backfill) | Em produção |
| #51 | Contas a pagar e recorrências | `0bc1844` | `PayablesSupplierAndRecurrences` (aditiva + fornecedor das compras) | Em produção |
| #52 | Fluxo de caixa | `fbdb126` | `AccountOpeningBalance` (aditiva) | Em produção |
| #53 | Integração financeira das verticais | `511f9c4` | `VerticalExpensesInLedger` (backfill: despesas do restaurante e movimentações confirmadas) | Em produção |
| #54 | Menu: pedido → venda | `9d950c5` | `RestOrderDeliveryTerms` (aditiva + termos cotados nas comandas abertas) | Em produção |
| #55 | Fechamento do Menu | — | — | Em revisão |


## Marcos por fase

Estimativas honestas de maturidade, na mesma régua da auditoria de linha de base.

**Fase 1 — Operação de venda (PRs 42–45)**

| Área | Antes | Depois | O que ainda falta |
|---|---|---|---|
| Auth / papéis | 50% | ~80% | recuperação de senha, sessões, MFA (PRs 63–64) |
| PDV | 40% | ~75% | limite de desconto por papel (regra comercial a decidir), atalhos avançados |
| Caixa | 45% | ~80% | conciliação com o financeiro (PR 52) |
| Vendas (cancelamento / devolução) | 50% | ~80% | documento fiscal da devolução (PR 66) |

**Fase 2 — Estoque, compras e preço (PRs 46–48)**

| Área | Antes | Depois | O que ainda falta |
|---|---|---|---|
| Estoque | 45% | ~75% | lotes e validade; custo médio ponderado (decisão contábil) |
| Compras | 25% | ~75% | recebimento parcial, cotação |
| Listas de preço | 20% | ~75% | grupos de clientes, preço por quantidade |
| Produtos | 65% | ~70% | `DELETE /categories/{id}` |

---

## PR 42 — RBAC backend completo

- **Objetivo:** autorização no backend em todas as áreas, sem depender da UI.
- **Entregue:**
  - `AppRoles` com papel por ação;
  - gate de módulo nos cupons e nas zonas de entrega;
  - `GET /users/{id}` e troca de senha só para o próprio usuário;
  - rate limit `credential-check` por usuário;
  - `RestaurantHub` restrito aos papéis do restaurante.
- **Testes:**
  - `RbacCoverageTests`, um guard estrutural: ação nova sem papel quebra o build;
  - `RbacMatrixTests`, 66 casos por papel.
- **Matriz:** [`docs/security/rbac-matrix.md`](../security/rbac-matrix.md).
- **Riscos restantes:** ~~`CostPrice` exposto em `GET /products`~~ (resolvido no PR 48); `DELETE /categories/{id}` não existe.
- **Maturidade:** Auth/Segurança e papéis 50% → ~80%.

## PR 43 — PDV: integridade de pagamentos

**Regras de dinheiro, válidas para o PDV e para o pagamento de comanda:**

| Pagamento | Gaveta (caixa físico) | Financeiro | Venda |
|---|---|---|---|
| Dinheiro à vista | `CashMovement` com o valor **líquido do troco** | — (o caixa é a fonte) | Paid |
| Pix, débito, crédito ou transferência à vista | **nunca** | receita **liquidada** (`Receivable` paga, `ReferenceType=Sale`) | Paid |
| A prazo | — | receita **pendente** com vencimento | Confirmed |

- **Troco:**
  - Só pagamento em dinheiro pode passar do total. Pix, cartão e transferência acima do total são recusados.
  - O troco precisa ser menor que o dinheiro recebido: dinheiro entregue e devolvido por inteiro é erro de digitação.
  - `SalePayment` guarda `Amount` (aplicado), `TenderedAmount` (recebido) e `ChangeAmount` (troco). O banco tem `CHECK change_amount >= 0`.
- **Caixa:**
  - Dinheiro exige caixa **aberto**. A venda usa a sessão em que foi aberta, e essa sessão precisa continuar aberta.
  - Se a venda não tem sessão, usa a do vendedor e, na falta dela, a da loja.
  - Nunca grava em caixa fechado.
- **Idempotência e concorrência:**
  - A venda é travada (`FOR UPDATE`) na confirmação.
  - Repetir a mesma confirmação devolve a venda sem gravar nada de novo. Uma confirmação diferente de uma venda já paga dá 409.
  - O pagamento de comanda também trava e confere o status, para não baixar a ficha técnica duas vezes.
- **Cancelamento:**
  - Estorna da gaveta só o dinheiro físico.
  - Cancela o que está pendente no financeiro.
  - Lança contrapartida (`Payable` paga, `SaleCancellation`) para a receita já liquidada. Nada é apagado.
- **Frontend:**
  - No PDV, Pix e cartão ficam travados no total, e o troco exibido vem do backend.
  - Na comanda, passa a ser possível pagar com troco em dinheiro. Antes, o troco aparecia, mas o pagamento era bloqueado.
- **Decisão:** sem caixa aberto, o salão não recebe em dinheiro.
  - O drawer da comanda avisa antes e mantém Pix e cartão liberados.
  - Antes, o dinheiro era aceito e simplesmente não entrava em gaveta nenhuma.
- **Valores em centavos:**
  - O domínio (`Sale.RecalculateTotals`) arredonda subtotal, desconto e total para 2 casas (arredondamento comercial) e recusa ajuste negativo.
  - Sem isso, 10% sobre 33,33 dava um total de 29,997, que nenhum Pix conseguia pagar.
- **Retry no PDV:** um checkout que falhou na confirmação reaproveita o mesmo rascunho. O backend responde com a venda já confirmada, em vez de criar uma segunda venda.
- **Cancelamento de vendas antigas:** estorna os movimentos reais de caixa da venda, inclusive o Pix e o cartão que entraram na gaveta antes deste PR. O dinheiro sai de um caixa aberto (o original ou o atual); dia fechado continua fechado.
- **Decisão provisória:** cartão é tratado como recebido na hora. O prazo e as taxas da adquirente entram com as contas financeiras, nos PRs 49–52.
- **Testes:**
  - `SalePaymentPlanTests`: 18 testes unitários.
  - `PdvPaymentIntegrityTests`: 13 testes de integração (troco, Pix/cartão fora da gaveta, misto, retry, concorrência, caixa fechado, desconto fracionado, cancelamento com contrapartida).

## PR 44 — Caixa 2.0

- **Divergência no domínio:**
  - `CashSession.Close` calcula o saldo **esperado**: abertura + vendas em dinheiro + suprimentos − sangrias − estornos de venda.
  - Guarda o **contado** e a **diferença** (contado − esperado: positiva é sobra, negativa é falta), mais a justificativa.
  - Esses valores ficam congelados no histórico.
  - A migration preenche esperado e diferença dos fechamentos antigos a partir dos próprios movimentos. Só preenche as colunas novas.
- **Movimento manual:**
  - Aceita só suprimento (`Deposit`) e sangria (`Withdrawal`), com motivo obrigatório e no máximo 2 casas decimais.
  - Antes dava para lançar `SaleReceipt`, `Opening` e `Closing` à mão, ou seja, forjar uma venda no caixa.
  - Sangria acima do saldo da gaveta é recusada.
  - Caixa fechado não aceita movimento nem um segundo fechamento.
- **Estorno de venda** tem tipo próprio (`SaleRefund`), separado da sangria nos relatórios.
- **Concorrência:**
  - Lock da sessão, com escopo de tenant, no fechamento, nos movimentos manuais e também quando uma venda ou um estorno grava na gaveta. Assim nada cai num caixa que está fechando.
  - Advisory lock por usuário na abertura: dois "abrir caixa" simultâneos geram um caixa só.
  - O estorno de venda não pode tirar da gaveta mais do que ela tem.
- **Histórico antigo:** antes do PR 43, Pix e cartão também eram gravados como `SaleReceipt`, com o meio na descrição. O backfill conta como dinheiro só os recebimentos que eram dinheiro de fato.
  - Um caixa que estava aberto durante o deploy pode mostrar esperado inflado até ser fechado. O efeito é pontual e vale só para essa sessão.
- **Resumo da sessão (backend):**
  - totais de vendas em dinheiro, suprimentos, sangrias e estornos;
  - saldo esperado ao vivo;
  - recebimentos por forma de pagamento (Pix e cartão aparecem, mas não entram na gaveta).
- **Auditoria:** abrir, movimentar e fechar geram registro. O fechamento com divergência entra como `warning`.
- **Tela `/caixa`:**
  - saldo esperado vindo do backend;
  - recebimentos por forma de pagamento;
  - histórico de fechamentos com esperado, contado e diferença;
  - a justificativa do fechamento é enviada (antes era descartada);
  - o layout no celular não rola mais na horizontal.
- **PDF de fechamento:** inclui os totais, o esperado, o contado, a diferença e os recebimentos por forma de pagamento.
- **Testes:**
  - `CashSessionTests`: 8 testes unitários.
  - `CashSessionFlowTests`: 12 testes de integração (turno completo, movimentos inválidos, sangria acima do saldo, abertura concorrente, estorno ≠ sangria, estorno acima do saldo, limite da justificativa).
  - Playwright: tela do caixa em 1440 e 390 px.

## PR 45 — Cancelamento, estorno e devolução

**Política de reversão.** Toda operação que produz efeito tem um caminho de volta, e nada é apagado:

| Situação | Caminho | Efeitos |
|---|---|---|
| Venda em rascunho | cancelar | nenhum |
| Venda confirmada com valores a prazo em aberto | **cancelar** (motivo obrigatório na UI) | o estoque volta; o dinheiro sai da gaveta (`SaleRefund`); os recebíveis pendentes são cancelados; contrapartida para o que já foi liquidado |
| Venda **paga** | **devolução** total ou parcial (`SaleReturn`) | o estoque volta só para o que a venda tirou; reembolso em dinheiro sai de uma gaveta aberta e com saldo (`SaleRefund`); Pix, cartão ou transferência viram contrapartida `Payable` liquidada (`ReferenceType=SaleReturn`) |

- **Valor da devolução:**
  - Cada linha devolvida vale a sua parte **proporcional** do que o cliente pagou, com desconto, imposto, couvert e taxa de serviço incluídos. É uma decisão de produto: quem devolve um item recebe de volta também a parte da taxa que pagou sobre ele.
  - O valor é calculado pela fatia **acumulada** de cada item, então as devoluções de um item somam exatamente a sua parte, sem deriva de centavos entre devoluções parciais.
  - A devolução que esvazia a venda reembolsa exatamente o que resta, e qualquer centavo de diferença vai para a maior linha.
  - Nunca passa do total.
  - **Valor zero:** a devolução de um brinde ou de um item com 100% de desconto é aceita. Ela move só o estoque, sem reembolso.
- **Receita na data da venda:** a receita líquida é abatida na data da venda, não na data da devolução, então dias passados do dashboard podem mudar. O regime de competência formal entra no financeiro (PRs 49 a 52).
- **Quantidade:** nunca passa do que ainda não foi devolvido.
- **Status:** `Paid` → `PartiallyReturned` → `Returned`. `SaleStatus` ganhou os dois valores (gravados como texto) e `Sale.ReturnedAmount`.
- **Ficha técnica (restaurante):** comida servida não volta ao estoque.
  - Cancelamento e devolução só repõem o que a própria venda tirou (`SaleOutput`).
  - Antes, o cancelamento devolvia ao estoque o produto pronto de itens que tinham saído pelos insumos.
- **Idempotência e concorrência:**
  - Cada abertura do diálogo gera uma chave nova, e um índice único `(tenant, idempotency_key)` impede dois reembolsos com a mesma chave.
  - A devolução acontece sob lock da venda.
  - O teste concorrente confirma que duas requisições com a mesma chave geram uma devolução só.
- **Cancelamento:**
  - Guarda o motivo e quem cancelou.
  - O diálogo deixou de usar o stub de "senha do gerente", que sempre falhava. A tela já é só da gestão, e o backend garante o papel.
  - Venda paga não é mais oferecida para cancelamento.
- **Auditoria:** `sale_cancelled` e `sale_returned` (severidade `warning`).
- **Receita líquida:**
  - O dashboard, o relatório de vendas, o ranking de vendedores e produtos e o relatório de clientes contam só vendas realizadas (confirmadas, pagas e parcialmente devolvidas), sempre por `Total − ReturnedAmount`.
  - Isso corrige um bug antigo: rascunhos de checkouts abandonados no PDV entravam como receita.
- **Tela `/vendas/:id`:**
  - "Registrar devolução", com quantidade por item, forma de reembolso, motivo e valor estimado;
  - histórico de devoluções;
  - motivo do cancelamento;
  - filtro por "devolvidas" na lista.
- **Testes:**
  - `SaleReturnDomainTests`: 8 testes unitários.
  - `SaleReturnTests`: 8 testes de integração (parcial e depois total com desconto, quantidade acima do disponível, idempotência concorrente, venda a prazo, gaveta sem saldo, cancelamento com motivo, arredondamento sem deriva, brinde de valor zero).
  - Playwright: diálogo de devolução.

## PR 46 — Estoque avançado

- **Bug de integridade corrigido (multi-loja):**
  - `Product ↔ StockItem` estava mapeado como **um para um**, com índice único em `stock_items.product_id`, embora o estoque seja por loja.
  - Na prática, o mesmo produto não podia ter estoque em duas lojas.
  - Pior: quando o EF rastreava duas linhas do mesmo produto, **apagava a da outra loja como órfã**. A transferência expôs o problema.
  - Agora o mapeamento é um-para-muitos e o índice único virou comum, o que só relaxa a restrição. Continua único o par (tenant, loja, produto).
- **Inventário (contagem física):**
  - Abrir registra o saldo do sistema por produto (até 2.000 produtos). Só pode haver **um inventário aberto por loja**.
  - Quando a contagem de um item é registrada, o saldo do sistema **naquele momento** é guardado junto.
  - Ao aplicar, entra no estoque atual, com lock, apenas a **diferença** (contado − sistema na hora da contagem).
    - Assim, **vendas feitas entre a contagem e a aplicação são preservadas**, não desfeitas.
    - Exemplo: sistema 10, contado 8 → falta 2. Se depois vender 1, o estoque fica 7.
  - A tela envia só os produtos digitados ali, então duas pessoas contando corredores diferentes não apagam o trabalho uma da outra.
  - Cada ajuste gera um movimento `Adjustment` que referencia o inventário. Itens não contados ficam como estão.
  - Inventário aplicado ou cancelado fica congelado, e tudo é auditado.
- **Transferência entre lojas:**
  - Sai da loja atual para outra loja **ativa** do tenant a que o usuário tem acesso.
  - **O catálogo é por loja**, então o produto é mapeado para o produto de **mesmo código** na loja de destino. Se o destino não tiver esse produto cadastrado (ativo, com controle de estoque), a transferência é recusada com a lista dos produtos que faltam.
  - É **atômica**: `TransferOut` no produto de origem e `TransferIn` no produto de destino, ou nenhum dos dois.
  - As linhas das duas lojas são travadas em ordem determinística (pela chave primária, a mesma ordem das atualizações de venda do EF), com advisory lock por loja.
  - A origem precisa ter saldo disponível. Transferências concorrentes nunca tiram mais do que existe.
  - O histórico mostra cada transferência como enviada ou recebida, sempre com o produto da própria loja.
- **Telas:**
  - `/estoque/inventario`: abrir, contar, salvar, aplicar, cancelar e histórico.
  - `/estoque/transferencias`: destino, quantidades por produto e histórico enviado/recebido.
  - `/estoque` deixa de se chamar "Ingredientes".
- **Testes:**
  - `StockOperationsTests`: 5 testes de integração (venda depois da contagem preservada, um inventário aberto por loja, transferência para o produto equivalente com histórico dos dois lados, destino sem o produto, loja inválida ou alheia, concorrência).
  - RBAC: 4 linhas novas.
  - Playwright: inventário e transferência.

## PR 47 — Compras completas

**Fluxo:** pedido (em elaboração) → **pedido enviado** ao fornecedor (itens congelados) → **recebimento** → (cancelamento).

- **Compras são Core:** saiu o gate do módulo varejo, porque o restaurante também compra insumos.
- **Compra por loja:**
  - `RetPurchase` virou `StoreEntity`, ligada à loja que recebe, porque o estoque é por loja.
  - A migration atribui cada compra existente à loja que de fato recebeu a mercadoria, pelos movimentos de estoque. Na falta deles, usa a loja dos produtos; sem nenhuma pista, a loja padrão do tenant.
  - A numeração passa a ser por loja, sob advisory lock.
- **Recebimento** (sob lock da compra, atômico):
  - Entra no estoque (`PurchaseEntry`, com o custo no snapshot), só para produtos com controle de estoque.
  - O custo do produto passa a ser o último custo de compra, regra que já existia. Custo médio ponderado é uma decisão contábil e fica como opção futura.
  - Cria a **conta a pagar** ao fornecedor: `Payable` com `ReferenceType=Purchase`. O vencimento é o informado no recebimento; sem data, usa recebimento + **prazo de pagamento do fornecedor**. A conta pode já nascer **paga**.
  - Guarda NF e data, e registra auditoria.
  - A data de recebimento é a do recebimento, não a da criação do rascunho; datas sem fuso são aceitas como UTC.
  - O mesmo produto não pode aparecer em duas linhas do pedido.
  - Adicionar ou remover item é feito sob o lock da compra, para nada entrar numa compra que está sendo recebida.
  - Um conflito concorrente de estoque (uma venda, um inventário) devolve "tente novamente" em vez de erro 500.
- **Cancelamento:**
  - Recebida: a mercadoria sai do estoque (`PurchaseReversal`), mas **nunca abaixo de zero**. Se parte já foi vendida, o cancelamento é recusado com uma orientação.
  - Conta pendente é cancelada. Conta já paga vira **valor a receber do fornecedor** (`Receivable` com `ReferenceType=PurchaseCancellation`).
  - O motivo fica guardado e há auditoria. Nada é apagado.
- **Bugs corrigidos no caminho:**
  - **Adicionar item à compra nunca funcionava:** dava `DbUpdateConcurrencyException` e erro 500, o mesmo bug de rastreamento do EF de outros agregados. Agora o item é marcado explicitamente como novo.
  - **Fornecedor e cliente com documento "Outro" davam 500:** a UI oferecia o tipo, mas o enum não tinha o valor. `DocumentType.Other` foi adicionado.
  - Tipos inválidos agora respondem 422, não 500.
- **Telas:**
  - `/compras`: lista e "Criar pedido".
  - `/compras/:id`: itens; enviar pedido; receber com NF, vencimento e "já paga"; cancelar com motivo.
  - Receber e cancelar ficam só para a gestão; o estoquista monta e envia o pedido.
- **Testes:**
  - `PurchaseFlowTests`: 10 testes de integração (vencimento contado do recebimento, produto duplicado, recebimento completo com prazo do fornecedor, compra paga cancelada vira a receber, compra pendente cancelada, estoque já vendido bloqueia o cancelamento, fornecedor inativo, remoção de item, tipo de documento).
  - Playwright: fluxo de compra.

## PR 48 — Listas de preço

- **Bug de integridade corrigido:**
  - A venda usava o **preço unitário enviado pelo cliente** (`AddSaleItemRequest.UnitPrice`), então qualquer chamada direta à API podia vender pelo preço que quisesse.
  - Agora **o servidor define o preço** de cada linha vinda da API e ignora o valor enviado.
  - Só o fechamento da comanda do restaurante, que é uma chamada interna com o preço já calculado (adicionais incluídos), passa o preço adiante.
- **Resolução (`IPriceResolver`):**
  - Candidatas: a lista do cliente e a lista padrão.
  - Só contam listas **ativas**, **dentro da vigência** e que tenham o produto.
  - Vence a de **maior prioridade**; no empate, a do cliente. Exemplo: uma promoção na lista padrão com prioridade maior vale até para quem tem tabela própria.
  - Sem candidata, vale o preço do produto.
- **Listas:**
  - vigência (`valid_from`, `valid_until`) e prioridade;
  - preço sempre em centavos;
  - ativar e desativar;
  - **vincular e desvincular cliente**, com no máximo uma lista por cliente (vincular a outra troca a anterior).
  - "Grupos de clientes" não existe como entidade no produto, então o vínculo é por cliente.
- **Bug corrigido:** adicionar um produto novo a uma lista sofria o mesmo problema de rastreamento do EF dos outros agregados. Agora o item é marcado explicitamente como novo.
- **Custo protegido:**
  - `GET /products` devolve `CostPrice = null` para quem não é da gestão nem do estoque (vendedor, cozinha).
  - A ficha PDF do produto, que mostra o custo, ficou restrita ao papel de estoque.
- **PDV:** ao adicionar um produto, busca o preço resolvido, o mesmo que o servidor vai cobrar. Se a busca falhar, usa o preço do catálogo.
- **Tela `/listas-de-preco`** (gestão, módulo varejo):
  - criar;
  - editar nome, vigência e prioridade;
  - tornar padrão;
  - ativar e desativar;
  - preços por produto;
  - clientes vinculados.
- **Testes:**
  - `PriceListTests`: 8 testes de integração (preço do servidor contra o do cliente, precedência cliente/padrão/prioridade, vigência expirada e futura, lista inativa, itens novos e atualizados com vínculo único de cliente, custo oculto ao vendedor).
  - Playwright: tela de listas.
- **Revisão independente (corrigido antes do merge):**
  - O custo ainda vazava por dois caminhos: as linhas da venda (`SaleItemDto.CostPrice`) e a ficha técnica do restaurante. Agora as linhas vêm sem custo para quem não é da gestão nem do estoque, e a ficha técnica ficou restrita ao estoque (nenhuma tela da cozinha usa a ficha).
  - Era possível ter duas listas padrão: uma padrão inativa mantinha a marca e podia ser reativada. Agora, ao definir uma padrão, todas as outras perdem a marca, inclusive as inativas. Entrou também um índice único parcial, `ux_ret_price_lists_one_default`, e a migration desmarca as duplicadas antes, mantendo a padrão mais recente.
  - PDV: se o preço mudar entre montar o carrinho e fechar a venda, a venda para antes de cobrar.
  - Vigência mostrada no dia local; prioridade inteira; vínculo de cliente valida que o cliente existe e troca a lista numa única gravação; contagem de itens correta na listagem; nomes de produtos de outras lojas aparecem.
  - +3 testes de integração (padrão única após reativar, cliente inexistente → 404, custo oculto na venda para o vendedor).

## PR 49 — Modelo financeiro unificado

- **Antes:** o financeiro só via parte do dinheiro.
  - Venda em dinheiro ia só para a gaveta, e antes do PR 43 nenhuma venda à vista chegava ao financeiro.
  - Estorno do Service era lançado como despesa.
  - Não havia loja, competência nem categoria.
  - Um título quitado podia ser cancelado e um cancelado podia ser pago.
- **Agora: `FinancialTransaction` é o livro único.**
  - Entram todas as formas de pagamento da venda, devoluções, cancelamentos, compras e Service.
  - A gaveta (`CashMovement`) continua sendo o controle físico do caixa, não o livro.
  - Campos novos:
    - `category`, a linha gerencial: receita, dedução de receita, compras, despesas operacionais, folha, comissões, impostos, despesas financeiras, outras entradas e saídas;
    - `competence_date`;
    - `store_id`;
    - `reverses_transaction_id`.
- **Dois regimes sobre os mesmos lançamentos** (decisão aprovada: caixa e competência):
  - **competência:** data do fato; tudo que não foi cancelado, inclusive em aberto;
  - **caixa:** data do pagamento; só o que foi quitado.
- **Regras contábeis aplicadas:**
  - Estorno de receita é **dedução de receita**, nunca despesa (corrige o estorno do Service).
  - Compra de mercadoria é **compras**: no regime de caixa é saída; no de competência não é despesa, porque o custo entra pelo CMV da venda (relatórios, PR 65).
- **Máquina de estados:**
  - valor maior que zero e em centavos;
  - só um lançamento em aberto pode ser quitado, cancelado ou editado;
  - quitado se corrige com estorno (`CreateReversal`), e o original continua visível.
- **Lançamentos dos módulos** (`Sale*`, `Purchase*`, `Svc*`):
  - não são editados nem cancelados no financeiro; corrigem-se na origem;
  - receber um título a prazo de venda continua permitido;
  - a referência não pode ser forjada num lançamento manual.
- **Lançamento manual:**
  - categoria coerente com a direção;
  - competência (padrão: vencimento);
  - loja opcional, validada.
- **API:** `GET /api/financial/ledger?regime=accrual|cash&from&to&storeId&category`.
  - Devolve totais do período por categoria, entradas, saídas e resultado.
  - Devolve até 1000 lançamentos, com a flag `truncated`.
- **Tela `/financeiro`** (gestão), a primeira tela do financeiro do núcleo:
  - competência e caixa;
  - mês;
  - totais por categoria;
  - lançamentos com origem e situação.
- **Migration `UnifiedFinancialLedger` (aditiva):**
  - classifica os lançamentos existentes;
  - preenche competência e loja a partir da origem;
  - liga estornos ao original;
  - traz para o livro o histórico à vista: um lançamento quitado por pagamento de venda não cancelada, sem duplicar o que já estava lá, mais as devoluções em dinheiro.
  - Os lançamentos do backfill reaproveitam o id do pagamento ou do movimento da gaveta, e o Down remove exatamente eles.
  - Validada num Postgres descartável com histórico simulado:
    - venda anterior ao PR 43;
    - venda mista;
    - venda a prazo;
    - venda cancelada;
    - devolução;
    - suprimento;
    - despesa manual;
    - Up e Down conferidos.
- **Testes:**
  - unitários `FinancialTransactionTests`: 9 casos (centavos, estados, estorno, categorias);
  - integração `FinancialLedgerTests`: 9 casos (dinheiro e devolução no livro, a prazo em competência × caixa, quitado imutável, lançamentos manuais inválidos, regime inválido);
  - Service: estorno vira dedução ligada ao original;
  - PDV: os testes atualizados refletem o dinheiro no livro;
  - Playwright: tela do financeiro nos dois regimes.
- **Revisão independente (corrigido antes do merge):**
  - **Backfill decidia por venda, não por pagamento.** Numa venda antiga paga com Pix + a prazo cujo título a prazo já foi quitado, o Pix nunca entraria no livro. Agora cada pagamento é reconhecido individualmente (mesma venda, valor e descrição exata). Revalidado no Postgres descartável com esse cenário.
  - **Corrida entre pagar e cancelar:** quitar, cancelar e editar rodam sob trava da linha (`FOR UPDATE`), e o cancelamento de venda ou de compra trava os mesmos lançamentos.
  - **Resultado por competência:** deixa de fora as compras de mercadoria, que vão para o estoque. O campo `result` está separado de `net`.
  - **Título de compra em aberto:** voltou a ser editável (renegociar vencimento ou valor).
  - **Contas padrão:** a única conta a receber ou a pagar ativa não pode ser desativada, porque vendas e compras dependem dela.
  - **Datas sem fuso:** normalizadas para UTC.
  - **Vínculo de estorno no histórico:** pareado um a um.
  - **Extrato da conta:** limitado aos 500 lançamentos mais recentes.
- **Decisão de comportamento:** pagar um lançamento já quitado agora responde 422 (antes era aceito em silêncio). Nenhum cliente atual faz isso.
- **Janela de deploy:** vendas em dinheiro confirmadas pelo container antigo durante a migration podem ficar fora do livro. A migration do PR 50 reexecuta o mesmo backfill, que é idempotente por pagamento.
- **Fica para os próximos PRs:**
  - recebimento parcial, juros e vencidos (PR 50);
  - contas a pagar com recorrência (PR 51);
  - fluxo de caixa e conciliação (PR 52);
  - despesas do restaurante, Build e folha no livro (PR 53).

## PR 50 — Contas a receber

- **Baixa total ou parcial** (`POST /api/financial/transactions/{id}/settle`), sob trava da linha:
  - o título é baixado pelo **valor recebido + desconto**;
  - uma baixa menor que o saldo **desmembra o título**: a parte baixada fica quitada e o restante continua em aberto, com o mesmo vencimento, competência, categoria, loja e cliente (`parent_transaction_id`);
  - os dois regimes continuam exatos: competência vê o título inteiro e caixa vê só o que entrou.
- **Juros e multa** viram um lançamento próprio de **receitas financeiras** (categoria nova `FinancialIncome`).
- **Desconto concedido** vira **despesa financeira**.
  - Num título a pagar (PR 51), a mesma operação é espelhada: juros pagos são despesa e desconto obtido é receita.
  - Esses ajustes (referência `Settlement`) não são editáveis à mão.
- **Parcelamento** (`POST /api/financial/transactions/installments`):
  - de 1 a 60 parcelas mensais;
  - os centavos são divididos por igual e a última parcela fica com a sobra, então as parcelas somam exatamente o total;
  - cliente e loja validados;
  - categoria coerente com a direção.
- **Cliente no título** (`customer_id`), vindo da venda (inclusive estorno e devolução) e do pagamento do Service. O lançamento manual também aceita cliente.
- **Vencidos:**
  - a listagem mostra como vencido o título em aberto com vencimento antes de hoje (dia do Brasil, `BusinessClock`);
  - o job `OverdueTitlesJob` (o primeiro `BackgroundService` do produto) grava `Overdue` de hora em hora, com um UPDATE idempotente;
  - um título renegociado para uma data futura volta a "em aberto".
- **`GET /api/financial/receivables`** (e `/payables`):
  - filtros: em aberto, vencidos, recebidos ou todos; período de vencimento; cliente;
  - **aging** do que está em aberto: a vencer, 1–30, 31–60 e mais de 60 dias.
- **Tela `/financeiro/receber`:**
  - aging;
  - abas;
  - **Receber** (valor, data, juros, multa, desconto, prévia do que fica em aberto);
  - **Novo recebimento** parcelado.
- **Cancelar uma venda a prazo parcialmente recebida:** o restante em aberto é cancelado e o que entrou é estornado como dedução de receita, ligada ao original e ao cliente.
- **Migration:**
  - aditiva;
  - preenche o cliente a partir da origem;
  - **reexecuta o backfill à vista do PR 49**, idempotente por pagamento, para cobrir vendas em dinheiro confirmadas pelo container antigo durante aquela migration;
  - validada num Postgres descartável: a venda da janela entrou, a venda já registrada não duplicou e o cliente foi preenchido.
- **Testes:**
  - +4 unitários (desmembramento, limite da baixa, categorias dos ajustes, renegociação);
  - `ReceivablesTests`, 5 de integração: baixa parcial nos dois regimes, parcelas e centavos, juros e desconto, aging com job de vencidos e renegociação, cancelamento de venda a prazo parcialmente recebida, parcelamentos inválidos;
  - Playwright: tela de contas a receber.
- **Revisão independente (corrigido antes do merge):**
  - **Desconto contava como dinheiro no regime de caixa.** Uma baixa de 90 + 10 de desconto registrava 100 recebidos. Agora a parte abatida pelo desconto tem status próprio, **`Abated`** (abatido): conta em competência e nunca em caixa. O lançamento do desconto também fica abatido. Receitas recebidas no caixa passam a ser só o dinheiro.
  - **Cancelar a origem depois de uma baixa com desconto deixava saldo.** O estorno devolvia o valor cheio, e o caixa ficava negativo pelo desconto. Agora estorno e devolução cobrem só o dinheiro que entrou, e a parte abatida não é estornada. O teste de integração confere que caixa e competência fecham em zero depois do cancelamento.
  - **Juros recebidos continuam como receita financeira quando a venda é cancelada.** Eles compensaram o atraso; devolvê-los é decisão comercial.
  - **Datas em fuso do Brasil:**
    - um título renegociado para hoje volta a "em aberto" pelo dia do Brasil;
    - uma data sem hora vira meio-dia no Brasil, para nunca cair na véspera;
    - os filtros `dueFrom`/`dueTo` usam dias do Brasil, com `dueTo` inclusivo.
  - **Frontend:** "1.000" é lido como mil reais, e o parcelamento valida R$ 0,01 por parcela.
  - **Abas "Recebidos" e "Todos":** mostram só títulos de verdade. Ficam de fora pagamentos à vista do PDV, Service, estornos, devoluções e ajustes de juros ou desconto.
  - **Job de vencidos:** índice parcial `(due_date) WHERE status = 'Pending'`.
  - **Parcelamento:** valida que a conta está ativa e é do mesmo tipo.
  - **Cancelamento:** trava os lançamentos da origem duas vezes, para pegar o restante criado por uma baixa concorrente.
- **Fica para depois:** estornar uma baixa feita por engano, reabrindo o título.
- **Decisão pendente (regra comercial e contábil, não implementada):** baixar um título como **perda** (calote) e cobrar juros ou multa **automaticamente** por percentual. Hoje juros e multa são informados na baixa.

## PR 51 — Contas a pagar e recorrências

- **Fornecedor no título** (`supplier_id`):
  - vem da compra (título a pagar e o "a receber do fornecedor" do cancelamento);
  - pode ser informado no lançamento manual e no parcelamento;
  - a migration preenche o fornecedor dos títulos de compra existentes.
- **`GET /api/financial/payables`:** filtro por fornecedor, nome do fornecedor e aging.
  - A baixa de um título a pagar espelha a de receber: juros e multa pagos são **despesa financeira**; desconto obtido é **receita financeira**, abatida (só em competência).
- **Despesas recorrentes** (`FinancialRecurrence`, tabela `financial_recurrences`):
  - descrição, valor, categoria, dia do vencimento (o último dia quando o mês é mais curto), início, fim opcional e fornecedor;
  - geram **um título por mês até o fim do mês seguinte**;
  - cada título gerado é comum: paga-se, renegocia-se ou cancela-se sozinho;
  - encerrar a recorrência só para os próximos meses.
  - **Geração sem job que grave atravessando tenants:** acontece ao criar ou alterar a recorrência e antes de toda leitura do financeiro do tenant (contas a pagar e a receber, livro). Roda dentro da requisição do próprio tenant, sob trava das recorrências, de forma idempotente (`generated_through`). Quando não há nada pendente, o caminho é rápido: sem transação e sem trava.
  - Endpoints: `GET/POST /api/financial/recurrences`, `PUT /api/financial/recurrences/{id}` e `POST /api/financial/recurrences/{id}/deactivate`.
- **Tela `/financeiro/pagar`:**
  - mesma visão de títulos de contas a receber (agora um componente só, `TitlesView`, parametrizado pela direção);
  - categoria de despesa no lançamento;
  - seção **Despesas recorrentes**: criar e encerrar.
- **Testes:**
  - +4 unitários de recorrência (meses pendentes sem repetir, fim e encerramento, dia 31 em mês curto, validação);
  - `PayablesTests`, 3 de integração: fornecedor e baixa com juros e desconto; recorrência gera 3 meses uma vez só, título gerado cancelável, encerramento; recorrências inválidas;
  - `PurchaseFlowTests` confere o fornecedor e a categoria do título da compra;
  - Playwright: tela de contas a pagar.
- **Revisão independente (corrigido antes do merge):**
  - **Recorrência sem limite de início.** Um início omitido (0001-01) ou errado gerava milhares de títulos vencidos. Agora o início é obrigatório, vai de 12 meses atrás a 24 meses à frente, e cada geração cria no máximo 24 meses.
  - **Recorrência com problema derrubava as leituras.**
    - Uma descrição longa demais estourava a coluna ao gerar o título, e um tenant sem conta padrão também falhava. Depois disso, toda leitura do financeiro do tenant quebrava, e a recorrência ficava salva.
    - Agora a descrição vai até 480 caracteres e a conta padrão é validada antes.
    - Criar ou alterar uma recorrência e gerar seus títulos formam uma transação só.
    - Uma falha de geração nunca bloqueia leitura: ela vai para o log e a leitura mostra o que existe.
  - **Aging:** passa a respeitar o filtro de fornecedor.
  - **Nome do fornecedor:** um nome fantasia vazio mostra a razão social.
  - **Recorrência encerrada:** não se altera.
  - **Fim da recorrência:** não pode ficar antes dos meses já gerados.
  - **Recorrência a pagar só aceita fornecedor** (e a de receber, só cliente).
  - **Migration:** também preenche o fornecedor dos ajustes de juros e desconto dos títulos de compra.
  - **Tela:**
    - encerrar pede confirmação;
    - o mês de início é validado (navegadores sem seletor de mês);
    - mostra "desde MM/aaaa";
    - criar atualiza o livro.
- **Fica para depois:** editar recorrência pela tela (o endpoint existe) e reaplicar uma mudança de valor nos títulos já gerados e ainda em aberto.

## PR 52 — Fluxo de caixa

- **`GET /api/financial/cash-flow?from&to&granularity=day|week|month`** (dias do Brasil, `to` inclusivo, até 366 dias):
  - **realizado:** entradas e saídas quitadas, pela data do pagamento;
  - **previsto:** títulos em aberto, pelo vencimento, de hoje em diante;
  - **saldo corrido** período a período e saldo final;
  - semanas de segunda a domingo, meses do calendário, períodos recortados no intervalo.
- **Vencidos e não pagos ficam à parte** (`overdueIn` / `overdueOut`). Podem ou não acontecer, então não entram no saldo projetado.
- **Saldo inicial** (`PUT /api/financial/accounts/{id}/opening-balance`), só em contas de caixa e banco:
  - o valor que havia na data, antes dos lançamentos do sistema (pode ser negativo);
  - o saldo do fluxo parte da soma dos saldos iniciais mais tudo que foi realizado desde a data mais antiga;
  - sem saldo inicial, parte de zero no primeiro lançamento, e a tela avisa.
- **Recorrências:** são geradas antes do cálculo, e uma falha não impede a leitura.
- **Tela `/financeiro/fluxo`:**
  - próximos 30 dias, mês atual ou próximos 90 dias (por semana);
  - saldo no início e no fim, entradas, saídas e vencidos à parte;
  - gráfico de barras e linha de saldo, carregado sob demanda;
  - tabela por período;
  - diálogo de saldo inicial.
- **Testes:**
  - `CashFlowTests`, 4 de integração: projeção no dia do vencimento e saldo corrido fechando no saldo final; vencidos à parte e fora da projeção; buckets de semana e mês; validações;
  - Playwright: tela do fluxo.
- **Revisão independente (corrigido antes do merge):**
  - **Recorrências além do mês seguinte ficavam fora da projeção.** Em 90 dias, aluguel e folha de dezembro e janeiro sumiam e o saldo final ficava alto demais. Agora os meses ainda não gerados entram projetados virtualmente, sem gravar nada.
  - **Contas com datas de saldo inicial diferentes contavam duas vezes.** Os pagamentos não são lançados em Caixa ou Banco, então não dá para saber o que já estava em cada saldo. Agora há **uma data de corte por empresa**: uma conta com outra data é recusada.
  - **Saldo inicial = saldo no fim do dia.** O que foi pago naquele dia já está dentro, e o realizado conta a partir do dia seguinte. A tela sugere o fim de ontem, para não contar duas vezes as vendas de hoje.
  - **Período começando no futuro:** o saldo inicial inclui o que vence entre hoje e o início.
  - **Contas desativadas** continuam contando seu saldo inicial; fechar uma conta não reescreve a história.
  - **Validações:** sem data futura, sem conta inativa, valor dentro do limite. Saldo inicial agora pode ser limpo (`DELETE`).
  - **Desempenho:** realizado e previsto são agrupados por dia do Brasil no próprio banco (SQL), sem carregar cada venda na memória.
  - **Tela:**
    - "-1.000" é lido como menos mil;
    - valor vazio não salva;
    - escolher a conta preenche o saldo atual;
    - o tooltip mostra saldo negativo com sinal.
- **Não incluso:** conciliação bancária (importar extrato OFX e casar com os lançamentos). É uma feature própria, fica como lacuna para depois do 1.0, sem número na missão.

## PR 53 — Integração financeira das verticais

- **Antes:** despesas do restaurante (`RestExpense`) e movimentações do Build/Interpreter (`FinancialMovement`) ficavam fora do livro. Contas a pagar, fluxo de caixa e o livro não as viam.
- **`VerticalLedgerPostingService`**, chamado pelas próprias origens, na mesma gravação delas:
  - **Despesa do restaurante** vira uma **conta a pagar quitada**:
    - quitada na data de pagamento, ou na data de competência quando a tela não informa uma (a tela do restaurante não tem esse campo, e o resumo do restaurante sempre contou a despesa como gasta no mês dela);
    - competência no mês da despesa;
    - loja da despesa;
    - categoria do livro a partir da categoria do restaurante (impostos/taxas → impostos; folha/salário → folha; outros → outras saídas; o resto → despesas operacionais).
    - Editar só a descrição não muda o livro. Valor, data, categoria ou pagamento trocam o lançamento: o anterior é estornado **nas datas originais** (correção, o mês fica certo) e entra o novo.
    - Excluir a despesa estorna o lançamento **hoje**; o livro guarda a história.
  - **Movimentação confirmada** (obra, loja…) vira lançamento **quitado na data dela**:
    - saída de despesa → despesas operacionais; outras saídas → outras saídas; entrada → outras entradas;
    - fornecedor da saída;
    - loja quando o contexto é loja;
    - **transferência entre contas fica fora** (não é receita nem despesa);
    - movimentação interna também fica fora;
    - anular uma movimentação confirmada estorna o lançamento hoje; reprocessar (volta a rascunho) estorna nas datas originais;
    - confirmar de novo lança outra vez.
- **No financeiro, esses títulos se pagam na origem:** pagar ou baixar uma despesa do restaurante ou uma movimentação pelo financeiro responde 422 com a orientação. Editar e cancelar também ficam na origem.
  - A tela de contas a pagar mostra "Pague na despesa do restaurante" ou "Quitado na obra" no lugar do botão.
  - O livro nomeia as novas origens.
- **Migration `VerticalExpensesInLedger`:**
  - backfill das despesas existentes (valor maior que zero) e das movimentações confirmadas que não são transferência;
  - reaproveita o id da origem e não duplica;
  - o Down remove exatamente esses lançamentos;
  - validada num Postgres descartável (despesa aberta e paga, imposto, valor zero, transferência, rascunho, anulada, contexto loja; Up e Down).
- **Folha do restaurante:** o salário mensal dos funcionários continua sendo um custo de referência no resumo do restaurante, não um pagamento. No livro, a folha entra como **despesa recorrente** (categoria Folha) em contas a pagar.
- **Testes:**
  - unitário: a confirmação lança no livro, e a transferência não;
  - integração em `EmployeeExpenseTests`: despesa vira conta a pagar, o financeiro recusa o pagamento, informar a data de pagamento troca o título aberto pelo quitado, editar só a descrição não muda o livro, excluir estorna; categorias mapeadas.
- **Revisão independente (corrigido antes do merge):**
  - **Bloqueio:** a despesa sem data de pagamento virava conta a pagar aberta para sempre, vencida, sem como pagar e fora do caixa realizado; o backfill fazia isso com todo o histórico. Agora ela é quitada na competência, na execução e na migration.
  - **Correção no mesmo mês:** corrigir uma despesa ou reprocessar uma movimentação de um mês fechado não soma duas vezes; o estorno usa as datas do original.
  - **Concorrência:** trava por registro de origem (`pg_advisory_xact_lock`) e transação na tela de despesas. Duas confirmações ou edições simultâneas não lançam duas vezes, e lançamentos duplicados antigos são retirados todos.
  - **Movimentação interna** fora do livro; valores menores que um centavo são ignorados sem erro; o Down remove todos os tipos de referência novos.
  - **Fica para depois:** campo "data de pagamento" na tela de despesas do restaurante (para despesas pagas fora do mês); classificação contábil de adiantamento (hoje entra como outras saídas).

## Marco — Fase 3 (financeiro, PRs 49–53)

| Área | Antes | Depois | O que ainda falta |
|---|---|---|---|
| Financeiro (núcleo) | 20% | ~85% | conciliação bancária (OFX), estornar uma baixa feita por engano, DRE com CMV (relatórios, PR 65) |
| Contas a receber | ◐ | ~85% | baixa como perda, juros e multa automáticos por percentual (regra comercial) |
| Contas a pagar | ◐ | ~85% | editar recorrência pela tela |
| Fluxo de caixa | ○ | ~80% | saldo por conta (exige lançar cada pagamento em caixa ou banco), conciliação |
| Integração das verticais | ○ | ~80% | data de pagamento na tela de despesas do restaurante, folha a partir do cadastro de funcionários |

Livro único, competência × caixa, títulos com baixa parcial, recorrências, fluxo de caixa e verticais no livro: o financeiro deixou de ser cinco registros paralelos.

## PR 54 — Menu: pedido → venda

- **Antes:** a comanda já virava venda (`SaleService`), mas o pedido de delivery perdia dinheiro no caminho.
  - O total cotado ao cliente era itens + taxa de entrega − desconto do cupom. Ao ser aceito, o pedido virava uma comanda só com os itens.
  - Na venda, a **taxa de entrega sumia da receita** e o **desconto do cupom não era aplicado**: o valor cobrado diferia do cotado.
  - Além disso, a **taxa de serviço (10% do garçom)** era cobrada também em delivery e retirada.
  - E o endpoint genérico `POST /api/restaurante/delivery-orders`, acessível a qualquer usuário do salão, **aceitava o preço enviado pelo cliente**, que seguia até a venda.
- **Agora:**
  - A comanda carrega `delivery_fee_amount` e `discount_amount` do pedido aceito.
  - O total é itens + couvert + serviço + entrega − desconto.
  - A venda é confirmada com a entrega nos acréscimos e o cupom no desconto, então **cobra exatamente o que foi cotado**. O livro registra essa receita.
  - Taxa de serviço: a comanda que veio de um pedido de delivery ou portal (cotado sem ela) não paga; salão, balcão e retirada aberta no balcão continuam pagando.
  - No endpoint genérico, **produto do catálogo usa o preço e o nome do catálogo** (e o adicional usa o do cadastro). Item sem produto (externo, não mapeado) mantém o preço declarado, mas nunca chega à comanda.
- **Telas:**
  - a conta da comanda, o fechamento e a impressão mostram taxa de entrega e desconto do cupom;
  - o card do delivery abre a comanda para receber (antes não havia como fechar e pagar um pedido sem mesa).
- **Decisão registrada:** preço próprio por marketplace (iFood com preço diferente do salão) fica para listas de preço por canal, uma regra comercial a decidir. Hoje vale o preço do catálogo.
- **Testes:** integração em `DeliveryPortalFlowTests`. Com taxa de serviço de 10% ligada no salão:
  - o pedido por WhatsApp com preço adulterado sai com o preço do catálogo;
  - com entrega de R$ 7,50 e cupom de R$ 5, a venda fecha em R$ 32,40, igual ao cotado, sem taxa de serviço.
- **Revisão independente (corrigido antes do merge):**
  - **Comanda presa.** Um cupom que cobria o pedido inteiro, ou um item cancelado depois do aceite, deixava uma comanda que não se pagava nem se cancelava.
    - Agora o desconto efetivo tem teto (itens + entrega).
    - O portal recusa um cupom que zeraria o pedido.
    - O fechamento recusa uma comanda sem valor a cobrar enquanto ela ainda pode ser ajustada.
  - **Relatórios do restaurante:** resumo e relatórios somam a entrega e abatem o desconto, iguais à venda e ao livro.
  - **Adicionais com preço** passam a contar no total do pedido de delivery, que estava subcotado em relação à comanda; o cupom percentual usa esse total.
  - **Endpoint genérico de delivery:** produto ativo, quantidade inteira positiva, adicionais do próprio produto (ativos e dentro dos limites do grupo), adicional livre sem preço negativo, retirada sem taxa de entrega.
  - **Fechamento:** a resposta devolve o total certo.
  - **Migration:** comandas aceitas de delivery e ainda não pagas recebem os termos cotados; as pagas ficam como estavam.
  - +1 teste (cupom maior que o pedido depois de cancelar item; retirada sem taxa). O teste da taxa de serviço passa a criar as configurações quando faltam.

## PR 55 — Fechamento do Menu

- **Adicionais pela tela** (`/restaurante/adicionais`, gestão). Antes só existia a API.
  - Por produto: criar grupo (nome, obrigatório), ajustar mínimo e máximo, remover grupo.
  - Opções: incluir com preço (pode ser negativo, por exemplo "sem queijo") e remover.
  - Endpoint novo `DELETE /api/restaurante/modifier-groups/{id}`, que desativa o grupo; o histórico dos pedidos guarda os snapshots.
- **Cancelar item da comanda pela tela.** O endpoint existia sem UI. Na comanda em aberto, "Cancelar item" pede confirmação no segundo toque.
- **Fechamento recusado** (por exemplo, cupom maior que o pedido) mostra o motivo do servidor, em vez de uma mensagem genérica.
- **Cozinha em tempo real:** depois de uma reconexão automática, o SignalR volta a entrar no grupo da loja. Antes, a conexão reconectada não recebia mais nenhum evento; se a reentrada falhar, a tela cai no polling.
- **Portal público:**
  - **rate limit** por IP em criar pedido e validar cupom (`public-orders`, 20 por minuto, configurável);
  - loja inativa ou tenant sem **Orken Menu** ativo: o cardápio continua visível, mas `acceptingOrders` é falso e pedidos são recusados. Antes, o portal recebia pedidos que ninguém conseguia aceitar.
- **Já estava pronto (a auditoria estava desatualizada):** a cozinha já mostrava os adicionais dos itens.
- **Testes:**
  - integração: portal sem módulo ativo mostra o cardápio e recusa o pedido; grupo removido sai do produto;
  - Playwright: tela de adicionais.
- **Revisão independente (corrigido antes do merge):**
  - **Cancelar item:**
    - atualiza a comanda aberta pela mesa (antes a tela ficava desatualizada);
    - avisa a cozinha em tempo real (antes o prato cancelado continuava na tela e era preparado);
    - recalcula o status da comanda (o último prato pendente cancelado deixa a comanda pronta) e sincroniza o rastreio do delivery;
    - item entregue ou já cancelado é recusado com o motivo.
  - **Comanda de delivery mantém a cotação:** os itens não são cancelados pela comanda; muda-se pelo painel de entregas (recusar ou cancelar o pedido). Recotar com o cliente fica para depois.
  - **Adicionais:**
    - grupo obrigatório sem opções não trava o produto (salão e portal);
    - mínimo ≤ máximo validado no domínio, com erro 422 em vez de 500;
    - remover grupo desativa as opções e pede confirmação;
    - adicional de outro produto ou de grupo removido é recusado no salão;
    - item com valor negativo é recusado;
    - "1.000" lido como mil;
    - rótulos com o nome do grupo.
  - **SignalR:** uma reentrada que falha depois de a tela fechar não deixa polling órfão.
  - **RBAC:** linha nova na matriz para remover grupo.
- **Fica para depois:** integrações iFood, Rappi e AnotaAí (dependem de contrato e API dos parceiros); editar opção já criada (hoje remove e cria de novo).

## Marco — Orken Menu (PRs 54–55)

| Área | Antes | Depois | O que ainda falta |
|---|---|---|---|
| Delivery, portal e balcão → venda | 🔴 | ~85% | atualizar o pedido de delivery quando itens da comanda são cancelados |
| Cardápio e adicionais | 35% | ~75% | editar opção, adicionais na tela de produto |
| Cozinha / KDS | 55% | ~70% | tempos de preparo e alertas |
| Portal público | 45% | ~70% | integrações de marketplace |
| **Orken Menu (geral)** | **50%** | **~75%** | integrações iFood, Rappi e AnotaAí |

