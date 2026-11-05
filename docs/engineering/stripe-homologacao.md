# Homologação do Stripe em modo de teste

Roteiro para exercitar a cobrança do ERP sem dinheiro real, antes do primeiro cliente pagante.

**Nada deste roteiro foi executado contra o Stripe.** O código e os testes usam eventos assinados localmente e um cliente
Stripe simulado. Os comandos do CLI e os cartões vêm da documentação pública do Stripe e podem pedir ajuste na primeira
execução: anote aqui o que mudar.

O roteiro descreve o comportamento com as guardas de configuração e a releitura da assinatura já na `master`
(`docs/audits/2026-11-04-auditoria-comercial-e-fiscal.md`, §2.4 e §2.8).

## 1. Pré-requisitos humanos

Tudo isso depende de uma pessoa com acesso ao painel do Stripe. Nada de dado real: clientes, e-mails e empresas de teste.

| O quê | Onde | Observação |
|---|---|---|
| Conta em **modo de teste** | Painel do Stripe | Nunca use `--live` no CLI neste roteiro |
| Chave `sk_test_...` | Developers → API keys | Fora de `Production`, o backend recusa subir com `sk_live_` ou `rk_live_` |
| Preços de teste | Product catalog | Um `price_...` por combinação vendida: `restaurante_monthly`, `restaurante_annual`, `build_monthly`, `build_annual` |
| Endpoint de webhook de teste | Developers → Webhooks | URL `https://<staging>/api/billing/webhook`, com os eventos do §3.1. Guarde o `whsec_...` |
| Versão da API do endpoint | Idem | `2025-03-31.basil` ou mais nova. O handler lê a assinatura da fatura em `parent.subscription_details`: em versão anterior, `invoice.payment_failed` passa sem efeito |
| Customer Portal de teste | Settings → Billing → Customer portal | Com cancelamento e troca de plano ligados |
| Stripe CLI | Máquina de quem executa | `stripe login` (modo de teste é o padrão) |
| Staging do ERP | Railway ([`staging-e-promocao.md`](staging-e-promocao.md)) | Banco próprio, dados sintéticos do `DataSeeder`. O Stripe fica desligado lá até haver `sk_test_...` |

Variáveis a acrescentar ao backend em staging. Não copie nada de produção:

| Variável | Valor |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Staging` (já definida) |
| `Integrations__Features__StripeEnabled` | `true` |
| `Integrations__Stripe__SecretKey` | `sk_test_...` |
| `Integrations__Stripe__WebhookSecret` | `whsec_...` do endpoint de staging |
| `Integrations__Stripe__PriceIds__{modulo}_{periodo}` | `price_...` de teste |
| `App__FrontendUrl` | URL do frontend de staging (base dos redirects do checkout e do portal) |

Com `StripeEnabled=true`, o backend recusa subir sem `SecretKey` e `WebhookSecret`. A mensagem cita o nome da
configuração, nunca o valor.

## 2. Como conferir o resultado

Em cada passo, olhe três coisas: a linha, o acesso e o registro do evento.

**Linha e registro**, em SQL somente leitura no banco de staging:

```sql
SELECT module_key, status, plan_type, stripe_subscription_id, stripe_price_id,
       current_period_end, cancel_at_period_end, past_due_since, canceled_at
FROM nexo.module_subscriptions
WHERE tenant_id = '<tenant>';

SELECT stripe_event_id, event_type, created_at
FROM nexo.stripe_processed_events
ORDER BY created_at DESC
LIMIT 10;
```

**Acesso**, com a sessão de um usuário Gerente ou Diretoria da empresa de teste:

```bash
curl -s -o /dev/null -w '%{http_code}\n' -H "Authorization: Bearer $TOKEN" \
  https://<staging-api>/api/v1/build/budget-templates
