# Orken Build — API interna (Waves 1 a 4)

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


Upload de imagem de opção de escolha: contexto `build-selection-option` (**privado**, papéis de gestão,
`private/tenants/{tenant}/build/selections/...`); a imagem só sai para o cliente como URL assinada de ~15 min, e só
para escolhas marcadas como visíveis ao cliente.

# Wave 2 — Comercial

Migration aditiva `BuildWave2Commercial`. Mesmas regras: gestão + módulo `build`; `404` para inexistente/de outra
empresa; `409` para conflito (decisão repetida, aprovação repetida, alteração simultânea); `422` regra de negócio com
`code`. Valores em BRL com 2 casas. Toda mudança é auditada (`audit_records`, `entityType` `Build*`). Rotas abaixo
relativas a `/api/v1/build`.

## Orçamentos (compatível com o contrato anterior)

Os endpoints existentes continuam iguais; campos novos são opcionais na entrada e aditivos na saída.

- `POST /budgets` aceita também `validUntil` (data), `notes` (vai para o cliente/PDF), `internalNotes` (nunca sai),
  `areaM2`.
- `POST /budgets/{id}/items` e `PUT /budget-items/{id}` aceitam `kind` (`Material | Labor | Service | Equipment |
  Other`; padrão `Other` na criação, `null` mantém na edição) e `stageName` (nome de etapa para orçamento sem obra ou
  vindo de modelo; no PUT `null` mantém e `""` limpa). `category` continua texto livre e obrigatório.
- `BuildBudgetDto` ganha: `validUntil`, `isExpired`, `notes`, `internalNotes`, `areaM2`, `costPerM2`, `pricePerM2`,
  `version`, `parentBudgetId`, `rootBudgetId` (família de versões), `sourceTemplateId`,
  `totals { material, labor, service, equipment, other, subtotal, marginPercent, marginValue, total, areaM2,
  costPerM2, totalPerM2 }`, `approvedAt`, `approvedByName`, `rejectedAt`, `rejectedByName`, `rejectionReason`.
  Item: `kind`, `stageName`. Itens antigos aparecem como `Other`.
- Aprovação/recusa: mesmo comportamento, agora registrando quem/quando. `POST /budgets/{id}/reject` aceita corpo
  opcional `{ reason }`. **Vencido:** orçamento `Sent` com `validUntil` anterior a hoje (Brasil) → `422 budget_expired`
  ao aprovar (`isExpired: true` no DTO).
- `POST /budgets/{id}/convert` (contrato inalterado): além de vincular e definir `budgetApproved`, se a obra **não tem
  etapas**, cria uma etapa por `stageName` distinto dos itens (na ordem dos itens) e liga esses itens às etapas
  criadas. Obra que já tem etapas fica como está.

| Método | Rota | Corpo / observação |
|---|---|---|
| PUT | `/budgets/{id}/terms` | `{ validUntil?, notes?, internalNotes?, areaM2? }` — substitui os quatro; só `Draft`/`Sent` |
| POST | `/budgets/{id}/duplicate` | `{ name? }` (opcional) → `201` novo `Draft` com os mesmos itens, fora da família de versões |
| POST | `/budgets/{id}/versions` | → `201` nova versão `Draft` (`version` = maior da família + 1, `parentBudgetId`); só de `Sent`/`Approved`/`Rejected` (`422 budget_version_not_allowed`); a anterior não muda |
| GET | `/budgets/{id}/versions` | versões da família: `{ id, version, name, status, finalPrice, parentBudgetId, validUntil, isExpired, createdAt }` |
| GET | `/budgets/{id}/pdf` | `application/pdf` — marca da empresa (nome/logo de `portal/company`; sem cadastro, nome do tenant; logo opcional, nunca derruba o PDF), obra/cliente, itens agrupados por tipo com subtotais, **preços já com a margem** (sem custo, sem % de margem, sem observação interna), total, total por m², validade, observações, versão |
| POST | `/budgets/{id}/save-as-template` | `{ name, description?, perSquareMeter? }` — com `perSquareMeter` (exige área) as quantidades viram "por m²" |

### Modelos de orçamento

| Método | Rota | Corpo / observação |
|---|---|---|
| GET | `/budget-templates` · `/budget-templates/{id}` | |
| POST | `/budget-templates` | `{ name, description?, defaultMarginPercent, items: [{ name, kind?, category?, unit, quantityPerM2? ou fixedQuantity?, unitCost, stageName? }] }` — exatamente uma das quantidades |
| PUT | `/budget-templates/{id}` | substitui tudo, itens inclusive |
| DELETE | `/budget-templates/{id}` | `204`; orçamentos criados dele continuam (`sourceTemplateId` vira `null`) |
| POST | `/budget-templates/{id}/budgets` | `{ name, projectId?, areaM2?, marginPercent?, validUntil?, notes? }` → `201` orçamento `Draft`; quantidade = `quantityPerM2 × areaM2` (3 casas) ou a fixa; item por m² sem área → `422 template_area_required` |

## Aditivos (change orders)

`BuildChangeOrder`: número sequencial por obra, `publicId` opaco (o portal só usa ele), `reason`
(`ClientRequest | DesignChange | SiteCondition | Error | Other`), `costImpact` (pode ser negativo),
`scheduleImpactDays` (pode ser 0/negativo; diferente de 0 exige `stageId`), status
`Draft → SentToClient → Approved | Rejected` (ou `Cancelled`), `decidedBy` (nome do usuário ou `Cliente (portal)`),
`decisionChannel` (`Internal | Portal`), `clientComment`, `signerName`, `sourceSelectionId`, `appliedToBudget`, itens
opcionais `{ name, kind, quantity, unit, unitCost }`.

