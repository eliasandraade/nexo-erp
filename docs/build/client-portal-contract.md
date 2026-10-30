# Orken Build — Portal do Cliente — contrato da API pública (v1.6)

Documento para o time do frontend externo (Astra). Descreve **somente** a API pública do portal do cliente de
uma obra. Tudo aqui é estável na versão `contract v1.6` (compatível com a v1–v1.5, exceto os ajustes de segurança da v1.5
listados no §9 "Changelog"); mudanças incompatíveis viram `v2` (ver "Versionamento").

## 1. Base, endereço e slugs

| Item | Valor |
|---|---|
| Base da API | `{API_BASE}` — a mesma API do Orken (produção informada pela Orken). Ex.: `https://api.exemplo/` |
| Prefixo | `{API_BASE}/api/public/build/{companySlug}/{projectSlug}` |
| URL pública sugerida no site Astra | `/{companySlug}/{projectSlug}` (ex.: `/construtora-silva/residencia-alfa`) |
| Autenticação | Nenhuma conta. Portais com senha usam uma **sessão de portal** (ver §3). **Não** envie `Authorization`. |
| Formato | JSON UTF-8, `camelCase`, datas `yyyy-MM-dd`, instantes ISO-8601 com fuso (`2026-10-30T14:03:11.123456+00:00`), enums como texto. |
| CORS | A origem do site Astra precisa estar em `Cors:AllowedOrigins` da API (configuração da Orken). O header `X-Build-Portal-Session` (e `If-None-Match`) é aceito; `Retry-After` e `ETag` são expostos (v1.6 — antes o navegador de outra origem não conseguia ler o `ETag`). `https://www.orken.com.br` e `https://app.orken.com.br` já estão na lista fixa. |

**Regras de slug** (empresa e obra): 3–60 caracteres; só `a-z`, `0-9` e `-`; sem `-` no começo/fim nem `--`;
sempre minúsculo (a API normaliza para minúsculas). O slug da empresa é único na plataforma; o da obra é único
dentro da empresa.

**Slugs reservados** (recusados para empresa e obra — são rotas do app Orken ou nomes de infraestrutura):

```
acesso-encerrado agendar assinatura auditoria build caixa check-email clientes compras configuracoes
dashboard esqueci-senha estoque financeiro fiscal forgot-password fornecedores impersonate
listas-de-preco login menu pdv perfil platform produtos rastrear redefinir-senha register relatorios
restaurante service tributos usuarios vendas verify-email workspaces
api app admin assets static www orken portal obra obras health public logout reset-password
```

Se o site Astra tiver rotas próprias no primeiro segmento (ex.: `/sobre`), avise a Orken para reservá-las também.

## 2. Rotas

| Método | Rota (após o prefixo) | Rate limit | Sessão (portal com senha) |
|---|---|---|---|
| GET | `` (raiz) — bootstrap | `build-portal` | obrigatória |
| POST | `/session` — abrir sessão | `build-portal-auth` | — |
| DELETE | `/session` — encerrar sessão | `build-portal` | header opcional |
| GET | `/timeline?before=&take=` | `build-portal` | obrigatória |
| GET | `/photos?skip=&take=` | `build-portal` | obrigatória |
| GET | `/progress` | `build-portal` | obrigatória |
| GET | `/change-orders` | `build-portal` | obrigatória |
| POST | `/change-orders/{id}/decision` | `build-portal-decision` | **exige portal com senha** |
| GET | `/selections` | `build-portal` | obrigatória |
| POST | `/selections/{id}/choice` | `build-portal-decision` | **exige portal com senha** |
| GET | `/measurements` | `build-portal` | obrigatória |
| GET | `/handover` (v1.2) | `build-portal` | obrigatória |
| POST | `/handover/accept` (v1.2) | `build-portal-decision` | **exige portal com senha** |
| GET | `/warranty` (v1.2) | `build-portal` | obrigatória |
| GET | `/assistance` (v1.2) | `build-portal` | obrigatória |
| POST | `/assistance` (v1.2) | `build-portal-decision` | **exige portal com senha** |
| POST | `/assistance/{id}/photos` (v1.2, multipart) | `build-portal-decision` | **exige portal com senha** |
| POST | `/assistance/{id}/feedback` (v1.2) | `build-portal-decision` | **exige portal com senha** |
| GET | `/twin` (v1.3) | `build-portal` | obrigatória |
| GET | `/twin/state?date=` (v1.3) | `build-portal` | obrigatória |
| GET | `/reality-captures?skip=&take=` (v1.3) | `build-portal` | obrigatória |
| GET | `/documents` (v1.4) | `build-portal` | obrigatória |

Em portal **público** a sessão é ignorada — e todas as rotas de escrita (decisão de aditivo, escolha, aceite da entrega,
abrir chamado, enviar foto, avaliar) respondem `403 portal_decision_requires_password` (agir em nome do cliente exige o
portal com senha). **v1.5:** em portal público as seções com dados pessoais/do imóvel (`postConstruction`, `changeOrders`,
`selections`, `documents`, `realityCapture`) **nunca** são servidas, mesmo com a flag ligada: somem de `sections`/`property` e
as leituras respondem `404 portal_section_disabled` (ver §4).

Ordem das verificações (v1.5): portal existe e está publicado → **sessão** (portal com senha) → portal com senha (escritas)
→ seção ligada. Sem sessão num portal com senha a resposta é sempre `401`, mesmo para uma seção desligada: nada da
configuração do portal é revelado antes da senha.

Corpos JSON das rotas `POST` (sessão, decisão, escolha, aceite, abrir chamado, avaliação) têm no máximo **64 KB** →
`413 payload_too_large`.

### 2.1 GET bootstrap

`GET {API_BASE}/api/public/build/construtora-silva/residencia-alfa`

`200` — só aparecem as seções habilitadas pela construtora (uma seção desligada **não existe** no JSON, nem como
`null`). `sections` lista os nomes das seções ligadas.

