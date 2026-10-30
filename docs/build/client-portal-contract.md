# Orken Build — Portal do Cliente — contrato da API pública (v1)

Documento para o time do frontend externo (Astra). Descreve **somente** a API pública do portal do cliente de
uma obra. Tudo aqui é estável na versão `contract v1`; mudanças incompatíveis viram `v2` (ver "Versionamento").

## 1. Base, endereço e slugs

| Item | Valor |
|---|---|
| Base da API | `{API_BASE}` — a mesma API do Orken (produção informada pela Orken). Ex.: `https://api.exemplo/` |
| Prefixo | `{API_BASE}/api/public/build/{companySlug}/{projectSlug}` |
| URL pública sugerida no site Astra | `/{companySlug}/{projectSlug}` (ex.: `/construtora-silva/residencia-alfa`) |
| Autenticação | Nenhuma conta. Portais com senha usam uma **sessão de portal** (ver §3). **Não** envie `Authorization`. |
| Formato | JSON UTF-8, `camelCase`, datas `yyyy-MM-dd`, instantes ISO-8601 com fuso (`2026-10-30T14:03:11.123456+00:00`), enums como texto. |
| CORS | A origem do site Astra precisa estar em `Cors:AllowedOrigins` da API (configuração da Orken). O header `X-Build-Portal-Session` é aceito; `Retry-After` é exposto. |

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

Em portal **público** a sessão é ignorada.

### 2.1 GET bootstrap

`GET {API_BASE}/api/public/build/construtora-silva/residencia-alfa`

`200` — só aparecem as seções habilitadas pela construtora (uma seção desligada **não existe** no JSON, nem como
`null`). `sections` lista os nomes das seções ligadas.

```json
{
  "contractVersion": "1",
  "sections": ["overallProgress", "stageProgress", "schedule", "photos", "weather", "clientUpdates", "contact"],
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
- `portal`, `company`, `project`, `contractVersion` e `sections` sempre vêm.
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
- Erros: `401 portal_password_invalid` (senha errada), `429 portal_locked` (bloqueio), `400 portal_password_not_required`
  (portal público), `404 portal_not_found`.

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
5. Bloqueio: 20 senhas erradas seguidas bloqueiam o portal por 15 min → `429 portal_locked` com `retryAfterSeconds`
   e header `Retry-After`.

## 4. Visibilidade (o que cada flag libera)

A construtora decide por obra. Padrões seguros entre parênteses.

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
| `videos`, `documents`, `financialCategories`, `financialTransactions`, `receipts`, `model3d`, `realityCapture`, `risks`, `team` | reservadas para próximas versões — ainda não geram dados | desligado |

Garantias: nada financeiro aparece sem `financialSummary`; a nota interna do diário nunca sai (só `ClientSummary`);
fotos só se a foto e o registro do dia foram marcados; nenhum id interno, senha, hash ou custo detalhado.

## 5. Erros

Corpo padrão: `{ "error": "mensagem em português", "code": "…" }` (+ `portal` nos 401, + `retryAfterSeconds` no 429).

| Status | `code` | Quando |
|---|---|---|
| 404 | `portal_not_found` | empresa/obra inexistente **ou** portal não publicado — corpo **idêntico** nos dois casos |
| 404 | `portal_section_disabled` | rota de uma seção desligada (`/photos`, `/progress`) |
| 400 | `portal_password_not_required` | `POST /session` num portal público |
| 401 | `portal_password_required` | portal com senha, sem header de sessão |
| 401 | `portal_password_invalid` | senha errada |
| 401 | `portal_session_expired` | sessão vencida, revogada ou de antes da troca de senha |
| 429 | `portal_locked` | portal bloqueado após 20 senhas erradas (15 min) |
| 429 | — (corpo vazio) | rate limit por IP estourado (ver §6) |
| 500 | — | erro inesperado (`{ error, requestId }`) |

## 6. Rate limits (por IP)

| Política | Rotas | Padrão | Configuração |
|---|---|---|---|
| `build-portal` | GETs e `DELETE /session` | 120 req / 60 s | `RateLimiting:BuildPortal:*` |
| `build-portal-auth` | `POST /session` | 10 req / 300 s | `RateLimiting:BuildPortalAuth:*` |

Estourou → `429` sem corpo. Faça backoff; não repita em loop.

## 7. Cache e mídia

- Todas as respostas vêm com `Cache-Control: no-store`: não guarde em CDN/cache compartilhado (podem conter URLs
  assinadas e dados de portais com senha). No navegador, refaça o bootstrap ao abrir a página.
- Fotos: URLs **assinadas** de curta duração (~15 min, `urlExpiresAt`). Não salve as URLs; peça de novo à API
  quando expirarem (ex.: ao rolar a galeria após 10 min, recarregue a página de fotos).
- Logo e capa (`logoUrl`, `coverUrl`): URLs públicas estáveis da CDN da Orken; podem ser cacheadas pelo navegador.
- Arquivos de projeto (BIM etc.) nunca são servidos por este portal.

## 8. Versionamento

`contract v1` (`contractVersion: "1"` no bootstrap). Mudanças **aditivas** (campos novos, novos `type` na timeline,
novas seções em `sections`) podem chegar sem aviso — ignore o que não conhecer. Remoções ou mudanças de significado
só numa `v2`, anunciada antes.
