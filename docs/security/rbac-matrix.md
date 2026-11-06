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
| Restaurante: tempo real (SignalR `RestaurantHub`; quando a sessão acaba: [sessoes-e-tempo-real.md](sessoes-e-tempo-real.md)) | RestaurantOps | — | — | — | — |
| Restaurante: delivery | Floor | Floor | Floor | — | cancelar / recusar: Floor |
| Restaurante: adicionais, configurações | RestaurantOps | Management | Management | Management | — |
| Restaurante: cupons, zonas | Floor | Management | Management | Management | — |
| Restaurante: ficha técnica (tem custos) | Stock | Stock | Stock | Stock | — |
| Restaurante: funcionários, despesas, financeiro, relatórios | Management | Management | Management | Management | Management |
| Build (obras, orçamentos, etapas, diário) | Management | Management | Management | Management | aprovar orçamento: Management |
| Interpreter (lançamentos, anexos, vocabulário) | Management | Management | Management | Management | confirmar / estornar: Management |
| Service (todas as telas) | Management (preset e settings: Todos) | Management | Management | Management | Management |
| Tax Intelligence (cálculos, simulações, oportunidades, créditos, split) | Management | Management | — (versões imutáveis) | — | nova versão do perfil tributário e aprovação de oportunidade: Directors |
| Tax Intelligence: catálogo de regras e fontes jurídicas | Management (leitura) | policy Platform | só status, policy Platform | — | ativar regra exige fontes conferidas; revisão profissional, aprovação interna para piloto e sua retirada, janela e participantes do piloto: policy Platform, auditadas |
| Platform admin | policy Platform | policy Platform | policy Platform | policy Platform | policy Platform |
| Público (cardápio, agendamento, login) | anônimo, com rate limit | anônimo, com rate limit | — | — | — |
| Relatórios de violação de CSP (`POST /api/csp-reports`, ver `docs/security/csp.md`) | — | anônimo, com rate limit por IP; responde sempre 204, sem banco | — | — | — |

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
- **A empresa nunca perde a última Diretoria ativa.** Rebaixar, inativar, bloquear ou excluir o último
  usuário ativo da Diretoria responde `409` com `code: "last_director"` e nada muda: nem o carimbo de
  segurança, nem as sessões. Vale também para a própria pessoa. Quem está em `PendingVerification`,
  `Inactive` ou `Blocked` não conta. Promover, reativar e editar nome ou e-mail seguem livres, e uma
  empresa que já está sem Diretoria pode ser alterada em tudo que não tire uma, para o estado poder ser
  consertado.
  - A regra está em `NexoDbContext.SaveChangesAsync` (`LastDirectorGuard`), então nenhum caminho que grave
    pelo EF escapa dela. `PUT /users/{id}` a aplica também antes de mexer em qualquer coisa, para a recusa
    não encerrar sessão nenhuma.
  - Dois diretores se rebaixando ao mesmo tempo, em conexões diferentes, nunca passam os dois: a decisão é
    tomada sob um advisory lock da empresa, na mesma transação da escrita, sobre o que já foi confirmado.
    Se a espera pelo lock passar de 5 s, a resposta é `409` com `code: "users_busy"` (tente de novo).
  - Não existe exclusão de usuário nem de empresa. `UserWriteSurfaceTests` e `LastDirectorTests` falham se
    alguém criar uma rota, uma remoção ou uma escrita em massa que contorne a regra.
  - Toda recusa fica na auditoria (`user_last_director_refused`, severidade `warning`), com o id de quem tentou, o id do alvo, a
    empresa e o papel e a situação pedidos, sem nome, e-mail nem login. A entrada é gravada em conexão própria
    (`ILastDirectorRefusalAudit`), porque a transação recusada é desfeita. Nada espera por ela: uma auditoria que falha ou trava não
    muda a resposta e não prende o lock da empresa. O preço é que um processo que morra logo depois da recusa pode perder a entrada.
    A falha ao gravar vai para o log de erros. Aparece no log de atividade da plataforma e na tela de auditoria da empresa.
- **Editar um usuário não libera acesso de quem não confirmou o e-mail.** O link enviado por e-mail é o único caminho de
  `PendingVerification` para `Active`.

  | Situação atual | Pedido no `PUT /users/{id}` | Resultado |
  |---|---|---|
  | `PendingVerification` | sem status, ou `pendingverification` | continua pendente, e o resto do formulário é salvo |
  | `PendingVerification` | `active` | `409` `email_not_verified`, e nada muda |
  | `PendingVerification` | `inactive`, `blocked` | para: os links enviados deixam de valer |
  | `Inactive`, `Blocked`, sem nunca ter confirmado | `active`, `pendingverification` | volta para `PendingVerification`, e é preciso um link novo |
  | `Inactive`, `Blocked`, com e-mail confirmado | `active` | volta para `Active`, como sempre |
  | `Active`, `Inactive`, `Blocked`, com e-mail confirmado | `pendingverification` | `409` `status_not_editable`, e nada muda |

  Trocar o e-mail de quem nunca confirmou também encerra o link já enviado, que não prova nada sobre o endereço novo. Sem coluna nova: o
  token de verificação só é apagado quando um link é seguido, então ele marca quem nunca confirmou (`User.EmailNeverVerified`).

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
- **`LastDirectorTests`** (comportamento e concorrência, com PostgreSQL de verdade): a última Diretoria ativa
  não sai por nenhum caminho, e dois diretores agindo ao mesmo tempo nunca deixam a empresa sem nenhum. Cada recusa fica na
  auditoria uma única vez, mesmo com a auditoria falhando ou travada, sem mudar a resposta nem prender o lock.
- **`PendingUserEditTests`** (comportamento): editar um usuário nunca libera o acesso de quem não confirmou o e-mail, e parar e
  reativar a pessoa não contorna o link.

## Sessão no navegador

A autorização de todas as rotas vem só do access token em `Authorization: Bearer` (no hub, também em `access_token` na query). Nenhuma rota autentica por cookie.

O cookie `__Host-nexo_rt` é o refresh token: vale apenas em `auth/refresh`, `auth/switch-store` e `auth/logout` (e em `platform/auth/refresh`), com `X-Orken-Session: cookie` e `Origin` da lista. `auth/logout`
é a única rota que aceita a requisição sem access token, e só nesse modo. Detalhes em [sessoes-e-tempo-real.md](sessoes-e-tempo-real.md).