```json
{
  "contractVersion": "1.2",
  "phase": "construction",
  "sections": ["overallProgress", "stageProgress", "schedule", "photos", "weather", "clientUpdates", "contact",
               "changeOrders", "selections"],
  "portal": {
    "title": "Acompanhe sua obra",
    "welcomeMessage": "Bem-vindo!",
    "primaryColor": "#0a3d62",
    "accentColor": null,
    "logoUrl": "https://cdn.exemplo/tenants/…/build/portal/logo/…png",
    "coverUrl": null,
    "access": "Public"
  },
  "company": { "name": "Construtora Silva", "logoUrl": null },
  "project": {
    "name": "Residência Alfa", "type": "House", "status": "InProgress",
    "startDate": "2026-01-05", "expectedEndDate": "2026-12-20"
  },
  "progress": {
    "asOf": "2026-10-30",
    "actualPercent": 40.0,
    "plannedPercent": 55.0,
    "forecastDelivery": "2027-01-15",
    "methodology": "weighted-stages/1"
  },
  "stages": [
    { "name": "Fundação", "order": 1, "status": "Completed", "actualPercent": 100.0 },
    { "name": "Alvenaria", "order": 2, "status": "InProgress", "actualPercent": 35.0 }
  ],
  "schedule": {
    "startDate": "2026-01-05",
    "expectedEndDate": "2026-12-20",
    "stages": [
      { "name": "Fundação", "order": 1, "plannedStart": "2026-01-05", "plannedEnd": "2026-03-31" },
      { "name": "Alvenaria", "order": 2, "plannedStart": "2026-04-01", "plannedEnd": null }
    ]
  },
  "weather": {
    "date": "2026-10-30", "temperatureMax": 28.4, "temperatureMin": 19.1, "precipitationMm": 2.0,
    "description": "Parcialmente nublado", "source": "Open-Meteo",
    "notice": "Previsão do tempo para o local da obra — informativa, pode mudar ao longo do dia."
  },
  "financialSummary": { "approvedBudget": 450000.00, "realizedTotal": 182340.55, "currency": "BRL", "asOf": "2026-10-30" },
  "contact": { "name": "Eng. Ana", "phone": "11 4000-0000", "email": "contato@construtora.exemplo", "whatsApp": "11999990000" },
  "latestUpdates": [
    { "kind": "Milestone", "title": "Fundação concluída", "body": "As sapatas foram concretadas.", "publishedAt": "2026-10-28T17:02:11.53+00:00" }
  ]
}
```

Observações:
- `portal`, `company`, `project`, `contractVersion`, `sections` e `phase` (v1.2, §2.12) sempre vêm.
- `progress.plannedPercent` some quando nenhuma etapa tem datas previstas; `forecastDelivery` só vem com a flag
  `forecastDelivery`.
- `weather` só vem se a flag estiver ligada **e** a obra tiver coordenadas **e** o clima estiver ativo na
  plataforma **e** o provedor responder. É previsão meteorológica: mostre como informativa, nunca como certeza.
- `stages[].status`: `Pending | InProgress | Completed | Delayed | Blocked`. `project.status`:
  `Planning | InProgress | Paused | Completed | Cancelled`. `project.type`: `House | Commercial | Renovation | Building | Other`.
- `latestUpdates`: até 5 atualizações publicadas, mais recentes primeiro.

### 2.2 POST /session (portal com senha)

```http
POST /api/public/build/construtora-silva/residencia-alfa/session
Content-Type: application/json

{ "password": "a senha que a construtora passou ao cliente" }
```

`200`

```json
{ "token": "q3V0bWx5LXJhbmRvbS0zMi1ieXRlcy1iYXNlNjR1cmw", "expiresAt": "2026-10-31T02:10:00+00:00" }
```

- O `token` (base64url, 43 caracteres) aparece **só nesta resposta**. Guarde em memória/sessionStorage e envie em
  toda chamada seguinte no header `X-Build-Portal-Session: {token}`.
- Validade: `Build:Portal:SessionHours` (padrão **12 h**, fixa — não renova com uso).
- Erros: `401 portal_password_invalid` (senha errada), `429 portal_locked` (bloqueio, ver §3), `429 portal_busy` (muitas
  verificações de senha ao mesmo tempo no servidor — tente em `retryAfterSeconds`), `400 portal_password_not_required`
  (portal público), `404 portal_not_found`, `413 payload_too_large`.

### 2.3 DELETE /session

Encerra a sessão do header `X-Build-Portal-Session`. Sempre `204` (idempotente), exceto `404` se o portal não existe.

### 2.4 GET /timeline

`GET …/timeline?take=20&before=2026-10-20T12:00:00%2B00:00`

- `take`: 1–50 (padrão 20). `before`: instante ISO-8601 (exclusivo); omita na primeira página.
- Paginação por cursor: use `nextBefore` da resposta como `before` da próxima; `nextBefore: null` = fim.
- Ordem: `occurredAt` decrescente. Itens com o mesmo instante exato na borda de página podem ser pulados (raro).

```json
{
  "items": [
    { "type": "photo", "occurredAt": "2026-10-29T18:22:05.1+00:00", "date": "2026-10-29",
      "caption": "Laje pronta", "photoUrl": "https://…assinada…" },
    { "type": "stage_completed", "occurredAt": "2026-10-29T18:20:00+00:00", "date": "2026-10-29",
      "stageName": "Fundação", "percent": 100, "previousPercent": 80 },
    { "type": "stage_progress", "occurredAt": "2026-10-22T13:00:00+00:00", "date": "2026-10-22",
      "stageName": "Alvenaria", "percent": 35, "previousPercent": 20 },
    { "type": "daily_log", "occurredAt": "2026-10-21T15:00:00+00:00", "date": "2026-10-21",
      "text": "Concretagem da laje concluída.", "photoCount": 3 },
    { "type": "update", "occurredAt": "2026-10-20T10:00:00+00:00", "kind": "WeeklyDigest",
      "title": "Resumo da semana", "text": "…" }
  ],
  "nextBefore": "2026-10-20T10:00:00+00:00"
}
```

| `type` | Campos | Aparece se a flag… |
|---|---|---|
| `update` | `kind` (`Update`/`Milestone`/`WeeklyDigest`), `title`, `text` | `clientUpdates` |
| `daily_log` | `date`, `text` (resumo escrito para o cliente; **nunca** a nota interna), `photoCount` (só com `photos`) | `dailyLogs` (e o registro marcado como visível) |
| `stage_progress` / `stage_completed` | `date`, `stageName`, `percent`, `previousPercent` | `stageProgress` |
| `photo` | `date`, `caption`, `photoUrl` (assinada, pode ser `null` se o storage estiver indisponível) | `photos` (foto **e** registro do dia visíveis) |

Campos ausentes num item simplesmente não existem no JSON.

### 2.5 GET /photos

`GET …/photos?skip=0&take=24` — `take` 1–50 (padrão 24). Só fotos marcadas para o cliente de registros também
visíveis. `404 portal_section_disabled` se a flag `photos` estiver desligada.

```json
{
  "items": [
    { "url": "https://…assinada…", "caption": "Laje pronta", "date": "2026-10-29", "urlExpiresAt": "2026-10-30T14:18:00+00:00" }
  ],
  "total": 37, "skip": 0, "take": 24
}
```

### 2.6 GET /progress

Curvas de avanço (pontos semanais). `404 portal_section_disabled` se `overallProgress` estiver desligada.

```json
{
  "asOf": "2026-10-30",
  "actualPercent": 40.0,
  "plannedPercent": 55.0,
  "forecastDelivery": "2027-01-15",
  "methodology": "weighted-stages/1",
  "curves": {
    "actual":   [ { "date": "2026-01-05", "percent": 0.0 }, { "date": "2026-10-30", "percent": 40.0 } ],
    "planned":  [ { "date": "2026-01-05", "percent": 0.5 }, { "date": "2026-12-20", "percent": 100.0 } ],
    "forecast": [ { "date": "2026-10-30", "percent": 40.0 }, { "date": "2027-01-15", "percent": 100.0 } ]
  }
}
```