| Método | Rota | Corpo / observação |
|---|---|---|
| GET | `/projects/{id}/change-orders?status=` | |
| POST | `/projects/{id}/change-orders` | `{ title, description?, reason?, costImpact?, scheduleImpactDays?, stageId?, items? }` — `costImpact` nulo = soma dos itens |
| GET/PUT | `/change-orders/{id}` | PUT só em `Draft` (`422 change_order_not_draft`); itens substituídos |
| POST | `/change-orders/{id}/send` | `Draft → SentToClient` (aparece no portal) |
| POST | `/change-orders/{id}/approve` · `/reject` | `{ comment? }` — decisão interna (canal `Internal`; a empresa pode decidir também um rascunho); já decidido → `409` |
| POST | `/change-orders/{id}/cancel` | rascunho ou enviado; decidido → `409` |

**Efeitos da aprovação** (pela empresa ou pelo cliente, na mesma transação):
1. `project.budgetApproved += costImpact` se a obra tem orçamento aprovado (`appliedToBudget = true`). Sem orçamento
   aprovado nada é somado e o aditivo entra só no total do contrato. Contrato negativo → `422 change_order_negative_contract`.
2. A etapa afetada tem o **fim previsto** deslocado em `scheduleImpactDays` — **só ela**; as seguintes não são empurradas.
3. Se já existe linha de base, o plano é congelado como revisão com motivo `Aditivo #N aprovado`.
4. O retrato de progresso de hoje é atualizado.

`GET /projects/{id}/financial-summary` ganha (aditivo): `approvedChangeOrdersTotal` (soma dos aditivos aprovados) e
`contractTotal` = `approvedBudget` (que já inclui os aditivos aplicados) + aditivos aprovados quando não havia
orçamento aprovado; `null` quando não há nenhum dos dois. Limitação conhecida: aprovar/converter um orçamento
**depois** de aditivos aplicados redefine `budgetApproved` para o preço do orçamento (comportamento original), sem
somar os aditivos anteriores.

Duas decisões simultâneas do mesmo aditivo: o status é token de concorrência; a segunda recebe `409`.

## Escolhas do cliente (selections)

`BuildSelection` (`title`, `description`, `stageId?`, `deadline?` inclusivo, `clientVisible` padrão `true`, status
`Open → Submitted → Confirmed` ou `Cancelled`, `chosenOptionId`, `clientComment`, `chosenVia`, `changeOrderId`) com
opções (`name`, `description`, `priceDelta` relativo à opção padrão, `isStandard` — no máximo uma, `imageKey`
privado, `leadTimeDays`).

| Método | Rota | Corpo / observação |
|---|---|---|
| GET/POST | `/projects/{id}/selections` | `{ title, description?, stageId?, deadline?, clientVisible? }` |
| GET/PUT | `/selections/{id}` | PUT só em `Open` (`422 selection_closed`) |
| POST | `/selections/{id}/options` | `{ name, description?, priceDelta?, isStandard?, imageKey?, leadTimeDays?, order? }` (só `Open`) |
| PUT/DELETE | `/selection-options/{optionId}` | só com a escolha `Open` |
| POST | `/selections/{id}/confirm` | `{ optionId? }` — confirma a escolha do cliente (`Submitted`); em `Open` registra e confirma a opção informada (`422 selection_option_required` sem ela); confirmar de novo → `409` |
| POST | `/selections/{id}/cancel` | confirmada → `409` |

Confirmar uma opção com `priceDelta` diferente de 0 ou `leadTimeDays > 0` cria, na mesma gravação, um **aditivo em
rascunho** (`sourceSelectionId`, motivo `ClientRequest`, `costImpact = priceDelta`, `scheduleImpactDays = leadTimeDays`
quando a escolha tem etapa — sem etapa o prazo vai só na descrição) e liga `changeOrderId`. A empresa revisa e envia o
aditivo ao cliente: escolha → aditivo → contrato/cronograma.

## Materiais

`BuildMaterialRequest`: `materialName`, `unit`, `quantity`, `stageId?`, `neededBy?`, status
`Requested → Quoted → Ordered → PartiallyDelivered → Delivered` ou `Cancelled`, `supplierId?` (fornecedor do tenant),
`estimatedUnitCost?` (`estimatedTotal` no DTO), `deliveredQuantity`, `remainingQuantity`, `deliveredAt`, `movementId?`,
`isLate` (`neededBy` anterior a hoje e não entregue/cancelado) e o histórico `deliveries[]`.

| Método | Rota | Corpo / observação |
|---|---|---|
| GET | `/projects/{id}/materials?status=&stageId=&late=true` | |
| POST | `/projects/{id}/materials` | `{ materialName, unit, quantity, stageId?, neededBy?, notes? }` |
| GET/PUT | `/materials/{id}` | PUT só em `Requested`/`Quoted` (`422 material_not_editable`) |
| POST | `/materials/{id}/quote` | `{ supplierId?, estimatedUnitCost? }` (corpo opcional) |
| POST | `/materials/{id}/order` | `{ supplierId?, unitCost? }` (corpo opcional) |
| POST | `/materials/{id}/deliveries` | `{ quantity, date?, note?, receivedBy? }` — parcial até completar; só pedidos (`422 material_not_ordered`); passar do pedido → `422 delivery_exceeds_quantity` |
| POST | `/materials/{id}/cancel` | entregue → `422 material_invalid_transition` |
| PUT | `/materials/{id}/movement` | `{ movementId }` ou `{ movementId: null }` — liga um lançamento **existente** desta obra (`ContextType=Obra`, mesma obra), senão `422 movement_not_of_project`. Nenhum lançamento é criado automaticamente |