```

`200` tem acesso, `403` não tem. Para `restaurante`, use uma rota do módulo. O webhook invalida o cache, então o
resultado muda na hora.

| Status do Stripe | `status` na linha | Acesso |
|---|---|---|
| `active`, `trialing` | `Active`, `Trialing` | Sim, até `current_period_end` |
| `past_due` | `PastDue` | Sim, por 7 dias contados da primeira falha (`past_due_since`) |
| `incomplete`, `paused`, `unpaid`, desconhecido | `Suspended` | Não |
| `canceled`, `incomplete_expired` | `Canceled` | Não |

Sem nenhum módulo com acesso, a empresa fica só com leitura: as escritas respondem 402 `subscription_required`, salvo as
rotas isentas, como checkout e portal ([`tenant-access-enforcement.md`](tenant-access-enforcement.md)).

**Regra de ordem:** `customer.subscription.updated` e `invoice.payment_failed` não aplicam o corpo do evento. O backend
relê a assinatura atual no Stripe e grava esse estado, com a linha travada até o fim da transação. Se a leitura falhar, o
webhook responde 500, nada é gravado e o Stripe reenvia. O registro em `stripe_processed_events` entra na mesma transação
dos efeitos: evento registrado é evento aplicado.

## 3. Passo a passo

### 3.1 Eventos que o endpoint precisa assinar

```
checkout.session.completed
customer.subscription.updated
customer.subscription.deleted
invoice.payment_failed
```

`StripeWebhookService` trata só esses. Qualquer outro evento é aceito e registrado, sem efeito.

### 3.2 Máquina local, com `stripe listen`

O `stripe listen` assina com um segredo próprio, diferente do `whsec_` do endpoint do painel. Use esse segredo no backend
local, com um PostgreSQL local (`ConnectionStrings__Default`; `docker-compose.dev.yml` sobe um na porta 5433):

```bash
stripe listen --print-secret   # devolve o whsec_ desta máquina

cd nexo-backend/src/Nexo.Api
ASPNETCORE_ENVIRONMENT=Development \
Integrations__Features__StripeEnabled=true \
Integrations__Stripe__SecretKey=sk_test_... \
Integrations__Stripe__WebhookSecret=whsec_... \
dotnet run --urls http://localhost:5199

stripe listen \
  --events checkout.session.completed,customer.subscription.updated,customer.subscription.deleted,invoice.payment_failed \
  --forward-to localhost:5199/api/billing/webhook
