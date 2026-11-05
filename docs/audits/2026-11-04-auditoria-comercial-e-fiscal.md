# Orken ERP — Auditoria comercial e fiscal (04/11/2026)

| | |
|---|---|
| **Data** | 04/11/2026 |
| **Base auditada** | `master` @ `a170ff1` (deploy `SUCCESS` nos serviços `backend` e `main` da Railway) |
| **Método** | Leitura do código, nomes de variáveis do serviço `backend` na Railway (CLI somente leitura; valores não lidos; para segredos do Stripe, só se estão preenchidos) e sondas HTTP anônimas na produção. |
| **Limites** | O painel do Stripe não foi acessado: modo (teste ou produção), endpoint de webhook registrado, eventos assinados e preços cadastrados lá **não foram verificados**. O banco de produção não foi consultado. |
| **Documento irmão** | `2026-11-04-estado-e-maturidade.md` |

Este documento não propõe preços nem estima receita. As perguntas ao dono estão no §2.11 e no §3.5.

---

## 1. Resumo

- **O Stripe está ligado em produção.** `POST https://api.erp.orken.com.br/api/billing/webhook` sem assinatura responde 400 ("Cabeçalho Stripe-Signature ausente"). Com a flag desligada, a resposta seria 404 (`BillingController.cs:188-189`).
- **Só `restaurante` e `build` podem ser comprados.**
  - Há Price IDs só para eles, nas variáveis da Railway e em `appsettings.json`.
  - A tela `/assinatura` lista só esses dois (`nexo-main/src/modules/billing/pages/AssinaturaPage.tsx:19-30`).
  - O cadastro público cria trial de `varejo` (`RegistrationService.cs:80`).
  - Resultado: **quem testa o Store não tem como pagar o Store**, e o `service` também não tem preço.
- **O webhook é bem defendido nos pontos principais:**
  - assinatura verificada antes de tudo;
  - status desconhecido falha fechado;
  - carência de 7 dias para inadimplência;
  - registro de evento processado com índice único.
- **Riscos de cobrança abertos:**
  - a revogação pela plataforma não cancela no Stripe;
  - `customer.subscription.updated` é aplicado sem checar ordem;
  - o trial vencido continua usando o núcleo pela API;
  - MRR e churn são estimativas de catálogo, não valores cobrados.
- **Fiscal:**
  - Existe a base de dados fiscais: emissor por loja, NCM/CEST/CFOP/CST do produto, IE do cliente, CPF na nota e prontidão.
  - **Nenhum documento fiscal é emitido**, nem NFC-e, nem NF-e, nem NFS-e: o provedor é `NotConfiguredFiscalProvider` e o método de emissão não é chamado em nenhum lugar.
  - O comprovante da venda é um PDF "RECIBO DE VENDA" sem aviso de que não é documento fiscal.
- **Financeiro:** livro único com contas a receber e a pagar, parcelamento, recorrência e fluxo de caixa. Faltam:
  - conciliação bancária;
  - agenda de recebíveis de cartão;
  - DRE com CMV;
  - dimensão de obra no livro;
  - passagem do dinheiro do Service pelo caixa.

---

## 2. Comercial

### 2.1 Onde o Stripe está no código

