# ORKEN — Estado Atual do Produto

| | |
|---|---|
| **Data da auditoria** | 09/10/2026 |
| **Commit auditado** | `master` @ `400f1f1` (após os PRs #35–#39, ver §17). |
| **Ambiente** | Código local completo (backend .NET 8, frontend React/TS) e PostgreSQL via Testcontainers. Produção foi checada de fora, por HTTP público (Railway: `backend-production-b2bc.up.railway.app`, `app.orken.com.br`). Não houve acesso ao painel Railway (CLI sem autenticação) nem credenciais de produção. |
| **Método** | Sete frentes rastreando UI → API → Application → Domain → EF → migration. Relatórios brutos, com evidências por arquivo e linha, em [`docs/audits/2026-10-raw/`](2026-10-raw/). Este documento consolida esses relatórios, corrige o que mudou depois deles e é a fonte factual de status a partir desta data. |

**Escala de status:**

| Selo | Significado |
|---|---|
| 🟢 PRODUÇÃO | Ponta a ponta e confiável |
| 🟡 FUNCIONAL / INCOMPLETO | Fluxo principal funciona; faltam partes relevantes |
| 🟠 PARCIAL | Partes importantes existem, mas não formam uma funcionalidade |
| 🔴 NÃO FUNCIONAL | Há estrutura, mas o fluxo não funciona |
| ⚪ SCAFFOLD | Só estrutura inicial |
| ⚫ LEGADO / MORTO | Abandonado ou substituído |

**Escala de maturidade:** 0–10 conceito · 10–30 início · 30–50 parcialmente utilizável · 50–70 funcional incompleto · 70–90 maduro · 90–100 pronto.

---

## 1. Resumo executivo

**O ORKEN está aproximadamente 45% maduro como plataforma SaaS comercial.**

O ORKEN é um ERP modular e multi-tenant para pequenos negócios brasileiros, com quatro verticais sobre um núcleo comum:

| Vertical | Chave do módulo | Situação |
|---|---|---|
| **Orken Store** (varejo) | `varejo` | Produtos, estoque, PDV, caixa, vendas |
| **Orken Menu** (restaurante) | `restaurante` | Salão, cozinha, delivery, cardápio público, CMV |
| **Orken Service** | `service` | Agenda, comandas, pacotes, comissões, portal público de agendamento, nove presets por ramo |
| **Orken Build** | `build` | Gestão de obras |

Ao redor dos módulos há um núcleo (auth, multi-loja, usuários, financeiro), um backoffice de plataforma (super-admin, impersonação, métricas) e uma integração Stripe ainda desligada.

### O que já é produto

- **Orken Service.** É o módulo mais maduro (~65%).
  - A Onda 1 de encerramento está **completa e em produção**: preset barbearia, comanda, pagamento lançado no financeiro, comissões ponta a ponta com estorno e repasse.
  - Tem a melhor cobertura de testes do repositório: 189 testes de integração e ~150 unitários.
  - O portal público de agendamento com branding funciona.
  - Faltam a Onda 2 (bloqueios de agenda, cancelamento público, lembretes, dashboard financeiro) e alguns furos de UX: não há como abrir a comanda a partir do agendamento, e a agenda mostra só um dia.
- **Núcleo de autenticação e multi-tenancy.** O desenho é sólido: filtros globais por tenant e loja em todas as entidades de negócio, interceptor e cerca de 130 testes de segurança.
- **Backoffice da plataforma** (~70%).

### Onde estão os maiores gaps

1. **Integridade do varejo.**
   - O PDV quebra quando há troco.
   - PIX e cartão entram como dinheiro na gaveta.
   - O cancelamento de venda pela tela é impossível.
   - Ajustes de estoque estavam invertidos e as vendas aceitavam valores negativos — **corrigidos no PR #36**.
2. **Financeiro.**
   - Não há tela de contas a pagar e a receber.
   - Existem cinco registros financeiros paralelos por módulo, sem consolidação.
   - O dono não vê no ERP o dinheiro que o Service e o varejo já lançam.
3. **Autorização no backend.**
   - Até esta auditoria, praticamente só o frontend checava papel.
   - Os PRs #36 e #38 fecharam financeiro, auditoria, billing, ajuste de estoque e todo o Service.
   - Restaurante, Build, cancelamento de venda e caixa continuam abertos a qualquer papel do tenant.
4. **Billing.** Não está pronto para cobrar: o Stripe está desligado, há um bug de webhook que deixa assinaturas sem controle de acesso, e `varejo` e `service` não têm preço configurado.
5. **Restaurante.**
   - Pedidos de delivery e do portal **nunca viram venda**, então ficam fora do caixa, do estoque e dos relatórios.
   - Comanda com adicional pago não podia ser paga — **corrigido no PR #36**.
6. **IA.** Não existe. Nenhuma chamada real a LLM ou OCR. O "Claude" é um stub, e o botão com ícone de brilho só roda regex.
7. **Engenharia.**
   - Não há CI de frontend.
   - O lockfile do frontend está no `.gitignore`, então os builds não são reproduzíveis.
   - O `/health` não checa banco nem Redis.
   - O refresh token depende de Redis.
   - Não há observabilidade além do console.

---

## 2. Health geral

| Área | Estado em 09/10/2026 | Evidência |
|---|---|---|
| Backend build | ✅ 0 erros (8 avisos NuGet de versão) | `dotnet build Nexo.sln` |
| Frontend build | ✅ | `npm run build` (Vite) |
| Backend testes unitários | ✅ **383/383** | `Nexo.UnitTests` (inclui `JwtSecretPolicyTests`) |
| Backend testes de integração | ✅ **367/367** | `Nexo.IntegrationTests` (Testcontainers PostgreSQL) |
| Frontend testes (Vitest) | ✅ **178/178**, 18 arquivos | Concentrados em Service, portal, workspace e auth |
| E2E | ⚠️ 17 testes Playwright (só auth), **não executados** | `nexo-main/e2e/auth.e2e.spec.ts`. Exigem a stack rodando e não estão no CI |
| TypeScript | ✅ 0 erros (eram 18; alguns eram bugs reais) | `npm run typecheck` |
| Lint | ✅ 0 erros, 16 avisos (fast-refresh / exhaustive-deps) | `npx eslint .` |
| Migrations | ✅ 43 migrations, a última `20261009201827_AddServiceCommissions` (aditiva). Aplicadas do zero em todas as suítes de integração e em produção no deploy do PR #35 | Boot limpo em produção |
| CI | 🟡 Só backend: `backend-tests.yml`, mais SonarCloud e GitGuardian como apps. **Sem CI de frontend** | `.github/workflows/` |
| Produção | ✅ `/health` 200; rotas protegidas 401; frontend 200. Deploys de #35, #36 e #37 observados | Smoke test HTTP. Fluxos autenticados não verificados (sem credencial) |
| Segredo JWT em produção | ✅ **Definido e não é placeholder.** Tokens assinados com os segredos versionados foram recusados na assinatura. O PR #37 impede o boot com segredo fraco | `docs/security/2026-10-dumps-and-jwt-secret.md` |

---

## 3. Mapa do produto

Legenda das colunas BE (backend), FE (frontend) e Testes:

| Símbolo | BE / FE | Testes |
|---|---|---|
| ● | existe | bom |
| ◐ | parcial | algum |
| ○ | não existe | nenhum |

| Módulo | Estado | Maturidade | BE | FE | Testes | Produção |
|---|---|---:|:-:|:-:|:-:|---|
| **Autenticação e sessões** | 🟡 | 60% | ● | ● | ● | Sim |
| **Multi-tenancy** (tenant/loja) | 🟡 | 75% | ● | ● | ● | Sim |
| **Autorização / papéis** | 🟠 | 50% | ◐ | ● | ◐ | Sim (melhorado pelos #36 e #38) |
| **Usuários** | 🟡 | 65% | ● | ● | ◐ | Sim |
| **Lojas / workspaces** | 🟡 | 55% | ◐ | ● | ◐ | Sim |
| **Registro / trial / onboarding** | 🟡 | 50% | ● | ◐ | ◐ | Sim |
| **Platform Admin** (super-admin) | 🟡 | 70% | ● | ● | ◐ | Sim |
| **Billing / Stripe** | 🟠 | 30% | ◐ | ◐ | ◐ | Desligado |
| **Módulos / entitlement** | 🟡 | 55% | ◐ | ● | ◐ | Sim |
| **Feature flags** | 🟠 | 25% | ● | ◐ | ○ | Sem efeito |
| **Configurações** (AppSettings) | 🟠 | 35% | ● | ● | ○ | Sem efeito |
| **Auditoria / logs** | 🟡 | 45% | ◐ | ● | ◐ | Sim |
| **Produtos e categorias** | 🟡 | 65% | ● | ● | ◐ | Sim |
| **Estoque** | 🟠 | 45% | ◐ | ◐ | ◐ | Sim (o ajuste invertido foi corrigido no #36) |
| **Clientes e fornecedores** | 🟡 | 65% | ● | ● | ◐ | Sim |
| **Vendas** | 🟡 | 50% | ● | ◐ | ◐ | Sim (cancelamento pela UI 🔴) |
| **PDV** | 🟡 | 40% | ◐ | ● | ○ | Sim (troco quebra a venda) |
| **Caixa** | 🟡 | 45% | ◐ | ● | ◐ | Sim |
| **Listas de preço** | 🟠 | 20% | ● | ○ | ○ | Sem UI |
| **Compras** | 🟠 | 25% | ● | ○ | ○ | Sem UI |
| **Financeiro (núcleo)** | 🟠 | 20% | ◐ | ○ | ◐ | Sem UI |
| **Relatórios (núcleo)** | 🟠 | 15% | ◐ | ○ | ○ | Sem UI |
| **Dashboard (núcleo)** | 🟡 | 50% | ● | ● | ○ | Sim (KPIs com semântica errada) |
| **Fiscal / devolução** | ⚪ | 0% | ○ | ○ | ○ | Não existe |
| **Orken Service** | 🟡 | 65% | ● | ● | ● | Sim (Onda 1 completa) |
| **Orken Menu** (restaurante) | 🟡 | 50% | ● | ◐ | ◐ | Sim |
| **Orken Build** | 🟡 | 55% | ● | ● | ◐ | Sim |
| **Interpreter** | 🟠 | 35% | ◐ | ◐ | ◐ | Motor interno do Build |
| **IA (LLM / OCR)** | ⚪ | 5% | ○ | ◐ (cosmético) | ○ | Não existe |
| **Storage (R2)** | 🟡 | 65% | ● | ● | ◐ | Sim (tudo público) |
| **Integrações** (CEP, CNPJ, barcode, clima, e-mail) | 🟡 | 60% | ● | ● | ● | Parcial (várias flags desligadas) |
| **Portal público — agendamento** | 🟡 | 75% | ● | ● | ● | Sim |
| **Portal público — cardápio/pedido** | 🟡 | 45% | ● | ● | ◐ | Sim (pedido não vira venda) |
| **CI/CD e infra** | 🟠 | 40% | — | — | — | Auto-deploy Railway |

---

## 4. Auditoria módulo por módulo

Cada seção traz o essencial. As evidências detalhadas (arquivo:linha, endpoints, testes) estão nos relatórios brutos indicados.

### 4.1 Autenticação, sessões e multi-tenancy
*Fonte: [`01-auth-tenancy-security.md`](2026-10-raw/01-auth-tenancy-security.md)*

- **Objetivo:** login por tenant e por plataforma, JWT de acesso (15 min) e refresh (7 dias), troca de loja, isolamento por tenant e loja.
- **Status:** 🟡 · **Maturidade:** 60% (auth) / 75% (tenancy).
- **Backend existente:**
  - `AuthController`, `AuthService`, `JwtTokenService`;
  - `SecurityStampValidationMiddleware`, `TenantResolutionMiddleware`;
  - filtros globais gerados por reflexão para todo `TenantEntity`/`StoreEntity` (`NexoDbContext`);
  - `TenantSaveChangesInterceptor`;
  - sessões em `UserSession`;
  - refresh em Redis (`refresh:valid:{jti}`).
- **Frontend existente:** login, registro, verificação de e-mail, seleção de workspace, troca de loja (`modules/auth`, `modules/workspace`).
- **Prontas:** login e refresh com rotação, troca de loja, isolamento por tenant e loja (com testes), bootstrap do super-admin, headers de segurança.
- **Parciais:**
  - a revogação de sessão não cobre tokens vindos de troca de loja ou de verificação de e-mail;
  - trocar a senha não derruba sessões;
  - o refresh não detecta reuso.
- **Faltantes:** "esqueci a senha" (o link `/forgot-password` não tem rota nem endpoint), MFA, convite de usuário por e-mail.
- **Testes:** cerca de 130 em Auth e Security. O PR #36 adicionou cross-tenant do slug e "refresh preserva loja".
- **Problemas e segurança:**
  - ✅ **Corrigido (#36):** escrita cross-tenant em `PATCH /api/stores/{id}/public-slug`.
  - ✅ **Corrigido (#36):** o refresh devolvia a sessão para a 1ª loja, gravando dados na loja errada.
  - ✅ **Corrigido (#37):** a API não sobe com segredo JWT fraco ou versionado.
  - 🔴 **Rate limit contornável:** a partição usa o primeiro valor de `X-Forwarded-For`, que vem do cliente. Isso permite força bruta, inclusive no super-admin, que não tem MFA nem lockout.
  - 🔴 **Rate limit agressivo demais:** 5 requisições por 15 min por IP também no refresh. Equipes atrás do mesmo NAT perdem a sessão.
  - 🟠 Tokens ficam em `localStorage` e não há CSP.
  - 🟠 O login é ambíguo entre tenants (o `Login` é único só por tenant e a busca é global).
  - 🟠 O refresh depende do Redis: com Redis fora do ar, todos são deslogados a cada 15 min.
  - 🟠 Entidades com `TenantId` mas sem filtro global: `ModuleSubscription*`, `TenantNote`, `UserSession`, `TenantFeatureOverride`.
- **Inventário de bypass de filtro:** 91 usos de `IgnoreQueryFilters` e SQL bruto auditados. Só um quebrava o isolamento (o slug), e está corrigido.
- **Próximos passos:**
  1. Partição do rate limit com `ForwardedHeaders` restrito a proxy confiável, com limite separado para refresh.
  2. Revogação completa (todas as sessões registradas; stamp checado no refresh).
  3. MFA e lockout no super-admin.
  4. Recuperação de senha.
  5. Refresh resiliente à queda do Redis.

### 4.2 Autorização e papéis
*Fontes: relatórios 01, 03, 04, 05, 06*

- **Objetivo:** papéis Diretoria, Gerente, Vendedor, Estoquista e Cozinha, mais gates por módulo.
- **Status:** 🟠 · **Maturidade:** 50%. Era 35% antes dos PRs #36 e #38.
- **Estado real:**

  | Situação | Escopo |
  |---|---|
  | Gate de módulo no backend | Funciona: 403 por módulo em varejo, restaurante, build e service |
  | Papel checado no backend | Usuários, `PUT /settings`, **financeiro inteiro, auditoria (Diretoria), billing checkout/portal, ajuste de estoque, slug público** (#36) e **todo o Service** (#38; leituras de preset continuam abertas) |
  | **Ainda aberto a qualquer papel do tenant** | Restaurante (inclusive salários e P&L), Build e Interpreter (aprovar orçamento, confirmar despesa), `POST /sales/{id}/cancel` (sem `verify-manager` no backend), caixa (fechar a sessão de outro operador), `/reports`, `/dashboard` |
  | Controllers núcleo sem gate de módulo | Produtos, estoque, caixa, clientes, fornecedores, financeiro. Um tenant com trial vencido continua usando a API |

- **Próximos passos:**
  1. Matriz papel × ação por módulo.
  2. Aplicar nos controllers restantes, com testes por papel.
  3. Exigir `verify-manager` no cancelamento de venda.
  4. Decidir o gate de módulo do núcleo.

### 4.3 Usuários, lojas, registro e onboarding
*Fonte: [`02-platform-billing-admin.md`](2026-10-raw/02-platform-billing-admin.md)*

- **Status:** 🟡 · **Maturidade:** usuários 65%, lojas 55%, registro e trial 50%.
- **Prontas:**
  - CRUD de usuários com papéis e reset por gerente;
  - registro → e-mail (Resend) → verificação → login automático, com trial `varejo` de 7 dias e as 4 contas financeiras padrão;
  - seleção de workspace (testada);
  - slug público por loja.
- **Parciais e faltantes:**
  - **trial → pago é um beco sem saída:** a tela de assinatura não vende `varejo` nem `service`, e o bloqueio pós-trial existe só no frontend;
  - o registro não é transacional e não tem rate limit;
  - não há criação nem edição de lojas;
  - `requirePasswordChange` é gravado mas não aplicado;
  - senha temporária padrão `nexo@temp`;
  - o wizard de onboarding nunca é renderizado. O contrato da chamada dele foi corrigido no #36, mas o componente segue morto.

### 4.4 Platform Admin (super-admin)
*Fonte: 02*

- **Status:** 🟡 · **Maturidade:** 70%.
- **Prontas:**
  - tenants (CRUD, status, módulos, notas);
  - impersonação com banner;
  - reset de senha e force-logout de usuários;
  - métricas (MRR, churn, trial expirado);
  - auditoria das ações privilegiadas;
  - política `Platform` em todos os controllers.
- **Problemas:**
  - não há papéis entre platform users nem MFA;
  - o refresh da impersonação não funciona;
  - a impersonação não marca o token, então as ações aparecem como do usuário Diretoria real;
  - o MRR fica errado porque o catálogo `ModuleDefinitions` vem só do seeder (que não roda em produção), não tem `service` e traz SKUs extintas.
- **Feature flags (🟠 25%):** CRUD e overrides funcionam, mas **nenhum código lê as flags**. A tela não tem efeito.
- **Configurações (🟠 35%):** salvar e carregar funciona, mas nenhum serviço lê os valores (desconto máximo no PDV, comissão, alertas). Não têm efeito.

### 4.5 Billing / Stripe
*Fonte: 02*

- **Status:** 🟠 · **Maturidade:** 30%. **Não está pronto para cobrar clientes reais.**
- **Existe:**
  - checkout, customer portal, webhook com assinatura verificada;
  - idempotência (`stripe_processed_events`);
  - enforcement por `ModuleSubscription` (status e fim de período).
- **Bugs:**
  - checkout sobre uma assinatura que já existe não grava `StripeSubscriptionId`, então cancelamento e inadimplência no Stripe **nunca cortam o acesso**;
  - status desconhecidos do Stripe viram `Active`;
  - checkout duplicado deixa uma assinatura órfã cobrando;
  - open redirect em `SuccessUrl`/`CancelUrl`.
- **Faltantes:**
  - preços e cards de `varejo` e `service` (a tela tem um array fixo só com restaurante e build);
  - faturas e histórico;
  - período de carência de `PastDue`;
  - testes do `StripeWebhookService`.
- **Já corrigido (#36):** checkout e portal restritos a Gerente/Diretoria.

### 4.6 Auditoria / logs
*Fontes: 02 e 07*

- **Status:** 🟡 · **Maturidade:** 45%.
- **Prontas:** a escrita é transacional, a leitura isolada por tenant e a tela é real. Desde o #36, a leitura é só para Diretoria.
- **Faltantes:**
  - só usuários e ações da plataforma são auditados (vendas, caixa, financeiro e Service não);
  - os logs da aplicação vão para console e para um arquivo efêmero no container;
  - não há APM nem sink persistente;
  - `EnableSensitiveDataLogging` ligava em produção quando `ASPNETCORE_ENVIRONMENT` não estava definido. Corrigido no #39.

### 4.7 Orken Store — Produtos, Estoque, Clientes, Fornecedores
*Fonte: [`03-varejo-financeiro-core.md`](2026-10-raw/03-varejo-financeiro-core.md)*

- **Produtos (🟡 65%).**
  - Pronto: CRUD real, imagem no R2, PDF de ficha, lookup por código de barras (OpenFoodFacts, desligado por padrão).
  - Bug: **excluir categoria quebra**. A tela chama `DELETE /api/categories/{id}`, que não existe (405).
  - Problemas: código de barras não é único; não há variações nem histórico de preço.
- **Estoque (🟠 45%).**
  - Pronto: saldo, trilha de movimentos (imutável) e alertas.
  - ✅ **Corrigido (#36):** saída e perda **somavam** ao estoque. O sinal agora vem do tipo, "ajuste de inventário" é a diferença com sinal, e o ajuste é restrito a gerência e estoquista.
  - Faltantes: transferência entre lojas, inventário por contagem absoluta, bloqueio de estoque negativo.
- **Clientes e fornecedores (🟡 65%).**
  - Pronto: CRUD com CEP e CNPJ.
  - Problemas:
    - o limite de crédito é ignorado e não há extrato;
    - `UNIQUE(TenantId, DocumentNumber)` com documento obrigatório impede dois clientes sem documento, e o portal do Service grava o telefone como "CPF" para contornar;
    - o vínculo cliente↔lista de preço não pode ser criado.

### 4.8 Orken Store — Vendas, PDV, Caixa, Listas de Preço, Compras
*Fonte: 03*

- **Vendas (🟡 50%).**
  - Pronto: confirmação atômica (estoque, pagamentos, caixa, recebível a prazo), listagem e recibo em PDF.
  - ✅ **Corrigido (#36):**
    - itens com quantidade, preço ou desconto negativos agora são recusados;
    - item contado duas vezes quando a venda já estava carregada em memória (o caso da comanda do restaurante).
  - 🔴 **Cancelamento pela UI é impossível:**
    - o diálogo usa um stub que sempre falha;
    - toda venda do PDV nasce `Paid`, e o backend recusa cancelar `Paid`;
    - não existe devolução.
  - Outros problemas:
    - o recebível pago não liquida a venda;
    - cancelar depois do recebível pago deixa o valor órfão;
    - o preço vem do cliente, não do servidor.
- **PDV (🟡 40%).**
  - Pronto: venda à vista de valor exato.
  - 🔴 **Troco quebra a venda:** o valor recebido é enviado como valor pago, o backend exige igualdade e o rascunho fica órfão, inflando os KPIs.
  - Desconto percentual com dízima também quebra.
  - 🔴 **PIX e cartão entram como dinheiro na gaveta**, o que falseia o caixa.
  - O fluxo não é atômico (3 chamadas).
  - Não usa `resolve-price`, não tem split nem venda a prazo.
- **Caixa (🟡 45%).**
  - Pronto: abrir, sangria, suprimento, fechar, PDF.
  - Problemas:
    - esperado e divergência existem só na tela, estão errados para PIX e cartão e não são persistidos;
    - qualquer usuário fecha o caixa de outro;
    - aberturas simultâneas podem passar (falta índice único);
    - não há tela de histórico.
- **Listas de preço (🟠 20%) e Compras (🟠 25%).** Backend completo, sem UI e sem testes. Compras não gera conta a pagar.

### 4.9 Financeiro (núcleo)
*Fonte: 03, seção Financeiro*

- **Objetivo:** contas a pagar e a receber, liquidação, fluxo de caixa, conciliação, integração com os módulos.
- **Status:** 🟠 · **Maturidade:** 20%.
- **Existe:**
  - `FinancialController` com 13 ações (contas e títulos);
  - 4 contas padrão por tenant, com backfill;
  - recebível automático da venda a prazo;
  - espelhamento do Service: `SvcPayment` vira Receivable quitada, o estorno vira contra-lançamento e o repasse de comissão vira Payable quitada;
  - lançamentos `Svc*` são somente leitura no módulo (#35) e o `ReferenceType` `Svc*` não pode ser forjado (#35);
  - acesso só para gerência (#36).
- **Não existe:**
  - **tela de financeiro**;
  - saldo por conta e extrato;
  - fluxo de caixa;
  - conciliação;
  - recorrência;
  - competência × caixa;
  - DRE e categorias;
  - parcelamento, juros e multa;
  - job de vencidos (`MarkOverdue` nunca é chamado).
- **Problemas de domínio:**
  - `FinancialTransaction` não tem máquina de estados: paga título cancelado, cancela título pago e aceita valor ≤ 0;
  - **cinco registros financeiros paralelos**, sem consolidação:
    - `FinancialTransaction` (varejo a prazo e Service);
    - `FinancialMovement` (Interpreter/Build);
    - `CashMovement`;
    - `RestExpense`/`RestEmployee`;
    - pedidos pagos do restaurante (só em `Sale`);
  - o estorno do Service é lançado como Payable, o que inflaria despesas num DRE;
  - não há dimensão de loja.

### 4.10 Relatórios e dashboards
*Fontes: 03, 04, 05*

- **Relatórios do núcleo (🟠 15%):**
  - três endpoints sem UI;
  - contam rascunhos como faturamento;
  - usam UTC, não o fuso do Brasil.
- **Dashboard do núcleo (🟡 50%):**
  - os dados são reais e o cache é por tenant e loja;
  - os **KPIs somam todo o histórico e incluem rascunhos**, mas a tela diz "no período";
  - o card de insights é baseado em regras.
- **Dashboard do Service (🟡 50%):** os KPIs são calculados no cliente a partir de listas completas (não escala). O dashboard financeiro (PR F) não existe.
- **Financeiro e relatórios do restaurante (🟡 50%):** funcionais, mas com modelo próprio (CMV com custo atual, despesas fora do financeiro).

### 4.11 Orken Service
*Fonte: [`05-service.md`](2026-10-raw/05-service.md). É a auditoria mais profunda.*

- **Objetivo:** motor único de serviços com nove presets internos (barbearia, salão, clínica, pet, oficina, …) por meio de `SvcSettings.PresetKey`.
- **Status:** 🟡 · **Maturidade:** 65% contra a definição "v1 encerrado" (código + testes + deploy + QA visual).

| Área | Estado | % |
|---|---|---:|
| Configurações / preset / labels | 🟡 | 75 |
| Onboarding | 🟡 | 60 |
| Branding do portal | 🟢 | 85 |
| Portal público de agendamento | 🟡 | 75 |
| Clientes e subjects | 🟡 | 55 |
| Registros (prontuário) | 🟠 | 40 |
| Profissionais e horários | 🟡 | 80 |
| Catálogo | 🟢 | 85 |
| Agenda | 🟡 | 60 |
| Comandas / OS | 🟡 | 60 |
| Pagamentos → financeiro | 🟡 | 70 |
| Pacotes e consumo | 🟡 | 65 |
| Comissões e repasses | 🟡 | 80 |
| Visão geral | 🟡 | 50 |
| Papéis | 🟡 | 70 (com o #38) |
| Bloqueios / cancelamento público / lembretes / dashboard financeiro | ⚪ | 0 |

**Planos de encerramento** (`docs/superpowers/specs/2026-08-20-orken-service-encerramento-design.md`):

| PR | Situação |
|---|---|
| **A** (#33) | ✅ Mergeado e em produção: preset barbearia, comanda em barbearia e salão, capabilities fictícias removidas, SKUs legadas convertidas por migration |
| **B** (#34) | ✅ Mergeado e em produção: contas padrão por tenant, pagamento → `FinancialTransaction`, estorno como contra-lançamento |
| **C** (#35) | ✅ Mergeado e em produção em 09/10: comissão das três fontes com snapshot e idempotência, estorno que reverte comissão, fechamento e repasse (Payable única), painel em Profissionais, travas de concorrência. 53 testes de integração dedicados |
| **D** — bloqueios de agenda | ⚪ **Não iniciado.** Não existe `SvcTimeBlock`; o `AvailabilityCalculator` só considera agendamentos |
| **E** — cancelamento público + reversão de pacote | ⚪ **Não iniciado.** Sem `PublicToken`, sem endpoint, sem reversão de consumo |
| **F** — lembretes por e-mail + dashboard financeiro | ⚪ **Não iniciado.** Nenhum `IHostedService`; o `IEmailService` só verifica e-mail; não existe `/service/financeiro` |

**Gaps de produto e de UX fora do plano:**
- **Não há botão para abrir comanda a partir do agendamento.** O endpoint e o hook existem, mas sem uso.
- Não dá para escolher o profissional por item nem na comanda depois de criada (`useUpdateOrder` e `useUpdateOrderItem` sem uso). Uma comanda sem profissional nunca gera comissão.
- A agenda é uma tabela de um dia: não há visão semanal nem por profissional.
- Concluir um agendamento exige 3 transições.
- A comissão de agendamento e a de pacote não dependem de pagamento registrado (decisão do spec, mas fere o "regime de caixa").
- Pluralização automática quebrada ("Profissionals", "Sessãos") e rótulos fixos na sidebar.
- O texto do painel promete despesa "em Contas a Pagar no Financeiro", mas essa tela não existe.

**Segurança:**
- ✅ Papéis no backend (#38).
- ✅ Cancelar comanda paga agora exige estornar antes (#38).
- 🟠 O portal devolve o nome cadastrado de quem tem aquele telefone (enumeração).
- 🟠 Double-booking por corrida: não há constraint de exclusão.
- 🟠 Consumo de pacote sem lock: dois consumos simultâneos podem passar do saldo.
- 🟠 Anexos de prontuário ficam em bucket público.

**Testes:** 189 de integração, cerca de 150 unitários, cerca de 84 Vitest. Nenhum E2E.

### 4.12 Orken Menu (Restaurante)
*Fonte: [`04-restaurante.md`](2026-10-raw/04-restaurante.md)*

| Área | Estado | % |
|---|---|---:|
| Cardápio / adicionais | 🟠 | 35 |
| Áreas e mesas | 🟡 | 75 |
| Pedidos de salão | 🟡 | 60 |
| Cozinha / KDS | 🟡 | 55 |
| Ficha técnica / CMV | 🟡 | 60 |
| Configurações food service | 🟠 | 45 |
| Funcionários, despesas, financeiro e relatórios | 🟡 | 50 |
| Delivery (hub, zonas, cupons) | 🟠 | 40 |
| Portal público de pedidos | 🟡 | 45 |
| Integrações iFood, Rappi, AnotaAí | ⚪ | 5 |

- **Status geral:** 🟡 · **Maturidade:** 50%.
- **Prontas:**
  - mesas, comanda de salão → `Sale` → pagamento (com couvert e taxa);
  - ficha técnica com baixa de insumos;
  - KDS em tempo real por SignalR, com escopo tenant + loja;
  - zonas de entrega e cupons;
  - portal com rastreio sem PII.
- ✅ **Corrigido (#36):**
  - comanda com adicional pago **não podia ser paga**: a venda saía sem o preço do adicional, e havia ainda a contagem dupla do item;
  - insumos apareciam e podiam ser pedidos no cardápio público;
  - o pedido público aceitava quantidade negativa e salvava pedidos vazios.
- 🔴 **Pedidos de delivery, portal e balcão nunca viram venda.**
  - Não existe tela para fechar e pagar comandas sem mesa, e o aceite não leva taxa de entrega nem desconto do cupom.
  - Resultado: a receita fica fora do caixa, do estoque e dos relatórios.
- **Outros problemas:**
  - não há CRUD de adicionais na UI (só via API);
  - o KDS não mostra os adicionais;
  - o SignalR não volta a entrar no grupo após reconectar;
  - o portal não verifica se o módulo está ativo nem o status da loja (adiado no #36 por risco de derrubar portais existentes);
  - endpoints públicos sem rate limit;
  - não há papéis no backend (salários e P&L abertos a qualquer papel);
  - despesas e folha ficam fora do financeiro.
- **Testes:** cerca de 48 de integração. Nenhum unitário nem de frontend.

### 4.13 Orken Build
*Fonte: [`06-build-interpreter-ai-storage.md`](2026-10-raw/06-build-interpreter-ai-storage.md)*

- **Status:** 🟡 · **Maturidade:** 55%. **É um produto vivo**, em `/build`, com gate de módulo.
- **Prontas:**
  - projetos com ciclo de vida;
  - etapas com %;
  - orçamento (itens, margem, aprovar);
  - diário com fotos (R2) e clima;
  - despesas via Interpreter;
  - resumo financeiro previsto × realizado.
- **Bugs:**
  - os cards da listagem mostram sempre "0/0 etapas" e 0% (falta `Include`);
  - a paginação do diário e dos orçamentos está quebrada (só os primeiros 20 ou 30 aparecem);
  - os validators nunca são executados;
  - uma despesa errada não pode ser estornada pela UI.
- **Arquitetura:** as despesas de obra ficam em `FinancialMovement` (ledger do Interpreter), fora do financeiro do tenant.
- **Segurança:** sem papéis no backend.
- **Testes:** 4 de integração. Nenhum unitário nem de frontend.

### 4.14 Interpreter e IA
*Fonte: 06*

- **Interpreter (🟠 35%).** Motor interno, não é produto. Faz texto → regex → rascunho → confirmação, com trilha de auditoria. Anexos, memória, categoria e reprocessar/estornar são stub ou não têm tela. Os anexos vão para o disco efêmero do container.
- **IA (⚪ 5%).**
  - Não existe nenhuma chamada a LLM nem OCR. `ClaudeAnalyzerStub` devolve campos vazios, e ligar a flag de OpenAI derruba o analyze (500).
  - O cockpit `/platform/ai` mostra telemetria real, mas os toggles de provider, as versões de prompt e os custos **não têm efeito em runtime**.
  - As chaves de API são gravadas em Base64.
  - O "Analisar" com ícone de brilho no Build é regex.
  - **Nada disso deve ser comunicado como IA.**

### 4.15 Storage e integrações
*Fonte: 06*

- **Storage R2 (🟡 65%):**
  - cliente lazy, chaves com prefixo do tenant, whitelist de tipo e tamanho;
  - **tudo público**, inclusive anexos de prontuário do Service;
  - sem verificação de conteúdo (magic bytes);
  - sem limpeza de órfãos.
- **Integrações:**

| Integração | Estado | % | Observação |
|---|---|---:|---|
| CEP / CNPJ | 🟡 | 75 | Polly |
| Barcode | 🟡 | 60 | Desligado |
| Clima | 🟡 | 55 | Desligado; licença não-comercial pendente |
| Resend | 🟡 | 60 | Só verificação de e-mail |
| Mercado Pago, WhatsApp, iFood, Fiscal | ⚪ | 0 | — |

  No `ORKEN_INTEGRATIONS_PLAN.md`, o "✅" significa *planejado*, não implementado.

### 4.16 Portais públicos (sem login)

| Fluxo | Endpoint | Isolamento | Estado |
|---|---|---|---|
| Agendamento: vitrine, profissionais, disponibilidade, criar | `GET/POST /api/public/service/{slug}/…` | Por slug, com tenant e loja explícitos; rate limit (contornável via XFF) | 🟡 75%. Sem cancelamento (PR E). Vaza nome por telefone |
| Cardápio e pedido | `GET /api/public/menu/{slug}`, `POST /api/public/orders` | Por slug; sem rate limit; insumos e quantidade inválida corrigidos (#36) | 🟡 45%. O pedido não vira venda |
| Rastreio de pedido | `GET /api/public/orders/{token}` | Token GUID de 128 bits, sem PII | 🟢 |
| Zonas e cupom | `GET /api/public/delivery-zones/{slug}`, `POST /api/public/coupons/validate` | Por slug; sem rate limit (força bruta de cupom) | 🟡 |
| Disponibilidade de slug | `GET /api/stores/check-slug` | Anônimo; permite enumerar slugs (baixo) | 🟡 |

---

## 5. Funcionalidades existentes (o que o ORKEN já faz hoje)

- **Conta e acesso:**
  - cadastro self-service com verificação de e-mail e trial;
  - login com refresh;
  - multi-loja com troca de loja;
  - usuários com 5 papéis;
  - super-admin com impersonação, métricas e auditoria.
- **Varejo:**
  - cadastro de produtos com foto e ficha em PDF;
  - estoque com trilha;
  - clientes e fornecedores com CEP e CNPJ;
  - PDV à vista (valor exato);
  - caixa com sangria, suprimento e fechamento em PDF;
  - vendas com recibo;
  - venda a prazo gerando recebível (via API).
- **Restaurante:**
  - salão com mesas e comandas;
  - KDS em tempo real;
  - pagamento da comanda com couvert e taxa;
  - ficha técnica com baixa de insumos e CMV;
  - delivery com zonas e cupons;
  - cardápio público com pedido e rastreio;
  - despesas e funcionários com resumo financeiro.
- **Service:**
  - nove ramos com rótulos próprios;
  - profissionais com horários, catálogo, clientes e subjects (pet, veículo, …);
  - agenda;
  - comandas com pagamentos lançados no financeiro;
  - pacotes com consumo;
  - comissões das três fontes com fechamento e repasse;
  - portal público de agendamento com branding e `.ics`.
- **Build:** obras com etapas, orçamento, diário com fotos e clima, despesas e previsto × realizado.
- **Plataforma:** gestão de tenants e módulos, flags (sem efeito), cockpit de interpretação (rule-based).

## 6. Funcionalidades incompletas (por módulo)

- **Auth:** revogação de sessão completa, rate limit confiável, MFA no super-admin.
- **Papéis:** backend de Restaurante, Build e Interpreter, cancelamento de venda e caixa.
- **Billing:** sincronização do webhook, venda de `varejo` e `service`, trial → pago.
- **Vendas e PDV:**
  - troco;
  - PIX e cartão separados do dinheiro;
  - split de pagamento;
  - cancelamento com gerente;
  - liquidação da venda a prazo;
  - preço resolvido no servidor.
- **Caixa:** esperado e divergência no backend, histórico, travas por operador.
- **Estoque:** transferência, contagem absoluta.
- **Restaurante:**
  - delivery, balcão e portal → venda;
  - CRUD de adicionais;
  - adicionais no KDS;
  - reconexão do SignalR;
  - checagem de módulo e status no portal.
- **Service:**
  - comanda a partir do agendamento;
  - profissional por item;
  - agenda semanal;
  - lock no consumo de pacote;
  - constraint anti-overlap;
  - não vazar o nome no portal.
- **Build:** contadores, paginação, estorno de despesa pela UI, validators, relatórios.
- **Dashboards:** período, KPIs só com vendas confirmadas.
- **Storage:** objetos privados e URL assinada para dados sensíveis.

## 7. Funcionalidades ainda não desenvolvidas (por módulo)

- **Núcleo:** recuperação de senha, convite por e-mail, criação e edição de lojas, MFA.
- **Financeiro:**
  - **tela de contas a pagar e a receber**;
  - fluxo de caixa;
  - conciliação;
  - recorrência;
  - DRE;
  - consolidação dos ledgers;
  - job de vencidos.
- **Varejo:**
  - devolução e troca;
  - fiscal (NF-e/NFC-e);
  - UI de compras e de listas de preço;
  - relatórios com UI;
  - comissão de vendedor.
- **Restaurante:** integrações de marketplace (iFood etc.), pagamento de delivery.
- **Service (Onda 2):**
  - bloqueios de agenda (D);
  - cancelamento público por token e reversão de consumo (E);
  - lembretes por e-mail e `/service/financeiro` (F);
  - papel "profissional" com agenda própria.
- **Build:** relatórios, custo por etapa, PDF de orçamento.
- **IA:** qualquer integração real com LLM ou OCR.
- **Plataforma:** catálogo de módulos por migration, faturas, consumo das feature flags.

## 8. Backend sem frontend

| Endpoint / área | Situação |
|---|---|
| `api/financial/*` (13 ações) | Contas e títulos; o razão de Vendas e do Service **sem tela** |
| `api/varejo/purchases/*` (7) e `api/varejo/price-lists/*` (7) | Compras e listas de preço; também sem testes |
| `GET api/varejo/pdv/resolve-price` | Sem consumidor |
| `api/reports/sales\|inventory\|customers` | Sem consumidor |
| `GET api/features` | Único consumidor (`useFeatureFlags`) está morto |
| `api/tenants/{id}`, `by-slug` | Sem consumidor |
| `POST api/platform/auth/login` | Duplicado (o `/auth/login` já tem fallback) |
| `POST api/auth/verify-manager` | Sem consumidor |
| `api/v1/movements/{id}`, `/reprocess`, `/void` | Interpreter, sem tela |
| `api/v1/interpreter/attachments` | Sem consumidor |
| `api/v1/tenants/stopwords\|memory-profile` | Sem consumidor |
| Mutações de `api/restaurante/modifier-groups` | Sem tela para criar ou editar adicionais |
| `DELETE api/restaurante/orders/{id}/items/{itemId}` | Cancelar item: sem UI |
| `PATCH api/restaurante/tables/{id}/status` | Sem UI |
| Service: `PUT orders/{id}`, `PUT orders/{id}/items/{itemId}`, `POST orders/from-appointment/{id}` | Hooks prontos, sem uso |
| Build: `convert`, `reorder`, `PUT daily-logs/{id}` | Sem UI |
| `PATCH api/products/{id}/prices`; `api/categories/{id}/activate\|deactivate` | Sem UI |

## 9. Frontend sem backend / sem fluxo real

| Tela / componente | Problema |
|---|---|
| Excluir categoria | Chama `DELETE /api/categories/{id}`, que **não existe** (405) |
| `SaleCancellationDialog` | O stub de autorização gerencial sempre falha |
| Cancelamento de item de venda | Sem endpoint |
| Feature flags da plataforma | Nada as lê |
| Configurações (PDV, comissão, estoque) | Salvas, sem efeito |
| `/platform/ai/providers\|prompts\|costs` | Sem efeito em runtime |
| Playground com Claude/OpenAI | Stub e erro, respectivamente |
| Cards de projetos no Build | Contadores sempre zerados |
| `AssinaturaPage` | Catálogo fixo divergente dos módulos reais |
| Divergência de caixa | Só visual |
| Link "Preços" da landing | Sem âncora |
| `/forgot-password` | Sem rota |
| Bloqueio de trial expirado | Só no cliente |
| Texto "despesa em Contas a Pagar no Financeiro" (comissões) | A tela não existe |

## 10. Código legado / morto

- **Nexo × Orken:**

  | Situação | Itens | Decisão |
  |---|---|---|
  | Visível ao usuário | `CompanyName "NexoERP Platform"` na sessão de plataforma; senha temporária `nexo@temp` | Corrigir já, é fácil |
  | Interno e arriscado | Namespaces `Nexo.*`, schema `nexo`, issuer/audiences do JWT, chaves de módulo `varejo`/`restaurante` | **Manter** |
  | Interno e fácil | Cookies e localStorage `nexo:*`, Swagger, logs, `package.json` `vite_react_shadcn_ts` | Fazer em janela planejada |

- **Componentes e código mortos:**
  - frontend:
    - `OnboardingWizard`, `useFeatureFlags`, `InventoryAlertCard`, `ProductRulesSection`, `PermissionGroupCard`, `LandingCtaBlock`, `LandingDifferentials`;
    - 6 componentes shared;
    - 19 componentes shadcn/ui;
    - services vazios `productService.ts` e `supplierService.ts`;
    - tipos legados de inventário;
    - `playwright-fixture.ts` (Lovable, quebrado);
    - `src/test/example.test.ts`;
  - backend:
    - `ClaudeAnalyzerStub`, `MovementMemoryServiceImpl` no-op;
    - validators de Build e Interpreter nunca executados;
    - `Dapper` sem uso;
    - `RequireModuleFilter`;
    - `UserRepository.GetByEmailAsync`;
    - `FinancialTransaction.MarkOverdue` sem chamador.
- **Arquivos e lixo no repositório:**
  - `nexo-main/dist/` versionado e desatualizado (maio);
  - `nexo-main/railpack.json`, redundante com o Dockerfile;
  - `nexo-backend/_migrations_backup_20260406_203531/`;
  - scripts `test_dashboard.py` e `test_dash_prod.py` (este lê segredos do Railway de produção);
  - resultados de teste antigos.
  - ✅ **Removidos do HEAD no #37:** os dumps `nexo_backup*.sql`. Continuam no histórico.
- **Documentos obsoletos:**
  - `nexo-main/README.md` e `nexo-main/CLAUDE.md`, que ainda diz "services use mock data";
  - `NEXO_MASTER_CONTEXT.md`;
  - `TEST_SUITE_REPORT.md`;
  - `SECURITY_CODE_REVIEW_REPORT.md`;
  - `docs/ORKEN_SYSTEM_AUDIT.md`;
  - `docs/llm-wiki-export/`.

  Continuam válidos: specs e planos do Service, `ORKEN_MENU_ARCHITECTURE.md` (em grande parte), `ORKEN_INTEGRATIONS_PLAN.md` como plano.
- **Branches:**
  - 34 remotas já mergeadas, que podem ser apagadas;
  - 15 `archive/*` de WIP;
  - `feature/orken-service-closure-prC-comissao`, substituída pela `-resume`;
  - `fix/redis-connectivity`, ainda **não mergeada** e relevante.

## 11. Dívida técnica (por severidade)

1. **Alta — múltiplos ledgers financeiros sem consolidação e sem UI.** Impede DRE, fluxo de caixa e confiança nos números.
2. **Alta — RBAC só parcial no backend.** Falta em Restaurante, Build, Vendas e Caixa.
3. **Alta — PDV e caixa com semântica errada.** Troco; PIX e cartão como dinheiro; fluxo não atômico.
4. **Alta — refresh e sessão dependem de Redis, e o rate limit pode ser contornado.**
5. **Média — sem CI de frontend e sem lockfile versionado.** Builds não reproduzíveis.
6. **Média — observabilidade.** Logs efêmeros, sem APM, `/health` cego para banco e Redis.
7. **Média — três mecanismos de gate de módulo** com caches distintos e sem invalidação.
8. **Média — modelagem:**
   - `UNIQUE` de documento que força CPF falso;
   - enums gravados como int no Build e no Interpreter;
   - Compras e Financeiro sem dimensão de loja;
   - 72 FKs `CASCADE` para `tenants`.
9. **Média — migrations de dados sem `Down()`,** sem runbook de backup e rollback.
10. **Baixa:**
    - versões de pacotes divergentes (EF 8.0.8 × 8.0.11, `Microsoft.Extensions.Http` 10.x em net8);
    - `BuildProjectDetailPage` com 1473 linhas;
    - estados vazios e pluralização.

## 12. Segurança (consolidado)

| # | Achado | Severidade | Estado |
|---|---|---|---|
| 1 | Escrita cross-tenant no slug público (sequestro de portal) | Alta | ✅ #36 |
| 2 | Refresh grava na loja errada (integridade multi-loja) | Alta | ✅ #36 |
| 3 | Financeiro, auditoria, billing e ajuste de estoque sem papel no backend | Alta | ✅ #36 |
| 4 | Service sem papel no backend (estorno, preset, portal) | Alta | ✅ #38 |
| 5 | Segredo JWT de exemplo aceito sem trava de produção | Alta (latente) | ✅ #37. Produção verificada: usa segredo próprio |
| 6 | Dumps com hashes bcrypt versionados | Média | ✅ Removidos do HEAD (#37). **Continuam no histórico.** Rotacionar a senha do platform user `elias@nexo.com` se reutilizada; purga opcional |
| 7 | Restaurante, Build/Interpreter, cancelamento de venda e caixa sem papel | Alta | ❌ Aberto |
| 8 | Rate limit contornável via `X-Forwarded-For`; super-admin sem MFA nem lockout | Alta | ❌ Aberto |
| 9 | Revogação de sessão incompleta; troca de senha não derruba sessão | Alta | ❌ Aberto |
| 10 | Billing: webhook não grava a assinatura em checkout repetido; status desconhecido vira `Active` | Alta (financeira, quando o Stripe for ligado) | ❌ Aberto |
| 11 | Arquivos sensíveis (prontuário) em bucket público; sem magic bytes | Média | ❌ Aberto |
| 12 | Portal do Service revela o nome por telefone | Média | ❌ Aberto |
| 13 | Endpoints públicos do restaurante (pedido, cupom) sem rate limit | Média | ❌ Aberto |
| 14 | Chaves de IA em Base64 | Média (latente: nenhuma é usada) | ❌ Aberto |
| 15 | `EnableSensitiveDataLogging` ligava em produção quando `ASPNETCORE_ENVIRONMENT` não estava definido | Média | ✅ #39 (agora só com Development/Testing explícito) |
| 16 | Tokens em `localStorage`, sem CSP; CORS com localhost em produção | Média/Baixa | ❌ Aberto |
| 17 | Tabelas com `TenantId` sem filtro global (`ModuleSubscription`, `UserSession`, …) | Baixa | ❌ Monitorar |
| 18 | Mass assignment / IDOR | — | Sem achado além do #1. DTOs explícitos; FKs de entrada às vezes sem validação por tenant (`CategoryId`), impacto baixo |
| 19 | SQL injection | — | Sem achado. Todo SQL bruto é parametrizado |

## 13. Qualidade e testes

| Tipo | Quantidade | Onde está forte | Onde falta |
|---|---|---|---|
| Unitários backend | 383 | Service (~150), Interpreter (61), Integrações (84), Auth | Restaurante, Build, Vendas, Financeiro |
| Integração backend | 367 | Service (~190), Auth (56), Security (~50), Restaurante (~48) | **Compras, listas de preço, relatórios, dashboard, financeiro (fluxos), Stripe webhook, Platform (funcional), Build (4)** |
| Frontend Vitest | 178 (18 arquivos) | Service, portal de agendamento, workspace, auth | **Zero** em restaurante, build, vendas/PDV, produtos, estoque, caixa, clientes, usuários, plataforma, cardápio, billing |
| E2E Playwright | 17 (auth) | — | Não roda em CI; nenhum fluxo de negócio |
| CI | backend-tests + Sonar + GitGuardian | — | Sem CI de frontend; sem teste de migration nem de build Docker |

**Módulos críticos com baixa cobertura:** PDV, caixa, financeiro, compras e Stripe webhook.

## 14. Infraestrutura (o que foi possível verificar)

| Item | Estado conhecido |
|---|---|
| **Railway** | Auto-deploy a partir de `master` (observado em 3 deploys nesta auditoria). Não há `railway.json`/`toml`: serviços, variáveis, health check path e réplicas vivem só no painel, que não foi acessado (CLI sem autenticação) |
| **Backend** | Container .NET 8 não-root. Migrations aplicadas no boot (`MigrateAsync`), sem lock nem passo separado. `SeedPlatformAdminAsync` em todo boot |
| **Frontend** | Container Caddy. `npm install` **sem lockfile** (`package-lock.json` no `.gitignore`), portanto build não reproduzível. `dist/` versionado e não usado |
| **PostgreSQL** | Schema `nexo`, 43 migrations, 86 tabelas. Backup e restore não documentados |
| **Redis** | Opcional (cai em NoOp), mas o refresh depende dele. Há histórico de timeout na rede privada do Railway. A branch `fix/redis-connectivity` não foi mergeada |
| **Health checks** | `/health` só verifica um diretório local; responde 200 com o banco fora |
| **CI/CD** | GitHub Actions só para o backend; Sonar e GitGuardian como apps; sem Dependabot nem CodeQL |
| **Rollback** | Não existe como processo. As migrations de dados de agosto têm `Down()` vazio por desenho |
| **Observabilidade** | Serilog para console e arquivo efêmero; sem sink persistente nem APM |

---

## 15. Roadmap recomendado

### P0 — Segurança e integridade (próximas 1–2 semanas)

1. **Papéis no backend do restante:** Restaurante (incluindo salários e P&L), Build/Interpreter, cancelamento de venda com `verify-manager` no backend e caixa (fechar só o próprio, salvo gerente).
2. **PDV e caixa com integridade:** troco no backend; PIX e cartão fora da gaveta física; checkout atômico; arredondamento do desconto.
3. **Restaurante: pedidos de delivery, portal e balcão viram venda,** com tela de comandas sem mesa, levando taxa de entrega e cupom.
4. **Sessão:** rate limit por IP real (proxy confiável) e separado para refresh; revogação completa; MFA e lockout no super-admin.
5. **Ação do owner:** rotacionar a senha do platform user `elias@nexo.com` se ela foi reutilizada; decidir a purga do histórico Git.

### P1 — Encerramento dos módulos atuais

1. **Service v1:**
   - QA visual dos PRs A, B e C em produção;
   - abrir comanda a partir do agendamento;
   - profissional por item;
   - agenda semanal;
   - lock no consumo de pacote;
   - constraint anti-overlap;
   - portal sem vazar o nome;
   - **Onda 2 (PRs D, E e F).**
2. **Financeiro mínimo com UI:** contas a pagar e a receber, máquina de estados, liquidação com conta de destino, visão consolidada dos lançamentos de cada módulo.
3. **Billing pronto para cobrar:** corrigir o webhook, mapear status, cadastrar preços de `varejo` e `service`, trial → pago, gate de módulo no núcleo.
4. **Vendas:** cancelamento e devolução com gerente; liquidação da venda a prazo; preço resolvido no servidor.
5. **Restaurante:** CRUD de adicionais; adicionais e reconexão no KDS; portal checando módulo e status; rate limit nos endpoints públicos.
6. **Build:** contadores, paginação, estorno de despesa pela UI, validators ativos.

### P2 — Maturidade

1. **CI de frontend** (typecheck, lint, vitest, build), lockfile versionado com `npm ci`, remoção de `dist/` e `railpack.json`.
2. Health check com PostgreSQL e Redis; refresh resiliente à queda do Redis (ou merge de `fix/redis-connectivity`); logs persistentes e APM; runbook de backup e rollback; lista de variáveis de ambiente.
3. Testes onde falta: PDV, caixa, financeiro, compras, Stripe webhook, frontend de restaurante e varejo, E2E dos fluxos de dinheiro.
4. Dashboards com período correto e endpoint agregado (núcleo e Service); relatórios do núcleo com UI.
5. Storage privado com URL assinada para dados sensíveis; validação de conteúdo dos uploads.
6. Limpeza: código morto, docs obsoletos, branches mergeadas, naming visível (`NexoERP Platform`, `nexo@temp`), gate de módulo unificado.

### P3 — Expansão

UIs de Compras e Listas de Preço; transferência de estoque; fiscal (NFC-e/NF-e); devolução; integrações de marketplace (iFood); WhatsApp; Mercado Pago; **IA real** (só depois de chaves criptografadas, limite de custo e fallback); papel "profissional" no Service; DRE e fluxo de caixa consolidados.

---

## 16. Próximos 10 trabalhos (por retorno técnico e de produto)

| # | Iniciativa | Objetivo | Motivo | Esforço | Risco | Dependências | Impacto |
|---|---|---|---|---|---|---|---|
| 1 | **PDV e caixa íntegros** | Troco, PIX e cartão separados do dinheiro, checkout atômico | É o fluxo principal do varejo; hoje quebra com troco e falseia o caixa | Médio | Médio (toca Sale e Cash) | — | Muito alto: varejo vendável |
| 2 | **RBAC backend completo** | Papéis em Restaurante, Build, Vendas e Caixa, com testes por papel | Fraude interna e salários expostos a qualquer papel | Médio | Baixo (espelha a UI) | Matriz papel × ação | Alto: segurança |
| 3 | **Delivery, portal e balcão → venda** | Fechar e pagar comandas sem mesa, com taxa e cupom | Receita do delivery fora do sistema | Médio | Médio | — | Muito alto: restaurante confiável |
| 4 | **Hardening de sessão** | Rate limit confiável, revogação completa, MFA no super-admin | Força bruta e sessões que sobrevivem | Médio | Médio (proxy do Railway) | Infra Railway | Alto |
| 5 | **Fechar o Service v1** | QA dos PRs A, B e C; comanda pelo agendamento; profissional por item; agenda semanal; locks | É o módulo mais perto de "produto pronto" | Médio | Baixo | — | Alto: vertical vendável |
| 6 | **Financeiro mínimo com UI** | Contas a pagar e a receber e visão consolidada | O dinheiro já é lançado, mas o dono não vê | Grande | Médio (decisão de modelo) | Decisão sobre os ledgers | Muito alto: valor central de ERP |
| 7 | **CI de frontend e lockfile** | Gate automatizado para PRs de frontend | Hoje um PR só de frontend vai para produção sem nenhum check | Pequeno | Baixo | — | Alto: previne regressões |
| 8 | **Billing pronto para cobrar** | Webhook correto, preços de `varejo`/`service`, trial → pago, gate no núcleo | Sem isso não há receita recorrente | Médio | Alto (dinheiro de cliente) | Contas Stripe e preços (decisão comercial) | Muito alto: monetização |
| 9 | **Service Onda 2 (PRs D, E, F)** | Bloqueios, cancelamento público, lembretes, dashboard financeiro | Previsto no plano aprovado; reduz no-show | Grande | Médio (scheduler single-instance) | #5 | Alto |
| 10 | **Observabilidade e resiliência** | Health check real, logs persistentes, refresh sem depender do Redis, runbook de backup | Incidentes hoje são invisíveis; queda do Redis desloga todos | Médio | Baixo | Acesso ao Railway | Alto: operação |

---

## 17. Ações executadas nesta auditoria

| PR | Conteúdo | Estado |
|---|---|---|
| #35 | Service: comissões de ponta a ponta, estorno que reverte comissão, proteção financeira; frontend com tsc, lint e vitest verdes | Mergeado e em produção |
| #36 | P0:<br>- slug cross-tenant;<br>- loja ativa no refresh;<br>- estoque invertido;<br>- venda com valores negativos;<br>- item contado em dobro;<br>- comanda com adicional impagável;<br>- insumos no cardápio;<br>- papéis em financeiro, auditoria, billing e estoque;<br>- wizard de onboarding com contrato errado. | Mergeado e em produção |
| #37 | Trava de boot para segredo JWT fraco; dumps de banco fora do HEAD | Mergeado e em produção |
| #38 | Papéis no backend do Service; cancelar comanda paga exige estorno antes | Mergeado |
| #39 | `EnableSensitiveDataLogging` só em Development/Testing | Mergeado |
