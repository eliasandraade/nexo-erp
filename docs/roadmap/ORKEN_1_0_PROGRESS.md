# ORKEN 1.0 — Progresso

> Registro contínuo da missão ORKEN 1.0, um PR por linha.
> - Linha de base: `docs/audits/ORKEN_PRODUCT_STATUS_2026-10.md` (~45% de maturidade comercial).
> - Ordem planejada: PRs 42 → 74. A ordem pode mudar quando aparecer uma dependência real.

| PR | Tema | Merge | Migration | Estado |
|---|---|---|---|---|
| #42 | RBAC backend completo | `8022737` | — | Em produção |
| #43 | PDV: integridade de pagamentos | `4724495` | `AddSalePaymentTenderedAndChange` (aditiva) | Em produção |
| #44 | Caixa 2.0 | `b6f55c6` | `AddCashSessionClosingDifference` (aditiva + backfill do histórico) | Em produção |
| #45 | Cancelamento, estorno e devolução | — | `AddSaleReturns` (aditiva) | Em revisão |

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
- **Riscos restantes:** `CostPrice` exposto em `GET /products` (resolver no PR 48); `DELETE /categories/{id}` não existe.
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