| Peça | Arquivo | Papel |
|---|---|---|
| Endpoints | `nexo-backend/src/Nexo.Api/Controllers/BillingController.cs` | `POST api/billing/checkout`, `POST api/billing/portal`, `GET api/billing/subscriptions`, `POST api/billing/webhook` |
| Chamadas ao Stripe | `nexo-backend/src/Nexo.Infrastructure/Integrations/Stripe/StripeProvider.cs` | Cliente (busca por `metadata['tenantId']` ou cria), Checkout Session (modo assinatura, só cartão) e Customer Portal |
| Webhook | `nexo-backend/src/Nexo.Infrastructure/Integrations/Stripe/StripeWebhookService.cs` | Verificação, idempotência e despacho por tipo de evento |
| Leitura ao vivo da assinatura | `.../Stripe/StripeSubscriptionFetcher.cs` | Usada só no `checkout.session.completed` |
| Regras puras | `nexo-backend/src/Nexo.Application/Integrations/Billing/BillingRules.cs` | Mapa de status (fail closed) e redirect seguro |
| Configuração | `nexo-backend/src/Nexo.Application/Integrations/Options/StripeOptions.cs` | `SecretKey`, `WebhookSecret` e `PriceIds` (`{modulo}_{periodo}`) |
| Assinatura local | `nexo-backend/src/Nexo.Domain/Entities/ModuleSubscription.cs` | Uma linha por (tenant, módulo), com índice único em `ModuleSubscriptionConfiguration.cs:82` |
| Eventos processados | `nexo-backend/src/Nexo.Domain/Entities/StripeProcessedEvent.cs` | Índice único em `StripeEventId` (`StripeProcessedEventConfiguration.cs:37`) |
| Tela | `nexo-main/src/modules/billing/pages/AssinaturaPage.tsx` | Lista de módulos à venda, assinar e "Gerenciar assinatura" |
| Biblioteca | `Nexo.Infrastructure.csproj:29` | `Stripe.net` 52.0.0 |

### 2.2 Fluxos

1. **Checkout** (`BillingController.cs:63-121`, papéis Gerente e Diretoria):
   - Recusa quando a flag está desligada (404) e quando falta Price ID para `{modulo}_{periodo}` (400).
   - Recusa com 409 quando o módulo já tem assinatura viva no Stripe ou uma concessão ativa que não é trial.
   - Cria ou reaproveita o cliente no Stripe.
   - Limita os redirects à origem do app (`BillingRules.SafeRedirect`).
   - Grava `tenantId` e `moduleKey` nos metadados da sessão e da assinatura (`StripeProvider.cs:72-84`).
   - **O acesso não é liberado no retorno do checkout, só pelo webhook.**
2. **Portal do cliente** (`BillingController.cs:127-152`): troca de plano, cartão e cancelamento ficam no Stripe.
3. **Webhook** (`BillingController.cs:184-220`, anônimo). Eventos tratados (`StripeWebhookService.cs:84-92`):

   | Evento | Efeito |
   |---|---|
   | `checkout.session.completed` | Lê a assinatura **ao vivo** no Stripe e cria ou atualiza a linha local com o status real |
   | `customer.subscription.updated` | Sincroniza status, período e `cancel_at_period_end` **a partir do corpo do evento** |
   | `customer.subscription.deleted` | Cancela |
   | `invoice.payment_failed` | Marca inadimplente; não reabre carência de assinatura já encerrada |

   Os demais eventos são aceitos e registrados como processados, sem efeito.

### 2.3 Verificação de assinatura

- `EventUtility.ConstructEvent(rawBody, signature, WebhookSecret, throwOnApiVersionMismatch: false)` roda **antes** de qualquer leitura ou escrita (`StripeWebhookService.cs:52-65`). Usa a tolerância padrão de horário da biblioteca.
- Sem cabeçalho a resposta é 400; com assinatura inválida, 400, sem efeito.
- O teste `A_deleted_subscription_cancels_and_a_bad_signature_changes_nothing` (`nexo-backend/tests/Nexo.UnitTests/Billing/BillingIntegrityTests.cs:177`) assina o corpo como o Stripe faz.
- **Ponto fraco:** não há validação na subida que exija `WebhookSecret` preenchido quando `StripeEnabled` está ligado (`Nexo.Infrastructure/Integrations/DependencyInjection.cs:225`).
  - Com o segredo vazio, a verificação usaria uma chave vazia, que qualquer um pode reproduzir.
  - **Na produção a variável está preenchida** (só a presença foi checada), então o risco é latente, de configuração.

### 2.4 Idempotência

