# ORKEN 1.0 — Progresso

> Registro contínuo da missão ORKEN 1.0, um PR por linha.
> - Linha de base: `docs/audits/ORKEN_PRODUCT_STATUS_2026-10.md` (~45% de maturidade comercial).
> - Ordem planejada: PRs 42 → 74. A ordem pode mudar quando aparecer uma dependência real.

| PR | Tema | Merge | Migration | Estado |
|---|---|---|---|---|
| #42 | RBAC backend completo | `8022737` | — | Em produção |
| #43 | PDV: integridade de pagamentos | — | `AddSalePaymentTenderedAndChange` (aditiva) | Em revisão |

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
- **Decisão provisória:** cartão é tratado como recebido na hora. O prazo e as taxas da adquirente entram com as contas financeiras, nos PRs 49–52.
- **Testes:**
  - `SalePaymentPlanTests`: 18 testes unitários.
  - `PdvPaymentIntegrityTests`: 9 testes de integração (troco, Pix/cartão fora da gaveta, misto, retry, concorrência, caixa fechado).