## Medições

`BuildMeasurement` (número por obra, período, status `Draft → Submitted → Approved | Rejected`, notas, quem aprovou)
com itens por etapa: `previousPercent` (o % da etapa), `measuredPercent` (acumulado, ≥ anterior, ≤ 100), `amount?` e
`suggestedAmount` (= delta % × valor orçado da etapa no **orçamento de referência** — o mesmo do custo por etapa).

| Método | Rota | Corpo / observação |
|---|---|---|
| GET/POST | `/projects/{id}/measurements` | `{ periodStart, periodEnd, notes?, items: [{ stageId, measuredPercent, amount? }] }` — abaixo do % atual → `422 measurement_below_previous` |
| GET/PUT | `/measurements/{id}` | PUT só em `Draft` (`422 measurement_not_draft`), itens substituídos |
| POST | `/measurements/{id}/submit` | sem itens → `422 measurement_empty` |
| POST | `/measurements/{id}/approve` | aplica: cada etapa recebe o % medido **pelo mesmo caminho do avanço manual** (evento em `bld_stage_progress_events` com `source=Measurement`, `sourceRef` = id da medição, data efetiva = fim do período, nunca depois de hoje) e o retrato de hoje é atualizado; se a etapa já passou do % medido → `422`; aprovar de novo → `409` |
| POST | `/measurements/{id}/reject` | `{ reason? }` |

## Portal do cliente — visibilidade nova

Flags novas em `visibility` (`PUT /projects/{id}/portal`): `changeOrders` (padrão **ligado**), `selections` (padrão
**ligado**), `measurements` (padrão **desligado**; valores só junto com `financialSummary`). Decisões do cliente
(aditivo, escolha) só em portal com senha. Rotas públicas: [`client-portal-contract.md`](client-portal-contract.md) (v1.1).

API pública consumida pelo site do cliente: ver [`client-portal-contract.md`](client-portal-contract.md).

# Wave 3 — Qualidade e pós-obra

Mesmas regras de acesso (gestão + módulo `build`), mesmo padrão de erros, toda mudança auditada (`build_inspection_*`,
`build_non_conformity_*`, `build_handover_*`, `build_warranty_term_*`, `build_assistance_*`). Migration aditiva
`BuildWave3Quality` (tabelas `bld_checklist_templates`, `bld_checklist_template_items`, `bld_inspections`,
`bld_inspection_items`, `bld_non_conformities`, `bld_handovers`, `bld_warranty_terms`, `bld_assistance_requests`).
Ações que podem ser disparadas duas vezes ao mesmo tempo (concluir vistoria, transições de NC, entrega, aceite, avaliação)
usam o status como token de concorrência → `409`.

**Uploads privados** (`POST /api/integrations/storage/upload`, papel de gestão): contextos novos `build-quality`
(fotos de vistoria/NC, pasta `build/quality`), `build-handover` (documentos da entrega, `build/handover`, PDF aceito) e
`build-assistance` (fotos de chamados, `build/assistance`). As chaves só são aceitas se forem do próprio tenant e da
pasta certa (`422 invalid_file_key`).

## Checklists (modelos)

`BuildChecklistTemplate` (`name`, `category` = disciplina, `stageNameHint`) com itens (`order`, `text`, `required`,
`requiresPhoto`).

| Método | Rota | Corpo / observação |
|---|---|---|
| GET/POST | `/checklist-templates` | `{ name, category?, stageNameHint?, items?: [{ text, required (padrão true), requiresPhoto }] }` |
| GET/PUT/DELETE | `/checklist-templates/{id}` | PUT substitui os itens. Vistorias já criadas **não mudam** (itens copiados) |

## Vistorias (inspeções)

`BuildInspection`: `kind` (`Quality` | `ClientWalkthrough`), `stageId?`, `templateId?`, `title`, `scheduledFor?`,
`status` `Planned → InProgress → Passed | PassedWithRemarks | Failed` (ou `Cancelled`), `inspectorName`, `notes`
(interna), `completedAt/By`. Itens: `text`, `required`, `requiresPhoto`, `result` (`Pending | Ok | NotOk |
NotApplicable`), `comment`, `severity?` (da NC que o item abrirá), `photoKeys`/`photoUrls`, `nonConformityId`.

| Método | Rota | Corpo / observação |
|---|---|---|
| GET | `/projects/{id}/inspections?kind=&status=` | |
| POST | `/projects/{id}/inspections` | `{ title, stageId?, templateId?, scheduledFor?, inspectorName?, notes?, items? }` — itens do modelo copiados (retrato) + itens avulsos |
| GET/PUT | `/inspections/{id}` | PUT `{ title, stageId?, scheduledFor?, inspectorName?, notes? }` só aberta (`422 inspection_closed`) |
| POST | `/inspections/{id}/start` | `Planned → InProgress` |
| PUT | `/inspections/{id}/items/{itemId}` | `{ result, comment?, photoKeys?, severity? }` — passa a vistoria para `InProgress` |
| POST | `/inspections/{id}/complete` | `{ notes? }` — calcula o resultado (abaixo); concluir de novo → `409` |
| POST | `/inspections/{id}/cancel` | concluída → `409` |