- **O que existe:** `ExistsAsync(eventId)`, depois o despacho, depois o registro do evento (`StripeWebhookService.cs:67-79`), com índice único em `StripeEventId`.
- **Como se comporta:**
  - Uma entrega repetida depois do registro responde 200 sem efeito.
  - Duas entregas simultâneas do mesmo evento passam ambas pela checagem. Os handlers são sincronizações de estado, e a criação de linha é protegida pelo índice único (tenant, módulo). Na prática a segunda falha com 500 e o Stripe reenvia: **efeito idempotente, mas não atômico**.
  - Se o despacho der certo e o registro falhar, o reenvio reprocessa, o que é seguro pelo mesmo motivo.
- **Ordem dos eventos:**
  - O `checkout.session.completed` lê o estado atual no Stripe.
  - O `customer.subscription.updated` aplica o corpo do evento sem comparar com o estado atual (`StripeWebhookService.cs:156-169`). O Stripe não garante ordem, então um `updated` antigo processado depois de um novo pode voltar o status. Exemplo: `past_due` aplicado depois de `active`, deixando a assinatura paga marcada como inadimplente até o próximo evento.
- **Testes:**
  - `BillingIntegrityTests` (8 métodos) e `BillingControllerTests` (15) são todos unitários, com repositórios simulados.
  - **Não há teste de integração** do webhook contra PostgreSQL, nem do caso de entrega simultânea.

### 2.5 Modo teste × produção (só nomes)

| Nome da configuração | Onde | Situação em produção |
|---|---|---|
| `Integrations__Features__StripeEnabled` | Railway, serviço `backend` | Presente. Efeito observado: ligado (sonda do webhook) |
| `Integrations__Stripe__SecretKey` | idem | Presente e preenchida |
| `Integrations__Stripe__WebhookSecret` | idem | Presente e preenchida |
| `Integrations__Stripe__PriceIds__restaurante_monthly`, `__restaurante_annual`, `__build_monthly`, `__build_annual` | idem | Presentes e preenchidas |
| `Integrations__Stripe__PriceIds__varejo_*`, `__service_*` | — | **Ausentes** |
| `App__FrontendUrl` | idem | Presente (base dos redirects) |

- **O código não distingue teste de produção:** há uma única `SecretKey`, e o modo é o da própria chave.
- O ERP não tem staging, então não existe ambiente isolado para exercitar o checkout sem tocar a configuração de produção.
- **O modo atual não foi determinado nesta auditoria.**

### 2.6 Planos e preços: fonte da verdade

Há três lugares, e nenhum é a fonte única:

| Lugar | O que define | Quem usa |
|---|---|---|
| Variáveis `Integrations__Stripe__PriceIds__*` | O preço que o checkout cobra (Price IDs do Stripe) | `BillingController.cs:76-78` |
| Tabela `module_definitions` (`ModuleDefinition.cs`) | Preços de referência em BRL e colunas de Price ID do Stripe | **Só o cálculo de MRR** (`PlatformController.cs:1121`). As colunas de Price ID não são lidas pelo checkout |
| `AssinaturaPage.tsx:19-30` | Quais módulos aparecem à venda (`restaurante`, `build`) | A tela. Ela não mostra preço: o valor só aparece na página do Stripe |

- **A tabela `module_definitions` não é populada em produção pelo código.** O seeder que cria as definições, com os preços de referência em `DataSeeder.cs:159-167` e `:515-520`, roda só fora de produção (`Program.cs:452-455`).
- Não há endpoint para administrar essas definições.
- O conteúdo da tabela em produção não foi lido.

### 2.7 Trials

- **Origem:** o cadastro público (`POST api/auth/register`, anônimo, com rate limit `auth-login`) cria tenant, loja, usuário (Diretoria, pendente de verificação), configurações e uma assinatura `Trial` de **`varejo` por 7 dias** (`RegistrationService.cs:69-110`; `ModuleSubscription.CreateTrial`, padrão de 7 dias, `ModuleSubscription.cs:91-102`).
  - São cinco `SaveChangesAsync` **sem transação**: uma falha no meio deixa um tenant incompleto.