- `actual`: retratos diários gravados pela Orken (um por dia); o último ponto é sempre hoje (valor ao vivo).
- `planned`: plano de referência (linha de base congelada ou, sem ela, datas atuais das etapas).
- `forecast` e `forecastDelivery`: só com a flag `forecastDelivery`.

### 2.7 GET /change-orders (v1.1)

Aditivos do contrato que a construtora enviou ao cliente (pendentes) e os já decididos — rascunhos e cancelados nunca
aparecem. **Só em portal com senha** (v1.5). `404 portal_section_disabled` se a flag `changeOrders` estiver desligada ou o
portal for público. Mais recentes primeiro. O exemplo abaixo é de um portal com `financialSummary` ligado.

```json
{
  "items": [
    {
      "id": "6c1f7c2e-0d4b-4c55-9a51-8a3b8f0e2d11",
      "number": 2,
      "title": "Fundação mais profunda",
      "description": "Solo mais fraco que o previsto.",
      "reason": "SiteCondition",
      "costImpact": 5000.00,
      "scheduleImpactDays": 10,
      "stageName": "Fundação",
      "status": "Pending",
      "canDecide": true,
      "sentAt": "2026-10-30T13:00:00+00:00",
      "items": [ { "name": "Concreto extra", "kind": "Material", "quantity": 5.000, "unit": "m³", "unitPrice": 1000.00, "total": 5000.00 } ]
    },
    {
      "id": "0b5e…", "number": 1, "title": "Troca de piso", "reason": "ClientRequest", "costImpact": -300.00,
      "scheduleImpactDays": 0, "status": "Approved", "canDecide": false,
      "decidedAt": "2026-10-20T18:30:00+00:00", "decisionChannel": "Portal",
      "clientComment": "De acordo.", "items": []
    }
  ]
}
```

- `id` é um identificador **opaco** do aditivo para o portal (não é o id interno nem o número). Use-o na decisão.
- `status`: `Pending` (aguardando o cliente) | `Approved` | `Rejected`. `canDecide` só é `true` para `Pending` em portal
  com senha.
- `reason`: `ClientRequest | DesignChange | SiteCondition | Error | Other`. `costImpact` pode ser negativo (crédito);
  `scheduleImpactDays` pode ser 0 ou negativo. `decisionChannel`: `Portal` (o cliente) | `Internal` (registrado pela construtora).
- **v1.5:** valores (`costImpact`, `items[].unitPrice`, `items[].total`) só aparecem com `financialSummary` ligado; sem
  ele esses campos **não vêm** (nem como `null`) — mostre o aditivo pelo título, descrição, itens/quantidades e prazo.
- **v1.5:** `signerName` não é mais devolvido (o cliente já sabe quem decidiu); `decidedAt`, `status` e `decisionChannel`
  continuam.

### 2.8 POST /change-orders/{id}/decision (v1.1)

```http
POST …/change-orders/6c1f7c2e-0d4b-4c55-9a51-8a3b8f0e2d11/decision
X-Build-Portal-Session: {token}
Content-Type: application/json

{ "decision": "approve", "comment": "De acordo.", "signerName": "Maria Souza" }
```

- `decision`: `approve` | `reject` (obrigatório). `signerName`: nome de quem decide (obrigatório, até 200). `comment`:
  opcional (até 2000).
- `200` → o aditivo atualizado (mesmo formato do item de §2.7). Ao aprovar, a construtora vê o valor do contrato e o
  prazo da etapa atualizados.
- Erros: `403 portal_decision_requires_password` (portal público), `401` (sessão, como nas demais rotas),
  `404 portal_item_not_found` (id inexistente, de outra obra, ou aditivo não enviado), `409 change_order_already_decided`
  (já aprovado/recusado — a decisão é única; não repita), `400 invalid_decision`, `400 signer_name_required`.
- Mostre ao cliente um resumo (título, valor, prazo) e peça confirmação explícita antes de enviar.

### 2.9 GET /selections (v1.1)

Escolhas que o cliente precisa fazer (ex.: "Piso da sala"). `404 portal_section_disabled` se a flag `selections`
estiver desligada. Só aparecem as marcadas como visíveis ao cliente e não canceladas; prazo mais próximo primeiro.

```json
{
  "items": [
    {
      "id": "a4f0…",
      "title": "Piso da sala",
      "description": "Escolha até o fim do mês.",
      "stageName": "Acabamento",
      "deadline": "2026-11-10",
      "deadlinePassed": false,
      "status": "Open",
      "canChoose": true,
      "options": [
        { "id": "11aa…", "name": "Cerâmica padrão", "priceDelta": 0.00, "isStandard": true },
        { "id": "22bb…", "name": "Porcelanato 90x90", "priceDelta": 2500.00, "isStandard": false,
          "imageUrl": "https://…assinada…", "leadTimeDays": 15 }
      ],
      "imageUrlsExpireAt": "2026-10-30T14:18:00+00:00"
    }
  ]
}
```

- `status`: `Open` (aguardando o cliente) | `Submitted` (cliente escolheu; aguardando a construtora) | `Confirmed`.
  `canChoose`: `Open`, dentro do prazo, portal com senha. Depois da escolha vêm `chosenOptionId`, `clientComment`, `decidedAt`.
- `priceDelta`: diferença em relação à opção padrão (pode ser negativa). `leadTimeDays`: prazo de entrega da opção.
- `imageUrl`: URL **assinada** (~15 min, `imageUrlsExpireAt`), pode ser `null`/ausente. Recarregue a lista quando expirar.
- `deadline` é inclusivo (o cliente pode escolher até o fim desse dia).

### 2.10 POST /selections/{id}/choice (v1.1)

```http
POST …/selections/a4f0…/choice
X-Build-Portal-Session: {token}

{ "optionId": "22bb…", "comment": "Quero o porcelanato." }
```

- `200` → a escolha atualizada (`status: "Submitted"`). A construtora confirma depois; se a opção muda preço ou prazo,
  ela prepara um aditivo, que chega ao cliente em `/change-orders`.
- Erros: `403 portal_decision_requires_password`, `401` (sessão), `404 portal_item_not_found`, `400 invalid_option`
  (opção que não é desta escolha), `409 selection_closed` (já escolhida/encerrada), `422 selection_deadline_passed`.

### 2.11 GET /measurements (v1.1)

Medições **aprovadas** da obra (avanço medido por etapa no período). `404 portal_section_disabled` se a flag
`measurements` estiver desligada (padrão). Mais recentes primeiro.

```json
{
  "items": [
    {
      "number": 3, "periodStart": "2026-10-01", "periodEnd": "2026-10-31",
      "approvedAt": "2026-11-02T12:00:00+00:00", "totalAmount": 18000.00,
      "items": [ { "stageName": "Fundação", "previousPercent": 60, "measuredPercent": 100, "amount": 18000.00 } ]
    }
  ]
}
```

- `measuredPercent` é acumulado (o % da etapa depois da medição); `previousPercent` o de antes.
- `totalAmount` e `amount` **só** aparecem quando a flag `financialSummary` também está ligada.