**Resultado ao concluir:** item obrigatório `Pending` → `422 inspection_required_items_pending` (`details` = itens);
item com `requiresPhoto` julgado `Ok`/`NotOk` sem foto → `422 inspection_photo_required`; sem itens →
`422 inspection_empty`. Depois: algum obrigatório `NotOk` → `Failed`; algum `NotOk` → `PassedWithRemarks`; senão
`Passed` (opcional pendente não bloqueia). **Cada item `NotOk` abre uma NC** na mesma gravação (título
`"{vistoria}: {item}"`, descrição = comentário, etapa da vistoria, fotos copiadas, gravidade do item ou — sem ela —
`High` se obrigatório, `Medium` se opcional).

## Não conformidades (NC)

`BuildNonConformity`: número por obra, `stageId?`, `inspectionId?`/`inspectionItemId?`, `title`, `description`,
`severity` (`Low | Medium | High | Critical`, padrão `Medium`), `status` `Open → InTreatment → Resolved → Verified`
(ou `Cancelled` enquanto aberta), `responsibleName`, `dueDate`, `isOverdue` (aberta e vencida), `rootCause`,
`correctiveAction`, fotos, `costImpact?` (**informativo**: nada financeiro é criado automaticamente — lance a despesa
normalmente se quiser), `resolvedAt/ByName`, `verifiedAt/ByName`, `verifiedBySameUser` (permitido, registrado).

| Método | Rota | Corpo / observação |
|---|---|---|
| GET | `/projects/{id}/non-conformities?status=&severity=&overdue=true` | |
| POST | `/projects/{id}/non-conformities` | `{ title, description?, severity?, stageId?, responsibleName?, dueDate?, costImpact?, photoKeys?, rootCause?, correctiveAction? }` |
| GET/PUT | `/non-conformities/{id}` | PUT só aberta/em tratamento (`422 nc_not_open`) |
| POST | `/non-conformities/{id}/start` | `Open → InTreatment` |
| POST | `/non-conformities/{id}/resolve` | `{ correctiveAction?, rootCause? }` — exige ação corretiva (agora ou antes): `422 nc_corrective_action_required` |
| POST | `/non-conformities/{id}/verify` | só depois de resolvida (`422 nc_not_resolved`) |
| POST | `/non-conformities/{id}/reopen` | verificação reprovou: `Resolved → InTreatment` |
| POST | `/non-conformities/{id}/cancel` | `{ reason }` — só aberta/em tratamento |

Repetir a mesma transição → `409`; transição inválida → `422 nc_invalid_transition`.

**Bloqueio da etapa:** enquanto a etapa tiver NC **Critical** `Open`/`InTreatment`, ela não chega a 100% — regra no
caminho único de progresso (`BuildStageProgressApplier`), então vale para `PUT /stages/{id}/progress` (inclusive
`status: "Completed"`) **e** para a aprovação de medição: `422 stage_has_critical_nonconformity`
("Etapa com não conformidade crítica aberta."). Resolvida (ou cancelada), libera.

`GET /projects/{id}/quality/summary` → `inspectionsByStatus` (vistorias de qualidade), `completedInspections`,
`passRate` (% de `Passed` + `PassedWithRemarks` sobre as concluídas; `null` sem nenhuma), `openNonConformities`,
`openNonConformitiesBySeverity`, `overdueNonConformities`, `awaitingVerification` (resolvidas), `criticalOpen`.

## Entrega (handover)

Uma por obra. `status` `Scheduled → InspectionDone → Delivered`, `scheduledFor`, `walkthroughInspectionId`,
`deliveredAt/ByName`, `keysDelivered`, `documents[{ name, fileKey?, fileUrl? }]`, `notes` (interna), aceite
(`acceptedAt`, `acceptedByName`, `acceptanceChannel` `Internal | Portal`, `acceptanceComment` — **nenhum IP é
guardado**), `projectStatus`, `projectStatusNote`, `openCriticalNonConformities`.

| Método | Rota | Corpo / observação |
|---|---|---|
| GET | `/projects/{id}/handover` | `404` enquanto não agendada |
| PUT | `/projects/{id}/handover` | `{ scheduledFor?, keysDelivered?, documents?, notes? }` — cria na primeira chamada |
| POST | `/projects/{id}/handover/walkthrough` | `{ title?, templateId?, scheduledFor?, inspectorName?, items? }` → vistoria `ClientWalkthrough` (`201`); já existe uma não cancelada → `409`. Concluída, a entrega vai a `InspectionDone` e cada pendência (`NotOk`) vira NC com a gravidade do item |
| POST | `/projects/{id}/handover/deliver` | `{ keysDelivered? }` — com NC crítica aberta → `422 handover_critical_nonconformity`; entregue de novo → `409` |
| POST | `/projects/{id}/handover/accept` | `{ signerName, comment? }` — aceite registrado pela empresa (canal `Internal`); só depois da vistoria/entrega (`422 handover_not_ready`); de novo → `409` |

**Entregar:** marca a obra `Completed` (pela transição existente, que grava `actualEndDate`) se ela não estiver
concluída nem cancelada — se estiver cancelada o status é mantido e `projectStatusNote` explica. A entrega pode ser feita
sem vistoria com o cliente (de `Scheduled`). As garantias sem início começam na data da entrega; sem nenhuma garantia,
cria o **conjunto de referência** abaixo.

## Garantias

`BuildWarrantyTerm`: `system`, `months` (1–600), `startsAt` (padrão = data da entrega), `expiresAt` (último dia
coberto, inclusive: dia de mesmo número do início, meses depois, ou o seguinte se o mês não o tiver — CC art. 132 §3), `isActive`, `description`, `isDefault`.

| Método | Rota | Corpo / observação |
|---|---|---|
| GET/POST | `/projects/{id}/warranty` | `{ system, months, startsAt?, description? }` |
| POST | `/projects/{id}/warranty/defaults` | cria o conjunto de referência (já há garantias → `409`) |
| PUT/DELETE | `/warranty-terms/{id}` | |

