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
| `RecipeReaders` | Gerente, Diretoria, Estoquista, Cozinha |

"Todos" na matriz abaixo significa qualquer usuário autenticado do tenant.

## Matriz área × ação

| Área | GET | CREATE | UPDATE | DELETE / desativar | Ação financeira / sensível |
|---|---|---|---|---|---|
| Usuários | lista: Management · por id: o próprio ou Management | Directors | Directors | — | trocar senha: só a própria · reset: Directors |
| Auditoria | Directors | — | — | — | — |
| Configurações | Todos | — | Management | — | — |
| Loja / slug público | Todos | — | Management | — | — |
| Billing | Todos (status) | — | — | — | checkout/portal: Management |
| Financeiro (contas, lançamentos) | Management | Management | Management | Management | Management |
| Dashboard / relatórios | Management | — | — | — | — |
| Produtos / categorias | Todos | Stock | Stock (inclui preço) | Stock | — |
| Custo de compra do produto | Stock | Stock | — | — | — |
| Clientes | Todos | Management | Management | Management | — |
| Fornecedores | Stock | Management | Management | Management | — |
| Estoque | Stock | — | — | — | ajuste: Stock |
| Compras | Stock | Stock (pedido, itens) | Stock | Stock (remover item) | confirmar / cancelar: Management |
| Listas de preço | Management | Management | Management | Management | — |
| PDV: preço resolvido | Sales | — | — | — | — |
| Vendas | lista: Management · por id / recibo: Sales | Sales | itens: Sales | — | confirmar: Sales · **cancelar: Management** |
| Caixa | sessão aberta: Sales · demais: Management | abrir: Management | — | — | fechar, sangria, suprimento: Management |
| Storage (upload/exclusão) | — | Stock | — | Stock | — |
| Restaurante: áreas / mesas | RestaurantOps | Management | Management · status da mesa: Floor | — | — |
| Restaurante: comandas | RestaurantOps | Floor | itens: Floor · status do item: RestaurantOps | remover item: Floor | fechar, pagar, cancelar (antes de pagar): Floor |
| Restaurante: delivery | Floor | Floor | Floor | — | cancelar / recusar: Floor |
| Restaurante: adicionais, configurações | RestaurantOps | Management | Management | Management | — |
| Restaurante: cupons, zonas | Floor | Management | Management | Management | — |
| Restaurante: ficha técnica | RecipeReaders | Stock | Stock | Stock | — |
| Restaurante: funcionários, despesas, financeiro, relatórios | Management | Management | Management | Management | Management |
| Build (obras, orçamentos, etapas, diário) | Management | Management | Management | Management | aprovar orçamento: Management |
| Interpreter (lançamentos, anexos, vocabulário) | Management | Management | Management | Management | confirmar / estornar: Management |
| Service (todas as telas) | Management (preset e settings: Todos) | Management | Management | Management | Management |
| Platform admin | policy Platform | policy Platform | policy Platform | policy Platform | policy Platform |
| Público (cardápio, agendamento, login) | anônimo, com rate limit | anônimo, com rate limit | — | — | — |

### Decisões que merecem nota

- **Cancelar comanda é Floor.** O domínio já impede cancelar uma comanda paga (`Closed`), e antes do
  pagamento ela não tem efeito financeiro. Cancelar **venda** (já paga, com efeito no caixa e no
  financeiro) exige Management. O estorno de comanda paga entra na política de reversões (PR 45).
- **O caixa é operado pela gestão.** O vendedor só lê a sessão aberta para vender no PDV. Isso
  espelha a UI, onde `/caixa` é só gestão. O "Caixa 2.0" (PR 44) revisita o caixa por operador.
- **Clientes:** a criação fica com a gestão porque nenhuma tela operacional cria cliente. O delivery
  cria o cliente pelo próprio fluxo do pedido.
- **Credenciais:** `change-password`, `validate-manager`, `verify-manager`, `register` e
  `resend-verification` passaram a ter rate limit (`auth-login`). Trocar senha só vale para a
  própria conta: antes, a resposta 204/403 deixava adivinhar a senha de um colega.

## Como isso é garantido

- **`RbacCoverageTests`** (guard estrutural): falha se alguma ação da API não tiver
  `[Authorize(Roles/Policy)]`, `[AllowAnonymous]` ou uma entrada deliberada na allowlist de leituras
  abertas a qualquer usuário do tenant. Endpoint novo sem papel quebra o build.
- **`RbacMatrixTests`** (comportamento): um usuário de cada papel chama ações representativas de
  cada área por HTTP. O teste também confirma que o token do platform admin é recusado nas rotas de
  tenant.
- O isolamento entre tenants é coberto por `TenantIsolationTests` e pelos query filters globais.