### 2.12 Fase e "Meu Imóvel" no bootstrap (v1.2)

O bootstrap ganha:

```json
{
  "contractVersion": "1.2",
  "phase": "delivered",
  "property": {
    "handoverDate": "2026-10-30",
    "handoverAccepted": true,
    "warrantyTerms": [
      { "system": "Estrutura", "months": 60, "startsAt": "2026-10-30", "expiresAt": "2031-10-30", "isActive": true,
        "description": "Solidez e segurança da estrutura (referência: Código Civil, art. 618)." }
    ],
    "openAssistanceCount": 1
  }
}
```

- `phase` sempre vem: `construction` até a construtora registrar a entrega, depois `delivered`. Use para trocar
  "Minha Obra" por "Meu Imóvel".
- `property` só vem com `phase = delivered` **e** a flag `postConstruction` ligada (nome `postConstruction` em `sections`).
- `expiresAt` é o **último dia coberto** (inclusive): o dia de mesmo número do início, `months` depois, ou o dia seguinte quando o mês não tem esse dia (Código Civil, art. 132 §3). `isActive` = hoje dentro do prazo.

### 2.13 GET /handover (v1.2)

`200` (flag `postConstruction`; `404 portal_item_not_found` enquanto a entrega não foi agendada):

```json
{
  "status": "Delivered",
  "scheduledFor": "2026-10-30",
  "deliveredAt": "2026-10-30T15:00:00+00:00",
  "keysDelivered": true,
  "documents": [ { "name": "Manual do proprietário", "url": "https://…assinada…" } ],
  "documentUrlsExpireAt": "2026-10-30T15:15:00+00:00",
  "walkthrough": {
    "status": "PassedWithRemarks", "scheduledFor": "2026-10-28", "completedAt": "2026-10-28T13:00:00+00:00",
    "totalItems": 2, "okItems": 1, "pendingItems": 1,
    "items": [ { "text": "Portas e janelas", "result": "Ok" }, { "text": "Pintura da sala", "result": "NotOk" } ]
  },
  "acceptedAt": "2026-10-30T16:00:00+00:00",
  "acceptedByName": "Maria Souza",
  "acceptanceChannel": "Portal",
  "acceptanceComment": "Recebido em ordem.",
  "canAccept": false
}
```

- `status`: `Scheduled | InspectionDone | Delivered`. `walkthrough.status`: `Planned | InProgress | Passed |
  PassedWithRemarks | Failed`. `result`: `Pending | Ok | NotOk | NotApplicable`; `pendingItems` = itens `NotOk`
  (pendências que a construtora vai tratar).
- Nunca saem: observações internas da entrega, comentários/fotos/vistoriador da vistoria, chaves de arquivo.
- `documents[].url` é assinada (~15 min) ou `null` (documento sem arquivo).

### 2.14 POST /handover/accept (v1.2)

```http
POST …/handover/accept
X-Build-Portal-Session: {token}

{ "signerName": "Maria Souza", "comment": "Recebido em ordem." }
```

- **Exige portal com senha** (`403 portal_decision_requires_password` em portal público) e sessão válida.
- Só depois da vistoria de entrega ou da entrega (`422 handover_not_ready`); aceite único (`409 handover_already_accepted`).
- `signerName` obrigatório, até 200 (`400 signer_name_required`); `comment` até 2000. Nenhum IP é guardado.
- `200` → o mesmo formato de §2.13.

### 2.15 GET /warranty (v1.2)

`200` → `{ "items": [ …mesmo formato de property.warrantyTerms… ], "notice": "Prazos de referência informados pela
construtora. Consulte o termo de garantia do seu contrato." }`. Só garantias que já começaram (na entrega). Mostre o
`notice`: os prazos são os cadastrados pela construtora, não orientação jurídica.

### 2.16 GET /assistance e POST /assistance (v1.2)

`GET` → `{ "items": [ … ], "categories": ["Esquadrias", …, "Outros"], "canOpen": true }` — os chamados da obra (abertos
pelo cliente ou pela construtora), mais recentes primeiro. `canOpen` = portal com senha **e** imóvel entregue.

```json
{
  "id": "b8f9…",
  "number": 3,
  "category": "Impermeabilização",
  "title": "Infiltração no banheiro",
  "description": "Mancha de umidade no teto.",
  "location": "Banheiro social",
  "openedVia": "Portal",
  "openedAt": "2026-11-02T12:00:00+00:00",
  "status": "Scheduled",
  "warrantyCovered": true,
  "scheduledFor": "2026-11-05T13:00:00+00:00",
  "photoUrls": ["https://…assinada…"],
  "photoUrlsExpireAt": "2026-11-02T12:15:00+00:00",
  "canAddPhotos": true,
  "canGiveFeedback": false,
  "timeline": [ { "status": "Open", "at": "…" }, { "status": "Triaged", "at": "…" } ]
}
```

- `status`: `Open | Triaged | Scheduled | InProgress | Resolved | Closed | Rejected`. `warrantyCovered` aparece depois
  da triagem (`true`/`false`; ausente se a construtora ainda não decidiu). `resolutionNotes` e `rejectionReason`
  aparecem quando existem; `rating`/`feedback` depois da avaliação.
- Nunca saem: notas internas da triagem, telefone de contato, ids internos.

`POST /assistance` (**portal com senha**, sessão; rate limit `build-portal-decision`):

```json
{ "category": "Impermeabilização", "title": "Infiltração no banheiro", "description": "Mancha de umidade no teto.",
  "location": "Banheiro social", "contactPhone": "11999990000", "openedByName": "Maria Souza" }
```

- `category`: uma de `categories` (sem diferença de maiúsculas) → senão `400 invalid_category`. `title` (até 200) e
  `description` (até 4000) obrigatórios; `location` até 200, `contactPhone` até 30, `openedByName` até 200 →
  senão `400 invalid_assistance_request`.
- Só depois da entrega (`422 property_not_delivered`); no máximo **20 chamados abertos pelo portal** por obra
  (`422 assistance_limit_reached`).
- `201` → o chamado (formato acima).

### 2.17 POST /assistance/{id}/photos (v1.2)

`multipart/form-data` com o campo `file`. **Portal com senha** e sessão. Portal, sessão e chamado são conferidos **antes** de
o corpo ser lido (v1.5): sem sessão → `401`, chamado de outra obra → `404`, sem que o servidor receba o arquivo.

- Tipos: `image/jpeg`, `image/png`, `image/webp` (`400 invalid_file_type`); até **8 MB** (`413 file_too_large`);
  o conteúdo precisa começar com a assinatura do tipo (`400 invalid_file_content` — ex.: um `.txt` renomeado `.png`).
- Até **6 fotos** por chamado (`422 assistance_photo_limit`); chamado resolvido/encerrado/recusado
  (`422 assistance_closed`).
