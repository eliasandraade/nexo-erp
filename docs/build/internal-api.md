# Orken Build — API interna (Waves 1, 2 e 3)

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