Conjunto de referência (**não é orientação jurídica**; ajuste ao contrato): Estrutura 60 meses (Código Civil, art. 618 —
solidez e segurança), Impermeabilização 60, Instalações elétricas 36, Instalações hidráulicas 36, Revestimentos 24,
Esquadrias 24, Pintura 24 (inspirado na tabela de prazos da ABNT NBR 15575).

## Assistência técnica

`BuildAssistanceRequest`: número por obra, `publicId` (o portal só usa ele), `category` (um `system` das garantias da
obra ou `Outros` — senão `422 invalid_category`), `title`, `description`, `location`, `openedVia` (`Internal | Portal`),
`openedByName`, `contactPhone`, `openedOn`, `status` `Open → Triaged → Scheduled → InProgress → Resolved → Closed` ou
`Rejected`, `triageNotes` (**interna**), `warrantyCovered` + `warrantyTermId`/`warrantySystem`, `scheduledFor`,
`resolutionNotes` e `rejectionReason` (visíveis ao cliente), `clientRating` 1–5, `clientFeedback`, fotos (até 6).

| Método | Rota | Corpo / observação |
|---|---|---|
| GET | `/assistance?projectId=&status=&covered=` | todos os chamados da empresa |
| GET/POST | `/projects/{id}/assistance` | `{ category, title, description, location?, openedByName?, contactPhone? }` |
| GET | `/assistance/{id}` | |
| POST | `/assistance/{id}/triage` | `{ warrantyCovered?, notes? }` — sem `warrantyCovered`, **calcula**: garantia da categoria cobrindo a data de abertura → `true`; fora do prazo → `false`; categoria sem garantia (ex.: `Outros`) → `null` |
| POST | `/assistance/{id}/schedule` | `{ scheduledFor }` (instante) |
| POST | `/assistance/{id}/start` · `/resolve` `{ resolutionNotes }` · `/close` · `/reject` `{ reason }` | transição inválida → `422 assistance_invalid_transition`; repetida → `409` |
| POST | `/assistance/{id}/photos` | `{ key }` (upload `build-assistance`) |

Portal do cliente: flag nova `postConstruction` (padrão **ligado**) e rotas da v1.2 em
[`client-portal-contract.md`](client-portal-contract.md).

# Wave 4 — Gêmeo digital (Digital Twin)

Mesmas regras de acesso (gestão + módulo `build`, prefixo `/api/v1/build`), mesmo padrão de erros, toda mudança
auditada (`build_model_*`, `build_element_state_recorded`, `build_reality_capture_*`, `build_tenant_settings_changed`).
Migration aditiva `BuildWave4DigitalTwin` (tabelas `bld_tenant_settings`, `bld_models`, `bld_model_versions`,
`bld_model_processing_jobs`, `bld_model_zones`, `bld_model_elements`, `bld_model_stage_bindings`,
`bld_element_progress_events`, `bld_reality_captures`). Desenho, formatos e regras: [`digital-twin.md`](digital-twin.md).

Três camadas que nunca se misturam: **fonte geométrica** (arquivo do modelo + derivado de visualização, ambos privados),
**verdade operacional** (etapas, cronograma, diário, medições, eventos de estado) e **interpretação** (vínculos etapa ↔
elemento/zona/categoria, com `source` `Manual` hoje; `AiSuggested`/`AiConfirmed` na Wave 5). Geometria nunca é inventada.

## Recursos por empresa (feature flags)

| Método | Rota | Observação |
|---|---|---|
| GET | `/api/v1/build/settings` | flags da empresa logada (só leitura): `build3DEnabled`, `buildAiEnabled`, `buildVisionEnabled`, `buildPredictEnabled`, `customized` |
| GET | `/api/platform/tenants/{tenantId}/build-settings` | **PlatformAdmin** (token de plataforma `super_admin`, com 2FA quando `Platform:RequireMfaForAdmin`). Sem linha → padrões |
| PUT | `/api/platform/tenants/{tenantId}/build-settings` | `{ build3DEnabled?, buildAiEnabled?, buildVisionEnabled?, buildPredictEnabled? }` — só o que vier muda; auditado (`actorType=platform`); empresa inexistente → `404` |

Padrões: 3D **ligado** (sem custo externo), IA **desligada**, visão **desligada**, previsões **ligadas**. Com 3D desligado,
upload/versão/visualizador/reprocessamento respondem `403 build_3d_disabled`; leituras continuam.

## Modelos e versões

`BuildModel` (`name`, `discipline` `Arquitetura | Estrutura | Hidraulica | Eletrica | Outro` — acentos aceitos,
`currentVersionId` = última versão `Ready`) → `BuildModelVersion` (`number`, `sourceFormat` `Ifc | Glb | Gltf | Rvt | Dwg |
Dxf | Pdf | Image`, `sourceFileName` só para exibição, `sizeBytes`, `sha256`, `geometrySource` `NativeBim | Converted3D |
AssistedMapping | SimplifiedRepresentation`, `viewerSource` `None | Uploaded | ConvertedServer | ConvertedBrowser`, `hasViewer`,
`status` `Uploaded | Queued | Processing | Ready | Failed | Rejected`, `statusMessage` em português, `elementCount`,
`storeyCount`, `schema`, `layers` (DXF), `processedAt`). Chaves de armazenamento **nunca** saem nas respostas.