```

Em staging não é preciso `stripe listen`: o endpoint do painel entrega direto, inclusive com os reenvios reais do Stripe.

### 3.3 Fumaça com `stripe trigger`

O `stripe trigger` cria objetos próprios do CLI, sem os metadados do ERP. Serve para provar que assinatura, roteamento e
registro funcionam. Não serve para liberar acesso.

```bash
stripe trigger checkout.session.completed
stripe trigger customer.subscription.updated
stripe trigger customer.subscription.deleted
stripe trigger invoice.payment_failed
```

**Esperado em todos:** o webhook responde 200 e o evento entra em `stripe_processed_events`, sem nenhuma linha nova ou
alterada em `module_subscriptions`. No checkout, o log registra a falta de metadados
(`checkout.session.completed missing metadata`). Nos demais, a assinatura do CLI não tem linha local.

### 3.4 Checkout

1. Entre no ERP de staging como Gerente ou Diretoria de uma empresa de teste e abra `/assinatura`.
2. Assine `build` (ou `restaurante`). O ERP abre o checkout do Stripe.
3. Pague com `4242 4242 4242 4242`, validade futura, qualquer CVC.

| Olhar | Esperado |
|---|---|
| Linha | `status=Active`, `plan_type=Monthly` ou `Annual`, `stripe_subscription_id` e `stripe_price_id` preenchidos |
| Acesso | `200` |
| Registro | `checkout.session.completed` e os `customer.subscription.updated` que vierem junto |

O acesso nunca vem da volta do checkout, só do webhook. Se o evento atrasar, a tela mostra sucesso e a rota continua `403`
até ele chegar.

**Variações:**

| Cartão | Esperado |
|---|---|
| `4000 0000 0000 0002` (recusado) | O checkout não conclui. Nenhuma linha, acesso `403` |
| `4000 0025 0000 3155` (exige autenticação) | Sem autenticar, nada muda. Autenticado, igual ao caso `4242` |

**Segundo checkout do mesmo módulo:** o ERP responde 409 e manda para "Gerenciar assinatura".

Guarde os ids para os próximos passos:

```bash
SUB=sub_...   # stripe_subscription_id da linha
CUS=cus_...   # tenants.stripe_customer_id
```

### 3.5 Renovação

Força a virada de ciclo agora, porque o cliente foi criado pelo ERP e não está em um relógio de teste:

```bash
stripe subscriptions update $SUB -d billing_cycle_anchor=now -d proration_behavior=none
```

| Olhar | Esperado |
|---|---|
| Linha | `status=Active`, `current_period_end` avançou um ciclo, `past_due_since` vazio |
| Acesso | `200` |
| Registro | `customer.subscription.updated` |

### 3.6 Pagamento falho

Troca o cartão padrão por um que aceita ser cadastrado e falha na cobrança, depois força a virada de ciclo:

```bash
stripe payment_methods attach pm_card_chargeCustomerFail --customer $CUS   # devolve pm_FALHA
stripe subscriptions update $SUB -d default_payment_method=pm_FALHA
stripe subscriptions update $SUB -d billing_cycle_anchor=now -d proration_behavior=none
stripe subscriptions retrieve $SUB   # confira status=past_due
```

| Olhar | Esperado |
|---|---|
| Linha | `status=PastDue`, `past_due_since` preenchido e sem mudar em novas tentativas |
| Acesso | `200` durante a carência de 7 dias |
| Registro | `invoice.payment_failed` e `customer.subscription.updated` |

O comportamento depois das tentativas depende de Settings → Billing → Manage failed payments. Anote a configuração usada.

**Fim da carência, sem esperar 7 dias:** em staging, recue a data e confira o `403`. Só em staging.

```sql
UPDATE nexo.module_subscriptions
SET past_due_since = now() - interval '8 days'
WHERE stripe_subscription_id = '<sub>';
```

O cache de acesso dura até 5 minutos. Um novo evento da assinatura também o limpa.

### 3.7 Recuperação

```bash
stripe payment_methods attach pm_card_visa --customer $CUS   # devolve pm_OK
stripe subscriptions update $SUB -d default_payment_method=pm_OK
stripe invoices pay in_...   # a fatura em aberto, em latest_invoice da assinatura
```

| Olhar | Esperado |
|---|---|
| Linha | `status=Active`, `past_due_since` vazio |
| Acesso | `200` |
| Registro | `customer.subscription.updated` |

### 3.8 Portal: troca de plano

Em `/assinatura`, use "Gerenciar assinatura" e troque de mensal para anual.

| Olhar | Esperado |
|---|---|
| Linha | `stripe_price_id` novo, `plan_type=Annual`, período atualizado |
| Acesso | `200` |
| Registro | `customer.subscription.updated` |

### 3.9 Portal: cancelar no fim do período

No portal, cancele a assinatura. O padrão é cancelar ao fim do período.

| Olhar | Esperado |
|---|---|
| Linha | `status=Active`, `cancel_at_period_end=true` |
| Acesso | `200` até o fim do período pago |
| Registro | `customer.subscription.updated` |

### 3.10 Cancelamento

```bash
stripe subscriptions cancel $SUB
```

| Olhar | Esperado |
|---|---|
| Linha | `status=Canceled`, `canceled_at` preenchido |
| Acesso | `403` na hora |
| Registro | `customer.subscription.deleted` |

Depois disso, um novo checkout do módulo volta a ser aceito e a mesma linha passa a seguir a assinatura nova.

### 3.11 Entrega repetida

```bash
stripe events resend evt_... --webhook-endpoint we_...
```

**Esperado:** 200, nenhuma mudança na linha e uma só linha daquele evento em `stripe_processed_events`.

### 3.12 Revogação pela plataforma

Com a assinatura viva, em `/platform` tente revogar o módulo da empresa e tente concedê-lo de novo.

| Ação | Esperado |
|---|---|
| Revogar assinatura viva | 409, com a mensagem de que é preciso cancelar no Stripe primeiro. Linha sem mudança |
| Conceder ou renovar assinatura viva | 409. Linha sem mudança |
| Revogar concessão sem Stripe | 204, acesso `403` na hora |
| Conceder depois do cancelamento (§3.10) | 204. A linha vira `plan_type=AdminGrant` e perde `stripe_subscription_id` e `stripe_price_id` |

## 4. Eventos sintéticos

`nexo-backend/tools/stripe_webhook_sim.py` assina eventos localmente com o segredo do webhook e os envia a uma URL. Só usa
a biblioteca padrão do Python. Não fala com o Stripe e não aceita chave do Stripe.

```bash
export STRIPE_WEBHOOK_SECRET=whsec_...
URL=https://<staging-api>/api/billing/webhook

