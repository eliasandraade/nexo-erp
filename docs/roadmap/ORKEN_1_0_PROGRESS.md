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
| #55 | Fechamento do Menu | `115458e` | — | Em produção |
| #56 | Service: agenda (bloqueios, semana, comanda) | `7edac28` | `ServiceTimeBlocks` (aditiva) | Em produção |
| #57 | Service: cancelamento pelo cliente e estorno de pacote | `cf00b11` | `ServiceSelfCancelAndUsageReversal` (aditiva + índice único refeito com filtro mais estreito) | Em produção |
| #58 | Service: avisos por e-mail (confirmação e lembrete) | `86ed34c` | `ServiceCustomerEmails` (aditiva) | Em produção |
| #59 | Service: financeiro, profissional por item, plurais | `0e6de63` | `ServiceFinanceIndexes` (só índices) | Em produção |
| #60 | Build: fechamento (contadores, paginação, validação, estorno de despesa, etapas) | `fc723ca` | — | Em produção |
| #61 | Billing: integridade (webhook, carência, redirects) | `1d5527b` | `BillingPastDueGrace` (aditiva) | Em produção |
| #62 | Billing: cobrança de verdade (preços, planos, meios de pagamento) | — | — | **Aguardando decisão do dono** (preços, vitalício, Pix/boleto, trial, pacote) |
| #63 | Auth: recuperação de senha e controle de sessões (GitHub #62) | `e64cd63` | `AuthPasswordReset` (aditiva) | Em produção |
| #64a | Auth: bloqueio por tentativas, auditoria de login, troca obrigatória, convite (GitHub #63) | `e5ec8ca` | — | Em produção |
| #64b | Auth: verificação em duas etapas (TOTP) (GitHub #65) | `baad911` | `AuthMfaTotp` (aditiva) | Em produção |
| #65 | Relatórios do núcleo e painel com período (GitHub #66) | `0b260f9` | — | Em produção |
| #66 | Fiscal: base (emissor, produto, cliente, CPF na nota) (GitHub #67) | `291acb5` | `FiscalFoundation` (aditiva) | Em produção |
| #67 | CI do frontend e lockfile versionado (GitHub #68) | `be47ace` | — | Em produção |
| #68 | Saúde e resiliência (live, ready, relatório) | — | — | Em revisão |


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

**Fase 3 — Financeiro (PRs 49–53)**

| Área | Antes | Depois | O que ainda falta |
|---|---|---|---|
| Financeiro (núcleo) | 20% | ~75% | conciliação bancária; DRE e relatórios gerenciais (PR 65); centro de custo |
| Contas a receber / a pagar | — | ~80% | boleto e cobrança automática (integração paga, decisão comercial) |
| Fluxo de caixa | — | ~75% | saldo por conta bancária conciliado |
| Verticais no livro | 0% | ~80% | comissões do Service como conta a pagar automática por repasse (já existe o lançamento do repasse) |

**Menu (PRs 54–55)**

| Área | Antes | Depois | O que ainda falta |
|---|---|---|---|
| Orken Menu | 50% | ~75% | NFC-e (PR 66), impressão de cozinha, integrações de delivery (pagas) |

**Service (PRs 56–59)**

| Área | Antes | Depois | O que ainda falta |
|---|---|---|---|
| Agenda | — | ~85% | papel "profissional" com agenda própria; recorrência de agendamento |
| Portal público | — | ~85% | pagamento online no agendamento (integração paga) |
| Pacotes e comissões | — | ~85% | relatório de repasse por período em PDF |
| Avisos ao cliente | 0% | ~70% | WhatsApp/SMS (integração paga); aviso à loja quando o cliente cancela |
| Financeiro do Service | 0% | ~75% | exportação; comparação entre períodos |
| **Orken Service (total)** | 65% | ~85% | — |

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

## PR 56 — Service: agenda

- **Bloqueios de agenda** (`SvcTimeBlock`, tabela `svc_time_blocks`):
  - por profissional (folga, almoço) ou para a loja inteira (feriado);
  - `GET/POST /api/v1/service/time-blocks` e `DELETE /api/v1/service/time-blocks/{id}` (gestão, módulo Service);
  - o agendamento interno num horário bloqueado responde 409;
  - o portal público tira os horários bloqueados da disponibilidade e recusa agendá-los;
  - o bloqueio não cancela o que já estava agendado; a agenda mostra os dois para a equipe decidir.
- **Agendamento duplo por corrida:** trava por profissional (`pg_advisory_xact_lock`) no agendamento interno, no reagendamento e no agendamento público. Duas reservas simultâneas do mesmo profissional fazem fila, e a segunda vê a primeira.
- **Concluir direto:** agendado ou confirmado pode ir direto para concluído (ou em atendimento), sem três transições. A comissão é reconhecida na conclusão, como antes.
- **Agenda** (`/service/agenda`):
  - visão **Dia** ou **Semana** (segunda a domingo, agrupada por dia);
  - filtro por profissional;
  - os bloqueios aparecem na agenda, com remoção;
  - **"Bloquear horário"** (profissional ou loja inteira, horário ou dia todo, motivo);
  - **"Abrir comanda"** a partir do agendamento (o endpoint existia sem uso) leva direto à comanda.
- **Testes:**
  - integração: bloqueio do profissional e da loja (409, outro profissional livre, remoção libera); bloqueio inválido; portal sem o horário bloqueado e recusando a reserva; conclusão direta;
  - unitários e Vitest das transições atualizados;
  - Playwright: agenda da semana, bloqueio, concluir e abrir comanda.
- **Correções da revisão independente** (sem bloqueadores; 3 should-fix e menores):
  - editar um agendamento feito antes de um bloqueio, sem sair do próprio horário, não é mais recusado. Movê-lo para dentro do bloqueio continua sendo 409;
  - a trava por profissional espera no máximo 5 s (`lock_timeout`), e o estouro vira 409 "tente de novo" em vez de pendurar a requisição;
  - "Abrir comanda" num agendamento que já tem comanda abre a existente, em vez de mostrar erro;
  - "Bloquear horário" não assume mais a loja inteira: parte do profissional filtrado ou exige a escolha;
  - remover um bloqueio pede dois toques;
  - "dia todo" vai até a meia-noite do calendário (seguro no horário de verão);
  - bloqueio de dia inteiro ou de vários dias mostra "Dia todo" ou as datas;
  - o diálogo lista só profissionais ativos;
  - a contagem bate com a lista e usa o termo do ramo;
  - a criação do bloqueio responde 201, `professionalId` vazio vale como loja inteira, e as mensagens estão em português;
  - testes novos: edição dentro do horário com bloqueio posterior, e duas reservas simultâneas (uma 201, uma 409).

