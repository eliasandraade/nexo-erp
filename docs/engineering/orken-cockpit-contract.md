# Contrato do ERP com o Orken Cockpit (`orken.erp.operations.v1`)

O Orken Cockpit (app.orken.com.br/admin) mostra números do ERP sem falar com o ERP. Quem coleta é a Orken Platform: o
worker dela lê este contrato a cada 5 minutos, valida e guarda o último resultado; o Cockpit lê só a Platform. O
desenho completo do lado da Platform está em `orken-platform/docs/architecture/cockpit.md`, seção "Métricas do ERP".

```
Orken ID ──token de serviço (erp.operations.read, aud orken-erp-api)──▶ worker da Platform
worker da Platform ──GET /api/orken/v1/operations──▶ ERP (este contrato)
Hub /admin ──▶ Platform GET /v1/admin/products/ERP/metrics
```

## Endpoint

`GET /api/orken/v1/operations`

- Só leitura, só agregados, `Cache-Control: no-store`.
- Fora do Swagger (`ExcludeFromDescription`): não é uma rota do produto, é um contrato de serviço.
- Código: `nexo-backend/src/Nexo.Api/Orken/OrkenOperationsEndpoints.cs` (autenticação e rota) e
  `nexo-backend/src/Nexo.Infrastructure/Orken/OrkenOperationsQuery.cs` (consultas).

## Autenticação

Um esquema JwtBearer próprio, `OrkenService`, separado do JWT do ERP. O esquema padrão continua sendo o do ERP, então:

- o token do Orken ID só vale nesta rota (em qualquer outra rota do ERP ele é recusado com 401);
- nenhum token do ERP (tenant, plataforma ou impersonação) vale nesta rota.

| Verificação | Regra | Falha |
|---|---|---|
| Rota ligada | `OrkenId:Issuer` configurado (URL absoluta) | 404: a rota nem existe |
| Assinatura e issuer | chaves e issuer lidos da descoberta OIDC de `OrkenId:Issuer` (HTTPS fora de desenvolvimento) | 401 |
| Audiência | `orken-erp-api` | 401 |
| Tipo | `at+jwt` (só access token do Orken ID) | 401 |
| Validade | `exp`/`nbf` com 30 s de tolerância | 401 |
| Escopo | `erp.operations.read` (texto separado por espaço ou várias claims) | 403 |
| Cliente | `client_id` em `OrkenId:AllowedClients` (padrão: `orken-platform-collector`) | 403 |

Claims sem mapeamento (`MapInboundClaims = false`): `sub`, `client_id` e `scope` chegam com os nomes do token.

**Desvio do desenho inicial:** o pedido era 403 também para audiência errada. Aqui a audiência é conferida junto com o
token, como na Platform API e como pedem a RFC 6750 (token inválido para este recurso é `invalid_token`, 401) e a
RFC 9068. Assim nenhum outro uso futuro do esquema pode esquecer a audiência. Escopo e cliente continuam dando 403.

No Orken ID, só o cliente `orken-platform-collector` (confidencial, client credentials, sem produto) pode pedir
`erp.operations.read`, e o token sai só com a audiência `orken-erp-api`: a Platform API o recusa.

## Configuração

| Variável (ambiente) | Valor | Obrigatória |
|---|---|---|
| `OrkenId__Issuer` | `https://account.orken.com.br/` em produção | sim, para ligar a rota |
| `OrkenId__AllowedClients__0` (e `__1`, ...) | lista de `client_id` aceitos | não; padrão `orken-platform-collector` |

Nenhum segredo fica no ERP: o ERP só valida tokens com as chaves públicas do Orken ID. Para desligar, apague
`OrkenId__Issuer` (a rota volta a 404 no próximo início).

## Campos

Todas as contagens são inteiros não negativos. Chaves de status em UPPER_SNAKE e sempre todas presentes (zero quando
não há), para o formato não mudar conforme os dados.

| Campo | Conteúdo | Fonte |
|---|---|---|
| `contract` | `"orken.erp.operations.v1"` | constante |
| `generatedAt` | instante da consulta (UTC) | relógio do servidor |
| `tenants.total` | número de tenants | `tenants` |
| `tenants.byStatus[]` | `{ key, count }` para `ACTIVE`, `SUSPENDED`, `CANCELLED` | `tenants.status` |
| `tenants.trialsEndingIn7Days` | tenants ativos com `trial_ends_at` entre agora e 7 dias | `tenants.trial_ends_at` |
| `modules[]` | `{ key, activeTenants }`: tenants distintos com acesso ao módulo agora, pela mesma regra dos gates (`ModuleSubscription.HasAccessAt`) | `module_subscriptions` |
| `build.projects` | obras do Orken Build | `bld_projects` |
| `build.projectsByStatus[]` | `PLANNING`, `IN_PROGRESS`, `PAUSED`, `COMPLETED`, `CANCELLED` | `bld_projects.status` |
| `build.publishedPortals` | portais do cliente publicados | `bld_project_portals.is_published` |
| `build.jobsLast24Hours[]` | jobs de processamento de modelo com atividade (atualizados) nas últimas 24 h, pelo estado atual: `PENDING`, `RUNNING`, `DONE`, `FAILED` | `bld_model_processing_jobs` |
| `interpreter.runsLast24Hours`, `interpreter.runsLast7Days` | execuções registradas na telemetria do interpretador | `interpreter_telemetry` |
| `interpreter.failuresLast24Hours` | execuções sem sucesso nas últimas 24 h | `interpreter_telemetry.success` |
| `interpreter.runsByProviderLast7Days[]` | `{ key, count }` por provedor (nome técnico do provedor, ex. `RuleBased`) | `interpreter_telemetry.provider` |
| `interpreter.estimatedCostLast7Days` | `{ kind: "estimate", currency: "USD", micros }`: soma do custo estimado por chamada, em milionésimos de dólar | `interpreter_telemetry.estimated_cost_micros` |
| `commercial.estimatedMrr` | `{ kind: "estimate", currency: "BRL", amount, method, activeSubscriptions, payingSubscriptions }` | `module_subscriptions` × `module_definitions` |

