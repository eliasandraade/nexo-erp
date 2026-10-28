# Matriz de autorização (RBAC) — ORKEN

> Fonte de verdade da autorização no backend. A UI (`nexo-main/src/app/router/routes.ts`) apenas
> esconde o que o backend já nega. Atualizado em 2026-10-14 (PR 42).

## Papéis

| Papel | Quem é |
|---|---|
| **Diretoria** | Dono. Tudo, mais usuários e auditoria. |
| **Gerente** | Gestão da operação: dinheiro, relatórios, configurações, cancelamentos. |
| **Vendedor** | Opera o PDV, o salão e o delivery. |
| **Estoquista** | Mantém catálogo, estoque e compras. |
| **Cozinha** | Opera a cozinha (KDS). |
| *Platform admin* | Operador do SaaS (policy `Platform`). **Não é usuário de tenant:** o token dele é recusado nas rotas de tenant. |

Os conjuntos estão em `Nexo.Api/Authorization/AppRoles.cs`:

| Constante | Papéis |
|---|---|
| `Directors` | Diretoria |
| `Management` | Gerente, Diretoria |
| `Sales` | Gerente, Diretoria, Vendedor |
| `Stock` | Gerente, Diretoria, Estoquista |
| `Floor` | Gerente, Diretoria, Vendedor |
| `RestaurantOps` | Gerente, Diretoria, Vendedor, Cozinha |
| `StockReaders` | Gerente, Diretoria, Estoquista, Vendedor |

"Todos" na matriz abaixo significa qualquer usuário autenticado do tenant.

## Matriz área × ação

| Área | GET | CREATE | UPDATE | DELETE / desativar | Ação financeira / sensível |
|---|---|---|---|---|---|
| Usuários | lista: Management · por id: o próprio ou Management | Directors | Directors | — | trocar senha: só a própria · reset: Directors |
| Auditoria | Directors | — | — | — | — |
| Configurações | Todos | — | Management | — | — |
| Loja / slug público | Todos | — | Management | — | — |
| Billing | Todos (status) | — | — | — | checkout/portal: Management |
| Financeiro (contas, lançamentos, livro) | Management | Management | Management | Management | Management |
| Dashboard / relatórios | Management | — | — | — | — |
| Produtos / categorias | Todos | Stock | Stock (inclui preço) | Stock | — |
| Custo de compra do produto | Stock | Stock | — | — | — |
| Clientes | Sales (dados pessoais: fora de Estoquista e Cozinha) | Management | Management | Management | — |
| Fornecedores | Stock | Management | Management | Management | — |
| Estoque | saldos: StockReaders (o PDV e o salão marcam itens sem estoque) · movimentações: Stock | — | — | — | ajuste: Stock |
| Inventário / transferências | Stock | Stock | contagem: Stock | cancelar: Stock | aplicar inventário / transferir: Stock |
| Compras | Stock | Stock (pedido, itens) | Stock | Stock (remover item) | confirmar / cancelar: Management |
| Listas de preço | Management | Management | Management | Management | — |
| PDV: preço resolvido | Sales | — | — | — | — |
| Vendas | lista: Management · por id / recibo: Sales | Sales | itens: Sales | — | confirmar: Sales · **cancelar: Management** |
| Caixa | sessão aberta: Sales · demais: Management | abrir: Management | — | — | fechar, sangria, suprimento: Management |
| Storage (upload/exclusão) | — | Stock | — | Stock | — |
| Restaurante: áreas / mesas | RestaurantOps | Management | Management · status da mesa: Floor | — | — |
| Restaurante: comandas | RestaurantOps | Floor | itens: Floor · status do item: RestaurantOps | remover item: Floor | fechar, pagar, cancelar (antes de pagar): Floor |
| Restaurante: tempo real (SignalR `RestaurantHub`) | RestaurantOps | — | — | — | — |
| Restaurante: delivery | Floor | Floor | Floor | — | cancelar / recusar: Floor |
| Restaurante: adicionais, configurações | RestaurantOps | Management | Management | Management | — |
| Restaurante: cupons, zonas | Floor | Management | Management | Management | — |
| Restaurante: ficha técnica (tem custos) | Stock | Stock | Stock | Stock | — |
| Restaurante: funcionários, despesas, financeiro, relatórios | Management | Management | Management | Management | Management |
| Build (obras, orçamentos, etapas, diário) | Management | Management | Management | Management | aprovar orçamento: Management |
| Interpreter (lançamentos, anexos, vocabulário) | Management | Management | Management | Management | confirmar / estornar: Management |
| Service (todas as telas) | Management (preset e settings: Todos) | Management | Management | Management | Management |
| Tax Intelligence (cálculos, simulações, oportunidades, créditos, split) | Management | Management | — (versões imutáveis) | — | nova versão do perfil tributário e aprovação de oportunidade: Directors |
| Tax Intelligence: catálogo de regras e fontes jurídicas | Management (leitura) | policy Platform | só status, policy Platform | — | ativar regra exige fontes conferidas; revisão profissional, aprovação interna para piloto e sua retirada: policy Platform, auditadas |
| Platform admin | policy Platform | policy Platform | policy Platform | policy Platform | policy Platform |
| Público (cardápio, agendamento, login) | anônimo, com rate limit | anônimo, com rate limit | — | — | — |

