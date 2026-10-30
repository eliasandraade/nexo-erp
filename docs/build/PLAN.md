# Orken Build — plano técnico das 6 Waves

Branch `feat/build-complete` (worktree `NexoERP-build`, a partir de `master` 6064c7a — depois de #87–#89). Módulo comercial `build`.

## Ponto de partida (auditoria 2026-10-30)

- Domínio: `BuildProject`, `BuildStage`, `BuildBudget`/`BuildBudgetItem`, `BuildDailyLog`/`BuildDailyLogPhoto` — todos `TenantEntity`,
  tabelas `bld_*`, uma migration (`20260507183533_AddBuildModule`).
- API `/api/v1/build/*` (`[Authorize(Roles=Management)]` + `[RequireModule("build")]`), serviços em `Nexo.Application/Modules/Build`
  com repositórios; query services em `Nexo.Infrastructure/Modules/Build`.
- Financeiro: `FinancialMovement` (`int_movements`) com `ContextType=Obra`, `ContextId=BuildProject.Id`; sem etapa.
- IA: só `RuleBasedAnalyzer` e `ClaudeAnalyzerStub` — **nenhuma chamada real a LLM**; telemetria em `interpreter_telemetry`.
- Clima: `IWeatherProvider` (Open-Meteo, current/history), flag `Integrations:Features:WeatherEnabled`; obra **sem coordenadas**.
- Storage: R2 privado, `StorageKeys`, `FileSignature` (magic bytes), URLs assinadas de 15 min.
- Frontend: `/build`, `/build/projetos/:id` (abas geral, etapas, orçamento, diário, financeiro); sem testes Vitest.
- Maturidade declarada: ~65% (`docs/audits/ORKEN_PRODUCT_STATUS_1_0.md`).

## Decisões de arquitetura

1. **Mesmo padrão do código novo do repo (Tax Intelligence):** entidades em `Nexo.Domain/Modules/Build`, DTOs em
   `Nexo.Application/Modules/Build`, serviços novos em `Nexo.Infrastructure/Modules/Build` usando `NexoDbContext` diretamente;
   controllers em `Nexo.Api/Controllers/Modules/Build` (internos) e `Nexo.Api/Controllers/Public` (portal).
2. **Migrations só aditivas**, uma por Wave. Nada é apagado nem renomeado.
3. **Livro financeiro único:** `FinancialMovement` ganha `StageId` e `BuildCostCategory` opcionais (nulos nos antigos);
   custo por etapa = movimentos confirmados de saída com `ContextType=Obra` agrupados por `StageId`.
4. **Progresso nunca digitado:** `BuildStage.Weight` (peso), progresso real vem de medições/atualizações de etapa;
   planejado vem de baseline (linear entre datas planejadas); previsão vem do motor determinístico. Geral = média ponderada.
   `BuildProgressSnapshot` (diário, imutável) guarda planejado/real/previsto por obra e por etapa → curvas e "como estava em X".
5. **Portal do cliente:** `BuildPortalCompany` (slug da empresa, por tenant) + `BuildProjectPortal` (slug da obra, visibilidade,
   senha BCrypt, versão de senha, branding, matriz de visibilidade). Sessão privada = token opaco de 32 bytes, guardado só como
   SHA-256, expiração fixa, revogada quando a senha muda. API pública em `/api/public/build/{company}/{project}/…`, DTOs públicos
   separados, allowlist explícita, 404 indistinguível para inexistente/não publicado, rate limit por IP e bloqueio por portal.
   URL pública sugerida ao Astra: `/{company}/{project}`; slugs validados contra a lista de rotas reservadas do app.
6. **Mídia pública:** arquivos continuam privados no R2; o portal só recebe URLs assinadas de curta duração, geradas na hora
   para itens que a construtora marcou como visíveis ao cliente. Arquivo BIM bruto nunca sai pelo portal.
7. **Digital Twin:** fonte geométrica (modelo) ≠ fonte operacional (etapas, diário, medições) ≠ interpretação (IA).
   `BuildModel` → `BuildModelVersion` (arquivo privado, formato, `GeometrySource`: NativeBim, Converted3D, AssistedMapping,
   SimplifiedRepresentation) → `BuildModelElement` (id externo, pai, zona, pavimento, categoria, material, bbox) e
   `BuildModelZone`; `BuildModelStageBinding` liga elemento/zona/categoria a etapa; `BuildElementProgressEvent` é o histórico
   (estado em uma data = último evento ≤ data). Processamento assíncrono por fila em tabela (`BuildModelProcessingJob`) e
   `BackgroundService`. IFC: hierarquia e elementos via **xBIM Essentials** (net8, sem nativo); GLB/GLTF: leitura do JSON do
   glTF; conversão IFC→GLB por **IfcConvert** (IfcOpenShell) opcional, só se configurado — sem ele o modelo fica com
   hierarquia e o estado continua consultável. RVT/DWG: recusados com mensagem legível (exigem serviço externo).
   PDF/imagem: mapeamento assistido, nunca "BIM preciso".
8. **IA — reaproveita o runtime de IA da plataforma (PRs #87–#89):** `AiProviderRegistry` (config do SuperAdmin, chave
   cifrada AES-GCM), `AnthropicClientFactory` (SDK oficial, URL fixa), `IAiUsageGuard` (limites com reserva sob lock),
   telemetria (`interpreter_telemetry` com modelo/tokens/custo) e prompts versionados (`stored_prompt_versions`, novos tipos
   `build-*`). Novo serviço genérico `IAiStructuredCompletion` (instruções do prompt ativo + JSON Schema + validação) usado
   pelas capacidades do Build. Toda sugestão (`BuildAiSuggestion`) guarda confiança, proveniência, provider/modelo, versão e
   hash do prompt, auditoria e exige confirmação humana quando altera estado. Sem provedor disponível: extração determinística
   (regras) e nada de texto gerado — a funcionalidade degrada, não quebra. Recursos caros por empresa: `BuildTenantSettings`
   (Build3D, BuildAI, BuildVision, BuildPredict) controlados pelo SuperAdmin.
9. **Previsão:** analytics determinísticos primeiro (SPI/CPI, produtividade, EAC, atraso projetado por etapa crítica),
   LLM só explica. Forecast guarda data, confiança, fatores, metodologia e versão.
10. **Feature flags por tenant** reaproveitando o mecanismo existente: `build.3d`, `build.ai`, `build.vision`, `build.predict`.

## Waves

1. **Base + portal:** coordenadas e modo de acompanhamento da obra; pesos/baseline/snapshots de progresso e curvas;
   `StageId` no movimento + custo por etapa; portal (empresa, obra, visibilidade, senha, sessão, branding) com API pública
   (bootstrap, timeline, progresso, fotos, financeiro permitido); contrato `docs/build/client-portal-contract.md`; telas internas.
2. **Comercial:** orçamento (templates, duplicar, por m², tipos material/mão de obra/serviço, validade, observações, versões,
   PDF com branding, conversão pela UI), aditivos (change orders), escolhas do cliente, materiais, medições.
3. **Qualidade e pós-obra:** checklists, inspeções, não conformidades; vistoria, entrega, garantias, assistência (cliente abre
   pelo portal).
4. **Digital Twin:** modelos, versões, processamento IFC/GLB, elementos, zonas, bindings, eventos, estado 4D, 5D, captura de
   realidade (foto, 360, vídeo, drone) e API do portal.
5. **IA:** cliente LLM real, copiloto do diário, interpretador de progresso, mapeador de modelo, atualização ao cliente, resumo
   semanal, inteligência de documentos, análise de atraso e custo.
6. **Previsões, clima, painel e SuperAdmin:** motor de previsão, clima por atividade, painel geral do Build, observabilidade e
   flags no SuperAdmin, Playwright, relatório final.

## Entrega

Um commit (ou poucos) por Wave na mesma branch; PR único em rascunho atualizado a cada Wave. Sem merge nem deploy sem
autorização (o merge na `master` publica em produção).

## Progresso

- **Wave 1 — concluída.** Migration `BuildWave1Portal` (aditiva). Obra com local/área/modo de acompanhamento; progresso ponderado
  (eventos, baseline/revisões, snapshot diário, curvas, "como estava em"); `StageId` no movimento + custo por etapa (orçamento de
  referência único: convertido, senão o último aprovado); portal (empresa, obra, visibilidade, senha BCrypt, sessão opaca, lockout,
  rate limit, 404 indistinguível), atualizações ao cliente, visibilidade do diário/fotos; telas internas (Progresso, Portal do
  cliente, custo por etapa, criação com escolha do acompanhamento). Contrato: `docs/build/client-portal-contract.md`.
- **Wave 2 — concluída (backend).** Migration `BuildWave2Commercial` (aditiva). Orçamento com tipos de item, validade
  (vencido não aprova), observações (cliente/interna), área e R$/m², versões (família, pai), duplicar, modelos (por m² ou
  fixo), PDF com marca (preço já com margem, sem custo/margem/nota interna), conversão cria etapas pelos nomes dos itens;
  aditivos (empresa ou cliente pelo portal com senha → contrato, fim da etapa, revisão do cronograma); escolhas do cliente
  (confirmação gera aditivo em rascunho); materiais (cotação, pedido, entregas parciais, atraso, vínculo a lançamento da
  obra); medições (aprovação move o progresso pelo mesmo caminho do avanço manual, origem `Measurement`). Portal v1.1:
  flags `changeOrders`, `selections`, `measurements`. Docs: `internal-api.md`, `client-portal-contract.md` (v1.1).
- **Wave 3 — concluída (backend).** Migration `BuildWave3Quality` (aditiva). Checklists (modelos com itens obrigatórios
  e que exigem foto), vistorias de qualidade com itens copiados do modelo e resultado calculado (obrigatório pendente
  bloqueia; foto exigida; `Passed`/`PassedWithRemarks`/`Failed`), cada item `NotOk` abre uma NC; NCs com gravidade,
  prazo, causa raiz, ação corretiva, resolução/verificação (mesmo usuário permitido e registrado), resumo de qualidade;
  **NC crítica aberta impede a etapa de chegar a 100%** no caminho único de progresso (manual e medição). Pós-obra:
  entrega (vistoria com o cliente → pendências viram NCs; entrega recusada com NC crítica; conclui a obra; aceite pelo
  portal ou interno, sem IP), garantias (conjunto de referência criado na entrega, editável — não é orientação jurídica),
  assistência técnica (abertura pela empresa ou pelo cliente, triagem calcula a cobertura pela garantia da categoria,
  agenda, resolve, avaliação do cliente). Portal v1.2: `phase`, `property` ("Meu Imóvel"), flag `postConstruction`,
  rotas de entrega/garantia/assistência com upload público de fotos (JPEG/PNG/WebP, 8 MB, magic bytes, 6 por chamado,
  privado). Uploads internos `build-quality`, `build-handover`, `build-assistance`. Docs: `internal-api.md` (# Wave 3),
  `client-portal-contract.md` (v1.2).
