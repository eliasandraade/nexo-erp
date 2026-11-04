# Orken ERP — Estado e maturidade (04/11/2026)

| | |
|---|---|
| **Data** | 04/11/2026 |
| **Base auditada** | `master` @ `a170ff1` (merge da PR #105, 04/11/2026 08:17 UTC) |
| **Método** | Leitura do código (backend .NET 8 em `nexo-backend/`, frontend React/TS em `nexo-main/`), contagem estática dos testes, resultados reais do GitHub Actions na HEAD, deploys da Railway lidos pelo CLI (somente leitura) e produção sondada por HTTP público sem sessão. |
| **Limites** | Nenhum dado de cliente foi lido. Nenhum banco de produção foi consultado. Das variáveis da Railway foram lidos só os **nomes** (e, para segredos do Stripe, só se estão preenchidos). Nada foi executado contra a produção além de leituras anônimas. |
| **Documento irmão** | `2026-11-04-auditoria-comercial-e-fiscal.md` (cobrança, Stripe, fiscal e financeiro). |

**Régua de evidência (qualitativa, sem percentuais):**

| Dimensão | O que conta como evidência |
|---|---|
| **Implementado** | Fluxo existe na API e na tela, ponta a ponta. |
| **Testado** | Testes automatizados no repositório: unitários, integração contra PostgreSQL real (Testcontainers) e Playwright. Testes que rodam no CI valem mais que testes fora dele. |
| **Em produção** | Deploy `SUCCESS` do commit na Railway e rota respondendo na produção (401 sem sessão indica rota publicada e protegida; 404 indica que não existe). |
| **Documentado** | Documento específico do módulo em `docs/` ou `nexo-main/docs/`. |
| **Lacunas** | O que falta ou está errado hoje, com arquivo e linha. |

Os níveis usados são **Sim**, **Parcial** e **Não**. Percentuais das auditorias anteriores aparecem só na seção de histórico (§6).

---

## 1. Resumo

- **Produção está saudável e atualizada.** Os dois serviços da Railway (`backend` e `main`) rodam `a170ff1` com deploy `SUCCESS`. `/health` responde 200 com PostgreSQL, Redis, storage de objetos e storage do interpretador saudáveis.
- **O CI real da HEAD está verde nos testes:** 1018 testes unitários e 737 de integração passaram no `backend-tests`. O frontend passou na última execução do `master` (`b439dfc`): 924 testes Vitest em 106 arquivos e 80 execuções Playwright.
- **O Quality Gate do SonarCloud falha no `master`** (check "SonarCloud Code Analysis"), como registrado na issue #84. Ele não bloqueia deploy desde a PR #95.
- **Maturidade por módulo:**
  - **Service:** o mais completo e o mais testado.
  - **Build:** o mais amplo (201 endpoints internos), com testes de frontend fortes, mas cobertura de integração fina e sem faturamento.
  - **Store:** operação de balcão íntegra no backend, com o PDV limitado na tela e **sem testes de frontend no módulo**.
  - **Menu:** fecha pedido em venda, mas tem defeitos de configuração, cozinha e cupom, também **sem testes de frontend no módulo**.
- **Nenhum módulo emite documento fiscal.** O provedor fiscal registrado é `NotConfiguredFiscalProvider` e o método de emissão não é chamado em nenhum lugar (detalhes no documento irmão).
- **Duas PRs abertas estão em conflito com o `master`:**
  - **#64** corrige uma corrida real na rotação do refresh token, que continua em produção.
  - **#85** é a finalização do Tax Intelligence e depende de decisão do dono.

---

## 2. Estado atual

### 2.1 `master`

| Item | Valor |
|---|---|
| HEAD | `a170ff178c706b5ec4c406aa846ed5fabe18fa06` — "Merge pull request #105 from eliasandraade/fix/build-progress-500" |
| Últimas PRs mergeadas | #105 (interpretador: confirmação aguarda o perfil de memória, fecha #100), #104 (playground exige empresa, fecha #102), #103 (catálogo tributário exige PlatformAdmin, fecha #101), #99 e #98 (raízes de `erp.` e `www.`) |
| Mudança desde a última execução do frontend-ci | `b439dfc..a170ff1` alterou só arquivos de `nexo-backend/` (6 arquivos), por isso o `frontend-ci` não rodou na HEAD (filtro de caminhos em `.github/workflows/frontend-ci.yml`) |

### 2.2 Deploys de produção (Railway, projeto `nexoerp`, ambiente `production`)

| Serviço | Deploy | Commit | Status | Criado em (UTC) |
|---|---|---|---|---|
| `backend` | `44087566-ca3a-4f34-8c5f-fd5b6f2de518` | `a170ff1` | SUCCESS | 2026-11-04 08:17:45 |
| `main` | `95fa3aed-1c10-490b-8cd7-d796b41fa926` | `a170ff1` | SUCCESS | 2026-11-04 08:17:45 |

Os quatro deploys anteriores de cada serviço (`b439dfc`, `b292733`, `971c585`, `c7492bc`) estão `REMOVED`, ou seja, substituídos. Fonte: `railway deployment list -p nexoerp -e production -s backend|main`. Os mesmos dois deploys aparecem como status de commit `nexoerp - backend` e `nexoerp - main` = `success` na HEAD.

**Observável sem dados de cliente (04/11/2026):**

| Sonda | Resultado |
|---|---|
| `GET https://api.erp.orken.com.br/health` | 200, `Healthy`: `postgres`, `redis`, `object-storage` ("public and private buckets"), `interpreter-storage` |
| `GET /health/ready` e `/health/live` | 200 (`ready` checa postgres, redis e storage; `live` não checa dependência) — `nexo-backend/src/Nexo.Api/Health/HealthEndpoints.cs:43-45` |
| `GET https://erp.orken.com.br/` | 307 para `/login` (PR #99); `/login` responde 200 |
| Rotas internas sem sessão: `/api/sales`, `/api/varejo/pdv/resolve-price`, `/api/cash/sessions`, `/api/restaurante/orders`, `/api/v1/service/appointments`, `/api/v1/build/projects`, `/api/fiscal/readiness`, `/api/financial/accounts`, `/api/billing/subscriptions`, `/api/platform/mrr` | 401 em todas (rota publicada e protegida) |
| Rotas públicas com identificador inexistente: `/api/public/menu/{slug}`, `/api/public/service/{slug}`, `/api/public/build/{empresa}/{obra}` | 404 em todas (sem vazamento de existência) |
| Rota inexistente e `/swagger/index.html` | 404 (Swagger não exposto) |

### 2.3 CI real na HEAD `a170ff1`

| Check | Resultado | Evidência |
|---|---|---|
| `backend-tests` (build + unitários + integração com PostgreSQL via Testcontainers) | success. Unitários: **1018/1018**. Integração: **737/737** | [run 41798403805](https://github.com/eliasandraade/nexo-erp/actions/runs/41798403805) |
| `sonarcloud` (workflow) | success | [run 41798403595](https://github.com/eliasandraade/nexo-erp/actions/runs/41798403595) |
| **SonarCloud Code Analysis** (Quality Gate do app) | **failure** (issue #84) | [check 126472258529](https://github.com/eliasandraade/nexo-erp/runs/126472258529) |
| `frontend-ci` | não disparou na HEAD (só backend mudou). Última execução no `master`: `b439dfc`, success. Vitest: **924 testes em 106 arquivos**. Playwright: **80 execuções** | [run 41793692804](https://github.com/eliasandraade/nexo-erp/actions/runs/41793692804) |

**Observações:**

- **O Quality Gate falha, mas não bloqueia deploy.** Ele falha pelo período de *new code* do `master` (#84): reliability C, security D, cobertura de código novo 56,6% contra o limite de 80%, e 24 hotspots não revisados. A PR #95 tornou o gate bloqueante só em PR, de modo que o "Wait for CI" da Railway não pule deploys do `master`.
- **Uma PR só de documentação dispara apenas o workflow `sonarcloud`.** O `backend-tests` e o `frontend-ci` têm filtro de caminhos (`nexo-backend/**` e `nexo-main/**`).
- **O Playwright do CI usa só três specs, todas com API simulada:** `nexo-main/playwright.config.ts:4` lista `experience.spec.ts` e duas specs da plataforma. Ficam fora do CI:
  - `auth.e2e.spec.ts` (17 testes contra a stack real);
  - `build-portal.spec.ts` (7);
  - `build-portal.integration.spec.ts` (17).

### 2.4 Issues e PRs abertos

| # | Tipo | Uma linha |
|---|---|---|
| **#84** | issue (bug) | O Quality Gate do `master` falha na primeira análise via CI: dívida anterior ao Tax Intelligence. Inclui 2 vulnerabilidades a triar (`FinancialService.cs:407`, `jssecurity:S8707`) e 24 hotspots. |
| **#83** | issue (enhancement) | Upgrades de dependências da triagem do piloto tributário. Duas linhas já andaram em PRs: `zod` removido na #96 e `Microsoft.Extensions.Caching.Memory` 8.0.1 na #97. A issue continua aberta para `react-router`, `SSH.NET` e `postcss-selector-parser`. |
| **#82** | issue (bug) | Teste instável `MfaTests.Sign_in_takes_a_code_once_the_second_step_is_on`, na fronteira do passo TOTP. O problema é só do teste. |
| **#81** | issue (enhancement) | 118 code smells do Sonar no Tax Intelligence, sendo 5 CRITICAL de manutenibilidade. |
| **#85** | PR, `feat/tax-intelligence-finalization` | Finalização do Tax Intelligence: 8 commits, +43.873/−1.095 linhas, 163 arquivos. Os checks estão verdes no head `f4ace2a`, mas o estado é **DIRTY** (conflito com o `master`). A própria descrição diz que "não deve ser mergeada sem decisão do PO". |
| **#64** | PR, `fix/refresh-rotation-race` | Rotação de refresh token em voo único: +212/−23 linhas, 7 arquivos. Checks verdes no head `fcce1da`, estado **DIRTY**. Aberta desde 15/10. O `master` não tem a correção: `TrySetFlagAsync` não existe em `nexo-backend/src`. A corrida continua em produção: dois refresh simultâneos criam tokens irmãos que sobrevivem à revogação, e duas abas podem deslogar todas as abas. |

---

## 3. Matriz de maturidade

### 3.1 Visão geral

| Dimensão | Store (`varejo`) | Menu (`restaurante`) | Service (`service`) | Build (`build`) |
|---|---|---|---|---|
| **Implementado** | Sim. Venda, caixa, estoque, compras e listas de preço; PDV limitado na tela | Sim. Salão, cozinha, delivery e cardápio público; configurações sem tela | Sim. Agenda, comandas, pacotes, comissões, portal e e-mails | Sim. Obras, orçamentos, diário, qualidade, gêmeo digital, previsão e portal do cliente; sem faturamento |
| **Endpoints** | 82 internos | 66 internos + 5 públicos + hub SignalR | 76 internos + 7 públicos | 201 internos + 5 da plataforma + 22 públicos |
| **Telas** (`*Page.tsx`) | 18 em 8 pastas | 10 + 2 do portal | 14 + 2 do portal | 2 páginas; a obra tem 16 abas + o portal |
| **Gate de módulo no backend** | **Parcial**: só vendas, PDV e listas de preço | Sim | Sim | Sim, exceto upload de storage |
| **Testes backend** (métodos) | Integração 64, unitários 26 | Integração 55, unitários 0 | Integração 206, unitários 154 | Integração 69, unitários 137 |
| **Testes frontend** (Vitest, declarações) | **0** no módulo | **0** no módulo (2 no rastreio do portal) | 94 | 308 |
| **E2E no CI** (API simulada) | 14 testes + rotas no laço de celular | 1 | 8 + laço | 6 + laço. Portal do cliente: 24 testes **fora do CI** |
| **Em produção** | Sim (rotas 401) | Sim (rotas 401; cardápio público 404 para slug inexistente) | Sim (rotas 401; variável `Resend__ApiKey` presente) | Sim (rotas 401; storage e storage do interpretador saudáveis) |
| **Documentado** | Parcial (só auditorias) | Parcial (`docs/ORKEN_MENU_ARCHITECTURE.md`, `nexo-main/docs/backlog-orken-menu-ux-review.md`) | Parcial (auditorias e roadmap) | Sim (`docs/build/`: plano, relatório final, contrato e OpenAPI do portal) |
| **Fiscal** | Não (sem NFC-e/NF-e) | Não (sem NFC-e) | Não (sem NFS-e) | Não (sem NFS-e/NF-e) |

**Como contar os testes:**

- **Backend:** contagem estática de métodos `[Fact]`/`[Theory]` por pasta de `nexo-backend/tests`. Uma `[Theory]` conta uma vez, o que explica a diferença para os 1018 e 737 casos executados no CI.
- **Frontend:** declarações `it(`/`test(` em `*.test.ts(x)`. Um `it.each` conta uma vez; o CI executou 924 casos.
- **Pastas que não entram em nenhum módulo:**
  - Financeiro (núcleo): integração 21, unitários 18.
  - Fiscal: integração 4, unitários 10.
  - Segurança: 76 de integração.
  - Auth: 74 de integração.
  - Tax Intelligence: integração 45, unitários 136.
  - Interpretador: integração 17, unitários 90.

### 3.2 Orken Store (`varejo`)

**Implementado**

- **Backend:**
  - `api/products` (lista, paginação, preços, imagem, ficha PDF), `api/categories`.
  - `api/stock` (ajuste, movimentações, inventário, transferências).
  - `api/sales` (itens, confirmar, cancelar, devoluções, `receipt.pdf`), `api/cash` (sessões, abrir, fechar, sangria e suprimento, PDF do fechamento).
  - `api/customers`, `api/suppliers`.
  - `api/varejo/pdv/resolve-price`, `api/varejo/price-lists`, `api/varejo/purchases`.
- **Telas:** `/pdv`, `/vendas`, `/caixa`, `/produtos`, `/estoque` (movimentações, ajustes, inventário, transferências), `/compras`, `/listas-de-preco`, `/clientes`, `/fornecedores` (`nexo-main/src/app/router/AppRouter.tsx`).
- **Integridade no backend:**
  - A confirmação trava a venda e é idempotente.
  - Troco só sai de dinheiro físico, e Pix/cartão ficam fora da gaveta.
  - Estoque sai como `SaleOutput`.
  - Dinheiro entra em gaveta aberta, líquido do troco.
  - Todo meio de pagamento vira lançamento no livro; "a prazo" vira conta a receber pendente (`nexo-backend/src/Nexo.Application/Features/Sales/SaleService.cs:152-303`).
  - Venda paga só sai por devolução, parcial ou total, idempotente e com checagem do saldo da gaveta (`SaleService.cs:312-546`).
  - A compra confirmada gera entrada de estoque e conta a pagar (`PurchaseService.cs:170-212`).

**Testado**

- **Integração:**
  - `Sales/` 36 (`PdvPaymentIntegrityTests` 11, `SaleReturnTests` 8, `PriceListTests` 8, `SaleFlowTests` 6, `PosDiscountRuleTests` 3);
  - `Cash/` 7, `Stock/` 9, `Purchases/` 9, `Reports/` 3.
- **Unitários:** `Sales/` 16, `Cash/` 5, `Reports/` 5.
- **Vitest:** nenhum arquivo de teste em `products`, `inventory`, `sales`, `cash`, `pricelists`, `purchases`, `suppliers` ou `customers`.
- **E2E (`experience.spec.ts`, API simulada):** clientes em 8 larguras de tela, PDV, caixa, devolução, inventário, compras, listas de preço, relatórios, fiscal e CPF na nota.

**Em produção:** as rotas `/api/sales`, `/api/varejo/pdv/resolve-price` e `/api/cash/sessions` respondem 401 sem sessão.

**Lacunas e riscos**

1. **O gate do módulo cobre só parte do Store.** Só `SalesController`, `PdvController` e `PriceListsController` têm `[RequireModule("varejo")]`. Produtos, estoque, caixa, clientes, fornecedores, compras, financeiro e fiscal não têm gate. Um tenant com o trial vencido continua operando caixa e estoque pela API, e o bloqueio fica só na tela (`nexo-main/src/app/router/ProtectedRoute.tsx:36-45`). Detalhes comerciais no documento irmão.
2. **"Cartão" sempre vira débito**, no PDV e na comanda do Menu (`nexo-main/src/modules/sales/hooks/use-pos-sale.ts:25`, `nexo-main/src/modules/restaurante/components/PaymentDrawer.tsx:29`). Venda no crédito é registrada como débito.
3. **Limites do PDV na tela:**
   - uma forma de pagamento por venda (`PosPaymentPanel.tsx:68`);
   - sem cliente na venda, o que impede lista de preço por cliente e venda a prazo pelo PDV (`use-pos-sale.ts:68`);
   - a finalização usa três chamadas separadas (`use-pos-sale.ts:40-91`).
4. **Estoque e cadastros:**
   - o estoque pode ficar negativo (`nexo-backend/src/Nexo.Domain/Entities/StockItem.cs:53-58`);
   - o ajuste manual não é auditado;
   - o código de barras não tem índice único (`ProductConfiguration.cs:109`).
5. **Numeração de venda por `MAX+1` sem nova tentativa** (`SaleRepository.cs:134-138`).
6. **Sem emissão de NFC-e** (documento irmão, §3).
7. **Sem nenhum teste de frontend no módulo.** Tudo o que a tela faz depende só do e2e com API simulada.

### 3.3 Orken Menu (`restaurante`)

**Implementado**

- **Backend** (`nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/`, todos com `[RequireModule("restaurante")]`):
  - áreas e mesas;
  - comandas (itens, status do item, fechar, pagar, cancelar);
  - delivery (manual, aceitar, recusar, status, entregador);
  - zonas de entrega, cupons, adicionais e fichas técnicas (CMV);
  - configurações, funcionários, despesas, financeiro e relatórios.
- **Públicos** (`Controllers/Public/PublicOrdersController.cs`): cardápio, rastreio, criar pedido, zonas de entrega e validar cupom. O pedido e o cupom têm rate limit.
- **Integração com o núcleo:**
  - Fechar a comanda cria uma venda rascunho. Pagar chama a confirmação do núcleo (gaveta, livro, troco), baixa os insumos com o custo do momento e libera a mesa (`OrderService.cs:313-503`).
  - O pedido de delivery aceito vira comanda e vira venda pelo valor cotado (teste `A_delivery_order_becomes_a_sale_charging_exactly_what_the_customer_was_quoted`).
  - Despesas entram no livro (`VerticalLedgerPostingService.cs:53-85`).

**Testado**

- **Integração:** `RestauranteFlowTests` 25, `DeliveryPortalFlowTests` 19, `EmployeeExpenseTests` 7, `FinanceiroReportTests` 2, `RecipeCardCmvTests` 2.
- **Unitários:** nenhum.
- **Vitest:** nenhum em `modules/restaurante`; 2 em `modules/portal` (rastreio).
- **E2E:** 1, adicionais, com API simulada.

**Em produção:** `/api/restaurante/orders` responde 401; `/api/public/menu/{slug}` responde 404 para slug inexistente.

**Lacunas e riscos**

1. **Todo produto ativo que não é insumo aparece no cardápio público e pode ser pedido.** `IsMenuVisible` nasce `true` (`nexo-backend/src/Nexo.Domain/Entities/Product.cs:69`). `SetMenuVisibility` (`:106`) não é chamado por nenhum endpoint nem tela.
2. **Cupom de "primeiro pedido" sempre falha.** O pedido é salvo antes da validação (`DeliveryOrderService.cs:362`). Depois disso, a contagem por telefone já inclui o próprio pedido (`:409-411`; `CouponRepository.cs:30-38` exclui só recusados e cancelados), e `Coupon.cs:143` recusa. Não há teste de cupom.
3. **Cupom ou adicional inválido deixa um pedido vazio como "Recebido" na caixa de entrada**, porque o pedido é salvo antes das validações (`DeliveryOrderService.cs:362` versus `:373` e `:404-423`).
4. **A cozinha nunca mostra adicionais.** `GET /restaurante/orders` não carrega os modificadores (`OrderRepository.cs:50-56`) e devolve todo o histórico.
5. **Concorrência:**
   - aceitar um delivery não usa transação nem trava (`DeliveryOrderService.cs:481-540`);
   - fechar a comanda não é transacional (`OrderService.cs:329-349`);
   - numeração da comanda por `MAX+1` (`OrderRepository.cs:68-72`).
6. **O resultado do restaurante distorce:**
   - a folha conta o mês inteiro, qualquer que seja o período (`FinanceiroController.cs:142-144`);
   - o CMV usa o custo de hoje, não o da venda (`:72`, `:203`);
   - devoluções de venda não entram nos números do restaurante.
7. **Configurações gravadas sem tela ou sem efeito:** couvert e taxa de serviço sem tela, tipos de pedido e horário de funcionamento não aplicados, e `MaxSelections` não aplicado no salão.
8. **O rate limit público usa a primeira entrada de `X-Forwarded-For`** (`nexo-backend/src/Nexo.Api/Program.cs:303-313`), e `GET` do cardápio não tem limite. O Build usa `ClientIpResolver`, cuja premissa sobre o proxy da Railway o próprio `docs/build/FINAL_REPORT.md` pede para confirmar em produção.
9. **Sem testes de frontend no módulo** e com 1 e2e.

### 3.4 Orken Service (`service`)

**Implementado**

- **Backend:** 14 controladores em `api/v1/service/*`:
  - agenda, bloqueios e comandas (inclusive a partir de agendamento);
  - catálogo, profissionais e assuntos;
  - pacotes e pacotes do cliente (consumo e estorno);
  - pagamentos (com estorno), comissões (lançamentos, resumo, repasses), painel financeiro e registros;
  - configurações, preset e portal.
- **Público** (`PublicServiceController.cs`): portal, catálogo, profissionais, disponibilidade, criar, consultar e cancelar pelo token. São 10 presets (`ServicePresetRegistry.cs`).
- **Itens da "Onda 2" pendentes em 09/10, hoje todos presentes:**
  - bloqueios de agenda (`TimeBlocksController`);
  - cancelamento público;
  - lembretes por e-mail (`ServiceCustomerEmailsJob.cs`);
  - painel financeiro (`FinanceController`);
  - comanda a partir do agendamento (`AgendaPage.tsx:40,128`);
  - visão de semana (`AgendaPage.tsx:64,278-279`).
- **Dinheiro:**
  - O pagamento lança um recebível liquidado no livro, com a loja (`ServiceFinancialPostingService.cs:53-57`).
  - O estorno lança a contrapartida e reverte comissões.
  - O repasse pago vira conta a pagar (`SvcCommissionService.cs:337-356`).

**Testado**

- **Unitários:** 154 em 18 arquivos.
- **Integração:** 206 em 12 arquivos. `ServiceCommissionTests` 57, `ServicePublicPortalTests` 28, `ServicePaymentsTests` 24.
- **Vitest:** 74 em `modules/service` + 20 em `modules/service-portal`.
- **E2E:** 8, com API simulada (agenda da semana, cancelamento pelo link, estorno de pacote, e-mails, financeiro, profissional por item).

**Em produção:**

- `/api/v1/service/appointments` responde 401 e `/api/public/service/{slug}` responde 404 para slug inexistente.
- A variável `Resend__ApiKey` existe no serviço `backend` (valor não lido). Sem ela, o envio cai em `ConsoleEmailService` (`nexo-backend/src/Nexo.Infrastructure/DependencyInjection.cs:301`).

**Lacunas e riscos**

1. **Comissão sem dinheiro recebido.** Um agendamento concluído sem comanda gera comissão sobre o preço (`SvcCommissionService.cs:200-210`). O consumo de pacote também gera comissão, sem checar se o pacote foi pago (`:220-233`). É uma regra de negócio a confirmar com o dono.
2. **Itens cobertos por pacote entram no total da comanda** (`SvcOrder.cs:63-67`, `SvcOrderItem.cs:47`). Só o painel financeiro os exclui (`SvcFinanceService.cs:52`). Há risco de saldo em aberto que nunca fecha.
3. **Pagamentos do Service não passam pelo caixa.** Não há referência a sessão de caixa no código do módulo. Em negócio misto, o dinheiro físico do Service não aparece na conferência da gaveta.
4. **Não existe papel "Profissional"** (`AppRoles.cs`). Os registros (prontuário) são apagados sem trilha (`SvcRecordEntryService.cs:85-90`) e a lista de comandas não tem paginação (`useServiceDashboard.ts:33-36`).
5. **O rate limit público usa a primeira entrada de `X-Forwarded-For`** (`Program.cs:276-279`). Portal, catálogo, profissionais e disponibilidade não têm limite.

### 3.5 Orken Build (`build`)

**Implementado**

- **13 controladores internos em `api/v1/build`:**
  - obras e etapas;
  - orçamentos (versões, PDF, modelos, conversão);
  - comercial (aditivos, escolhas, materiais, medições);
  - diário com fotos;
  - progresso e linha de base, previsão com clima;
  - qualidade (inspeções, não conformidades, entrega, garantia);
  - captura da realidade, gêmeo digital (IFC/GLB), painel, portal e o interpretador.
- **Plataforma:** `api/platform/build` e configurações por tenant.
- **Portal "Minha Obra":** 22 endpoints públicos com senha BCrypt, sessão em hash, bloqueio por cliente e por portal, e limites por IP (`Program.cs:357-360`).
- Chegou com a PR #90 ("Orken Build completo, Waves 1–6", 31/10).

**Testado**

- **Unitários:** 137.
- **Integração:** 69, em 9 arquivos.
- **Vitest:** 277 em `modules/build` (47 arquivos) + 31 em `modules/build-portal`.
- **E2E no CI:** 6 com API simulada.
- **Fora do CI:** `build-portal.spec.ts` (7) e `build-portal.integration.spec.ts` (17, contra API real e Postgres descartável).

**Em produção:**

- `/api/v1/build/projects` responde 401 e `/api/public/build/{empresa}/{obra}` responde 404 para obra inexistente.
- `/health` mostra `object-storage` e `interpreter-storage` saudáveis.
- As variáveis `Integrations__Features__StorageEnabled` e `Integrations__Features__WeatherEnabled` existem (valores não lidos).

**Lacunas e riscos**

1. **Sem faturamento da obra.** Aprovar orçamento ou aditivo muda só `BudgetApproved`, e a medição aprovada só move o progresso (`BuildChangeOrderService.cs:13-17`, `BuildMeasurementService.cs:12-16`). Nada vira conta a receber.
2. **O livro não tem dimensão de obra.** As movimentações do interpretador entram com `storeId` nulo (`VerticalLedgerPostingService.cs:105-110`).
3. **Cobertura de integração fina** para a superfície: 69 testes para mais de 200 endpoints, e a qualidade tem 4 testes. Os e2e do portal do cliente, inclusive o que roda contra API real, não estão no CI (`nexo-main/playwright.config.ts:4`).
4. **O diário de obra não pode ser editado na tela.** `useUpdateDailyLog` não é usado em nenhum `.tsx`. Remover uma foto do diário deixa o arquivo no storage (`BuildDailyLogService.cs:149-163`).
5. **O upload de storage checa o papel, mas não o módulo** (`nexo-backend/src/Nexo.Api/Controllers/Integrations/StorageController.cs:32-49`).
6. **Limites conhecidos** (`docs/build/FINAL_REPORT.md` §10 e §13):
   - upload grande passa pela API;
   - o estado do gêmeo carrega todos os elementos;
   - reaprovar orçamento depois de aditivos zera `BudgetApproved`;
   - o IP do cliente atrás do proxy precisa ser confirmado.
7. **O interpretador depende de provedores configurados pelo SuperAdmin.** Sem provedor, ou com o limite mensal atingido, ele cai em sugestão por regras ou responde 422 (provedor indisponível). Outras falhas do provedor respondem 503 (serviço do interpretador do Build em `nexo-backend/src/Nexo.Application/Modules/Build/`, linhas 171-192). As chaves dos provedores ficam no banco, por isso não foram verificadas aqui.

---

## 4. Riscos transversais

| Risco | Evidência |
|---|---|
| Corrida no refresh token em produção | PR #64 aberta e em conflito; `TrySetFlagAsync` ausente no `master` |
| Quality Gate do `master` falhando, com 2 vulnerabilidades e 24 hotspots sem triagem | Issue #84, check "SonarCloud Code Analysis" = failure na HEAD |
| Não há staging: todo merge no `master` vai para produção | Projeto `nexoerp` só tem o ambiente `production`. O deploy de `a170ff1` foi criado no push do merge (08:17:45 UTC) e o "Wait for CI" da Railway (descrito na PR #95) é o único freio |
| Testes contra a stack real fora do CI | `auth.e2e.spec.ts` e as duas specs do portal do Build (`playwright.config.ts:4`) |
| Frontend de Store e Menu sem testes unitários | Contagem do §3.1 |
| Teste instável conhecido | Issue #82 |

---

## 5. O que mudou desde 15/10/2026

As PRs mergeadas depois da auditoria `ORKEN_PRODUCT_STATUS_1_0.md` (#77):

| Frente | PRs | Efeito |
|---|---|---|
| Tax Intelligence | #78, #79, #80 | Domínio tributário versionado em modo sombra, desligado por padrão (`docs/tax-intelligence/README.md`). A finalização está na #85, aberta |
| Interpretador e plataforma | #87, #88, #89, #103, #104, #105 | Configuração de provedores pelo SuperAdmin, MFA e sessões revogáveis do SuperAdmin, limites antes da chamada paga, correções de 04/11 |
| Build | #90, #91 | Waves 1–6 (portal, comercial, qualidade, gêmeo digital, interpretador, previsão); conteúdo privado falha fechado |
| Hosts e segurança | #92, #93, #94, #98, #99 | `erp.orken.com.br` e `api.erp.orken.com.br`, cabeçalhos de segurança e HSTS, raízes de `www.` e `erp.` |
| CI e dependências | #95, #96, #97 | Gate do Sonar só em PR; `zod` removido; `Caching.Memory` 8.0.1 |

**Testes, de 15/10 para 04/11:**

| Suíte | 15/10 | 04/11 |
|---|---:|---:|
| Unitários | 584 | **1018** |
| Integração | ~515 | **737** |
| Vitest | 211 | **924** |
| Playwright no CI | 57 | **80** execuções |

**Pendências de 15/10 conferidas no código de hoje:**

| Pendência listada em 15/10 | Hoje |
|---|---|
| NFC-e | Continua inexistente |
| Pagamento dividido no PDV e finalização atômica | Continuam abertos (PDV com uma forma; três chamadas) |
| Ajuste de estoque auditado | Continua aberto |
| CMV com custo histórico / DRE | Continua aberto: o financeiro mostra "Resultado (antes do CMV)" (`nexo-main/src/modules/financial/pages/FinanceiroPage.tsx:101`) |
| Fechamento de comanda atômico | Continua aberto (`OrderService.cs:329-349`) |
| Build: converter orçamento, custo por etapa e editar diário | Conversão e custo por etapa existem; a edição do diário segue sem tela |
| Service: bucket do prontuário, papel "profissional", paginação | O storage privado está saudável em produção (`/health`); papel e paginação continuam ausentes |
| Trial no backend | Continua aberto: o núcleo não tem gate de módulo |
| Billing | Ativo em produção (o webhook responde 400 sem assinatura, não 404), mas só `restaurante` e `build` têm Price ID (documento irmão) |
| IP real atrás do proxy | O Build usa `ClientIpResolver`; Service e Menu continuam com a primeira entrada de `X-Forwarded-For` |

---

## 6. Histórico (não usar como estado atual)

As auditorias anteriores usavam percentuais de maturidade:

- **09/10/2026** (`ORKEN_PRODUCT_STATUS_2026-10.md`): plataforma ~45%, Service ~65%, Menu 50%, Build 55%, Billing 30%, Financeiro 20%, Fiscal 0%.
- **15/10/2026** (`ORKEN_PRODUCT_STATUS_1_0.md`): Store ~70%, Menu ~68%, Service ~80%, Build ~65%, Billing ~45%, Financeiro ~70%.

Esta auditoria **não** atualiza esses números. A régua acima é qualitativa e cada célula aponta para a evidência. As mudanças desde então estão no §5.