### Decisões que merecem nota

- **Tax Intelligence: o catálogo é da plataforma.** Regras e fontes jurídicas valem para todos os tenants e só o operador da plataforma as cria ou muda de status; um tenant nunca edita legislação. Aprovar uma oportunidade e criar versão do perfil (mudança de regime) é Diretoria; revisar, responder requisitos e anexar evidências é Management. Detalhes em `docs/tax-intelligence/architecture.md`.
- **Cancelar comanda é Floor.** O domínio já impede cancelar uma comanda paga (`Closed`), e antes do
  pagamento ela não tem efeito financeiro. Cancelar **venda** (já paga, com efeito no caixa e no
  financeiro) exige Management. O estorno de comanda paga entra na política de reversões (PR 45).
- **O caixa é operado pela gestão.** O vendedor só lê a sessão aberta para vender no PDV. Isso
  espelha a UI, onde `/caixa` é só gestão. O "Caixa 2.0" (PR 44) revisita o caixa por operador.
- **Clientes:** a criação fica com a gestão porque nenhuma tela operacional cria cliente. O delivery
  cria o cliente pelo próprio fluxo do pedido.
- **Credenciais:**
  - `change-password`, `validate-manager` e `verify-manager` usam o rate limit `credential-check`,
    particionado pelo usuário autenticado. Ele não pode ser forjado via `X-Forwarded-For`, e vários
    terminais da mesma loja não se bloqueiam entre si.
  - `register` e `resend-verification`, que são anônimos, usam `auth-login`.
  - Trocar senha só vale para a própria conta. Antes, a resposta 204/403 deixava adivinhar a senha
    de um colega.
- **Preço de venda:** o Estoquista altera preço porque a tela `/produtos` já é dele. Se o negócio
  quiser separar quem cadastra de quem precifica, basta trocar uma linha (`PATCH /products/{id}/prices`).

## Pendências conhecidas (fora do escopo do PR 42)

- **Custo exposto:** `GET /products` devolve `CostPrice` a qualquer papel. Para esconder o custo de
  Vendedor e Cozinha é preciso um DTO separado. Vai junto com as listas de preço (PR 48).
- **Categorias:** `DELETE /categories/{id}` é chamado pela UI (`ManageCategoriesDialog`), mas a
  ação não existe no backend. É um bug anterior a este PR.

## Como isso é garantido

- **`RbacCoverageTests`** (guard estrutural): falha se alguma ação da API não tiver
  `[Authorize(Roles/Policy)]`, `[AllowAnonymous]` ou uma entrada deliberada na allowlist de leituras
  abertas a qualquer usuário do tenant. Endpoint novo sem papel quebra o build.
- **`RbacMatrixTests`** (comportamento): um usuário de cada papel chama ações representativas de
  cada área por HTTP. O teste também confirma que o token do platform admin é recusado nas rotas de
  tenant.
- O isolamento entre tenants é coberto por `TenantIsolationTests` e pelos query filters globais.