| Método | Rota | Corpo / observação |
|---|---|---|
| GET | `/projects/{id}/models` | modelos com versões (mais nova primeiro) |
| POST | `/projects/{id}/models` | multipart `file`, `name?`, `discipline?` (padrão `Arquitetura`) → `201 { model, version }` |
| GET | `/models/{modelId}` | |
| POST | `/models/{modelId}/versions` | multipart `file` → `201 { model, version }` |
| GET | `/models/versions/{versionId}` | |
| GET | `/models/versions/{versionId}/viewer` | `{ viewerUrl, expiresAt }` — URL assinada (15 min) **só do GLB de visualização** |
| PUT | `/models/versions/{versionId}/viewer` | multipart `file` (GLB convertido no navegador com web-ifc, nós nomeados com os GlobalIds). Só versão IFC `Ready` (`422 viewer_requires_parsed_ifc`); não-GLB → `422 invalid_file_content`; menos de 50% dos nomes de nós são elementos da versão → `422 viewer_mismatch` (`Build:Models:ViewerMatchMinRatio`) |
| POST | `/models/versions/{versionId}/reprocess` | só versão `Failed` (`422 version_not_failed`) |

Upload: limite `Build:Models:MaxUploadMb` (padrão 150; o limite do corpo/multipart só é elevado nestas rotas) →
`413 file_too_large`; armazenamento desligado/indisponível → `503 storage_unavailable`. O formato vem dos **primeiros
bytes** (nunca do nome): IFC (`ISO-10303-21;`), GLB (`glTF` v2), glTF JSON (só com dados embutidos `data:` —
`422 gltf_external_uri` se apontar para arquivos externos), DXF ASCII, PDF, PNG/JPEG/WebP. **RVT** (OLE `D0 CF 11 E0`) e
**DWG** (`AC10…`) → `422 model_format_rejected` com `{ error, code, model, version }`: a versão fica registrada como
`Rejected` com a orientação de exportação e **nada é armazenado**. `.ifczip`/zip → `422 model_format_compressed`;
ifcXML/DXF binário → `422 model_format_unsupported`; desconhecido → `422 model_format_unknown` (nada registrado).
Chave: `private/tenants/{t}/build/models/{modelId}/{versionId}/source.<ext>` (o nome do cliente nunca entra na chave).
PDF/imagem ficam `Ready` na hora (folha para mapeamento assistido); os demais entram na fila (`Queued`).

## Processamento (fila)

`bld_model_processing_jobs` (`kind` `Parse | Convert`, `status` `Pending | Running | Done | Failed`, `attempts` ≤ 3,
`lockedUntil`). `BuildModelProcessingJobRunner` (não roda em `Testing`) consome a fila a cada `Build:Models:PollSeconds`
(10 s); a reserva é um `UPDATE … WHERE id = (SELECT … FOR UPDATE SKIP LOCKED) RETURNING id`. Arquivo ilegível/esquema não
suportado falha na hora (`Failed` + mensagem); outros erros voltam para a fila. Tempo limite `Build:Models:JobTimeoutMinutes`
(15). IFC: xBIM Essentials (gerenciado, sem nativo) → zonas (Site/Building/Storey/Space) e elementos (`IfcElement` exceto
aberturas: GlobalId, classe, nome, tipo, 1º material, zona, quantidades `netVolume`/`netArea`/`length`/`count`).
GLB/glTF: nós com malha → elementos (id = nome do nó se único, senão `node:{i}`). DXF: nomes das camadas.
Conversão IFC→GLB no servidor só com `Build:Models:IfcConvertPath` configurado.

## Zonas, elementos e estado (4D)

| Método | Rota | Corpo / observação |
|---|---|---|
| GET | `/models/versions/{id}/zones` | zonas lidas do modelo com `elementCount` |
| GET/POST | `/projects/{id}/zones` | zonas manuais: `{ kind (Floor, Area, Room, Facade, Roof, External), name, order?, parentZoneId?, outline? [{x,y}] (3–200 pontos, 0..1, exige sheetVersionId), sheetVersionId? (PDF/imagem/DXF da obra), simplifiedBox? { width, depth, height, elevation } (m) }` |
| PUT/DELETE | `/zones/{zoneId}` | só zonas manuais (`422 zone_from_model`) |
| GET | `/models/versions/{id}/elements?category=&zoneId=&state=&date=&skip=&take=` | `zoneId` inclui subzonas; `state` = nome do estado ou `Unbound`; `take` ≤ 500; cada item traz `state`, `stageId`, `explicitState`, `properties` |
| GET | `/models/versions/{id}/elements/{externalId}/events` | histórico de estados explícitos |
| GET | `/models/versions/{id}/state?date=yyyy-MM-dd` | `{ date, counts, states: { estado: [externalIds] }, zoneStates: { estado: [zoneIds] }, unbound, total }` com `ETag` (`304` com `If-None-Match`) |

**Regra do estado em uma data `d`:** (1) último evento explícito do elemento com data efetiva ≤ `d`; senão (2) derivado
da etapa vinculada — precedência Elemento > Zona (a do elemento ou ancestral mais próxima) > Categoria; várias etapas no
mesmo nível → a de **fim previsto mais tarde**; etapa em `d`: % = último evento de progresso ≤ `d` (sem histórico: % atual
a partir de hoje, 0 antes), 100 → `Completed`; bloqueada (status atual, só de hoje em diante) → `Blocked`; fim previsto
< `d` → `Delayed`; % > 0 → `InProgress`; senão `NotStarted`; (3) sem vínculo → `null` (não conta como avanço,
contado em `unbound`). Eventos explícitos e vínculos de elemento valem pelo id externo em qualquer versão da obra (GlobalId
é estável entre versões). NC por elemento (Wave 3) não entra no estado.