**MRR estimado (`method`).** Soma, por assinatura de módulo com status `Active` e período vigente (fim nulo ou futuro),
do preço de tabela do módulo em valor mensal: mensal; trimestral / 3; semestral / 6; anual / 12. Vitalício, cortesia e
avaliação contam zero. É a mesma conta de `GET /api/platform/mrr` (as duas usam `ModuleDefinition.MonthlyReferencePrice`).
É estimativa a partir da tabela: não é faturamento, nem receita recebida, nem conciliada com o Stripe.

**Custo do interpretador.** Estimativa calculada pelo ERP na hora da chamada (tokens × preço por milhão configurado do
provedor). Não é fatura do provedor.

Quem lê nunca soma estes valores com números da Platform, nem estimativa com fatura.

### Exemplo

```json
{
  "contract": "orken.erp.operations.v1",
  "generatedAt": "2026-11-04T12:00:00+00:00",
  "tenants": {
    "total": 12,
    "byStatus": [ { "key": "ACTIVE", "count": 10 }, { "key": "SUSPENDED", "count": 1 }, { "key": "CANCELLED", "count": 1 } ],
    "trialsEndingIn7Days": 2
  },
  "modules": [ { "key": "build", "activeTenants": 3 }, { "key": "varejo", "activeTenants": 8 } ],
  "build": {
    "projects": 7,
    "projectsByStatus": [ { "key": "PLANNING", "count": 1 }, { "key": "IN_PROGRESS", "count": 5 }, { "key": "PAUSED", "count": 0 }, { "key": "COMPLETED", "count": 1 }, { "key": "CANCELLED", "count": 0 } ],
    "publishedPortals": 2,
    "jobsLast24Hours": [ { "key": "PENDING", "count": 0 }, { "key": "RUNNING", "count": 0 }, { "key": "DONE", "count": 4 }, { "key": "FAILED", "count": 1 } ]
  },
  "interpreter": {
    "runsLast24Hours": 30,
    "runsLast7Days": 200,
    "failuresLast24Hours": 1,
    "runsByProviderLast7Days": [ { "key": "RuleBased", "count": 150 }, { "key": "Anthropic", "count": 50 } ],
    "estimatedCostLast7Days": { "kind": "estimate", "currency": "USD", "micros": 1234567 }
  },
  "commercial": {
    "estimatedMrr": {
      "kind": "estimate", "currency": "BRL", "amount": 4321.50,
      "method": "Soma, por assinatura de módulo com status Active e período vigente ...",
      "activeSubscriptions": 20, "payingSubscriptions": 15
    }
  }
}
```

## Privacidade

- Só `COUNT`, `SUM` e `GROUP BY`. Nenhuma linha sai: nada de nome, razão social, nome fantasia, identificador de tenant
  ou de usuário, e-mail, telefone, CNPJ/CPF, conteúdo de documento ou mensagem de erro.
- Chaves livres (módulo, provedor) passam por `OrkenOperationsQuery.Key`: só letras, dígitos, `_`, `.` e `-`, até 60
  caracteres.
- A Platform lê a resposta em tipos estritos e guarda só os campos deste contrato: um campo a mais, mesmo que alguém o
  adicione aqui por engano, não chega ao banco da Platform nem ao Cockpit.
- Agregados não são separados por tenant. Uma contagem pequena (por exemplo, um único tenant com o módulo Build) pode
  permitir inferência por quem já conhece a base; por isso o Cockpit só é aberto a PLATFORM_ADMIN com segundo fator.

## Versão

- O campo `contract` diz a versão. Campos novos são aditivos e os consumidores ignoram o que não conhecem.
- Mudança que quebre (renomear, remover, mudar unidade ou significado) é um contrato novo: rota `/api/orken/v2/...` em
  paralelo, com `contract` `orken.erp.operations.v2`, até a Platform migrar.

## Testes

- `nexo-backend/tests/Nexo.IntegrationTests/Security/OrkenOperationsContractTests.cs`: rota inexistente sem
  `OrkenId:Issuer` (404); sem token, assinatura de outra chave, outro issuer, tipo `JWT`, expirado, audiência de outro
  recurso ou token do ERP (401); escopo errado ou cliente fora da lista (403); o token do Orken ID não abre outras rotas
  do ERP; resposta válida só com agregados (nenhum e-mail, identificador, CNPJ, nome de tenant ou campo de nome).
  Os tokens são assinados com uma chave local, aceita só pelo host de teste.
- `nexo-backend/tests/Nexo.UnitTests/Billing/MonthlyReferencePriceTests.cs`: valor mensal de cada plano e chaves.