## PR 57 — Service: cancelamento pelo cliente e estorno de pacote

- **Link do cliente** (`/agendar/{slug}/reserva/{token}`):
  - todo agendamento novo ganha uma chave própria (`PublicToken`, 128 bits aleatórios, índice único);
  - a confirmação do portal mostra "Ver ou cancelar este agendamento", e o link também vai no calendário (.ics) e no texto copiado;
  - a página mostra serviço, profissional, horário e situação — nenhum dado pessoal, porque o link pode ser repassado;
  - agendamentos anteriores ao link ficam sem chave e só a equipe os gerencia.
- **Cancelamento pelo cliente:**
  - `GET /api/public/service/{slug}/appointments/{token}` e `POST .../{token}/cancel`, com o mesmo limite de taxa da reserva;
  - pode cancelar o que ainda não começou (agendado ou confirmado), com a mesma antecedência mínima exigida para reservar (`MinLeadMinutes`). Depois disso, a página orienta a falar com o estabelecimento;
  - motivo registrado: "Cancelado pelo cliente."; o horário volta a ficar livre no portal;
  - cancelar continua funcionando mesmo se a reserva online for desligada depois — só libera horário.
- **Correção de privacidade:** a confirmação da reserva devolvia o nome do cliente já cadastrado para aquele telefone — quem digitasse o número de outra pessoa descobria o nome dela. Agora devolve o nome digitado.
- **Estorno de consumo de pacote** (`POST /api/v1/service/customer-packages/{id}/usages/{usageId}/reverse`, com motivo obrigatório):
  - o saldo volta ao pacote, e um pacote que estava "Consumido" volta a "Ativo";
  - a comissão do consumo é estornada no mesmo padrão do estorno por pagamento anulado: some se ainda estava em aberto, ou vira lançamento negativo no próximo fechamento se já foi paga;
  - o consumo fica no histórico marcado como estornado (data, motivo, quem fez), e o agendamento que ele pagava pode ser pago de novo;
  - um consumo só é estornado uma vez (409), e não há estorno em pacote cancelado.
- **Consumo simultâneo:** consumir trava a linha do pacote (`FOR UPDATE`). Antes, dois consumos ao mesmo tempo podiam passar pela checagem de saldo.
- **Testes:**
  - integração: cancelamento pelo link libera o horário; não cancela dentro da antecedência; link desconhecido ou malformado dá 404; nome digitado ecoado; estorno devolve saldo e comissão; estorno após repasse pago desconta no próximo; estorno libera o agendamento; motivo obrigatório e consumo de outro pacote; consumo simultâneo da última unidade;
  - unitários: chave do link, prazo de cancelamento, estorno de saldo e de consumo;
  - Playwright: cliente vê e cancela pelo link; estorno exige motivo.
- **Migração validada** num Postgres descartável (up → down → up).
- **Correções da revisão independente** (sem bloqueadores; 4 should-fix e menores):
  - **comissão após o estorno:** se a comanda paga pelo pacote já estava quitada, ou o agendamento já concluído, o próprio serviço passa a ganhar a comissão. Antes, o profissional perdia a comissão de um serviço feito. O estorno trava a comanda na mesma ordem do consumo (pacote → comanda);
  - **cancelamento pelo link** é recusado quando o agendamento já tem comanda ou foi pago com pacote; a página explica que só a loja pode desmarcar. Antes, o saldo e a comissão ficavam presos sem ninguém saber;
  - **cancelamento × mudança da equipe:** cancelar pelo link, mudar o status e reagendar travam a linha do agendamento. Antes, a equipe podia sobrescrever um cancelamento recém-feito pelo cliente;
  - a tela de consumo volta a oferecer o agendamento cujo consumo foi estornado;
  - trocar o cliente de um agendamento gera um link novo, e o antigo deixa de funcionar;
  - um pacote vencido que recebe estorno fica "Vencido", não "Ativo";
  - cancelar o pacote também trava a linha;
  - a página do cliente distingue "não encontrado" de falha de rede (com "Tentar de novo"), não recarrega ao focar a janela, relê após um cancelamento recusado, e tem acessibilidade no carregamento e no foco da confirmação;
  - testes novos: comissão do item da comanda após o estorno; link recusa cancelar com comanda aberta; link novo ao trocar de cliente; estorno em pacote vencido.

## PR 58 — Service: avisos por e-mail

- **Opt-in por loja** (`PUT /api/v1/service/settings/customer-emails`, seção "Avisos por e-mail" em `/service/portal`):
  - desligado por padrão;
  - lembrete de 1 a 72 horas antes (a tela oferece 2, 6, 12, 24 e 48);
  - endpoint separado, como a identidade visual, para que salvar a reserva nunca apague a escolha.
- **O que sai,** pelo Resend que já envia a verificação de conta:
  - **confirmação** da reserva feita pelo portal ("Recebemos seu agendamento" ou "Seu horário está confirmado");
  - **lembrete** antes do horário, também para agendamentos feitos pela equipe;
  - os dois levam o link do cliente para ver ou cancelar (PR 57);
  - só vão para clientes com e-mail e para agendamentos ainda abertos (agendado ou confirmado) e futuros. Cancelado não recebe nada;
  - todo texto vindo de cadastro é escapado no HTML.
- **Entrega** (`ServiceCustomerEmailsJob`, a cada 2 minutos, para todos os tenants):
  - a reserva nunca espera o provedor: o portal só marca a confirmação como pendente;
  - cada e-mail é reivindicado com um `UPDATE` condicional antes do envio, então nunca sai duas vezes, mesmo com várias instâncias;
  - falha do provedor fica no log e não é repetida para o cliente;
  - reserva feita já dentro da janela do lembrete recebe só a confirmação;
  - agendamentos sem o link (anteriores ao PR 57) não recebem e-mail.
- **Volume:** depende do plano do Resend da conta. Por isso o recurso nasce desligado e cada loja liga.
- **Testes:**
  - integração (chamando a passada do job com um e-mail falso): uma confirmação e um lembrete, cada um uma vez, com o link e o nome escapado; nada com a loja desligada nem para cancelado; validação da antecedência;
  - unitário: padrões e limites;
  - Playwright: ligar os avisos e escolher o lembrete.