python nexo-backend/tools/stripe_webhook_sim.py --list
python nexo-backend/tools/stripe_webhook_sim.py --url $URL --all --expect 200
python nexo-backend/tools/stripe_webhook_sim.py --url $URL --event invoice.paid --repeat 3 --expect 200
python nexo-backend/tools/stripe_webhook_sim.py --url $URL --event invoice.paid --concurrent 5 --expect 200
python nexo-backend/tools/stripe_webhook_sim.py --url $URL --event invoice.paid --bad-signature --expect 400
python nexo-backend/tools/stripe_webhook_sim.py --url $URL --event invoice.paid --stale-signature --expect 400
```

**O que ele prova:** assinatura, janela de horário, idempotência e entrega simultânea, com o caminho real até o banco.

**Limite que vem da releitura:** o backend lê no Stripe a assinatura de um evento que tem linha local. Com um `sub_` sintético
não há linha, o evento é registrado e nada muda. Apontar `--subscription` para uma linha real tem estes efeitos:

| Evento | Com `sub_` de uma linha real |
|---|---|
| `customer.subscription.updated`, `invoice.payment_failed` | Lê a assinatura real de teste e aplica o estado dela, não o `--status`. Se ela não existir no Stripe, responde 500 e não registra |
| `customer.subscription.deleted` | Cancela a linha sem ler o Stripe, porque cancelada não volta. Só use em linha de teste |
| `checkout.session.completed` com `--tenant-id` | Lê a assinatura no Stripe. Sintética: responde 500 e não registra |

**Proteção de URL:** recusa `api.erp.orken.com.br`, `erp.orken.com.br`, `www.orken.com.br`, os hosts da Orken Platform e
subdomínios de `api.erp.orken.com.br`, em qualquer grafia. Também recusa `http://` fora da máquina local e não segue
redirecionamento. O domínio interno de um serviço de produção no Railway não dá para detectar. Nesse caso, a segunda
barreira é o segredo: o `whsec_` de teste não vale em produção, e o evento recebe 400 sem efeito.

Testes do script, sem rede externa:

```bash
cd nexo-backend/tools && python -m unittest test_stripe_webhook_sim
```

## 5. Relação com a Orken Platform

**Fonte de verdade hoje: a tabela `nexo.module_subscriptions` do ERP.** A Orken Platform não decide o acesso a módulo do ERP.

Evidências no ERP (`master`):

- Todo gate lê `ModuleSubscription.HasAccessAt` em `module_subscriptions` (`ModuleAccessService.cs`, `RequireModuleFilter.cs`).
- Quem escreve na tabela: o webhook do Stripe (`StripeWebhookService.cs`), o cadastro público (`RegistrationService.cs`) e a
  plataforma interna do ERP, `api/platform` (`PlatformController.cs`), que é outra coisa que não a Orken Platform.
- `grep -i entitlement nexo-backend/src`: só vocabulário do módulo Service (`ServicePresetRegistry.cs`,
  `RequireServiceModuleAttribute.cs`). Nenhuma chamada a `/v1/.../entitlements`.
- `Tenant` não tem referência a uma organização da Platform.
- A única ligação é no sentido contrário e só de leitura: a Platform lê agregados em `GET /api/orken/v1/operations`
  (`OrkenOperationsEndpoints.cs`, `docs/engineering/orken-cockpit-contract.md`), com MRR marcado como estimativa.

Evidências na Platform (`orken-platform`, `2568dfc`):

- `docs/adr/ADR-008-entitlements.md` define Product, Plan, Feature, Subscription e Entitlement, com `ERP` entre os produtos,
  e diz que os produtos consultam os entitlements efetivos. O ERP ainda não faz isso.
- O mesmo ADR: `billing_provider = MANUAL` por enquanto, gateway "virá depois". Nenhum arquivo de `src/` cita Stripe.
- `docs/audit/2026-10-31-inventory.md` lista "módulos + Stripe" do ERP como futuro Product/Plan/Feature/Subscription/Entitlement.
- `docs/architecture/implementation-plan.md` deixa o ERP na etapa 6, sem plano de migração escrito (o `docs/migration/erp.md`
  citado em `repositories.md` ainda não existe).

**O que falta decidir** (§2.11, item 4, da auditoria):

1. Quem é a fonte de verdade do acesso ao ERP daqui em diante: a tabela do ERP ou os entitlements da Platform.
2. Se for a Platform: quem recebe o webhook do Stripe e como `billing_provider` e `external_reference` passam a refletir a
   assinatura.
3. Como um tenant do ERP vira uma organização, e um módulo (`build`, `restaurante`) vira feature ou plano.
4. O que acontece com as linhas existentes de `module_subscriptions` e com as concessões da plataforma interna.

Até essa decisão, este roteiro vale como está: o Stripe alimenta o ERP, e a Platform só lê agregados.