## Vínculos e estados explícitos

| Método | Rota | Corpo / observação |
|---|---|---|
| GET | `/projects/{id}/twin/bindings` | |
| POST | `/projects/{id}/twin/bindings` | `{ stageId, scope (Element, Zone, Category), modelVersionId?, elementExternalId?, zoneId?, category? }` — etapa de outra obra → `422 stage_not_of_project`; elemento inexistente na versão → `422 element_not_found` |
| POST | `/projects/{id}/twin/bindings/bulk` | `{ stageId, modelVersionId, category?, zoneId?, externalIds?, replaceExisting? (padrão true) }` — ex.: todas as `IfcWall` do Térreo → Alvenaria. Cria vínculos de Elemento; ao menos um filtro; ids inexistentes → `422 element_not_found` → `{ matched, created, replaced }` |
| DELETE | `/twin/bindings/{id}` | |
| POST | `/projects/{id}/twin/element-events` | `{ modelVersionId, externalIds[] (≤ 5000), state, note?, effectiveDate? (padrão hoje; futura → 422 invalid_date), source? (Manual), sourceRef? }` — histórico só de inclusão; não mexe no % da etapa nem nos retratos diários |

## Visão geral e preparação 5D

- `GET /projects/{id}/twin` — `build3DEnabled`, `geometrySource` da obra (versão principal: 3D com visualizador,
  Arquitetura primeiro; senão caixas → `SimplifiedRepresentation`; senão `None`), modelos/versões/status, zonas manuais,
  `totalElements`, `boundElements`, `bindingCoveragePercent` (vinculados ÷ total, versões atuais), `pendingJobs`, etapas
  com `boundElements`.
- `GET /projects/{id}/twin/cost?date=` — por etapa: orçado (orçamento de referência), realizado até a data (lançamentos
  confirmados de saída da obra com etapa), elementos vinculados, soma de `volume`/`area`/`length`, custo por elemento e
  por m³/m² (**rateio por quantidade** — preparação para 5D, não é orçamento por elemento).

## Captura da realidade

`BuildRealityCapture`: `type` (`Photo`, `Photo360`, `Video`, `Drone`, `PointCloud`), `capturedAt`, `stageId?`, `zoneId?`,
`elementExternalId?`, `title`, `notes` (interna), `contentType`, `sizeBytes`, `clientVisible` (padrão falso), `url` assinada
(15 min), `processed` (falso para nuvem de pontos — guardada, não processada).

| Método | Rota | Corpo / observação |
|---|---|---|
| GET | `/projects/{id}/reality-captures?type=&stageId=&zoneId=&elementExternalId=&from=&to=` | até 500, mais recentes primeiro |
| POST | `/projects/{id}/reality-captures` | multipart `file`, `type`, `title`, `capturedAt?`, `stageId?`, `zoneId?`, `elementExternalId?`, `notes?`, `clientVisible?`. Conteúdo pelos primeiros bytes: imagem JPEG/PNG/WebP (≤ 25 MB, `Photo`/`Photo360`/`Drone`), vídeo MP4/MOV (`ftyp`)/WebM (≤ 200 MB, `Video`/`Drone`), `.las` (`LASF`)/`.e57` (`ASTM-E57`) (≤ 500 MB, `PointCloud`); incompatível → `422 invalid_file_content`; limites `Build:RealityCapture:Max{Image,Video,PointCloud}Mb` → `413 file_too_large` |
| GET/PUT/DELETE | `/reality-captures/{id}` | PUT `{ title, notes?, capturedAt?, stageId?, zoneId?, elementExternalId? }`; DELETE apaga também o arquivo |
| PUT | `/reality-captures/{id}/client-visibility` | `{ clientVisible }` |

Arquivos em `private/tenants/{t}/build/reality/{projectId}/…` (contexto de upload `build-reality`). Upload direto ao R2
por URL pré-assinada (PUT) não foi feito: o envio passa pela API (limite elevado só nesta rota).

Portal do cliente: flags `model3d` e `realityCapture` (padrão **desligadas**) e rotas da v1.3 em
[`client-portal-contract.md`](client-portal-contract.md).

# Wave 5 — IA

Arquitetura, capacidades, proveniência, prompts, custos e privacidade em [`ai.md`](ai.md). Todas as rotas abaixo em
`/api/v1/build` (Management, módulo `build`). **Gerar** exige `BuildAiEnabled` (`403 build_ai_disabled`); enviar fotos ou
arquivos ao provedor exige também `BuildVisionEnabled` (`403 build_vision_disabled`). Sem provedor utilizável: as
capacidades com fallback gravam uma sugestão `source=Rules`; as outras respondem `422 ai_provider_unavailable`. Falha do
provedor/resposta inválida → `503 ai_provider_failed` (`details[0]` = motivo, ex.: `invalid_output`) e nada é gravado.

## Gerar sugestões (→ `201` `BuildAiSuggestionDto`, status `Pending`)