- O nome do arquivo enviado é ignorado; a foto é guardada **privada** e só volta como URL assinada.
- Armazenamento desligado/indisponível → `503 storage_unavailable` (tente mais tarde).
- `{id}` precisa ser um chamado **desta obra** → senão `404 portal_item_not_found`.
- `200` → o chamado atualizado.

### 2.18 POST /assistance/{id}/feedback (v1.2)

`{ "rating": 5, "comment": "Atendimento rápido." }` — **portal com senha**. `rating` 1–5 (`400 invalid_rating`);
só com o chamado `Resolved` (`422 assistance_not_resolved`); uma vez (`409 feedback_already_given`). `200` → o chamado.

### 2.19 GET /twin (v1.3) — gêmeo digital

> Disponível quando a obra liga `model3d` **e** o 3D está habilitado para a construtora na plataforma. Se a plataforma
> desligar o 3D, `model3d` some de `sections` e `/twin` responde 404 `section_disabled` — o site deve esconder a seção.

Flag `model3d` (padrão **desligado**; desligada → `404 portal_section_disabled`). Somente leitura.

```json
{
  "geometrySource": "NativeBim",
  "modelName": "Arquitetura casa",
  "discipline": "Arquitetura",
  "viewerUrl": "https://…/viewer.glb?X-Amz-…",
  "viewerExpiresAt": "2026-10-30T14:18:11+00:00",
  "zones": [
    { "id": "2O2Fr$t4X7Zf8NOew3FLOH", "kind": "Storey", "name": "Térreo", "parentId": "…", "elevation": 0, "order": 2,
      "externalId": "2O2Fr$t4X7Zf8NOew3FLOH" },
    { "id": "z-8c1f0a2b3d4e5f60", "kind": "Area", "name": "Garagem", "order": 0,
      "simplifiedBox": { "width": 3, "depth": 5, "height": 2.6, "elevation": 0 } }
  ],
  "state": {
    "date": "2026-10-30",
    "counts": { "NotStarted": 0, "InProgress": 0, "Completed": 2, "Delayed": 0, "Blocked": 0, "NeedsReview": 1 },
    "states": { "Completed": ["3hX…", "1kQ…"], "NeedsReview": ["0aB…"] },
    "zoneStates": { "Completed": ["2O2Fr$t4X7Zf8NOew3FLOH"] },
    "unbound": 4
  },
  "minDate": "2026-01-05",
  "maxDate": "2026-10-30",
  "disclaimer": "Modelo BIM do projeto. As cores mostram o andamento registrado pela construtora; não substituem a vistoria.",
  "elementIndex": {
    "stages": [ { "name": "Alvenaria", "order": 2, "elementIds": ["0aB…", "1kQ…", "3hX…"] } ],
    "categories": [ { "name": "IfcSlab", "elementIds": ["5tR…"] }, { "name": "IfcWall", "elementIds": ["0aB…", "1kQ…", "3hX…"] } ],
    "zones": { "2O2Fr$t4X7Zf8NOew3FLOH": ["0aB…", "1kQ…", "3hX…", "5tR…"] }
  }
}
```

- `geometrySource`: `NativeBim` (IFC com geometria) · `Converted3D` (GLB/glTF enviado pela construtora) ·
  `AssistedMapping` (planta 2D: só zonas, sem 3D) · `SimplifiedRepresentation` (sem modelo: caixas digitadas pela
  construtora) · `null` (nada para mostrar ainda). **Mostre sempre `disclaimer`** — em `SimplifiedRepresentation` ele diz
  "Representação simplificada; não corresponde ao projeto arquitetônico".
- `viewerUrl` (pode faltar): URL **assinada** (~15 min, `viewerExpiresAt`) do arquivo de visualização (GLB). Nunca é o
  arquivo original do projeto (IFC, RVT, DWG, PDF…), que não sai por este portal. Expirou → chame `/twin` de novo.
- `zones`: estrutura (terreno, edifício, pavimentos, ambientes) e zonas criadas pela construtora. `simplifiedBox` só
  existe quando a construtora digitou as medidas (metros) — nunca é gerada.
- `state`: o mesmo formato de `/twin/state` para hoje. `states`: estado → ids externos dos elementos (só estados que
  ocorrem). `unbound`: elementos sem etapa vinculada — **não pinte como andamento** (deixe neutros).
- Estados: `NotStarted`, `InProgress`, `Completed`, `Delayed` (etapa passou do fim previsto sem terminar), `Blocked`,
  `NeedsReview` (marcado pela construtora para revisão).
- `elementIndex` (v1.6; ausente sem modelo): o que cada elemento **é**, sempre dado registrado — nunca deduzido do nome do
  nó. `stages`: etapa de cada elemento segundo os vínculos que a construtora fez (por elemento, categoria ou zona), em
  ordem de etapa; vem **vazio** quando o portal não mostra etapas (`stageProgress` e `schedule` desligados). `categories`:
  a classe IFC de cada elemento (`IfcWall`, `IfcSlab`…; num GLB enviado direto, a categoria gravada no arquivo ou `Mesh`). `zones`: id público da zona
  (o mesmo de `zones[].id`) → elementos que estão **diretamente** nela (não inclui subzonas). Elementos sem etapa não
  aparecem em `stages` (são os `unbound`). Os ids são os nomes dos nós do GLB.

### 2.20 GET /twin/state?date=yyyy-MM-dd (v1.3) — linha do tempo

Mesma flag. `date` entre `minDate` (início da obra) e hoje (padrão hoje); fora disso → `400 invalid_date`. Resposta = o
objeto `state` acima para aquela data. Vem com `ETag` e `Cache-Control: private, no-cache` (não tem URL assinada):
reenvie `If-None-Match` com o ETag e trate `304` reaproveitando o que já tem.

### 2.21 GET /reality-captures (v1.3)

Flag `realityCapture` (padrão **desligado**). Capturas que a construtora marcou como visíveis, mais recentes primeiro:

```json
{ "items": [ { "type": "Video", "capturedAt": "2026-10-20T13:00:00+00:00", "title": "Voo semanal", "stageName": "Alvenaria",
               "zoneName": "Térreo", "contentType": "video/mp4", "url": "https://…", "urlExpiresAt": "…" } ],
  "total": 1, "skip": 0, "take": 24 }
```

`type`: `Photo`, `Photo360` (equirretangular — use um visualizador 360°), `Video`, `Drone`, `PointCloud` (`.las`/`.e57`,
sem visualização no portal por enquanto — ofereça só o download). `take` até 50. Notas internas nunca saem.

### 2.22 Como desenhar o gêmeo digital (orientação para o Astra)

1. `GET /twin`. Com `viewerUrl`: carregue com o `GLTFLoader` do three.js (`loader.load(viewerUrl, gltf => scene.add(gltf.scene))`).
   **O nome de cada nó (`object.name`) é o id externo do elemento** (o GlobalId do IFC) — o mesmo que aparece em `states`.
