# Orken Build — API interna (Wave 1)

Rotas internas novas/alteradas da Wave 1. Todas exigem login com papel de gestão (`AppRoles.Management`) e o módulo
`build` ativo. Erros seguem o padrão da API: `400` validação, `404` inexistente/de outra empresa, `409` conflito
(slug em uso, congelamento simultâneo), `422` regra de negócio (`{ error, code, details }`). Toda mudança de portal,
cronograma e custo por etapa é auditada (`audit_records`, `entityType` `Build*`).

## Obra (alterações compatíveis)

- `POST /api/v1/build/projects` aceita, opcionais: `trackingMode` (`Standard` padrão | `ThreeD`), `latitude`
  (−90..90), `longitude` (−180..180; as duas juntas), `areaM2` (≥ 0).
- `PUT /api/v1/build/projects/{id}`: os mesmos campos. Coordenadas e área são substituídas como enviadas (null limpa,
  igual aos demais campos do PUT); `trackingMode` null mantém o atual.
- `BuildProjectDto` / `details` ganham `latitude`, `longitude`, `areaM2`, `trackingMode`.

## Etapas e progresso

- Etapas ganham `weight` (> 0, padrão 1) em `POST /projects/{id}/stages` e `PUT /stages/{id}` (null mantém).
- `PUT /api/v1/build/stages/{id}/progress` — contrato mantido (`progressPercent`, `status?`) + opcionais `note`,
  `effectiveDate` (padrão hoje, Brasil). Grava evento no histórico (`bld_stage_progress_events`, origem `Manual`)
  quando o % muda ou há nota, marca início/fim reais e atualiza o retrato de hoje.
- `GET /api/v1/build/projects/{id}/progress` — planejado/real/previsto geral, entrega prevista, metodologia
  (`weighted-stages/1`), origem do plano (`baseline`/`revision`/`current`), etapas (com `flags`, ex.
  `missing_planned_dates`), curvas semanais e avisos. O % geral nunca é digitado: Σ(peso·%)/Σpeso.
- `GET /api/v1/build/projects/{id}/progress/at?date=yyyy-MM-dd` — o retrato diário mais recente até a data
  ("como estava a obra em X"); `404` se não houver.
- `POST /api/v1/build/projects/{id}/schedule/baseline` `{ reason? }` — congela o plano atual (datas previstas e pesos
  por etapa). A primeira vez é a linha de base; as seguintes são revisões e exigem `reason`
  (`422 schedule_revision_reason_required`). `422 schedule_without_stages` sem etapas.
- `GET /api/v1/build/projects/{id}/schedule/revisions` — todas as versões congeladas.

Metodologia `weighted-stages/1`: planejado linear por dia entre início e fim previstos (inclusivo); etapas sem datas
ficam fora do planejado e são sinalizadas; previsão por etapa: concluída → fim real; em andamento com início e % > 0 →
hoje + restante ÷ ritmo observado; não iniciada → janela prevista deslocada pelo maior atraso das anteriores (nunca
antes de hoje); entrega prevista = maior fim previsto. Retratos diários: o de hoje é atualizado a cada mudança; dias
passados são imutáveis; um job horário (`BuildProgressSnapshotJob`) garante um retrato por obra ativa por dia.

## Custo por etapa (livro único)

- `PUT /api/v1/build/projects/{projectId}/movements/{movementId}/stage` `{ stageId | null }` — liga/desliga um
  lançamento da obra (`ContextType=Obra`, `ContextId=projectId`) a uma etapa da mesma obra. `422 movement_not_of_project`,
  `422 stage_not_of_project`.
- `GET /api/v1/build/projects/{id}/cost-by-stage` — por etapa: orçado (itens de orçamentos aprovados/convertidos da obra),
  realizado (lançamentos confirmados de saída), variação (orçado − realizado), nº de lançamentos; linha "Sem etapa"
  para o que não tem etapa. `financial-summary` continua igual.

## Portal do cliente (lado da construtora)

| Método | Rota | Corpo / observação |
|---|---|---|
| GET/PUT | `/api/v1/build/portal/company` | `{ slug, displayName, logoKey?, contactEmail?, contactPhone? }` — slug único na plataforma (`409`) |
| GET/PUT | `/api/v1/build/projects/{id}/portal` | `{ slug?, access (Public/Password), title?, welcomeMessage?, logoKey?, coverKey?, primaryColor? (#RRGGBB), accentColor?, contactName?, contactPhone?, contactEmail?, contactWhatsApp?, visibility? }` — PUT substitui tudo; flags omitidas em `visibility` assumem o padrão seguro; resposta nunca traz senha/hash, só `hasPassword`, `isLocked`, `publicPath` |
| PUT | `/api/v1/build/projects/{id}/portal/password` | `{ password }` (≥ 8) — encerra todas as sessões abertas |
| DELETE | `/api/v1/build/projects/{id}/portal/password` | recusado se o portal publicado exige senha |
| POST | `/api/v1/build/projects/{id}/portal/publish` | exige slug da empresa e da obra, e senha se `access=Password` (`422 portal_not_configured`, `portal_company_missing`, `portal_slug_missing`, `portal_password_missing`) |
| POST | `/api/v1/build/projects/{id}/portal/unpublish` | |
| PUT | `/api/v1/build/daily-logs/{logId}/client-visibility` | `{ clientVisible, clientSummary }` — visível exige resumo; a nota interna nunca vai ao cliente |
| PUT | `/api/v1/build/daily-log-photos/{photoId}/client-visibility` | `{ clientVisible }` — a foto também precisa do registro do dia visível |
| GET/POST | `/api/v1/build/projects/{id}/client-updates` | `{ title, body, kind? (Update/Milestone/WeeklyDigest) }` — nasce rascunho |
| PUT | `/api/v1/build/client-updates/{id}` | editar (não arquivadas) |
| POST | `/api/v1/build/client-updates/{id}/publish` · `/archive` | só publicadas aparecem no portal |

Logo e capa: enviar por `POST /api/integrations/storage/upload` com contexto `build-portal-logo` ou
`build-portal-cover` (públicos, papéis de gestão) e usar a `key` devolvida (`tenants/{tenant}/build/portal/...`).

API pública consumida pelo site do cliente: ver [`client-portal-contract.md`](client-portal-contract.md).