- **Migração validada** num Postgres descartável (up → down → up).
- **Correções da revisão independente** (sem bloqueadores; 4 should-fix e menores):
  - **lote:** confirmações e lembretes saem de consultas separadas, e o "lembrete vencido" usa a antecedência de cada loja no próprio SQL. Antes, agendamentos dentro de 72 h que ainda não estavam na hora ocupavam o lote de 100 e atrasavam os outros;
  - **rajada e falhas:** os envios são espaçados (600 ms). Falha temporária (429, 5xx ou timeout) devolve o e-mail para a próxima passada; recusa definitiva (endereço inválido) não repete. O HttpClient do Resend tem timeout de 10 s, e timeout não derruba mais o lote;
  - **destinatário:** a confirmação só sai quando o e-mail digitado na reserva é o do cadastro. Um telefone compartilhado ou reciclado não leva o link a outra pessoa;
  - **remarcação:** mudar o horário ou o cliente zera o lembrete já enviado;
  - o claim reconfere situação e horário (cancelado ou passado não recebe); cliente inativo e e-mail vazio ficam de fora; o join do cliente exige o mesmo tenant;
  - o lembrete só é suprimido quando a confirmação saiu de fato; confirmação com mais de um dia não é enviada;
  - **sem provedor configurado:** o job não reivindica nada e a tela avisa;
  - o log de erro do Resend não grava mais o corpo da resposta, que podia ter o destinatário;
  - o formulário não perde a edição quando outra seção salva.

## PR 59 — Service: financeiro, profissional por item e plurais

- **Financeiro do Service** (`GET /api/v1/service/finance/summary?from&to`, tela `/service/financeiro`):
  - só leitura, por dias do Brasil (padrão: o mês corrente até hoje; até 366 dias);
  - regime de caixa: conta os pagamentos ainda válidos pela data de recebimento;
  - total, quantidade, ticket médio por comanda ou pacote pago, separação comandas × pacotes, por meio de pagamento e por dia; estornos do período aparecem à parte;
  - **por profissional:**
    - o que as comandas pagas lhe renderam, rateando cada pagamento pelos itens (profissional do item, senão o da comanda), ao centavo, com o último item absorvendo o arredondamento;
    - o valor dos serviços que fez por pacote (consumos não estornados);
    - a comissão reconhecida. Um ganho estornado só conta se já tinha sido pago, porque o lançamento negativo o desconta;
  - pacotes vendidos no período (quantidade e valor, sem os cancelados);
  - desfecho da agenda: realizados, cancelados, faltas (com a taxa sobre os que já passaram) e em aberto;
  - entra na navegação para os ramos com comandas ou pacotes.
- **Profissional por item da comanda:**
  - a tabela de itens ganhou a coluna do profissional, editável enquanto a comanda está aberta;
  - "Adicionar item" permite escolher quem faz;
  - o padrão continua sendo o profissional da comanda. Um item já comissionado recusa a troca, como antes.
- **Plurais dos termos de cada ramo** (`pluralPt`): "Ordens de serviço", "Sessões", "Avaliações", "Professores", "Instrutores", "Personais". Antes saíam "Ordem de serviços", "Sessãos" etc. Aplicado nos títulos, contagens e descrições do Service.
- **Testes:**
  - unitários do cálculo: rateio ao centavo, totais por origem e meio, ticket, regra do ganho estornado;
  - integração: dois profissionais numa comanda paga + consumo de pacote → crédito e comissão de cada um; validação do período e 401 sem login;
  - Vitest dos plurais e das superfícies;
  - Playwright: financeiro (inclui 390 px sem rolagem lateral) e profissional por item.
- **Correções da revisão independente** (1 bloqueador, should-fix e menores):
  - **bloqueador:** dinheiro "sem profissional" (comanda sem profissional, ou consumo de pacote sem profissional) derrubava o painel com 500, por chave nula no dicionário. Agora vai para a linha "Sem profissional", com testes;
  - itens pagos por pacote ficam fora do rateio do dinheiro da comanda, tanto o item apontado pelo consumo quanto o do agendamento pago por pacote. Antes, o mesmo serviço contava duas vezes;
  - o rateio usa o maior resto: soma sempre exata, nunca negativa, e a ordem é estável (itens pela ordem de criação);
  - índices para os períodos: comissões, consumos e pacotes vendidos por loja e data, e estornos de pagamento;
  - a contagem da agenda e a soma dos pacotes passaram a ser agregadas no banco;
  - período validado na tela (até 366 dias, anos plausíveis) e no servidor (sem 500 em datas extremas); um 422 mostra a mensagem;
  - comanda no celular: tabela com rolagem própria e linha de "adicionar" que quebra; os textos usam o termo do ramo e evitam concordância de gênero;
  - mensagens em português para "profissional inativo" e "item já comissionado".

## PR 60 — Build: fechamento

- **Cards de obra com números reais:** a listagem não carregava etapas nem diário, então mostrava "Sem etapas cadastradas" e nunca a contagem do diário. Agora conta etapas, concluídas e registros numa consulta agregada, inclusive nas respostas de criar, editar e mudar status.
- **Paginação:**
  - o total devolvido era o tamanho da página, não o real; diário e orçamentos ganharam contagem;
  - `pageSize` limitado a 100;
  - o diário e as despesas da obra ganharam "carregar anteriores" (antes só os 30 primeiros apareciam), e o contador mostra o total.
- **Validação:** os validadores do Build nunca rodavam. Agora um filtro nos controllers do Build os executa, com mensagens em português, e há validadores novos para editar e reordenar etapas.
- **Etapas:**
  - nova rota `PUT /api/v1/build/stages/{id}` (o serviço existia sem rota) e tela para editar nome, descrição e datas previstas;
  - subir e descer etapa;
  - o reorder quebrava com 500, porque o frontend mandava `items` e o backend esperava `entries`. Agora usa `entries`, e um corpo errado dá 400;
  - criar etapa mandava as datas com nomes errados (`plannedStartDate`), que se perdiam.
- **Estornar despesa da obra:**
  - botão em dois toques na aba Financeiro, usando o `void` do Interpreter;
  - a despesa sai do realizado da obra, e o livro financeiro mantém o lançamento e ganha o contra-lançamento (PR 53);
  - teste de integração do caminho completo: analisar → confirmar → estornar → realizado zera → livro com estorno → segundo estorno recusado.
- **Correções de tela:**
  - **desvio com sinal invertido:** a API devolve aprovado − realizado, e a tela pintava de vermelho quando sobrava dinheiro. Agora "acima" ou "abaixo do aprovado" com a cor certa;
  - cancelar a obra só sai da página depois que o cancelamento deu certo;
  - a data padrão do diário usa o dia local (após as 21 h, o UTC já é amanhã);
  - mexer em etapas atualiza a aba Geral, os cards e o painel; despesas confirmadas ou estornadas atualizam o painel;
  - os tipos do frontend foram alinhados ao backend (`recentLogs`, `photoCount`).