2. Monte um mapa `id → estado` a partir de `state.states` e percorra a cena:
   ```js
   const stateOf = new Map(Object.entries(state.states).flatMap(([s, ids]) => ids.map(id => [id, s])));
   gltf.scene.traverse(o => {
     if (!o.isMesh && !o.children?.length) return;
     const s = stateOf.get(o.name) ?? stateOf.get(o.parent?.name);
     o.traverse(m => { if (m.isMesh) m.material = materials[s ?? 'unbound']; });
   });
   ```
   Cores sugeridas: `Completed` verde, `InProgress` azul, `Delayed` laranja, `Blocked` vermelho, `NeedsReview` roxo,
   `NotStarted` cinza claro, sem etapa (`unbound`) cinza translúcido. Clone os materiais por estado (não altere o material
   original compartilhado).
3. Linha do tempo: um slider de `minDate` a `maxDate`; ao soltar, `GET /twin/state?date=…` (com `If-None-Match`) e
   repinte. Não recarregue o GLB.
4. Sem `viewerUrl` e `geometrySource` = `SimplifiedRepresentation`: desenhe uma caixa por zona com `simplifiedBox`
   (largura × profundidade × altura, base em `elevation`), pintada por `state.zoneStates` — com o `disclaimer` visível.
   `AssistedMapping`: mostre a lista de zonas com seus estados (sem 3D).
5. `viewerUrl` expira (~15 min): se o carregamento falhar com 403, chame `/twin` de novo.

### 2.23 GET /documents (v1.4) — documentos da obra

Flag `documents` (padrão **desligada**). Só os documentos que a construtora marcou como visíveis ao cliente (contrato,
projeto, ART/RRT, alvará, nota fiscal, laudo, manual, garantia, foto, outro), ordenados por categoria e do mais novo ao mais
antigo:

```json
{
  "items": [
    {
      "title": "Manual do Proprietário",
      "category": "Manual",
      "fileName": "Manual do Proprietário.pdf",
      "contentType": "application/pdf",
      "sizeBytes": 482113,
      "validUntil": "2031-10-30",
      "url": "https://…assinada…",
      "urlExpiresAt": "2026-10-30T14:18:11+00:00",
      "uploadedAt": "2026-10-28T10:02:00+00:00"
    }
  ]
}
```

- `category`: `Contrato | Projeto | ArtRrt | Alvara | NotaFiscal | Laudo | Manual | Garantia | Foto | Outro`.
- `validUntil` pode ser `null`. `url` é assinada (~15 min) ou `null` (armazenamento indisponível): peça de novo ao expirar.
- `contentType`: `application/pdf`, `image/jpeg`, `image/png` ou `image/webp`.
- Nunca vêm: id interno, chave de armazenamento, hash, quem enviou, nem os dados que a IA extraiu do documento.
- Flag desligada → `404 portal_section_disabled`.

## 3. Senha e sessão

1. `GET` bootstrap sem sessão num portal com senha → `401`:
   ```json
   { "error": "Este portal pede senha.", "code": "portal_password_required",
     "portal": { "name": "Construtora Silva", "logoUrl": null, "title": "Acompanhe sua obra" } }
   ```
   Use `portal` para desenhar a tela de senha (é tudo que aparece antes da senha).
2. `POST /session` com a senha → `token`.
3. Toda chamada: header `X-Build-Portal-Session: {token}`.
4. Sessão vencida, revogada ou aberta antes de a construtora **trocar a senha** → `401 portal_session_expired`
   (mesmo formato com `portal`). Volte à tela de senha.
5. Bloqueio (v1.5), em duas camadas, ambas por 15 min → `429 portal_locked` com `retryAfterSeconds` e header `Retry-After`:
   - **por visitante (IP)**: 20 senhas erradas em 15 min bloqueiam **só aquele IP** — quem tenta adivinhar não tranca o
     cliente de verdade, que entra de outro IP;
   - **por portal**: 200 senhas erradas de qualquer origem bloqueiam o portal inteiro (limita ataque distribuído).
   Toda tentativa conta antes da verificação (tentativas em paralelo contam todas); a senha certa zera as contagens.
6. `429 portal_busy` (v1.5): o servidor limita quantas senhas confere ao mesmo tempo; tente de novo em `retryAfterSeconds`.

## 4. Visibilidade (o que cada flag libera)

A construtora decide por obra. Padrões seguros entre parênteses. **v1.5:** as flags marcadas com 🔒 só valem em portal
**com senha** — num portal público ficam desligadas seja qual for o valor salvo (dados pessoais e do imóvel não saem sem
senha); o painel da construtora mostra "exige portal com senha" nelas.

| Flag | Libera | Padrão |
|---|---|---|
| `overallProgress` | `progress` (bootstrap) e `GET /progress` | ligado |
| `stageProgress` | `stages[]` e itens `stage_*` da timeline | ligado |
| `schedule` | `schedule` (datas previstas) | ligado |
| `forecastDelivery` | `progress.forecastDelivery`, curva `forecast` | desligado |
| `dailyLogs` | itens `daily_log` (só resumo para o cliente) | desligado |
| `photos` | `GET /photos`, itens `photo`, `photoCount` | ligado |
| `weather` | `weather` | ligado |
| `financialSummary` | `financialSummary` (orçamento aprovado × total realizado) | desligado |
| `clientUpdates` | `latestUpdates`, itens `update` | ligado |
| `contact` | `contact` | ligado |
| `changeOrders` (v1.1) 🔒 | `GET /change-orders` e a decisão (valores só com `financialSummary`) | ligado (só com senha) |
| `selections` (v1.1) 🔒 | `GET /selections` e a escolha | ligado (só com senha) |
| `measurements` (v1.1) | `GET /measurements` (valores só com `financialSummary`) | desligado |
| `postConstruction` (v1.2) 🔒 | `property` (bootstrap, após a entrega), `GET /handover`, `GET /warranty`, `GET /assistance` e as escritas de pós-obra | ligado (só com senha) |
| `model3d` (v1.3) | `GET /twin` e `GET /twin/state` (gêmeo digital, só leitura) | desligado |
| `realityCapture` (v1.3) 🔒 | `GET /reality-captures` (só as marcadas como visíveis) | desligado |
| `documents` (v1.4) 🔒 | `GET /documents` (só os marcados como visíveis) | desligado |
| `videos`, `financialCategories`, `financialTransactions`, `receipts`, `risks`, `team` | reservadas para próximas versões — ainda não geram dados | desligado |

Garantias: nada financeiro aparece sem `financialSummary` (v1.5: inclusive os valores dos aditivos); a nota interna do diário nunca sai (só `ClientSummary`);
fotos só se a foto e o registro do dia foram marcados; nenhum id interno, senha, hash ou custo detalhado.

## 5. Erros

Corpo padrão: `{ "error": "mensagem em português", "code": "…" }` (+ `portal` nos 401, + `retryAfterSeconds` no 429).

