# ORKEN 1.0 — Estado do Produto

| | |
|---|---|
| **Data da auditoria** | 15/10/2026 |
| **Base auditada** | `master` após os PRs da missão 42–74 (GitHub até #76). Linha de base: [`ORKEN_PRODUCT_STATUS_2026-10.md`](ORKEN_PRODUCT_STATUS_2026-10.md) (09/10/2026). |
| **Ambiente** | Código local completo (backend .NET 8, frontend React/TS), PostgreSQL real via Testcontainers nas suítes de integração, e produção checada por HTTP público e pelo CLI da Railway (somente leitura). |
| **Método** | Seis frentes independentes (autenticação e plataforma; núcleo Store; financeiro; Service; Menu, Build e Interpreter; engenharia), cada uma rastreando UI → API → domínio → banco no código atual, com evidência por arquivo e linha. **Mesma régua da linha de base**: só conta o que funciona de ponta a ponta e tem teste. Onde o registro de progresso estimava mais alto, prevaleceu a régua. |

### Selos

| Selo | Significado |
|---|---|
| 🟢 PRODUÇÃO | Ponta a ponta e confiável |
| 🟡 FUNCIONAL / INCOMPLETO | Fluxo principal funciona; faltam partes relevantes |
| 🟠 PARCIAL | Partes importantes existem, mas não formam uma funcionalidade |
| 🔴 NÃO FUNCIONAL | Há estrutura, mas o fluxo não funciona |
| ⚪ SCAFFOLD | Só estrutura inicial |
| ⚫ LEGADO / MORTO | Abandonado ou substituído |

---

## 1. Resumo executivo

Desde a auditoria de 09/10, os PRs 42 a 74 da missão levaram o Orken de "muitas partes soltas" para **um produto que opera de verdade**: venda com devolução e troco certos, caixa com conferência, estoque por loja com inventário e transferência, compras que viram estoque e contas a pagar, um livro financeiro único com competência e caixa, relatórios coerentes com o painel, Service com agenda, pacotes, comissões e portal do cliente, Menu fechando pedido em venda, autenticação com recuperação de senha e verificação em duas etapas, e uma base de engenharia (CI das duas pontas, saúde, observabilidade, storage privado) que não existia.

**Não chegamos às metas de maturidade da missão.** Pela régua honesta da linha de base, o produto está entre 65% e 80% na maioria das áreas, contra metas de 85%–90%. Isso **não é falta de execução nos PRs** — cada um foi revisado e está em produção — e sim de quatro coisas que a própria missão separou como decisões ou integrações do dono, mais um conjunto de gaps estruturais que pedem mais ondas:

1. **Cobrança (PR 62) bloqueada:** sem preços, planos e meios de pagamento definidos, não há como cobrar nem cortar acesso. Billing fica em ~45%.
2. **Emissão fiscal:** a base está pronta (emissor, dados fiscais, CPF na nota, contrato do provedor), mas NFC-e/NF-e exige provedor pago e certificado — decisão do dono. Sem isso o varejo não fecha comercialmente.
3. **Bucket R2 privado:** o código está pronto e falha fechado; falta criar o bucket. Até lá, anexos de prontuário respondem 503 em produção.
4. **DRE com CMV:** o financeiro mostra "Resultado (antes do CMV)". Fechar a DRE pede custo histórico por venda — próxima onda.

### Meta da missão × situação atual

| Área | Meta | Antes (09/10) | Agora (15/10) | Distância |
|---|---:|---:|---:|---|
| Core (núcleo: auth, tenancy, RBAC, usuários, configurações) | ≥ 90% | ~60% | **~78%** | MFA da plataforma, trial no backend, login ambíguo entre empresas |
| Service | ≥ 90% | 65% | **~80%** | prontuário (bucket + anexos + auditoria de exclusão), papel "profissional", paginação |
| Store | 85–90% | ~40% | **~70%** | NFC-e, pagamento dividido no PDV, checkout atômico, ajuste de estoque auditado |
| Menu | 85–90% | 50% | **~68%** | CMV com custo histórico, fechamento de comanda atômico, testes de frontend |
| Build | 80–90% | 55% | **~65%** | relatórios, custo por etapa, telas de converter orçamento e editar diário |
| Auth/Security | ≥ 90% | ~55% | **~80%** | MFA no login da plataforma, IP real atrás do proxy, troca obrigatória no backend |
| Billing | ≥ 85% | 30% | **~45%** | **bloqueado no PR 62** (decisão do dono) |
| Infra | ≥ 85% | ~40% | **~70%** | bucket privado, Dependabot/CodeQL, disco efêmero, E2E contra backend real |
| Financeiro | ≥ 90% | ~20% | **~70%** | DRE/CMV, conta de destino e conciliação, estorno de baixa, idempotência no banco |

---

## 2. Health geral

| Área | Estado em 15/10/2026 | Evidência |
|---|---|---|
| Backend build | ✅ 0 erros (avisos de nulabilidade em `AuthController`, `StoreService`) | `dotnet build Nexo.sln` |
| Frontend build | ✅ | `npm run build`, etapa do `frontend-ci` |
| Testes unitários (backend) | ✅ **584/584** (eram 383) | `Nexo.UnitTests` |
| Testes de integração (backend) | ✅ **~515 casos** em 58 arquivos (eram 367), PostgreSQL real | `Nexo.IntegrationTests`, `backend-tests` verde |
| Vitest | ✅ **211/211**, 24 arquivos (eram 178 em 18) | `npx vitest run` |
| Playwright | ✅ **57** casos críticos no CI (API simulada); 17 de auth contra a stack real seguem fora do CI | `e2e/experience.spec.ts`, `playwright.config.ts` |
| TypeScript | ✅ 0 erros | `npm run typecheck` |
| Lint | ✅ 0 erros, 16 avisos | `npx eslint .` |
| Migrations | ✅ **64** (eram 43). As 21 novas são aditivas: nenhuma remove ou renomeia coluna; 4 trocam índice, 7 fazem backfill | `Persistence/Migrations/` |
| CI | ✅ `backend-tests`, `frontend-ci` (lockfile, typecheck, lint, Vitest, build, Playwright), SonarCloud e GitGuardian em todo PR | `.github/workflows/` |
| Build reproduzível | 🟡 frontend sim (`package-lock.json` + `npm ci --ignore-scripts`; bundle idêntico após redeploy). Backend sem `packages.lock.json` e imagens base com tag flutuante | `nexo-main/Dockerfile`, `nexo-backend/Dockerfile` |
| Produção | ✅ `/health/live` 200; `/health/ready` 200 **Degraded** só por falta do bucket privado; `/health` 20/20; rotas protegidas 401 com `x-request-id`; logs JSON interpretados pela Railway | curl + `railway logs` em 15/10 |

---

## 3. Mapa do produto

| Símbolo | BE / FE | Testes |
|---|---|---|
| ● | existe | bom |
| ◐ | parcial | algum |
| ○ | não existe | nenhum |

| Módulo | Estado | Maturidade (antes → agora) | BE | FE | Testes | Produção |
|---|---|---:|:-:|:-:|:-:|---|
| **Autenticação e sessões** | 🟡 | 60 → **80%** | ● | ● | ● | Sim — recuperação de senha, sessões, bloqueio, MFA |
| **Multi-tenancy** | 🟡 | 75 → **80%** | ● | ● | ● | Sim |
| **Autorização / papéis** | 🟢 | 50 → **85%** | ● | ● | ● | Sim — cobertura de RBAC quebra o build |
| **Usuários** | 🟢 | 65 → **85%** | ● | ● | ● | Sim — convite, troca obrigatória |
| **Lojas / workspaces** | 🟡 | 55 → **60%** | ◐ | ● | ◐ | Sim — sem criar/editar loja |
| **Registro / trial / onboarding** | 🟡 | 50 → **55%** | ◐ | ◐ | ◐ | Sim — trial não bloqueado no backend |
| **Platform Admin** | 🟡 | 70 → **75%** | ● | ● | ◐ | Sim — sem MFA |
| **Billing / Stripe** | 🟠 | 30 → **45%** | ◐ | ◐ | ◐ | Desligado — **PR 62 bloqueado** |
| **Feature flags** | 🟢 | 25 → **80%** | ● | — | ● | Infraestrutura morta removida; switches de config com efeito |
| **Configurações** | 🟡 | 35 → **70%** | ● | ● | ● | Sim — só campos com efeito |
| **Auditoria / logs** | 🟡 | 45 → **70%** | ● | ● | ◐ | Sim — financeiro e verticais ainda sem auditoria |
| **Produtos e categorias** | 🟡 | 65 → **70%** | ● | ◐ | ◐ | Sim |
| **Estoque** | 🟡 | 45 → **70%** | ◐ | ● | ◐ | Sim — inventário e transferência |
| **Clientes e fornecedores** | 🟡 | 65 → **68%** | ◐ | ● | ○ | Sim |
| **Vendas e devoluções** | 🟡 | 50 → **78%** | ● | ● | ● | Sim |
| **PDV** | 🟡 | 40 → **70%** | ● | ◐ | ● | Sim — troco, CPF na nota, limite de desconto |
| **Caixa** | 🟡 | 45 → **78%** | ● | ● | ● | Sim |
| **Listas de preço** | 🟡 | 20 → **70%** | ● | ● | ◐ | Sim |
| **Compras** | 🟡 | 25 → **72%** | ● | ● | ◐ | Sim |
| **Fiscal** | 🟠 | 0 → **25%** | ◐ | ● | ◐ | Base pronta, **sem emissão** |
| **Financeiro (núcleo)** | 🟡 | 20 → **70%** | ● | ◐ | ◐ | Sim — sem DRE/CMV |
| **Relatórios (núcleo)** | 🟡 | 15 → **60%** | ● | ● | ◐ | Sim — vendas com período e CSV |
| **Dashboard (núcleo)** | 🟡 | 50 → **68%** | ● | ● | ◐ | Sim — mês até hoje |
| **Orken Service** | 🟡 | 65 → **80%** | ● | ● | ◐ | Sim |
| **Orken Menu** | 🟡 | 50 → **68%** | ● | ● | ◐ | Sim |
| **Orken Build** | 🟡 | 55 → **65%** | ● | ◐ | ◐ | Sim |
| **Interpreter** | 🟠 | 35 → **40%** | ◐ | ◐ | ◐ | Motor interno do Build |
| **IA (LLM / OCR)** | ⚪ | 5 → **5%** | ○ | ◐ | ○ | Não existe (fora do escopo da missão) |
| **Storage (R2)** | 🟡 | 65 → **75%** | ● | ● | ● | Sim — privado no código, bucket pendente |
| **Integrações** | 🟡 | 60 → **65%** | ● | ● | ● | Sim — e-mail (Resend) ativo; CEP/CNPJ com switch |
| **Portal — agendamento** | 🟡 | 75 → **85%** | ● | ● | ● | Sim — cancelamento pelo cliente, e-mails |
| **Portal — cardápio/pedido** | 🟡 | 45 → **70%** | ● | ● | ◐ | Sim — pedido vira venda |
| **CI/CD e infra** | 🟡 | 40 → **70%** | — | — | — | Auto-deploy Railway + CI completo |

---

## 4. Por área

### 4.1 Autenticação, plataforma e segurança

**O que é produto.** "Esqueci a senha" com link de uso único guardado como hash; trocar senha encerra as sessões; lista de sessões e "sair de todos"; bloqueio de 15 min após 10 senhas erradas com contador atômico; verificação em duas etapas por aplicativo autenticador com códigos de recuperação; convite por e-mail (72 h) e troca obrigatória de senha; RBAC centralizado com teste que quebra o build se um endpoint ficar sem papel (66 casos de matriz); impersonação auditada como crítica; tela de erros inesperados da plataforma.

**Gaps que restam (por impacto).**
1. **A plataforma não tem verificação em duas etapas**: a senha do super-admin dá acesso a qualquer empresa, e o token de impersonação não é marcado como tal.
2. **Trial vencido não é bloqueado no backend** (só na tela); os controllers do núcleo não exigem módulo ativo.
3. Login ambíguo entre empresas (login é único por empresa, a busca é global).
4. Limite por IP usa o 1º `X-Forwarded-For`, que o cliente forja (o bloqueio por conta atenua).
5. Troca obrigatória de senha só é cobrada no frontend; o cadastro faz várias gravações sem transação; não há criar/editar loja.
6. Billing: Stripe desligado, sem preço para varejo e Service, sem faturas, sem Pix/boleto — **PR 62 bloqueado**.

### 4.2 Núcleo Store

**O que é produto.** Preço decidido no servidor; troco correto e Pix/cartão fora da gaveta; novas tentativas sem venda duplicada; devolução parcial ou total, proporcional e idempotente; cancelamento com motivo; caixa com esperado × contado, sangria e suprimento; inventário por contagem e transferência atômica entre lojas; compras com recebimento atômico (estoque + conta a pagar) e cancelamento que reverte; listas de preço com vigência e prioridade; CPF/CNPJ na nota conferido no servidor; limite de desconto com autorização de gerente auditada; relatórios de vendas com dia do Brasil, devoluções abatidas e planilha; configurações só com o que tem efeito; recibo com os dados do emissor.

**Gaps que restam.**
1. **Sem NFC-e** (base fiscal pronta, emissão depende de provedor e certificado).
2. PDV com uma forma de pagamento por venda; checkout em várias chamadas (não atômico, deixa rascunhos).
3. Ajuste manual de estoque sem trava de negativo nem auditoria; sem lote/validade; custo é o da última compra.
4. Cliente com documento obrigatório e único (força documento falso); limite de crédito gravado mas não conferido.
5. Operador vendedor depende de gerente para abrir o caixa.
6. Produtos, clientes e fornecedores sem testes de integração próprios; E2E só com API simulada.

### 4.3 Financeiro

**O que é produto.** Livro único com categoria gerencial e competência; estados consistentes (quitado se corrige com estorno; estorno de receita é dedução); contas a receber e a pagar com aging, baixa parcial, juros/multa/desconto e parcelamento; recorrências; fluxo de caixa realizado + previsto com saldo inicial; vencidos marcados de hora em hora; vendas, compras, restaurante, Service e Build/Interpreter lançam no livro com travas de concorrência.

**Gaps que restam.**
1. **Sem DRE**: o resultado por competência não desconta o CMV (a tela agora diz "antes do CMV").
2. Baixa sem conta de destino; sem conciliação bancária; sem estorno de baixa.
3. A tela não cobre cancelar/renegociar título, editar recorrência e manter contas (a API tem).
4. Comissão do Service reconhecida só no repasse; Build sem dimensão "obra" no livro.
5. Idempotência só na aplicação (índice por origem não é único); livro limitado a 1000 linhas.

### 4.4 Orken Service

**O que é produto.** Agenda por dia/semana com bloqueios e trava contra agendamento duplo; comanda com profissional por item; pacotes com consumo travado e estorno com motivo (reverte comissão); pagamentos e repasses no financeiro; financeiro do Service por período; portal público com tema por ramo, cancelamento pelo cliente via link e e-mails de confirmação e lembrete enviados no máximo uma vez; prontuário com chave privada validada e URL assinada.

**Gaps que restam.**
1. **Prontuário fraco**: interface só com texto, anexos 503 até o bucket privado existir, exclusão definitiva sem auditoria.
2. Sem papel "profissional": quem atende precisa de login de gestão.
3. Listagens sem paginação (comandas) e KPIs da visão geral calculados no navegador.
4. Cliente do portal grava o telefone como documento.
5. Sem QA visual humano dos PRs 56–59; E2E com API simulada.

### 4.5 Orken Menu, Build e Interpreter

**Menu — produto:** salão, delivery e portal fecham em venda com taxa de entrega, cupom e taxa de serviço cotados; adicionais com tela; cozinha em tempo real que se reconecta; portal com limite de taxa e recusa sem módulo ativo; despesas no livro. **Gaps:** CMV com custo de hoje (não o da venda); resumo soma a folha cheia; fechamento de comanda sem transação; frontend quase sem testes.

**Build — produto:** obras com contadores reais, etapas (criar, editar, reordenar), orçamento com aprovação, diário com fotos privadas, despesas pelo Interpreter com estorno, previsto × realizado. **Gaps:** sem relatórios próprios; sem custo por etapa; converter orçamento e editar diário só pela API; sem PDF de orçamento; testes rasos.

**Interpreter / IA:** texto → regras → rascunho → confirmação com trilha; anexos no disco do container (somem a cada deploy). IA real fora do escopo. O painel de IA da plataforma deixou de prometer criptografia que não existe (PR 74); o liga/desliga de provider e o "Claude" ainda são stub.

### 4.6 Engenharia

**Pronto:** CI nas duas pontas com SonarCloud e GitGuardian; lockfile versionado e build reproduzível do frontend; `/health/live` × `/health/ready` × relatório; `X-Request-Id` de ponta a ponta, logs JSON (Railway interpreta), erros 500 agrupados e mascarados em `error_events`; OpenTelemetry pronto (liga com `OTEL_EXPORTER_OTLP_ENDPOINT`); storage privado com URL assinada, papéis por contexto e magic bytes; flags mortas removidas; 64 migrations sem perda de dados; dívida de nomes "Nexo" classificada; código morto e docs antigas fora do caminho.

**Gaps:** bucket privado (dono); sem Dependabot/CodeQL; backend sem lockfile NuGet nem digest de imagem; disco efêmero (anexos do interpretador, imagens de ficha técnica); sem E2E contra backend real; OTel sem destino e sem alertas; avisos de nulabilidade.

---

## 5. Riscos principais

1. **Comercial:** sem cobrança (PR 62) e sem NFC-e, o produto opera mas não fecha venda de assinatura nem atende a obrigação fiscal do varejo.
2. **Segurança da plataforma:** super-admin sem MFA e impersonação sem marca no token.
3. **Número enganoso evitado, mas DRE pendente:** o resultado sem CMV está rotulado; decisões de margem ainda dependem da DRE.
4. **Dados clínicos:** prontuário sem anexos em produção e exclusão sem trilha.
5. **Regressão em telas de dinheiro do restaurante e do Build:** pouco teste de frontend e fechamento de comanda não atômico.

---

## 6. O que depende do dono

| Ação | Onde | Efeito |
|---|---|---|
| Decidir preços por módulo, plano vitalício, Pix/boleto, período de teste e pacote multimódulo | — | Destrava o PR 62 (cobrança) |
| Criar bucket R2 **privado** (sem domínio público), dar acesso ao token e configurar `Integrations__Storage__R2__PrivateBucketName` | Cloudflare + Railway | Libera anexos de prontuário; `/health/ready` volta a `Healthy` |
| Apontar o healthcheck do serviço da API para `/health/ready` | Railway | Deploy só recebe tráfego com o banco acessível |
| Escolher provedor fiscal e certificado A1 | — | Destrava emissão de NFC-e/NF-e |
| (Opcional) destino OTLP (`OTEL_*`) | Railway | Métricas e traces |
| (Opcional) tornar `frontend-ci` e `backend-tests` checagens obrigatórias | GitHub | Bloqueia merge com CI vermelho |
| Decidir o GitHub #64 (correção do refresh concorrente, de outra sessão) | GitHub | Remove um teste instável |

---

## 7. Próximas ondas recomendadas (por impacto)

1. **MFA no login da plataforma** e token de impersonação marcado; IP real atrás do proxy (`ForwardedHeaders` com proxy conhecido).
2. **Bloqueio de trial e de módulo no backend** para os controllers do núcleo.
3. **DRE com CMV histórico** (custo gravado na venda e na baixa de insumo) — fecha o financeiro e corrige o CMV do Menu.
4. **PDV**: pagamento dividido, venda a prazo na tela, checkout atômico num só endpoint.
5. **Prontuário completo**: anexos na tela, exclusão auditada (ou lógica), registros por comanda e agendamento.
6. **Papel "profissional"** no Service (própria agenda e próprias comissões).
7. **Financeiro operacional**: estorno de baixa, conta de destino, telas de cancelar/renegociar/editar recorrência; índice único por origem.
8. **Build**: relatórios, custo por etapa, telas de converter orçamento e editar diário.
9. **E2E contra backend real** no CI (stack em containers) e testes de integração de produtos/clientes/estoque manual.
10. **Engenharia**: Dependabot + CodeQL, lockfile NuGet, digest de imagens, storage persistente para anexos do interpretador.

---

## 8. PRs da missão

O registro completo, PR a PR, com o que mudou, as correções de cada revisão independente e a evidência de produção, está em [`docs/roadmap/ORKEN_1_0_PROGRESS.md`](../roadmap/ORKEN_1_0_PROGRESS.md). Todos os PRs de 42 a 74 estão em produção, exceto o **62 (cobrança)**, bloqueado por decisão do dono.