| Método | Rota | Corpo / observação |
|---|---|---|
| POST | `/projects/{id}/ai/daily-log-copilot` | `{ date? (padrão hoje; futura → 422 invalid_date), text (obrigatório, ≤ 8000 → 422 ai_input_required / ai_input_too_long), photoIds? (≤ 5 fotos do diário desta obra; 422 too_many_photos / photo_not_found / photo_too_large) }` — fallback: regras |
| POST | `/projects/{id}/ai/progress-interpreter` | `{ lookbackDays? (1–60, padrão 14), photoIds? }` — sem diário no período e sem fotos → `422 ai_no_evidence`; **sem fallback** |
| POST | `/projects/{id}/ai/model-mapper` | `{ modelVersionId? }` (padrão: versão principal) — sem modelo pronto → `422 model_not_ready`; sem etapas → `422 ai_no_stages`; fallback: tabela de classes IFC |
| POST | `/projects/{id}/ai/client-update` | `{ from?, to? }` (padrão últimos 7 dias; até hoje, ≤ 93 dias → `422 invalid_period`) — fallback: texto por modelo |
| POST | `/projects/{id}/ai/weekly-digest` | últimos 7 dias — fallback: texto por modelo |
| POST | `/projects/{id}/risk/explain` | `{ kind: delay ou cost }` — explicação da análise determinística; **sem fallback** |
| POST | `/documents/{id}/analyze` | exige IA **e** visão; PDF/JPEG/PNG/WebP ≤ 5 MB para a IA; **sem fallback** |

`BuildAiSuggestionDto`: `id`, `projectId`, `capability`, `status`, `source` (`Llm` ou `Rules`), `subjectId`, `confidence`,
`output` (validado; formatos em `ai.md`), `inputRefs`, `applied`, `provider`, `model`, `promptType`, `promptVersion`,
`promptVersionId`, `promptHash`, `inputTokens`, `outputTokens`, `costMicros`, `latencyMs`, `createdByName`, `createdAt`,
`decidedByName`, `decidedAt`, `decisionNote`, `expiresAt`. Itens selecionáveis: progresso → `output.items[].id` (= id da
etapa); mapeador → `output.bindings[].id` (`b1`, `b2`…). Ids que o provedor inventou são descartados e contados em
`output.discardedRefs`.

## Decidir

| Método | Rota | Corpo / observação |
|---|---|---|
| GET | `/projects/{id}/ai/suggestions?capability=&status=` | até 200, mais novas primeiro; vencidas viram `Expired` na leitura |
| GET | `/ai/suggestions/{id}` | |
| POST | `/ai/suggestions/{id}/accept` | `{ itemIds?, note? }` — sem `itemIds` = tudo; subconjunto → `PartiallyAccepted`; item desconhecido → `422 ai_item_not_found`; vencida/decidida → `409`; recusa do caminho de destino (ex.: `422 stage_has_critical_nonconformity`) mantém `Pending` |
| POST | `/ai/suggestions/{id}/reject` | `{ note? }` — nada muda na obra |

O que aceitar faz: copiloto → cria/completa o diário da data (`ClientVisible` falso); progresso → `BuildStageProgressApplier`
com `Source=AiConfirmed` e `SourceRef` = sugestão (nunca para trás) + retrato do dia; mapeador → vínculos de categoria/zona
(`AiConfirmed` ou `Rules`); atualização/resumo → `BuildClientUpdate` rascunho (`Ai` ou `Rules`); documento → categoria,
validade e `extracted`; risco → só ciência. `applied` registra os ids criados/alterados.

## Risco determinístico (sempre disponível, sem IA)

| Método | Rota | Resposta |
|---|---|---|
| GET | `/projects/{id}/risk/delay` | `BuildRiskAnalysisDto` `{ projectId, kind: "delay", asOf, level (none, low, medium, high), metrics { spi, actualPercent, plannedPercent, delayedStages, forecastSlipDays, lateMaterials, pendingChangeOrders, openCriticalNonConformities }, items[{ key, kind, severity, title, evidence, metrics, refs }], methodology: "build-risk/1" }` |
| GET | `/projects/{id}/risk/cost` | idem com `metrics { budget, realized, earnedValue, cpi, eac, vac, approvedChangeOrders, budgetWithChangeOrders, unlinkedMaterialsEstimated }` |

## Documentos da obra

`BuildDocument`: `category` (`Contrato`, `Projeto`, `ArtRrt`, `Alvara`, `NotaFiscal`, `Laudo`, `Manual`, `Garantia`, `Foto`,
`Outro`), `title`, `fileName` (só exibição, saneado), `contentType`, `sizeBytes`, `sha256`, `clientVisible` (padrão falso),
`validUntil`, `extracted` (dados confirmados da IA — **interno**), `suggestionId`, `uploadedByName`, `url` assinada (15 min).

| Método | Rota | Corpo / observação |
|---|---|---|
| GET | `/projects/{id}/documents?category=` | até 500 |
| POST | `/projects/{id}/documents` | multipart `file`, `category?` (padrão `Outro`), `title?` (padrão: nome do arquivo), `validUntil?`, `clientVisible?`. Conteúdo pelos primeiros bytes: PDF (`%PDF-`), JPEG, PNG, WebP → senão `422 invalid_file_content`; > 20 MB → `413 file_too_large`; armazenamento indisponível → `503 storage_unavailable` |
| GET/PUT/DELETE | `/documents/{id}` | PUT `{ category?, title?, validUntil? }`; DELETE apaga também o arquivo |
| PUT | `/documents/{id}/client-visibility` | `{ clientVisible }` |

Arquivos em `private/tenants/{t}/build/documents/{projectId}/{id}.<ext>` (contexto `build-document`; a chave nunca usa o nome
do cliente). Portal: flag `documents` e `GET /documents` na v1.4 de [`client-portal-contract.md`](client-portal-contract.md).

## Plataforma

- `GET /api/platform/interpreter/prompts/types` (Platform) → `[{ type, usedByRuntime, builtInHash }]` — os oito `build-*`
  entram como usados pelo runtime; `POST /api/platform/interpreter/prompts` aceita os novos tipos.
- `Build:Ai:WeeklyDigestJob:Enabled` (padrão `false`) liga o job de resumo semanal (só sugere; nunca publica).