- **Testes:**
  - integração (+5): contadores reais; paginação com total real e limite; validação com mensagem; edição de etapa; estorno de despesa ponta a ponta;
  - Playwright: reordenar e editar etapa, desvio, estorno em dois toques e 390 px sem rolagem lateral.
- **Fica para depois:**
  - custo por etapa: a despesa (`FinancialMovement`) não tem vínculo com etapa e precisa de modelagem;
  - PDF do orçamento e relatórios do Build;
  - "converter orçamento em obra" (o endpoint existe; falta definir o fluxo na tela).
- **Correções da revisão independente** (sem bloqueadores; 3 should-fix e menores):
  - o desvio de obra sem orçamento aprovado aparecia como estouro: agora mostra "—, sem orçamento aprovado";
  - reordenar fica bloqueado até a lista recarregar, para que um segundo clique rápido não aja sobre a ordem antiga;
  - criar etapa ou diário também atualiza os cards e o painel;
  - o clima de "hoje" usa o dia local;
  - a confirmação do estorno se desarma sozinha em 4 s (no toque não existe blur); mensagem do estorno repetido em português;
  - editar ou reordenar etapas de obra cancelada ou concluída é recusado, com a mesma regra da exclusão;
  - validadores também para editar item de orçamento, margem e diário;
  - botões das etapas com área de toque maior, quebrando linha no celular.

## PR 61 — Billing: integridade

O Stripe continua **desligado** em produção (`Integrations:Features:StripeEnabled=false`), então nada muda para os clientes hoje. São correções para que, ao ligar, a cobrança e o corte de acesso estejam certos. Preços, planos e meios de pagamento são decisões do dono e ficam para o PR 62.

- **Uma regra de acesso para todos os portões** (`ModuleSubscription.HasAccessAt`): o gate de módulo, os módulos do login e os módulos da loja usavam três cópias da mesma condição.
- **Carência de 7 dias para pagamento atrasado:** já estava documentada na entidade, mas não era aplicada (`PastDue` cortava na hora). Ela conta da primeira falha (`PastDueSince`), não reinicia a cada nova tentativa do cartão, e some quando o pagamento volta.
- **Status do Stripe sem acesso por padrão:** qualquer status desconhecido virava `Active`. Agora `incomplete`, `paused` e desconhecidos viram `Suspended`, e `incomplete_expired` vira `Canceled`.
- **Reassinatura:** um checkout que caía numa linha existente mantinha o id da assinatura antiga, então cancelamentos e atualizações da nova nunca chegavam e o acesso nunca era cortado. Agora a linha passa a seguir a nova assinatura, e um segundo checkout de módulo já pago no Stripe é recusado (409, "use Gerenciar assinatura") para não gerar uma cobrança dupla.
- **Redirect aberto:** as URLs de sucesso, cancelamento e retorno só são aceitas no domínio do app; fora dele, vale o padrão.
- **Cache de acesso:** os webhooks invalidam o cache do módulo, em vez de esperar até 5 minutos.
- **Testes:**
  - unitários de mapeamento de status, redirect e carência;
  - webhook com payload assinado como o Stripe faz: status desconhecido suspende e limpa o cache; assinatura inválida não muda nada; exclusão cancela;
  - o teste do portal sem Menu ativo passou a usar um atraso já fora da carência.
- **Migração validada** num Postgres descartável (up → down → up). Linhas `PastDue` antigas ficam sem data e seguem sem acesso, como antes.
- **Correções da revisão independente** (sem bloqueadores; 3 should-fix e menores):
  - um `invoice.payment_failed` atrasado ou reenviado não reabre carência de assinatura já cancelada ou suspensa (o Stripe não ordena eventos);
  - o checkout recusa (409) qualquer módulo cuja assinatura ainda vive no Stripe, mesmo atrasada ou suspensa, para não deixar a antiga cobrando o cartão em paralelo; também recusa módulo já liberado pelo admin ou vitalício;
  - a linha nova criada no checkout também assume o status real do Stripe (`incomplete` não dá acesso);
  - o webhook limpa também o cache Redis da lista de módulos e das informações do tenant, não só o cache em memória;
  - a impersonação da plataforma usa a mesma regra de acesso;
  - `IsActive()` sem compilar expressão a cada chamada;
  - o handler do checkout lê a assinatura por uma interface (`IStripeSubscriptionFetcher`), e por isso ganhou testes: reassinatura segue a nova assinatura com plano e status; linha nova `incomplete` fica sem acesso.

## PR 63 — Auth: recuperação de senha e controle de sessões

- **Esqueci a senha** (`POST /api/auth/forgot-password`, tela `/esqueci-senha`, link no login):
  - envia por e-mail um link de uso único que vale 1 hora, para cada conta ativa com aquele endereço;
  - a resposta é sempre a mesma, exista a conta ou não, para não permitir descobrir quem tem conta;
  - mesmo limite de taxa do login;
  - o banco guarda só o hash SHA-256 do token.
- **Redefinir senha** (`POST /api/auth/reset-password`, tela `/redefinir-senha?token=`):
  - grava a nova senha, limpa o link e encerra todas as sessões;
  - uma conta ainda não verificada fica verificada, porque o link provou o e-mail.
- **Trocar senha encerra todas as sessões:** antes, trocar a própria senha ou o gerente redefinir a de alguém mantinha as sessões antigas vivas. Agora a troca muda o *security stamp* e revoga todas; a tela leva de volta ao login com aviso.
- **Refresh respeita a revogação:** o refresh token guarda o stamp de quando foi emitido, e um refresh depois da troca é recusado. Isso fecha a brecha dos tokens que nunca viraram "sessão" registrada (verificação de e-mail, troca de loja), que a revogação por lista não alcançava.
- **Sessões do próprio usuário** (`GET /api/auth/sessions`, `POST /api/auth/sessions/revoke-all`, seção "Sessões ativas" em `/perfil`):
  - lista os aparelhos (navegador e sistema, último uso, IP);
  - "Sair de todos os dispositivos", em dois toques, inclui o aparelho atual;
  - o logout marca a sessão como encerrada, então ela sai da lista.
- **Testes:**
  - integração (+5, cada um com usuário próprio): esqueci a senha responde igual para endereço conhecido e desconhecido, guardando só o hash; o link funciona uma vez e encerra as sessões; link vencido, desconhecido ou senha curta são recusados; trocar a senha derruba os refresh tokens; "sair de todos" derruba inclusive token não registrado e o access token em uso;
  - RBAC: as rotas de sessão ficam abertas a qualquer papel, por serem do próprio usuário;
  - Playwright: esqueci, redefinir (erro, confirmação e sucesso) e sair de todos.