- **`TrialEndsAt` mostrado na sessão** vem da linha de trial (`StoreRepository.cs:46-51`).
  - A coluna `Tenant.TrialEndsAt` existe, mas **nunca é gravada**: `Tenant.SetTrialEnd` (`Tenant.cs:79`) não tem chamador.
- **Vencimento:**
  - **Backend:** a regra única `HasAccessAt` (`ModuleSubscription.cs:44-51`) tira o acesso dos controladores com gate quando o período acaba (falha fechado).
  - **Frontend:** `ProtectedRoute.tsx:36-45` leva para `/assinatura` quando o trial venceu e não há módulo ativo.
  - **Lacuna:** produtos, estoque, caixa, clientes, fornecedores, compras, financeiro e fiscal não têm gate de módulo. O tenant com trial vencido continua usando essas rotas pela API.
- **Lista de trials vencidos da plataforma** (`GET api/platform/tenants/trial-expired`, `PlatformController.cs:1012-1060`, tela `/platform/trial`):
  - O primeiro ramo usa `Tenant.TrialEndsAt`, que nunca é preenchida.
  - O segundo filtra `Status == Active`, mas o trial é `Trialing`.
  - **Um trial vencido nunca aparece nessa lista.**

### 2.8 Assinaturas por módulo e acesso

- **Regra de acesso:** ativa ou em trial dentro do período (fim nulo = vitalício), ou inadimplente há menos de 7 dias (`ModuleSubscription.cs:44-51`, `PastDueGraceDays = 7`).
- **Gates:**
  - `[RequireModule("build")]` e `[RequireModule("restaurante")]` em todos os controladores desses módulos;
  - `[RequireServiceModule]` no Service;
  - `[RequireModule("varejo")]` só em `SalesController`, `PdvController` e `PriceListsController`.
- **Cache:** o webhook invalida o cache por módulo e o do tenant no Redis (`StripeWebhookService.cs:203-208`).
- **Acesso antes do pagamento:** não acontece pelo checkout. `incomplete`, `paused` e status desconhecido viram `Suspended` (`BillingRules.cs:12-20`), e a linha nova já nasce com o status real (`StripeWebhookService.cs:145-148`).
- **Concessão pela plataforma** (`PlatformController.cs:407-489`, política `PlatformAdmin`):
  - **Revogar** cancela só a linha local. **Não cancela a assinatura no Stripe:** o cliente continua sendo cobrado. O próximo `customer.subscription.updated` (por exemplo, a renovação) devolve o status do Stripe e reabre o acesso.
  - **Renovar** uma linha existente muda status e período, mas mantém o `PlanType` e o `StripeSubscriptionId` antigos. Uma linha que veio do Stripe e foi renovada pelo admin passa a contar como pagante no MRR e como viva no Stripe (o que bloqueia um novo checkout), sem cobrança real.
- **Assinaturas criadas direto no painel do Stripe** (sem Checkout) não têm o metadado `tenantId`. Não existe handler de `customer.subscription.created`, então elas nunca liberam acesso.

### 2.9 MRR e churn: estimativa, não valor cobrado

- **MRR** (`GET api/platform/mrr`, `PlatformController.cs:1109-1165`):
  - É a soma, sobre assinaturas `Active` e dentro do período, do **preço de catálogo** da `module_definitions`: mensal = preço mensal; anual = anual ÷ 12; trial, concessão e vitalício = 0.
  - Não usa o valor faturado no Stripe nem o `StripePriceId` da assinatura.
  - Ignora cupons e descontos.
  - Exclui `Trialing` e `PastDue`.
  - Pula assinaturas cujo módulo não tem definição. Se a tabela estiver vazia em produção (§2.6), o MRR mostrado é zero; isso não foi verificado.
