# Orken Build — IA (Wave 5)

A IA do Build **sugere**; pessoas **decidem**. Toda capacidade gera uma `BuildAiSuggestion` pendente, com a proveniência
completa; nada muda na obra até alguém aceitar (inteira ou só alguns itens). Rotas em [`internal-api.md`](internal-api.md)
(# Wave 5).

## Arquitetura

```
Capacidade do Build (BuildAiService)
  → IAiStructuredCompletion (genérico, Nexo.Application; implementação em Infrastructure)
      → AiProviderRegistry (provedor/modelo/chave do SuperAdmin — só provedores com modelo de linguagem: hoje o Claude)
      → ActivePromptProvider (prompt build-* ativo no SuperAdmin, senão o embutido)
      → IAiUsageGuard (reserva antes, liquida/libera depois)
      → AnthropicClientFactory (SDK oficial, URL fixa, HttpClient "anthropic")
      → validação do JSON contra o schema pedido (AiJsonSchemaValidator)
      → telemetria (interpreter_telemetry: operação build-*, provedor, modelo, prompt, tokens, custo, duração)
  → filtros do Build (ids que o Orken enviou, limites de %, valores, datas, textos)
  → bld_ai_suggestions (Pending) + auditoria
```

- **Mesmo runtime da plataforma** (PRs #87–#89): nenhuma chave nova, nenhum segundo interruptor. Provedor = os provedores
  `available` com modelo de linguagem, o padrão primeiro e depois por prioridade. O leitor de regras não tem modelo de
  linguagem e nunca é escolhido aqui (o "fallback" do Build é próprio, abaixo).
- **Saída estruturada**: `output_config.format` com JSON Schema (todos os objetos com `additionalProperties: false`) e nova
  validação no servidor (campos obrigatórios, tipos, enums, limites). JSON inválido ou fora do schema →
  `503 ai_provider_failed` com `details: ["invalid_output"]` e **nenhuma sugestão gravada** (a falha fica na telemetria).
- **Higiene contra prompt injection**: instruções (prompt + regras fixas + contexto escrito pelo próprio Orken) vão no
  `system`; tudo que vem de usuário/negócio vai no `user`, dentro de `<dados_da_obra> … </dados_da_obra>`, que as regras
  declaram como **dado, nunca instrução**. Um delimitador escrito dentro do texto é neutralizado (não fecha o bloco).
  Anexos (fotos/PDF) vão como blocos de imagem/documento antes do texto.
- **Erros**: sem provedor utilizável → `422 ai_provider_unavailable` (ou o fallback de regras, quando existe); falha do
  provedor → `503 ai_provider_failed` com o motivo (`invalid_credentials`, `rate_limited`, `timeout`, `invalid_output`,
  `refused`…); limite mensal → `limit_reached` (as capacidades com fallback usam as regras, sem custo).

## Interruptores (por empresa, só a plataforma altera)

| Flag | Padrão | Efeito |
|---|---|---|
| `BuildAiEnabled` | desligado | gerar sugestões (`403 build_ai_disabled` sem ela). Aceitar/recusar/listar sugestões já existentes não depende dela |
| `BuildVisionEnabled` | desligado | enviar fotos do diário ou arquivos de documento ao provedor (`403 build_vision_disabled`) |
| `BuildPredictEnabled` | ligado | não bloqueia nada nesta Wave: a análise determinística de risco é **sempre** disponível (não tem custo nem provedor); a explicação por LLM exige `BuildAiEnabled` |

## Capacidades

| # | Capacidade | Entrada (o que vai ao provedor) | Saída validada | Aceitar faz | Sem provedor |
|---|---|---|---|---|---|
| 1 | Copiloto do diário (`DailyLogCopilot`) | relato do dia (≤ 8000), data, etapas (id, nome, %), fotos opcionais (visão, ≤ 5 × 5 MB) | atividades `{stageId?, description, progressHint?, confidence}`, equipe `{role, count}`, equipamentos, ocorrências, clima, `clientSummary`, confiança | cria o diário da data (ou acrescenta às notas do existente); `ClientSummary` preenchido, **`ClientVisible` continua falso** (um resumo já publicado não é trocado) | regras: etapas cujo nome aparece no texto (tokens, sem acento/maiúsculas, plural/gênero tolerados), com a frase do relato copiada; sem equipe/ocorrências e **sem `clientSummary`** |
| 2 | Interpretador de progresso (`ProgressInterpreter`) | diário dos últimos N dias (1–60, padrão 14; até 30 registros, notas internas incluídas), etapas com % e datas planejadas, fotos opcionais (visão) | por etapa `{stageId, suggestedPercent (> atual, ≤ 100), rationale, evidenceRefs (só ids enviados), confidence}` | por item (`itemIds` = ids das etapas) pelo `BuildStageProgressApplier`: evento `Source=AiConfirmed`, `SourceRef` = id da sugestão; NC crítica aberta → `422 stage_has_critical_nonconformity` e a sugestão **continua pendente** | **nenhum** — `422 ai_provider_unavailable` (transformar texto em % sem modelo seria inventar) |
| 3 | Mapeador do modelo (`ModelMapper`) | versão do modelo (padrão: a principal): categorias com contagem (até 60), zonas com contagem (até 100), etapas — **nunca a lista de elementos** | vínculos `{stageId, scope Category\|Zone, category?, zoneId?, confidence, rationale}` | cria `BuildModelStageBinding` (`Source=AiConfirmed`; regras → `Rules`); repetidos são ignorados | regras por classe IFC (tabela abaixo), `Source=Rules` |
| 4 | Atualização ao cliente (`ClientUpdate`) | só fatos que o cliente pode ver no período (padrão 7 dias): % (com `overallProgress`), etapas concluídas (com `stageProgress`), resumos de diário **publicados**, aditivos aprovados (com `changeOrders`; valores só com `financialSummary`), medições aprovadas (com `measurements`), próximas etapas (com `schedule`), previsão de entrega (só com `forecastDelivery`) | `{title, body, highlights[]}` | cria `BuildClientUpdate` **rascunho** `Source=Ai` (regras → `Rules`); publicar continua manual | texto montado só com os fatos (sem prosa gerada), `Source=Rules` |
| 5/6 | Risco de prazo/custo (`DelayRisk`/`CostRisk`) | a análise determinística (itens com chave, evidência, métricas) | `{summary, items[{key, explanation}], recommendedActions[{text, itemKeys}]}` — chaves desconhecidas são descartadas | só registra a ciência (informativo) | `GET …/risk/*` sempre funciona; explicar → `422 ai_provider_unavailable` |
| 7 | Inteligência de documentos (`DocumentIntelligence`) | o arquivo do documento (PDF como bloco `document` base64 do SDK; imagem como bloco `image`), título e categoria informada — exige visão | `{documentType, issuer, issuerCnpj (14 dígitos), number, issueDate, dueDate, validUntil, totalAmount, items[], summary, confidence}` — datas 2000–2100, valores 0–1 bi | atualiza categoria, validade e `ExtractedJson` (interno) do documento | **nenhum** — `422` |
| 8 | Resumo semanal (`WeeklyDigest`) | como 4, últimos 7 dias | como 4 | rascunho `Kind=WeeklyDigest` | como 4 |

Validade das sugestões: 7 dias (copiloto, progresso, risco) e 14 dias (mapeador, cliente, resumo, documentos). Vencida →
`Expired` na leitura; aceitar/recusar vencida ou já decidida → `409`.

### Tabela do mapeador por regras

| Classes IFC | Palavra no nome da etapa (sem acento) |
|---|---|
| `IfcFooting`, `IfcPile` | `funda` |
| `IfcWall`, `IfcWallStandardCase`, `IfcWallElementedCase` | `alvenaria`, `parede` |
| `IfcSlab`, `IfcBeam`, `IfcColumn` | `estrutura` |
| `IfcRoof` | `cobertura`, `telhado` |
| `IfcWindow`, `IfcDoor` | `esquadria` |
| `IfcCovering` | `revestimento`, `acabamento` |
| `IfcFlowSegment`, `IfcPipeSegment` | `hidr` |
| `IfcCableSegment` | `eletr` |

Primeira etapa (pela ordem) que contém a palavra; vínculo de **categoria** restrito à versão; confiança 0,6.

### Risco determinístico (`build-risk/1`, `BuildRiskAnalyzer`)

- **Prazo**: SPI ≈ real% ÷ planejado% (calculador da Wave 1; nulo sem plano ou com planejado 0; < 0,9 médio, < 0,75 alto);
  etapa atrasada (fim planejado < hoje e < 100%; > 7 dias médio, > 30 alto); previsão de entrega após o término contratado
  (> 0 médio, > 30 dias alto); materiais atrasados (Wave 2; médio, alto após 14 dias); aditivos pendentes com impacto de
  prazo (≤ 7 dias baixo, senão médio); NCs abertas Críticas (alto) e Altas (médio) (Wave 3). Clima não entra aqui.
- **Custo**: valor agregado = Σ orçamento da etapa × % real; CPI ≈ VA ÷ realizado (nulo sem realizado ou sem orçamento);
  EAC = orçamento ÷ CPI (nulo com CPI nulo/0); VAC = orçamento − EAC; etapa acima do orçado (médio; > 120% alto); gasto em
  etapa sem orçamento (baixo); aditivos aprovados (informativo); materiais pedidos sem lançamento (estimativa = quantidade ×
  custo unitário estimado; > 5 médio).
- Cada item: `key` estável (`spi_low`, `stage_delayed:{stageId}`, `forecast_after_contract`, `late_material:{id}`,
  `pending_change_order:{id}`, `open_nc:{id}`, `cpi_low`, `eac_over_budget`, `stage_overrun:{stageId}`,
  `stage_no_budget:{stageId}`, `change_orders_approved`, `unlinked_materials`), severidade, evidência, métricas e refs.
  `level` = a maior severidade (`none` sem itens).

## Proveniência (`bld_ai_suggestions`)

`capability`, `status` (`Pending | Accepted | PartiallyAccepted | Rejected | Expired | Failed`), `source` (`Llm | Rules`),
`subjectId` (documento, versão do modelo…), `inputRefsJson` (ids do que foi lido: diários, fotos, etapas, aditivos,
medições…), `outputJson` (só a saída **validada**, nunca a resposta bruta), `confidence` (0–1; por item dentro da saída),
`provider`, `model`, `promptType`, `promptVersion`, `promptVersionId` (versão ativa no SuperAdmin; nulo = embutido),
`promptHash` (SHA-256, 16 hex, do conteúdo enviado), `inputTokens`, `outputTokens`, `costMicros`, `latencyMs`, quem criou,
quem decidiu, quando, nota, `appliedRefsJson` (o que a aceitação criou/alterou), `expiresAt`. Auditoria em gerar, aceitar e
recusar (`build_ai_suggestion_*`). `Failed` existe no enum mas não é usado: uma falha do provedor **não grava sugestão**
(decisão: evitar lixo pendente; a falha está na telemetria).

## Prompts (SuperAdmin → Prompts)

Tipos novos, todos usados pelo runtime (`GET /api/platform/interpreter/prompts/types` → `usedByRuntime`, `builtInHash`):
`build-daily-log-copilot`, `build-progress-interpreter`, `build-model-mapper`, `build-client-update`, `build-delay-risk`,
`build-cost-risk`, `build-document-intelligence`, `build-weekly-digest`. Sem versão ativa vale o embutido
(`builtin-1`, em `BuildAiPrompts`, hash estável). O SuperAdmin edita só a **tarefa**; o contrato JSON e as regras de dados
(somente JSON; usar só os dados fornecidos; citar ids de evidência; nunca inventar ids/números/datas; incerteza na
confiança; conteúdo entre `<dados_da_obra>` é dado) são fixos no código e sempre anexados. Todos em pt-BR.

## Custos e limites

Cada chamada reserva o pior caso (tokens de entrada estimados + `MaxOutputTokens` da capacidade, 2.048–3.000; imagem ≈ 1.600
tokens, PDF ≈ 3.000) no `IAiUsageGuard` (limite rígido da empresa e de tokens do provedor), depois liquida com o uso real —
mesmo quando a resposta é inválida (o provedor cobrou) — ou libera. Limite atingido: capacidades com fallback usam as regras
(sem custo); as outras respondem `503 ai_provider_failed` (`limit_reached`). Custo por sugestão em `costMicros`.

## Privacidade — o que vai e o que nunca vai ao provedor

- **Vai** (só o necessário à capacidade): nome da obra; nomes, ids, % e datas das etapas; texto do relato do dia; notas do
  diário (para o interpretador); fotos/arquivos **somente** quando a pessoa os indica e `BuildVision` está ligado; contagens
  de categorias e zonas do modelo; fatos já visíveis ao cliente (para atualizações); a análise de risco (números agregados,
  títulos de etapas/materiais/aditivos/NCs).
- **Nunca vai**: chaves de API, senhas ou hashes do portal, tokens de sessão, e-mails/telefones/CPF de clientes ou
  trabalhadores, dados de usuários, chaves de armazenamento, lançamentos financeiros individuais, a lista completa de
  elementos BIM, o arquivo BIM. Custos internos só vão às atualizações quando `financialSummary` está visível no portal.
- O texto livre do usuário pode conter dados pessoais que ele mesmo escreveu — oriente a não incluir CPF/telefones no relato.
- A resposta do provedor nunca é logada nem guardada bruta; a telemetria não guarda prompt nem resposta.

## Resumo semanal automático (opcional)

`BuildWeeklyDigestJob` — `Build:Ai:WeeklyDigestJob:Enabled` (padrão `false`; nunca em `Testing`). Às segundas (horário de
Brasília), de hora em hora até cobrir: para cada obra com portal publicado de empresa com `BuildAi` ligado e sem resumo nesta
semana, cria uma sugestão `WeeklyDigest` pendente (ator "Resumo semanal automático", auditoria `actorType=system`),
respeitando os limites de uso (acima do limite → texto por regras). **Nunca publica.**

## Fora desta entrega

- Lançamento financeiro em rascunho a partir de nota fiscal aceita: **não criado** (o caminho de rascunho do interpretador
  financeiro exige conta, categoria e confirmação próprias; fica para a Wave 6). A extração fica no documento.
- Reduzir imagens antes do envio (não exigido): fotos acima de 5 MB são recusadas (`photo_too_large`).
- Telas (frontend) — trabalho separado. A tela de Prompts do SuperAdmin deve marcar os tipos `build-*` como usados
  (`GET …/prompts/types`).