| Status | `code` | Quando |
|---|---|---|
| 404 | `portal_not_found` | empresa/obra inexistente **ou** portal não publicado — corpo **idêntico** nos dois casos |
| 404 | `portal_section_disabled` | rota de uma seção desligada (`/photos`, `/progress`), ou seção 🔒 em portal público (v1.5) |
| 400 | `portal_password_not_required` | `POST /session` num portal público |
| 401 | `portal_password_required` | portal com senha, sem header de sessão |
| 401 | `portal_password_invalid` | senha errada |
| 401 | `portal_session_expired` | sessão vencida, revogada ou de antes da troca de senha |
| 429 | `portal_locked` | 20 senhas erradas deste IP ou 200 de todas as origens (15 min) — §3 |
| 429 | `portal_busy` | muitas verificações de senha ao mesmo tempo; tente em `retryAfterSeconds` (v1.5) |
| 409 | `portal_conflict` | escrita simultânea a outra alteração (abrir chamado, enviar foto) — tente de novo (v1.5) |
| 413 | `payload_too_large` | corpo JSON acima de 64 KB (v1.5) |
| 403 | `portal_decision_requires_password` | decisão (aditivo/escolha) em portal público (v1.1) |
| 404 | `portal_item_not_found` | aditivo/escolha inexistente, de outra obra ou não visível ao cliente (v1.1) |
| 409 | `change_order_already_decided` | aditivo já aprovado/recusado (v1.1) |
| 409 | `selection_closed` | escolha já feita, confirmada ou encerrada (v1.1) |
| 422 | `selection_deadline_passed` | prazo da escolha terminou (v1.1) |
| 400 | `invalid_decision` · `signer_name_required` · `invalid_option` | corpo da decisão/escolha inválido (v1.1) |
| 422 | `handover_not_ready` | aceite antes da vistoria de entrega (v1.2) |
| 409 | `handover_already_accepted` | entrega já aceita (v1.2) |
| 422 | `property_not_delivered` | abrir chamado antes da entrega (v1.2) |
| 400 | `invalid_category` · `invalid_assistance_request` | categoria fora da lista · campos do chamado inválidos (v1.2) |
| 422 | `assistance_limit_reached` | 20 chamados abertos pelo portal (v1.2) |
| 422 | `assistance_closed` · `assistance_photo_limit` | chamado não aceita fotos · já tem 6 fotos (v1.2) |
| 400 | `invalid_file_type` · `invalid_file_content` | tipo não aceito · conteúdo não é a imagem declarada (v1.2) |
| 413 | `file_too_large` | foto com mais de 8 MB (v1.2) |
| 503 | `storage_unavailable` | envio de fotos indisponível (v1.2) |
| 422 | `assistance_not_resolved` | avaliação antes da resolução (v1.2) |
| 409 | `feedback_already_given` | avaliação já enviada (v1.2) |
| 400 | `invalid_rating` | nota fora de 1–5 (v1.2) |
| 400 | `invalid_date` | `GET /twin/state` com data depois de hoje ou antes do início da obra (v1.3) |
| 429 | — (corpo vazio) | rate limit por IP estourado (ver §6) |
| 500 | — | erro inesperado (`{ error, requestId }`) |

## 6. Rate limits (por IP)

| Política | Rotas | Padrão | Configuração |
|---|---|---|---|
| `build-portal` | GETs e `DELETE /session` | 120 req / 60 s | `RateLimiting:BuildPortal:*` |
| `build-portal-auth` | `POST /session` | 10 req / 300 s | `RateLimiting:BuildPortalAuth:*` |
| `build-portal-decision` | `POST …/decision`, `POST …/choice` (v1.1), `POST …/handover/accept`, `POST …/assistance`, `…/photos`, `…/feedback` (v1.2) | 30 req / 300 s | `RateLimiting:BuildPortalDecision:*` |

Estourou → `429` sem corpo. Faça backoff; não repita em loop.

IP do cliente (v1.5): a API roda atrás da borda da Railway, que descarta o `X-Forwarded-For` enviado pelo cliente e grava o
IP da conexão como primeiro item; por isso o padrão é o **primeiro item do `X-Forwarded-For`**. A Orken pode trocar a fonte
sem mudar código (`RateLimiting:ClientIpSource` = `ForwardedFor` | `RealIp` (`X-Real-IP`) | `Remote` (IP da conexão)). O
mesmo IP é usado no bloqueio de senha por visitante (§3), guardado só como hash.

## 7. Cache e mídia

- Todas as respostas vêm com `Cache-Control: no-store`: não guarde em CDN/cache compartilhado (podem conter URLs
  assinadas e dados de portais com senha). No navegador, refaça o bootstrap ao abrir a página.
- Fotos: URLs **assinadas** de curta duração (~15 min, `urlExpiresAt`). Não salve as URLs; peça de novo à API
  quando expirarem (ex.: ao rolar a galeria após 10 min, recarregue a página de fotos).
- Logo e capa (`logoUrl`, `coverUrl`): URLs públicas estáveis da CDN da Orken; podem ser cacheadas pelo navegador.
- Arquivos de projeto (BIM etc.) nunca são servidos por este portal. O gêmeo digital (v1.3) recebe só o arquivo de
  **visualização** (GLB) por URL assinada de ~15 min.
- Exceção ao `no-store`: `GET /twin/state` (sem URLs assinadas) usa `Cache-Control: private, no-cache` + `ETag`
  (revalide com `If-None-Match`; `304` = nada mudou).

## 8. Versionamento

`contract v1.6` (`contractVersion: "1.6"` no bootstrap; a v1.5 respondia `"1.5"`, a v1.4 respondia `"1.4"`, a v1.3 respondia `"1.3"`, a v1.2 respondia `"1.2"`, a v1.1 `"1.1"`, a v1 `"1"`). Compare só o número antes do ponto
(major): `1.x` continua compatível. Mudanças **aditivas** (campos novos, novos `type` na timeline,
novas seções em `sections`) podem chegar sem aviso — ignore o que não conhecer. Remoções ou mudanças de significado
só numa `v2`, anunciada antes.

### OpenAPI e tipos TypeScript

- [`openapi-portal.json`](openapi-portal.json): OpenAPI 3 das rotas `/api/public/build/…` com todos os DTOs públicos,
  o sucesso de cada rota e o corpo de erro `PublicBuildErrorDto` (`{ error, code, portal?, retryAfterSeconds? }`).
- [`portal-types.ts`](portal-types.ts): tipos TypeScript gerados desse documento (campo com `?` pode vir `null` ou ausente).
- Os dois são gerados da API em execução (Swagger) e conferidos por um teste: se a API mudar e os arquivos não, o teste
  falha. Para regenerar (em `nexo-backend`, com Docker):

  ```bash
  ORKEN_WRITE_PORTAL_OPENAPI=1 dotnet test tests/Nexo.IntegrationTests --filter "FullyQualifiedName~BuildPortalOpenApiTests"
  ```

  (PowerShell: `$env:ORKEN_WRITE_PORTAL_OPENAPI='1'; dotnet test …`). Este documento (markdown) continua sendo a referência
  de regras e significado; o OpenAPI descreve os formatos.

## 9. Changelog

### v1.6 — integração do portal "Minha Obra"