- **Migração validada** num Postgres descartável (up → down → up).
- **Correções da revisão independente** (sem bloqueadores; 4 should-fix e menores):
  - **anti-spam do "esqueci a senha":** um link enviado há menos de 5 minutos continua sendo o válido, sem novo e-mail; há também um teto de 5 pedidos por hora por endereço, seja qual for o IP. Antes, repetir o formulário enchia a caixa da vítima, invalidava o link que ela ia usar e gastava a cota do Resend;
  - **CSRF:** "sair de todos" exige corpo JSON, que um formulário de outro site não consegue mandar sem preflight. Com cookies `SameSite=None` em produção, isso era um ataque fácil para deslogar alguém;
  - **mesmo e-mail em vários tenants:** o e-mail traz o nome da empresa e o login, para a pessoa saber qual conta está redefinindo; empresa inativa não recebe link;
  - **cache do security stamp** é limpo de novo depois do commit, porque uma requisição concorrente da sessão antiga podia recolocar o stamp velho por até 60 s;
  - o token sai da barra de endereço logo depois de lido;
  - teste novo: pedir de novo em seguida mantém o mesmo link; o "sair de todos" por formulário é recusado.
- **Fica para depois (anotado):** particionar o rate limit por IP real atrás do proxy da Railway (`ForwardedHeaders` com proxies conhecidos) e enviar e-mails em segundo plano, eliminando a pequena diferença de tempo entre endereço conhecido e desconhecido. O cadastro já revela se o e-mail existe.

## PR 64a — Auth: endurecimento do acesso

- **Bloqueio por conta:**
  - 10 senhas erradas seguidas, de qualquer IP, pausam o login daquela conta por 15 minutos. O limite por IP sozinho era fácil de contornar trocando de endereço;
  - durante a pausa nem a senha certa é conferida, então um chute não pode ser confirmado;
  - um login correto zera a contagem;
  - resposta 429 com "Muitas tentativas… ou use Esqueci a senha".
- **Auditoria de login:** login com sucesso, senha errada e pausa da conta ficam no registro de auditoria, com IP. As constantes já existiam e nunca eram usadas.
- **Senha definida pelo gestor:**
  - quando o gestor redefine a senha de alguém, a pessoa precisa trocá-la no próximo acesso (antes, a redefinição limpava essa exigência);
  - o login informa `requirePasswordChange`, e o app leva direto para `/perfil` com o aviso "Crie sua própria senha para continuar" até a troca. Depois disso, a pessoa entra de novo.
- **Convite por e-mail:**
  - criar usuário passa a enviar, por padrão, um convite: um link de 72 h para a pessoa criar a própria senha, que ninguém mais conhece. É o mesmo link e a mesma tela da redefinição, com o título "Criar sua senha";
  - o gestor ainda pode digitar uma senha, desligando o convite;
  - convite sem e-mail é recusado;
  - removido o padrão `"nexo@temp"` que o frontend mandava quando a senha vinha vazia.
- **Acessibilidade:** campos de nome, e-mail e login do cadastro de usuário ganharam rótulos associados.
- **Testes:**
  - integração (+4, usuário próprio por teste): 10 erradas pausam até a certa, com auditoria; login certo zera e é auditado; senha do gestor força a troca e a troca limpa a exigência; convite gera link de 72 h, a pessoa cria a senha e entra; convite sem e-mail é recusado;
  - suíte completa: integração 568/568, unitários 481/481;
  - Playwright: troca obrigatória e convite no cadastro.
- **Correções da revisão independente** (1 bloqueador, should-fix e menores):
  - **bloqueador:** corrigir o e-mail de um usuário, ou o gestor definir a senha dele, agora invalida o link de convite ou de redefinição pendente; bloquear ou desativar também. Antes, um convite mandado para um endereço digitado errado continuava valendo 72 h, e quem o recebesse podia assumir a conta;
  - **contador atômico** (`ICacheService.IncrementAsync`, `INCR` no Redis): antes, requisições em paralelo dividiam a mesma vaga do contador e permitiam muito mais que 10 palpites;
  - **guarda compartilhado** (`LoginAttemptGuard`) também na verificação de gerente, na validação de gerente e na senha atual de "trocar senha". Antes, dava para testar senhas por ali sem limite;
  - **login da plataforma:** e-mails de operador são conferidos primeiro e têm o próprio bloqueio, inclusive no `/platform/auth/login`. Antes, a conta mais valiosa do sistema não tinha limite por conta, e um usuário de tenant com o mesmo login contava erro a cada login do operador;
  - nova senha (esqueci, gestor ou troca) **encerra o bloqueio**, como a mensagem do 429 promete;
  - a troca obrigatória **sobrevive ao recarregar** a página: `/me` e troca de loja também informam a exigência;
  - **convite:** recusado quando não há provedor de e-mail configurado; um convite de 72 h não segura mais o "esqueci a senha"; botão "Reenviar convite por e-mail" na edição do usuário (`POST /api/users/{id}/resend-invite`);
  - o formulário não trava por confirmação de senha escondida ao ligar o convite;
  - testes novos: convite morre ao corrigir o e-mail e ao definir senha; senha nova destrava; convite não segura o esqueci; 20 palpites em paralelo ainda travam. O host de teste usa um provedor de e-mail que aceita tudo e não envia nada.
- **Fica para depois (anotado):** identificar o IP real atrás do proxy da Railway (`ForwardedHeaders` com proxies conhecidos). Isso deixaria o limite por IP firme e a auditoria com o IP do cliente; a exigência de troca de senha também passaria a ser aplicada na API, não só na tela.

## PR 64b — Auth: verificação em duas etapas

- **Aplicativo autenticador** (TOTP, RFC 6238: 6 dígitos a cada 30 s, tolerância de ±1 intervalo). Funciona com Google Authenticator, Microsoft Authenticator, 1Password, Authy e outros.
  - Implementado sem biblioteca externa, conferido com os vetores de teste oficiais do RFC.
  - Cada código vale uma vez (o último intervalo usado fica gravado).
- **Segredo cifrado:** AES-256-GCM, com chave derivada (HKDF, rótulo próprio) do segredo do servidor que já existe. Nenhuma chave nova para gerenciar.
  - Se `Jwt:Secret` for trocado, os segredos ficam ilegíveis: a pessoa entra com um código de recuperação ou um diretor desliga o recurso, e ela ativa de novo.
- **Ativar** (`/perfil` → "Verificação em duas etapas"):
  - QR code e a chave para digitar no app;
  - o primeiro código confirma a configuração;
  - aparecem 8 códigos de recuperação, que valem uma vez cada, são guardados só como hash e não são mostrados de novo.