- **Churn** (`GET api/platform/churn`, `PlatformController.cs:1171-1211`):
  - É canceladas no período ÷ (ativas agora + canceladas no período).
  - Mistura revogações da plataforma com cancelamentos de clientes.
  - Não usa coorte.
- **Os dois números são indicadores aproximados.** Não há conciliação com faturas do Stripe.

### 2.10 Riscos comerciais

| # | Risco | Gravidade | Evidência |
|---|---|---|---|
| C1 | Trial de `varejo` sem caminho de compra; `service` sem preço | Alta | `RegistrationService.cs:80`; `AssinaturaPage.tsx:19-30`; Price IDs só de `restaurante` e `build` |
| C2 | Revogar na plataforma não cancela no Stripe: cobrança continua, e o próximo evento reabre o acesso | Alta | `PlatformController.cs:457-489`; `StripeWebhookService.cs:156-169` |
| C3 | Trial vencido mantém o núcleo pela API: o bloqueio é só na tela | Alta | Gates do §2.8; `ProtectedRoute.tsx:36-45` |
| C4 | Endpoint do webhook depois da mudança de host: o backend saiu de `api.orken.com.br` para `api.erp.orken.com.br` (PR #93). Hoje `POST https://api.orken.com.br/api/billing/webhook` responde 401 (outro serviço). Se o Stripe ainda apontar para o host antigo, nenhum evento chega | Alta se estiver errado (não verificável pelo repositório) | Sondas de 04/11 |
| C5 | `customer.subscription.updated` aplicado fora de ordem | Média | `StripeWebhookService.cs:156-169` |
| C6 | MRR e churn não refletem o que foi cobrado; renovação administrativa conta como pagante | Média | §2.8, §2.9 |
| C7 | Lista de trials vencidos sempre vazia para trials | Média | §2.7 |
| C8 | Cadastro sem transação | Baixa | `RegistrationService.cs:69-110` |
| C9 | Sem validação de `WebhookSecret` na subida | Baixa (preenchido em produção) | `DependencyInjection.cs:225` |
| C10 | Checkouts simultâneos podem criar dois clientes no Stripe (a busca do Stripe é eventualmente consistente) | Baixa | `StripeProvider.cs:24-50`; `BillingController.cs:96-105` |

### 2.11 Decisões comerciais pendentes (para o dono)

1. Quais módulos são vendidos hoje: só `restaurante` e `build`, ou também `varejo` e `service`? Em quais combinações?
2. O trial do cadastro deve continuar sendo de `varejo`? Com quantos dias? Deve exigir cartão?
3. Depois do trial vencido, ou de um cancelamento, o que o cliente ainda pode fazer: nada, somente leitura, ou exportar os dados? Isso define o gate do núcleo.
4. Qual é a fonte da verdade de planos, preços e acesso por módulo: o Stripe, a tabela `module_definitions` do ERP ou a Orken Platform (repositório `orken-platform`)? Hoje o código do ERP não tem integração com a Platform: não há referência a OIDC nem à Platform em `nexo-backend/src` e `nexo-main/src`.
5. Os períodos trimestral, semestral e vitalício (`PlanType.cs`, colunas da `module_definitions`) devem existir comercialmente, ou só mensal e anual?
6. A assinatura aceita só cartão (`StripeProvider.cs:61`). Boleto ou Pix devem ser oferecidos?
7. Quem emite a nota fiscal da própria assinatura do Orken (NFS-e do serviço prestado ao cliente)? Por qual sistema?
8. A carência de 7 dias para inadimplência deve continuar?
9. Como tratar cortesias, concessões e "vitalício" no MRR e no churn?
10. **Conferir no painel do Stripe:**
    - modo (teste ou produção);
    - URL do endpoint (`https://api.erp.orken.com.br/api/billing/webhook`);
    - os quatro eventos assinados (§2.2);
    - Customer Portal configurado;
    - produtos e preços que correspondem aos Price IDs das variáveis.

---

## 3. Fiscal e financeiro

### 3.1 O que existe para documentos fiscais (base comum)

| Peça | Evidência |
|---|---|
| Emissor por loja: razão social, CNPJ, IE (ou "ISENTO"), IM (para NFS-e), regime, endereço com código IBGE, natureza da operação, séries e próximos números de NFC-e e NF-e | `nexo-backend/src/Nexo.Domain/Entities/FiscalProfile.cs:16-40`. O CRT é derivado do regime (`:46`) |
| Dados fiscais do produto: NCM, CEST, origem, CFOP, CST/CSOSN e unidade tributável | `nexo-backend/src/Nexo.Infrastructure/Fiscal/FiscalService.cs:97-101` |
| IE do cliente pessoa jurídica | `FiscalService.cs:115` |
| CPF/CNPJ do consumidor na venda | `Sale.ConsumerDocument` (`Sale.cs:45,102-106`), impresso no recibo |
| Prontidão: emissor faltando, produtos sem NCM e código de barras que não é GTIN | `FiscalService.cs:129-160`; `GET api/fiscal/readiness` |
| Validações: CPF, CNPJ, GTIN, NCM, CEST, município IBGE e UF | `nexo-backend/src/Nexo.Application/Common/FiscalCodes.cs` |
| Tela | `/fiscal` (`nexo-main/src/modules/fiscal/pages/FiscalPage.tsx`), papéis de gestão |
| Testes | Integração: `FiscalFoundationTests` (4). Unitários: `FiscalCodesTests` (10 métodos, 42 `InlineData`). Vitest: `modules/fiscal` (4). E2E com API simulada: emissor e produtos, CPF na nota no PDV |
| Provedor de emissão | Só a interface `IFiscalDocumentProvider` com `NotConfiguredFiscalProvider`, que responde "A emissão de nota fiscal ainda não está disponível." (`nexo-backend/src/Nexo.Application/Features/Fiscal/IFiscalDocumentProvider.cs:35-43`), registrado em `Nexo.Infrastructure/DependencyInjection.cs:348` |
| Chamada de emissão | **Nenhuma.** `IssueConsumerNoteAsync` não é chamado em nenhum ponto do backend |
| Flag | `Integrations:Features:FiscalEnabled = false` (`appsettings.json:82`). Não há variável `Integrations__Features__FiscalEnabled` na Railway |
| Comprovante | PDF "RECIBO DE VENDA" com CNPJ do emissor e CPF do consumidor (`nexo-backend/src/Nexo.Infrastructure/Integrations/Pdf/SaleReceiptDocument.cs:28-38`), **sem aviso de que não é documento fiscal** |
| Tax Intelligence | Domínio tributário em modo sombra, desligado por padrão (`docs/tax-intelligence/README.md`). Não há variável `TaxIntelligence__*` no serviço `backend`. Não emite nem altera nada fiscal. A finalização está na PR #85 (aberta) |

### 3.2 Documentos fiscais por módulo

| Módulo | Documento esperado | Emissão | Cancelamento | Contingência | XML/DANFE guardados | Observação |
|---|---|---|---|---|---|---|
| **Store** | NFC-e no PDV; NF-e em venda para empresa e devolução | Não | Não | Não | Não | Dados do emissor e do produto já existem; IE do cliente PJ existe |
| **Menu** | NFC-e (a comanda paga vira venda do núcleo) | Não | Não | Não | Não | Vale o mesmo caminho do Store (`OrderService.cs:366-503` chama a confirmação do núcleo) |
| **Service** | NFS-e (municipal) | Não | Não | Não | Não | O perfil tem a IM, mas nada consome. O pagamento do Service não é uma `Sale` |
| **Build** | NFS-e (serviços e medição); NF-e se houver venda de material | Não | Não | Não | Não | Medição e orçamento não geram título nem documento |

### 3.3 Caixa e financeiro

**O que existe:**

- **Livro único** (`FinancialTransaction`; migration `20261014221451_UnifiedFinancialLedger`):
  - competência e caixa;
  - contas a receber e a pagar com baixa parcial, juros, multa e desconto;
  - parcelamento (1 a 60 parcelas, `FinancialService.cs:371`);
  - recorrências, saldo inicial por conta, estorno por contrapartida e fluxo de caixa realizado × previsto.
- **API:** `api/financial`, 25 endpoints, papéis de gestão.
- **Telas:** `ContasReceberPage`, `ContasPagarPage`, `FinanceiroPage` e `FluxoCaixaPage`.
- **Testes:** integração 21 (`CashFlowTests`, `DefaultAccountProvisioningTests`, `FinancialLedgerTests`, `PayablesTests`, `ReceivablesTests`); unitários 18; 4 e2e com API simulada.
- **Caixa** (`api/cash`): sessão por loja com esperado e diferença no fechamento, sangria e suprimento, e PDF do fechamento. Abrir caixa usa trava consultiva (`CashRepository.cs:24-28`).

**Como cada módulo lança dinheiro:**

| Módulo | Gaveta (caixa) | Livro | Lacuna |
|---|---|---|---|
| Store | Dinheiro, líquido do troco | Todos os meios: à vista liquidado; a prazo como título pendente (`SaleService.cs:230-283`). A compra vira conta a pagar | "Cartão" sempre vira débito na tela (`use-pos-sale.ts:25`). O cartão entra liquidado no dia da venda, sem prazo da adquirente nem taxa |
| Menu | Via venda do núcleo | Via venda do núcleo; despesas lançadas | Folha fora do livro; CMV do restaurante com custo atual |
| Service | **Não passa** | Recebível liquidado com a loja; repasse de comissão vira conta a pagar | Dinheiro físico do Service fora da conferência da gaveta |
| Build | Não se aplica | Movimentações do interpretador lançadas como liquidadas, **sem loja nem obra** (`VerticalLedgerPostingService.cs:105-110`) | Medição e orçamento não viram conta a receber |

**Lacunas do financeiro:**

1. **Sem conciliação bancária:** não há importação de extrato (OFX) nem casamento de lançamentos. A busca por `reconcil|concilia|ofx|extrato` no backend só encontra o split payment do Tax Intelligence.
2. **Sem agenda de recebíveis de cartão** nem taxa da adquirente, e não há integração com adquirente ou maquininha.
3. **Sem emissão de cobrança** (boleto ou Pix com QR) para contas a receber.
4. **Sem DRE com CMV.** A tela mostra "Resultado (antes do CMV)" (`nexo-main/src/modules/financial/pages/FinanceiroPage.tsx:101`), e o item de venda não guarda custo.
5. **Sem dimensão de obra no livro**, e sem a passagem do Service pela gaveta.
6. **Vulnerabilidade apontada pelo Sonar (#84) em `FinancialService.cs:407`** (S6680, limite de laço). O laço está limitado a 1–60 parcelas na linha 371, o que indica falso positivo. A confirmação fica para a triagem do #84.

### 3.4 Plano de implementação ordenado

**Esforço:** P = até alguns dias; M = uma a duas semanas; G = várias semanas; GG = mais de um mês. São tamanhos relativos, não prazos.

**A. Pode começar sem decisão do dono** (engenharia):

| Ordem | Item | Depende de | Risco | Esforço |
|---|---|---|---|---|
| A1 | Aviso "não é documento fiscal" no recibo PDF, com texto validado pela contabilidade antes do merge | — | Baixo | P |
| A2 | Corrigir "cartão sempre débito" no PDV e na comanda (escolher crédito ou débito) | — | Baixo | P |
| A3 | Custo do item na venda (snapshot na confirmação) e DRE com CMV; CMV do restaurante pelo custo da venda | — | Médio (mexe na confirmação da venda) | M |
| A4 | Tabela de documentos fiscais com máquina de estados (pendente, autorizado, rejeitado, cancelado, em contingência), fila com nova tentativa em segundo plano e guarda do XML no bucket privado. A venda nunca espera a nota, como já prevê `IFiscalDocumentProvider.cs:3-9` | — | Médio | M |
| A5 | Teste de integração do webhook contra PostgreSQL (entrega duplicada e simultânea); leitura ao vivo no `customer.subscription.updated`; validação de `WebhookSecret` na subida | — | Baixo | P |
| A6 | Corrigir a lista de trials vencidos (usar a linha de trial); cadastro em transação | — | Baixo | P |

**B. Depende de decisão do dono ou da contabilidade:**

| Ordem | Item | Decisão necessária | Depende de | Risco | Esforço |
|---|---|---|---|---|---|
| B1 | Provedor fiscal (adaptador de `IFiscalDocumentProvider`) e ambiente de homologação. Sem staging no ERP, a homologação precisa de configuração isolada | Qual provedor; quem contrata e paga; custódia do certificado A1 por loja | A4 | Alto (conformidade) | M |
| B2 | NFC-e no PDV e na comanda do Menu: emissão, QR Code, cancelamento no prazo, inutilização e contingência | Regimes atendidos no lançamento; CFOP/CST padrão por regime; política de contingência offline; UFs atendidas | B1 | Alto | G |
| B3 | NF-e (venda para empresa, devolução de compra) | Quais operações entram na primeira versão | B1, B2 | Alto | G |
| B4 | NFS-e para Service e Build | Municípios atendidos (cada prefeitura tem regras); quem é o tomador; retenções | B1 | Alto (variação municipal) | G a GG |
| B5 | Faturamento do Build: medição ou parcela vira conta a receber, e o livro ganha dimensão de obra | Modelo de cobrança da obra (por medição, parcelas ou aditivos) | — | Médio | M |
| B6 | Dinheiro do Service na gaveta | Uma gaveta para negócio misto ou caixas separados | — | Médio | P a M |
| B7 | Agenda de recebíveis de cartão e taxas | Quais adquirentes; integrar ou lançar manualmente | A2 | Médio | M |
| B8 | Conciliação bancária (importação de extrato e casamento) | Bancos e formato (OFX ou Open Finance) | — | Médio | G |
| B9 | Gate do núcleo após trial ou cancelamento e caminho de compra de `varejo`/`service` | §2.11, perguntas 1 a 3 | — | Médio (afeta clientes ativos) | M |
| B10 | Revogação na plataforma cancelando também no Stripe; MRR a partir do que foi faturado | §2.11, perguntas 4 e 9 | A5 | Médio | M |

**Sequência sugerida:**

1. Primeiro A1, A2, A5 e A6, que são pequenos e reduzem risco já.
2. Depois A3 e A4, que preparam o terreno.
3. B1 e B2 assim que o provedor for escolhido, porque o Store e o Menu não fecham comercialmente sem NFC-e.
4. B4 depois, porque a NFS-e varia por município.
5. B5 a B10 conforme as respostas do §2.11 e do §3.5.

### 3.5 Decisões fiscais e contábeis pendentes

1. Qual provedor de emissão será usado? `docs/ORKEN_INTEGRATIONS_PLAN.md` §Fase 10 cita NFe.io e Focus NFe como opções, sem decisão registrada.
2. Quais regimes tributários entram no lançamento (Simples, MEI, regime normal) e quais UFs?
3. Que CFOP e CST/CSOSN padrão o sistema propõe por regime e operação? A tabela precisa ser validada por um contador.
4. Qual é a política de contingência da NFC-e (offline) e o prazo operacional de cancelamento adotado?
5. Quais municípios a NFS-e atende no início (Service e Build)?
6. O certificado A1 fica com o provedor, com o Orken ou com o cliente? Quem paga?
7. Qual é o modelo de cobrança do Build: medição, parcelas ou aditivos? Isso define B5.
8. Como apresentar a DRE (plano de contas, CMV, despesas de pessoal por competência)?