Só acréscimos; nada da v1–v1.5 mudou de significado.

- `contractVersion` passa a `"1.6"`.
- `GET /twin` ganha `elementIndex` (§2.19): etapa (pelos vínculos reais), classe IFC e zona de cada elemento, para os
  filtros do visualizador. Nomes de etapa só quando o portal já mostra etapas.
- CORS: `ETag` passa a ser exposto ao navegador de outra origem (antes só `Retry-After`), para a revalidação de
  `/twin/state` com `If-None-Match` funcionar no site.

### v1.5 — revisão de segurança do portal

`contractVersion` passa a `"1.5"`. Ajustes de segurança e privacidade — alguns **restringem** o que a v1.4 servia (exceção
consciente à regra "só acréscimos" do §8, por proteção de dados do cliente):

- **Seções só com senha** (§4 🔒): em portal público, `postConstruction` (Meu Imóvel: entrega, garantias, chamados),
  `changeOrders`, `selections`, `documents` e `realityCapture` deixam de ser servidas mesmo com a flag ligada — somem de
  `sections` e de `property`, e as rotas respondem `404 portal_section_disabled`. As escritas continuam `403
  portal_decision_requires_password`.
- **Aditivos** (§2.7): `costImpact`, `items[].unitPrice` e `items[].total` só vêm com `financialSummary` ligado (antes vinham
  sempre); `signerName` não é mais devolvido. Trate esses campos como opcionais.
- **Ordem das verificações** (§2): a sessão do portal com senha é conferida antes de qualquer outra regra — sem sessão,
  sempre `401` (antes, uma escrita numa seção desligada respondia `404` sem sessão).
- **Bloqueio de senha** (§3): por IP (20 erros/15 min, só aquele IP) + por portal (200 erros/15 min de todas as origens),
  contando cada tentativa antes da verificação; novo `429 portal_busy` quando o servidor está no limite de verificações
  simultâneas de senha.
- Novos códigos: `portal_busy` (429), `portal_conflict` (409, escrita simultânea em chamados/fotos), `payload_too_large`
  (413, corpo JSON acima de 64 KB).
- Envio de foto (§2.17): portal, sessão e chamado são conferidos antes de ler o arquivo. Formato inalterado.
- Rate limit (§6): fonte do IP configurável (`RateLimiting:ClientIpSource`), padrão inalterado.
- URLs assinadas: cada seção só assina arquivos da sua própria pasta privada (fotos do diário, opções de escolha, documentos
  da entrega, fotos de chamado, documentos, capturas, modelo de visualização). Formato inalterado.

### v1.4 (revisão Wave 6 — sem mudança de formato)

Nenhum campo, rota ou código novo: `contractVersion` continua `"1.4"`.

- Publicados [`openapi-portal.json`](openapi-portal.json) e [`portal-types.ts`](portal-types.ts) (§8), com o corpo de erro
  documentado como `PublicBuildErrorDto` — o mesmo formato de sempre.
- Quando a construtora tem as previsões ligadas (padrão), as datas **previstas** (`forecastDelivery`, curva `forecast`, fim
  previsto por etapa) passam a vir do motor de produtividade `productivity/1` (dias úteis, ritmo recente, sequência das
  etapas) em vez da média simples; o significado ("previsão") e os formatos são os mesmos.
- O clima do bootstrap (`weather`) não mudou.

### v1.4 — Wave 5 (IA e documentos)

Só acréscimos; nada da v1–v1.3 mudou de significado.

- `contractVersion` passa a `"1.4"`. A flag `documents` (antes reservada) passa a valer, desligada por padrão, e aparece em
  `sections` quando ligada.
- Nova rota: `GET /documents` (§2.23) — documentos marcados como visíveis, com URL assinada de ~15 min.
- A IA da construtora não fala com o portal: textos gerados por IA (atualizações, resumo semanal) só chegam ao cliente depois
  que uma pessoa os publica, pelas rotas e campos que já existiam (`latestUpdates`, timeline `update`).

### v1.3 — Wave 4 (Gêmeo digital)

Só acréscimos; nada da v1–v1.2 mudou de significado.

- `contractVersion` passa a `"1.3"`. As flags `model3d` e `realityCapture` (antes reservadas) passam a valer, ambas
  desligadas por padrão, e aparecem em `sections` quando ligadas.
- Novas rotas: `GET /twin` (fonte da geometria, URL assinada do GLB de visualização — nunca o arquivo original —, zonas,
  estado de hoje, aviso), `GET /twin/state?date=` (linha do tempo, com `ETag`) e `GET /reality-captures` (§2.19–§2.21).
- Orientação de visualização com three.js (§2.22).
- Novo código: `invalid_date` (400).

### v1.2 — Wave 3 (Qualidade e pós-obra)

Só acréscimos; nada da v1/v1.1 mudou de significado.

- `contractVersion` passa a `"1.2"`; bootstrap ganha `phase` (`construction` | `delivered`) e, depois da entrega,
  `property` (data da entrega, aceite, garantias com `expiresAt`/`isActive`, chamados abertos) — §2.12.
- Nova flag `postConstruction` (padrão ligado; nome em `sections`).
- Novas rotas: `GET /handover`, `POST /handover/accept`, `GET /warranty`, `GET /assistance`, `POST /assistance`,
  `POST /assistance/{id}/photos` (multipart: JPEG/PNG/WebP, até 8 MB, assinatura conferida, até 6 por chamado),
  `POST /assistance/{id}/feedback` (§2.13–§2.18). Escritas só em portal com senha (`403 portal_decision_requires_password`).
- Novos códigos: `handover_not_ready`, `handover_already_accepted`, `property_not_delivered`, `invalid_category`,
  `invalid_assistance_request`, `assistance_limit_reached`, `assistance_closed`, `assistance_photo_limit`,
  `invalid_file_type`, `invalid_file_content`, `file_too_large` (413), `storage_unavailable` (503),
  `assistance_not_resolved`, `feedback_already_given`, `invalid_rating`.

### v1.1 — Wave 2 (Comercial)

Só acréscimos; nada da v1 mudou de significado.

- `contractVersion` passa a `"1.1"`.
- Novas rotas: `GET /change-orders`, `POST /change-orders/{id}/decision`, `GET /selections`,
  `POST /selections/{id}/choice`, `GET /measurements` (§2.7–§2.11).
- Novas flags de visibilidade (e nomes em `sections`): `changeOrders` (padrão ligado), `selections` (padrão ligado),
  `measurements` (padrão desligado).
- Decisões do cliente (aprovar/recusar aditivo, escolher opção) **só em portal com senha** com sessão válida; portal
  público → `403 portal_decision_requires_password`.
- Novos códigos de erro: `portal_decision_requires_password`, `portal_item_not_found`, `change_order_already_decided`,
  `selection_closed`, `selection_deadline_passed`, `invalid_decision`, `signer_name_required`, `invalid_option`.
- Nova política de rate limit `build-portal-decision`.

### v1 — Wave 1

Bootstrap, sessão, timeline, fotos e progresso.