- **Entrar:**
  - com a verificação ligada, a senha certa devolve um desafio de 5 minutos em vez da sessão (`mfaRequired`), e o código fecha a entrada (`POST /api/auth/mfa/verify`);
  - código errado conta para o mesmo bloqueio por conta da senha;
  - o desafio vale uma vez;
  - é possível entrar com um código de recuperação.
- **Desativar:**
  - a própria pessoa precisa da senha e de um código (ou código de recuperação);
  - um diretor pode desligar para quem perdeu o celular e os códigos (`POST /api/users/{id}/mfa/disable`, auditado como crítico);
  - a lista de usuários informa quem tem a verificação ligada.
- **Mensagens de 401:** o cliente HTTP passa a mostrar a explicação do servidor, como "Código inválido", "E-mail não verificado" ou "Conta bloqueada", que antes viravam "Unauthorized". Login errado agora responde em português.
- **Testes:**
  - unitários: vetores do RFC 6238, Base32, tolerância e reuso, segredo cifrado e à prova de adulteração;
  - integração (+4, usuário próprio por teste): entrada em duas etapas; código e desafio valem uma vez; recuperação vale uma vez; desligar exige senha e código; desligamento pelo diretor; códigos errados levam ao bloqueio;
  - RBAC: as rotas de MFA da própria pessoa ficam abertas a qualquer papel;
  - Playwright: entrada com código (errado e certo) e ativação com QR e códigos de recuperação, em 390 px.
- **Dependência nova:** `qrcode` 1.5.4 (MIT), para desenhar o QR no navegador. O segredo não sai para nenhum serviço externo.
- **Migração validada** num Postgres descartável (up → down → up).
- **Correções da revisão independente de segurança** (1 bloqueador, should-fix e menores):
  - **bloqueador:** a senha certa zerava o contador de tentativas antes do código. Reentrando a senha entre os palpites, dava para chutar o código sem fim. Agora o contador só zera depois do código certo (teste: 9 códigos errados, nova senha, 1 errado → 429);
  - **replay concorrente:** o intervalo TOTP e o código de recuperação passam a ser reivindicados de forma atômica no banco (`UPDATE … WHERE`), então duas requisições simultâneas com o mesmo código não passam ambas;
  - **ativar pede a senha**, não só uma sessão aberta, para que quem usa uma sessão esquecida não tranque o dono fora. Ativar também **encerra todas as sessões**: a pessoa entra de novo, já com o código;
  - **ativar, confirmar e desativar** usam o limite por usuário (`credential-check`) e o mesmo bloqueio por conta; a mensagem de erro é única para senha ou código errado;
  - **segredo vinculado ao usuário** (AAD do AES-GCM) e chave própria `Mfa:EncryptionKey` (opcional; sem ela, usa `Jwt:Secret`). Segredo ilegível não dá mais erro 500: o código é recusado e o caso fica no log;
  - **códigos de recuperação com HMAC** (chave derivada), em vez de SHA-256 puro: um dump do banco não basta para adivinhá-los offline;
  - o campo de código aceita também um código de recuperação (o servidor distingue);
  - código errado é auditado como `user_mfa_failed`, não como senha errada;
  - **frontend:** o erro traz o `code` do servidor (desafio expirado detectado por código, não pelo texto); chamadas de login e de verificação não tentam renovar token num 401; 429 sem corpo ganha mensagem; o "Voltar" limpa o estado; o erro fica ligado ao campo (`aria-describedby`).
- **Fica para depois (prioritário):** verificação em duas etapas no login da plataforma (operadores Orken) e na impersonação. A senha de um super-admin continua sendo a chave de acesso a qualquer tenant.
- **Conflito anotado:** o PR do GitHub #64 (correção do refresh concorrente, aberto por outra sessão) toca `AuthService` e `ICacheService` e precisa de rebase sobre este.
- **Em produção** (merge `baad911`): `GET /api/auth/mfa` passou de 404 para 401 sem token, o bundle do frontend trocou e `/health` respondeu 20/20. Antes do merge, o GitGuardian apontou um segredo Base32 de exemplo fixo no Playwright: ele passou a ser gerado na hora e o branch foi recompactado (sem o segredo no histórico). O Sonar apontou `HMACSHA1` (S4790): o código usa `IncrementalHash` com SHA-1, que é o que o RFC 6238 e os aplicativos autenticadores exigem; como HMAC, não herda a fraqueza de colisão do SHA-1.

## PR 65 — Relatórios do núcleo e painel com período

- **Uma definição de "vendas do período"** (`SalesAnalytics`), usada pelo painel e pelos relatórios, para que os dois nunca discordem:
  - a venda pertence ao **dia do Brasil** em que foi finalizada (antes, ao dia em UTC: uma venda às 22h do dia 31 caía no dia 1º);
  - **vendas** = confirmadas, pagas ou devolvidas em parte; as devolvidas por inteiro e as canceladas ficam à parte;
  - **faturamento** = vendido − devolvido, com a devolução abatida da venda a que pertence;
  - **produtos**: o desconto da venda é repartido entre os itens, como na devolução, então a soma dos produtos bate com o faturamento.
- **Painel (`/dashboard`):** os números de vendas são **do mês até hoje** (antes somavam todo o histórico com a tela dizendo "no período"). A resposta diz o período (`periodFrom`/`periodTo`), o gráfico traz os 30 dias inteiros (dias sem venda com zero) e o cache passa a virar à meia-noite do Brasil. A taxa de cancelamento usa vendas + canceladas como base.
- **Relatório de vendas (`GET /api/reports/sales?from&to`):** período em dias do Brasil (padrão: mês até hoje; até 366 dias; data inválida → 400 com mensagem, não mais 500). Traz vendas, faturamento, bruto, devolvido, ticket, descontos dados, canceladas, dia a dia, 10 produtos, vendedores e formas de pagamento.
- **Planilha (`GET /api/reports/sales/export`):** CSV que o Excel em português abre com dois cliques (UTF-8 com BOM, `;`, vírgula decimal, hora do Brasil). Nomes que começam com `=`, `+`, `-` ou `@` ganham apóstrofo, para a planilha não executar fórmula vinda de um cadastro.
- **Estoque e clientes:** os alertas do relatório usam o disponível (em mão − reservado), como o painel; "novos no mês" segue o calendário do Brasil; "já compraram" conta as vendas confirmadas também (antes, só as pagas).
- **Tela `/relatorios`** (gerência): Hoje, 7 dias, Este mês, Mês passado ou datas escolhidas (com aviso antes de pedir um período inválido); faturamento por dia, produtos, vendedores, formas de pagamento com participação, estoque de agora e clientes; botão "Exportar planilha".
- **Testes:** unitários do período (calendário do Brasil, limites, padrão) e da planilha (fórmula, separador); integração: venda às 23h30 cai no dia certo, devolução abatida, cancelada à parte, formas de pagamento, produtos somando o líquido, planilha com as três vendas, períodos inválidos → 400, painel informando o período e os 30 dias; Vitest dos atalhos de período; Playwright da tela (período inválido, download, 390 px).
- **Correções da revisão independente** (sem bloqueadores):
  - planilha que falha no meio do download corta a conexão em vez de gerar um segundo erro no log; o erro antes do início não vai mais com o cabeçalho de anexo;
  - os atalhos de período da tela usam o dia de São Paulo, não o fuso do navegador (um caixa em Manaus ou num computador em UTC pedia o dia errado);
  - venda cancelada ainda em rascunho (checkout abandonado) não conta como cancelada, nem no painel, nem no relatório, nem na planilha;
  - data final absurda (ano 9999) responde 400, não 500;
  - vendedores com o mesmo nome não se misturam mais;
  - período com todas as vendas devolvidas não mostra mais "nenhuma venda";
  - o seletor de período virou um grupo de opções acessível, e o gráfico ganhou a tabela "Ver dia a dia" como alternativa em texto;
  - fica documentado: a devolução é abatida no período da venda, então um mês fechado muda se uma venda dele for devolvida depois (o razão financeiro lança o estorno no dia do estorno).
- **Fica para depois:** o download de arquivos não renova um token vencido (comportamento anterior, também do PDF); índice por `COALESCE(confirmed_at, created_at)` se as vendas crescerem muito.
- **Em produção** (merge `0b260f9`): `GET /api/reports/sales/export` passou de 404 para 401 sem token, o bundle do frontend trocou, `/relatorios` responde 200 e `/health` respondeu 20/20.

## PR 66 — Fiscal: base (sem emissão)

A emissão de NFC-e/NF-e depende de um provedor pago e de certificado digital, então fica fora (decisão do dono). Este PR deixa os dados prontos e confere o que falta, sem calcular imposto nem falar com a Sefaz.

- **Emissor por loja** (`fiscal_profiles`, um por loja, porque cada filial tem CNPJ e IE próprios):
  - razão social, nome fantasia, CNPJ, IE (números ou ISENTO), IM, regime tributário (Simples, Simples acima do sublimite, MEI, Presumido, Real) e endereço com código IBGE;
  - o CRT sai do regime pelo layout da NF-e (1, 2, 3, 4), não por regra de imposto;
  - CNPJ, IE e CEP são guardados sem máscara; UF em maiúsculas;
  - `GET/PUT /api/fiscal/profile` (gerência). Sem perfil, o GET responde 204.
- **Dados fiscais do produto:**
  - NCM (8), CEST (7), origem da mercadoria (0–8), CFOP de venda (4, começando por 5, 6 ou 7) e CST/CSOSN (2 ou 3), todos opcionais e informados pelo contador;
  - `GET/PUT /api/fiscal/products/{id}`;
  - botão "Dados fiscais" na tela do produto (gerência).
- **Preparação** (`GET /api/fiscal/readiness`): emissor preenchido ou não, produtos à venda sem NCM, códigos de barras que não são GTIN (vão como "SEM GTIN") e a lista dos primeiros 50 para corrigir. `emissionAvailable` é sempre `false` por enquanto.
- **CPF/CNPJ na nota:**
  - campo opcional no PDV, com máscara e conferência dos dígitos antes de finalizar;
  - o servidor confere de novo (422 com mensagem; a venda continua em rascunho);
  - fica gravado na venda (`consumer_document`) e sai no recibo.
- **Validadores** (servidor e navegador, mesmas regras): CPF, CNPJ (numérico e alfanumérico), GTIN-8/12/13/14, NCM, CEST e código IBGE por UF.
- **Tela `/fiscal`** (gerência): aviso de que a emissão ainda não existe, indicadores, formulário do emissor e lista de produtos para revisar com correção em diálogo.
- **Migração `FiscalFoundation`** (aditiva: tabela nova e colunas anuláveis), validada num Postgres descartável (sobe → desce → sobe).
- **Testes:**
  - unitários dos validadores (25);
  - integração: perfil recusado com CNPJ/IBGE/UF errados e salvo sem máscara, CRT por regime, produto listado até ganhar NCM, formatos fiscais inválidos → 400, CPF na nota inválido → 422 com a venda em rascunho, válido gravado só com dígitos;
  - Vitest dos validadores e das regras da tela;
  - Playwright: tela fiscal (CNPJ inválido barrado, emissor salvo, produto corrigido) e CPF na nota no PDV.
- **Completa o que a missão pede para a base fiscal:**
  - **natureza da operação** e **série/próximo número** da NFC-e e da NF-e no emissor (guardados para quando a emissão começar);
  - **unidade tributável** no produto (vazia = a unidade do produto);
  - **dados fiscais do cliente**: inscrição estadual, indicador (contribuinte, isento, não contribuinte) e código IBGE do município, em `GET/PUT /api/fiscal/customers/{id}` e no botão "Dados fiscais" da tela do cliente. Contribuinte do ICMS exige IE;
  - **contrato do provedor** (`IFiscalDocumentProvider`): o 1.0 usa `NotConfiguredFiscalProvider`, que responde "não disponível" e não envia nada. "Emissão disponível" só fica verdadeira com `Integrations:Features:FiscalEnabled` ligado **e** um provedor configurado.
  - impostos: não há motor tributário (a missão proíbe improvisar). O produto guarda CST/CSOSN e CFOP informados pelo contador; o cálculo virá das regras oficiais via provedor.
- **Correções da revisão independente** (sem bloqueadores):
  - **CNPJ alfanumérico** (emitido desde julho de 2026) aceito no servidor, no navegador, no emissor e no CPF/CNPJ na nota; guardado em maiúsculas, sem máscara;
  - o diálogo de dados fiscais do produto não mostra mais edições canceladas nem os dados de outro produto, e não salva se a leitura falhou;
  - duas primeiras gravações simultâneas do emissor não dão mais 500: a segunda é aplicada sobre a primeira;
  - repetir uma venda já confirmada pedindo outro CPF na nota responde 409, em vez de devolver a venda antiga como se tivesse dado certo;
  - IE com letras é recusada (antes as letras sumiam); o regime só vale pelo nome (antes "3" virava MEI); o código IBGE precisa ser da UF escolhida (servidor e tela);
  - códigos internos (13 dígitos começando por 2, como etiqueta de balança) e zeros não contam como GTIN;
  - o aviso de CPF incompleto no PDV só aparece quando o número parece completo ou ao sair do campo.
- **Fica para depois:** emissão de verdade (provedor, certificado A1, contingência e documento fiscal da devolução); unificar o CNPJ de `Tenant.TaxId`, das configurações da empresa e do emissor; clientes do portal público que hoje guardam um identificador no lugar do CPF; mascarar o CPF do consumidor para o papel vendedor (LGPD); conferir CST × CSOSN pelo regime antes de emitir.

- **Em produção** (merge `291acb5`): `GET /api/fiscal/readiness` passou de 404 para 401 sem token (a API subiu, então a migração foi aplicada), o bundle trocou, `/fiscal` responde 200 e `/health` 20/20.

## PR 67 — CI do frontend

- **Workflow `frontend-ci`** (em todo PR e push no master que toque o frontend): instalação pelo lockfile (`npm ci`), typecheck, lint, Vitest, build de produção e a suíte crítica de Playwright (API simulada, sem backend). Em falha, o relatório do Playwright fica como artefato por 7 dias.
- **Lockfile versionado:** `nexo-main/package-lock.json` deixa de ser ignorado. O lock gerado no Windows não traz os binários nativos de outras plataformas (bug do npm #4828, por isso o Dockerfile instalava sem lock e cada deploy resolvia versões novas). O lock agora é gerado no Linux (`npm install --package-lock-only` em `node:20-slim`) e traz os binários de todas as plataformas (25 variantes do Rollup); `npm ci` funciona no Windows, no CI e no Docker.
- **Build reproduzível:** o Dockerfile do frontend passa a usar `COPY package.json package-lock.json` + `npm ci`. Imagem construída e servida localmente (página e rota SPA 200).
- **Lint limpo:** os 30 erros eram `any` no arquivo de testes de navegador; viraram um tipo `Json` (o que o `postDataJSON()` do Playwright devolve). Ficam 16 avisos, sem erro.
- **Playwright no CI** com 1 nova tentativa (só no CI), para um runner lento não reprovar um PR que não mexeu no teste.
- **Cadeia de dependências:** a instalação (CI e Docker) usa `--ignore-scripts`, então nenhum pacote roda script de instalação na máquina de build (os binários nativos vêm como pacotes opcionais do lockfile); o Playwright roda pelos scripts do projeto (`npm run e2e:install` / `e2e:critical`), sem `npx` baixando pacote na hora. Apontado pelo SonarCloud (S6505/S8543).
- **Para regerar o lock** depois de mudar dependências: `docker run --rm -v "$PWD:/w" -w /w node:20-slim npm install --package-lock-only` dentro de `nexo-main`.

- **Em produção** (merge `be47ace`): o log de build da Railway mostra `npm ci --ignore-scripts` a partir do lockfile e o `vite build` concluído; deploy `SUCCESS`; páginas 200. O hash do bundle saiu **idêntico** ao anterior (mesmo código, mesmas versões): o build ficou reproduzível.

## PR 68 — Saúde e resiliência

- **Três endpoints de saúde** (sem login, `Cache-Control: no-store`, resposta em JSON com status, nome de cada verificação, descrição curta fixa e duração — nunca texto de exceção, host ou string de conexão):
  - `/health/live`: só diz que o processo responde. Não verifica nada, para que uma queda de banco ou de cache não faça a plataforma reiniciar um processo saudável;
  - `/health/ready`: pode receber tráfego? 503 **só** se o PostgreSQL estiver fora; Redis e storage fora aparecem como `Degraded` (200);
  - `/health`: relatório completo (inclui o armazenamento de anexos do interpretador), com os mesmos códigos do ready. Continua respondendo 200 enquanto o banco estiver de pé, então os smoke tests de deploy seguem iguais.
- **Verificações:**
  - PostgreSQL: `CanConnect` com limite de 3 s;
  - Redis: `PING` com limite de 1 s; sem Redis configurado (dev, testes) é saudável; fora do ar, degrada (o cache já é fail-open);
  - storage de objetos (R2): só quando o storage está ligado; o resultado vale por 1 minuto, porque a sonda roda muitas vezes e cada consulta ao R2 é cobrada.
- **Correção encontrada no caminho:** o "ping" do R2 listava **todos os buckets da conta**, coisa que o token da aplicação não tem permissão de fazer, então falharia sempre. Agora lista no máximo um objeto do bucket da aplicação.
- **Testes:** integração (live sem verificações; ready com Postgres saudável e sem o armazenamento de anexos; relatório completo sem vazar detalhes) e unitários (Redis não configurado, storage fora degrada e não é consultado de novo por 1 minuto, storage desligado não é consultado).
- **Mudança de comportamento intencional:** antes, `/health` só olhava o armazenamento de anexos e respondia 200 mesmo com o banco fora; agora responde 503 quando o PostgreSQL cai.
- **Correções da revisão independente** (sem bloqueadores):
  - sonda cancelada pelo cliente não guarda mais "storage fora" por 1 minuto;
  - o limite de 3 s do PostgreSQL cancela a tentativa de conexão (antes ela continuava rodando num contexto já descartado);
  - nenhuma verificação anexa a exceção ao resultado (vai para o log), e o JSON troca qualquer descrição de verificação que lançou exceção por "check failed";
  - o Redis é resolvido dentro da verificação (connection string inválida vira `Degraded`, não 500);
  - o resultado guardado do storage é trocado de forma atômica;
  - o armazenamento de anexos fora agora degrada de verdade (devolvia `Unhealthy` direto e derrubaria o `/health`);
  - sondas de saúde bem-sucedidas saem do log de requisições (rodam a cada poucos segundos); falhas continuam como erro.
- **Para o dono (configuração da Railway, não mudada aqui):** apontar o healthcheck do serviço da API para `/health/ready`.

## PR 65b — Relatórios: os das outras áreas na mesma página

- A missão pediu relatórios de vendas, caixa, financeiro, estoque, produto, cliente, profissionais, Service, restaurante e Build. Os que já existiam ficavam espalhados, cada um na sua tela. A página `/relatorios` ganhou **"Outros relatórios"**, com o caminho para cada um:
  - Financeiro (competência ou caixa), Fluxo de caixa, Caixa (sessões e fechamento), Estoque e Clientes;
  - Service — financeiro (receita, comissões e resultado por profissional), Restaurante e Obras, **só para quem tem o módulo**.
- Vendas, produtos, vendedores, formas de pagamento, estoque e clientes ficam na própria página (PR 65).
- Playwright: os atalhos aparecem e levam às telas certas.
