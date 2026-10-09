# Auditoria 06 — Build, Interpreter, IA, Storage e Integrações externas

Commit auditado: `e8b4873`. Auditoria somente leitura. Nenhum arquivo do repositório foi alterado.

> **Achado crítico (🚨):** nenhum. Não encontrei acesso cross-tenant, bypass de autenticação, vazamento de dados entre tenants nem corrupção financeira.
>
> Todas as entidades de Build e Interpreter herdam de `TenantEntity`, que recebe o filtro global (`NexoDbContext.cs:168-208`). As chaves do storage levam o prefixo `tenants/{tenantId}/` (`StorageController.cs:98,135`).
>
> Os achados mais graves têm severidade **média**: autorização por papel ausente no backend, chaves de IA guardadas em base64 e ledger financeiro paralelo. Estão detalhados nas seções abaixo.

## Veredito rápido: o que é produto e o que não é

| Item | Classificação | Evidência |
|---|---|---|
| **Orken Build** | **Produto vivo, acessível na UI** para tenants com o módulo `build` e papéis de gestão. | Rota `/build` (`routes.ts:97`, `AppRouter.tsx:179-183`); `ModuleRoute moduleKey="build"`; `[RequireModule("build")]` em todos os controllers de Build. |
| **Interpreter** | **Motor interno, não é produto.** Não tem tela própria para o tenant. É usado apenas pelo diálogo "Registrar despesa" do Build (`BuildExpenseDialog.tsx`) e pelo cockpit da plataforma (`/platform/ai/*`). | Únicos consumidores: `modules/build/api/interpreter.api.ts` (analyze, confirm, list) e `modules/platform/services/interpreterAdminApi.ts`. |
| **IA / LLM** | **Não existe chamada real a LLM no sistema.** O "Claude" é um stub. Não há OCR. | Ver a seção IA. |
| **Cockpit "AI Operations"** (`/platform/ai`) | **Ferramenta interna da plataforma**, em parte cosmética. Os controles de provider e de prompt não afetam o runtime. | `InterpreterAdminController.cs`; `AnalyzerSelectorService.cs` lê só a configuração. |

---

## Orken Build (gestão de obras)

**Objetivo:** gerir obras com:
- projeto e ciclo de vida;
- etapas com % de avanço;
- orçamentos com itens e margem;
- diário de obra com fotos e clima;
- despesas realizadas comparadas ao orçamento (previsto × realizado).

**Estado:** 🟡 **FUNCIONAL / INCOMPLETO**   **Maturidade:** 55%
- O fluxo principal fecha ponta a ponta (UI → API → service → EF → migration `20260507183533_AddBuildModule`).
- Faltam relatórios, custo por etapa, estorno de despesa na UI, autorização por papel no backend e paginação.
- Há bugs visíveis (contadores da listagem zerados).
- A cobertura de testes é mínima: 4 testes de integração e nenhum teste unitário de Build.

### Backend existente
- **Controllers** em `Nexo.Api/Controllers/Modules/Build/`, todos com `[Authorize]` + `[RequireModule("build")]`:
  - `BuildProjectsController`:
    - `GET/POST /api/v1/build/projects`
    - `GET /{id}` e `GET /{id}/details`
    - `PUT /{id}`
    - `POST /{id}/start|pause|complete|cancel`
    - `GET /{id}/financial-summary`
  - `BuildStagesController`:
    - `GET/POST /api/v1/build/projects/{projectId}/stages`
    - `PUT /stages/{id}/progress`
    - `PUT /projects/{id}/stages/reorder`
    - `DELETE /stages/{id}`
  - `BuildBudgetsController`:
    - `GET/POST /api/v1/build/budgets`
    - `GET /budgets/{id}`
    - `POST /budgets/{id}/send|approve|reject|convert`
    - `PUT /budgets/{id}/margin`
    - `POST /budgets/{id}/items`
    - `PUT/DELETE /budget-items/{id}`
  - `BuildDailyLogsController`:
    - `GET/POST /api/v1/build/projects/{id}/daily-logs`
    - `GET/PUT /daily-logs/{id}`
    - `POST /daily-logs/{id}/photos`
    - `DELETE /daily-log-photos/{photoId}`
  - `BuildDashboardController`: `GET /api/v1/build/dashboard`.
- **Services** em `Nexo.Application/Modules/Build/`: `BuildProjectService`, `BuildStageService`, `BuildBudgetService`, `BuildDailyLogService` e `BuildFinancialSummaryService`.
- **Read models** em `Nexo.Infrastructure/Modules/Build/`: `BuildFinancialQueryService` e `BuildDashboardQueryService`.
- **Domínio** em `Nexo.Domain/Modules/Build/`, com máquinas de estado:
  - `BuildProject`, `BuildStage`, `BuildBudget`, `BuildBudgetItem`, `BuildDailyLog`, `BuildDailyLogPhoto`.
- **EF:** configurations em `Persistence/Configurations/Modules/Build/`. Há índice único `(tenant, project, date)` para o diário (`BuildDailyLogConfiguration.cs:33-34`).

### Frontend existente
- `nexo-main/src/modules/build/`:
  - `BuildProjectsPage.tsx` (428 linhas): lista, criação e `BuildDashboardSection`.
  - `BuildProjectDetailPage.tsx` (1473 linhas, monolítico), com as abas Geral, Etapas, Orçamento, Diário e Financeiro.
  - `BuildExpenseDialog.tsx`: despesa via Interpreter.
  - `EditProjectDialog.tsx`.
  - Hooks em `use-build.ts` e `use-interpreter.ts`.
- **Rotas:** `/build` e `/build/projetos/:id`, restritas a papéis MGMT (`routes.ts:97`).

### Funcionalidades concluídas
- **CRUD de projeto e ciclo de vida** Planning → InProgress ⇄ Paused → Completed/Cancelled.
  - Backend: `BuildProjectsController` + domínio `BuildProject`.
  - UI: botões start/pause/complete/cancel em `BuildProjectDetailPage.tsx:195-232`.
  - Teste: `BuildFlowTests.ProjectLifecycle_Start_TransitionsToInProgress`.
- **Etapas:** criar, atualizar progresso (100% conclui automaticamente) e remover. UI na aba Etapas (`BuildProjectDetailPage.tsx:248-410`).
- **Orçamento por projeto:**
  - criar e adicionar/editar/remover itens;
  - total recalculado no servidor;
  - margem e preço final;
  - enviar/aprovar/rejeitar.
  - Aprovar um orçamento vinculado grava `project.BudgetApproved` (`BuildBudgetService.ApproveBudgetAsync`, linhas 390-412). Teste: `ApprovingProjectLinkedBudget_Sets_ProjectBudgetApproved`.
- **Diário de obra:**
  - um registro por data, com validação na aplicação e índice único (teste `DailyLog_DuplicateDate_IsRejected`);
  - notas;
  - clima em texto livre;
  - fotos via R2 (`uploadFile(file,"build-daily-log")` → `POST /daily-logs/{id}/photos` com `storageKey`). A URL é resolvida na leitura (`BuildDailyLogService.MapPhotoToDto` → `IStoragePublicUrlResolver`).
- **Despesas da obra:**
  - Fluxo: `BuildExpenseDialog` envia texto → `POST /api/v1/interpreter/analyze` (gera Draft) → revisão → `POST /api/v1/movements/{id}/confirm` com `contextType=Obra`, `contextId=projectId` e fornecedor opcional.
  - Listagem na aba Financeiro: `GET /api/v1/movements?contextType=Obra&contextId=…`.
- **Resumo financeiro e dashboard com dados reais:**
  - Fonte: soma de `FinancialMovement` Confirmed/Out/Obra (`BuildFinancialQueryService.cs:80-92`, `BuildDashboardQueryService.cs:159-180`).
  - Teste: `FinancialSummary_Exposes_CamelCaseBudgetFields`.
- **Busca de clima no diário** (geolocalização ou lat/lon manual, `BuildProjectDetailPage.tsx:802-860`), com estado honesto quando o recurso está desabilitado.

### Funcionalidades parciais
- **Conversão de orçamento → projeto:** o backend existe (`POST /budgets/{id}/convert`), mas `useConvertBudget` não é usado em nenhuma tela. Na prática a aprovação já cumpre esse papel.
- **Reordenar etapas:** o backend existe (`PUT …/stages/reorder`), mas `useReorderStages` não é usado. Não há setas nem drag. A spec (`docs/superpowers/specs/2026-06-16-orken-build-completion.md:93`) diz que "move up/down cobre a necessidade", porém não está implementado.
- **Editar diário:** `PUT /daily-logs/{id}` existe, mas `useUpdateDailyLog` não é usado. O diário só pode ser criado, sem edição.
- **Despesas:**
  - `POST /api/v1/movements/{id}/void` e `/reprocess` existem, mas nenhum consumidor no frontend os chama. **Uma despesa lançada errada não pode ser estornada pela UI** e continua somando no realizado.
  - Drafts abandonados (diálogo fechado após "Analisar") ficam órfãos no banco (`BuildExpenseDialog.tsx:91-97` não chama void).
- **Orçamento "pré-venda"** (sem projeto): suportado no backend, sem tela.
- **Clima:** `WeatherEnabled=false` por padrão (`appsettings.json`). O snapshot é só texto (`WeatherSummary` com 200 caracteres); não há dados estruturados persistidos como o plano pedia.

### Funcionalidades não desenvolvidas
- **Relatórios** (previsto × realizado, por categoria, por fornecedor). Constam como backlog P2.1 na spec.
- **Custo realizado por etapa:** `FinancialMovement` não tem `StageId` e `BuildStage` não tem custo.
- **Vínculo do cliente da obra com `Customer`:** `ClientName` é texto livre (`BuildProjectConfiguration.cs:18`).
- **PDF de orçamento e envio ao cliente:** "send" apenas muda o status.
- **Análise de foto ou diário por IA:** não existe em lugar algum. O antigo "analyze 500" citado na memória era o endpoint de texto do Interpreter, não análise de imagem.
- **Limpeza do objeto R2** ao remover foto (backlog P3.5).

### Problemas
- **Bug de UI — contadores da listagem sempre zerados:**
  - `BuildProjectRepository.GetAllAsync` (linhas 28-44) não faz `Include` de `Stages`/`DailyLogs`, e não há lazy loading.
  - Com isso, `MapToDto` devolve `StageCount=0`, `CompletedStageCount=0` e `LogCount=0`.
  - Os cards em `BuildProjectsPage.tsx:69-140` mostram "0/0 etapas" e progresso 0% sempre.
  - O mesmo acontece em start/pause/update, que usam `GetByIdAsync` sem include.
- **Paginação quebrada:**
  - `BuildDailyLogService.GetByProjectAsync` (linha 65) e `BuildBudgetService.GetAllAsync` (linha 59) devolvem `TotalCount = dtos.Count`, ou seja, o tamanho da página e não o total real.
  - A UI não tem controles de paginação: o diário mostra só os 20 primeiros, e as despesas só as 30 primeiras (`interpreter.api.ts:120`).
- **N+1:** `GetByProjectAsync` do diário faz uma consulta de fotos por log (`BuildDailyLogService.cs:59-63`).
- **Validators mortos:**
  - `BuildValidators.cs` (6 validators) e `Validators/Interpreter/*` (3) são registrados (`AddValidatorsFromAssembly`), mas nenhum controller ou service injeta `IValidator<…>` deles, e não existe auto-validação.
  - Fica valendo só o guard do domínio. Exemplo: `ConfirmMovement` aceita `amount = 0` e data no futuro via API, porque `FinancialMovement.CreateDraft`/`UpdateFields` só barram valores negativos.
- **Autorização:**
  - O frontend restringe a papéis MGMT, mas o backend aceita qualquer usuário autenticado do tenant com o módulo (nenhum `[Authorize(Roles=…)]` em Build).
  - Um Vendedor pode, via API, aprovar orçamento, alterar `BudgetApproved` e cancelar obra.
- **Arquitetura financeira — ledger paralelo:**
  - Os comentários dizem "Core (ContextType=Obra)", mas `FinancialMovement` é a tabela do Interpreter, separada do financeiro global (`FinancialTransaction`, `Nexo.Domain/Entities/FinancialTransaction.cs`).
  - `IntMovements` só é lido por Build e pelo cockpit de IA (grep). **Despesas de obra não aparecem no Financeiro, DRE ou caixa do tenant.**
- **Aprovação de orçamento sobrescreve:** aprovar um segundo orçamento do mesmo projeto substitui `BudgetApproved` sem aviso.
- **Concorrência no diário:** uma corrida na criação do mesmo dia passa pelo `ExistsForDateAsync` e estoura o índice único, o que provavelmente vira 500.
- **Página de detalhe monolítica** com 1473 linhas.
- **Seed do módulo:** não há migration que crie o `ModuleDefinition "build"`; ele só vem do `DataSeeder.SeedBuildModuleAsync`, que roda apenas fora de produção (`Program.cs:305-313`). A existência em produção depende de dados históricos e não é verificável em modo somente leitura.
- **Testes:**
  - 4 testes de integração em `Nexo.IntegrationTests/Build/BuildFlowTests.cs`;
  - nenhum teste unitário de domínio ou service de Build;
  - nenhum teste de frontend.

### Dependências
- Interpreter: analyze/confirm/list e o ledger `FinancialMovement`.
- Storage R2: fotos.
- Weather (Open-Meteo).
- Suppliers: fornecedor na despesa.
- Gate de módulo `build`.
- Stripe: price IDs `build_monthly`/`build_annual` em `appsettings.json`.

### Próximos passos (ordem recomendada)
1. Adicionar `[Authorize(Roles = ManagerRoles)]` nos controllers de Build e no confirm/void de movements.
2. Corrigir os contadores da listagem (projeção com `Count` em SQL ou `Include`) e o `TotalCount` paginado; adicionar paginação na UI.
3. Expor estorno (void) de despesa na aba Financeiro e descartar o Draft ao fechar o diálogo.
4. Ligar os validators (filtro de auto-validação) ou removê-los.
5. Decidir a arquitetura financeira: espelhar despesas de obra em `FinancialTransaction` ou documentar o ledger separado.
6. Relatórios (P2.1), edição do diário, reordenação de etapas e custo por etapa.
7. Testes unitários do domínio Build e mais testes de integração (budget items, fotos, financial summary com movimentos).

---

## Interpreter (Operational Interpretation Engine)

**Objetivo:** transformar texto, documento ou áudio em movimento financeiro:
- extração (valor, data, favorecido, conta);
- interpretação (direção, natureza, categoria, contexto);
- Draft → confirmação humana;
- correções do usuário usadas como dataset;
- memória por usuário;
- telemetria de custo.

**Estado:** 🟠 **PARCIAL**   **Maturidade:** 35%
- Funciona: o caminho texto → regex → Draft → confirmação, com auditoria e correções.
- É stub ou inexistente:
  - LLM (stub);
  - OCR (inexistente);
  - anexos (gravados em disco local e nunca vinculados ao movimento);
  - memória (no-op);
  - interpretação de categoria/contexto (sempre nula);
  - reprocess, void e stopwords sem UI.

### Backend existente
- **Controllers:**
  - `MovementsController`:
    - `POST /api/v1/interpreter/analyze`
    - `POST /api/v1/movements/{id}/confirm|reprocess|void`
    - `GET /api/v1/movements`
    - `GET /api/v1/movements/{id}`
  - `AttachmentsController`: `POST /api/v1/interpreter/attachments`.
  - `TenantInterpreterController`:
    - `GET/POST/DELETE /api/v1/tenants/stopwords`
    - `GET /api/v1/tenants/memory-profile`
    - `POST /api/v1/tenants/memory-profile/rebuild`
  - Todos têm só `[Authorize]`: **sem `RequireModule` e sem restrição de papel**.
- **Use cases:** `AnalyzeMovementUseCase`, `ConfirmMovementUseCase`, `ReprocessMovementUseCase`, `VoidMovementUseCase` e `RebuildMovementMemoryProfileUseCase`.
- **Infra** em `Nexo.Infrastructure/Modules/Interpreter/`:
  - `RuleBasedAnalyzer`: regex PT-BR de valor, data, favorecido e conta, com confiança por campo.
  - `ClaudeAnalyzerStub`: devolve `Unknown` em todos os campos, prompt `"0.0.0-stub"`.
  - `RuleBasedInterpretationService`: sempre Out/Expense; categoria, contexto e conta nulos.
  - `MovementMemoryServiceImpl`: no-op que grava `"{}"`.
  - `LocalAttachmentStorage`: disco local em `wwwroot/attachments`.
  - `DescriptionNormalizer`.
  - `TelemetryWriterService`: singleton com `CreateScope`, sem problemas.
  - `AnalyzerSelectorService`.
- **Migrations:** `20260507044426_AddInterpreterModule`, `20260507181414_AddInterpreterAdminEntities` e `20260616183140_AddSupplierToFinancialMovement`.

### Frontend existente
- O tenant não tem tela própria. O uso se limita a `BuildExpenseDialog.tsx`, que chama analyze e confirm, e à aba Financeiro do Build, que chama list.
- Nenhuma tela usa `attachments`, `reprocess`, `void`, `GET /movements/{id}`, stopwords ou memory-profile.

### Funcionalidades concluídas
- **Analyze de texto:** cria `FinancialMovement` Draft + `ExtractionResult` + `InterpretationSuggestion` numa transação (`AnalyzeMovementUseCase.cs:150-157`).
  - Testes: `AnalyzeMovementUseCaseTests` (10), `RuleBasedAnalyzerTests` (18) e o de integração `InterpreterAnalyzeTests.Analyze_BuildExpenseText_DoesNotReturn500`.
- **Confirm:** Draft → Confirmed, com diff contra a sugestão gerando `UserCorrection` e `MovementAuditLog` com estado anterior/novo, numa transação (`ConfirmMovementUseCase.cs:108-118`). Testes: 10 unitários.
- **Listagem por contexto** com paginação limitada a 100 (`MovementsController.cs:229-280`).

### Funcionalidades parciais
- **Anexos:**
  - O upload valida magic bytes e sanitiza o nome (`AttachmentsController.cs:68-80`), o que é bom.
  - Porém grava no disco local do container (`DependencyInjection.cs:210-212`), que é efêmero na Railway.
  - A URL `/attachments/...` não é servida: não há `UseStaticFiles` em `Program.cs`.
  - O anexo nunca é vinculado ao movimento (`MovementId = Guid.Empty`, `MovementAttachment.cs:45,63`), então o detalhe sempre retorna a lista de anexos vazia.
  - Sem LLM e sem OCR, analisar um anexo devolve todos os campos como `RequiresInput`.
- **Reprocess e void:** backend completo (10 testes de reprocess) e nenhum consumidor.
- **Stopwords e memory profile:** endpoints sem consumidor. O rebuild é no-op.

### Funcionalidades não desenvolvidas
- OCR, de qualquer tipo.
- Analyzer com LLM real (Claude ou OpenAI).
- Classificação de categoria, contexto e conta.
- Uso das correções como aprendizado.
- Entrada por áudio, XML, e-mail ou webhook (o enum `InputSourceType` existe, nada mais).

### Problemas
- **Fire-and-forget sobre DbContext scoped:** `_ = _memoryService.RebuildProfileAsync(tenantId, userId)` (`ConfirmMovementUseCase.cs:128`) roda depois da resposta, usando o repositório scoped do request. Pode gerar `ObjectDisposedException` ou concorrência no contexto, com exceção não observada. É inofensivo hoje só porque o rebuild é no-op.
- **`EnableOpenAIAnalyzer=true` derruba o analyze:** não existe `IDocumentAnalyzer` OpenAI registrado (`DependencyInjection.cs:195-196`), e `Require(...)` lança `InvalidOperationException` (`AnalyzerSelectorService.cs:90-93`), resultando em 500.
- **`EnableClaudeAnalyzer=true`** troca a extração de arquivos pelo stub. O texto continua indo para RuleBased.
- **Sem gate de módulo:** qualquer tenant autenticado, mesmo sem Build, cria e confirma movimentos com `ContextType=Obra`. `ContextId` e `SupplierId` não são validados contra entidades existentes.
- **Validators `Analyze`, `Confirm` e `Reprocess` não são executados** (ver Build).

### Dependências
`FinancialMovement` funciona como ledger de Build. Telemetria e cockpit da plataforma. Storage local (anexos).

### Próximos passos
1. Gate de módulo/papel e validação de `ContextId` (projeto do tenant).
2. Remover o fire-and-forget ou despachar o rebuild via job com escopo próprio.
3. Migrar anexos para R2 (`IStorageProvider`) e vincular o anexo ao movimento.
4. Só então: analyzer LLM real com teto de custo (ver IA).
5. Expor void na UI do Build.

---

## IA (LLM, OCR e "inteligência")

**Objetivo declarado** (`nexo-main/CLAUDE.md`, Design Context): "texto curto → OCR → interpretação → sugestão → confirmação".

**Estado:** ⚪ **SCAFFOLD**   **Maturidade:** 5% — **não existe nenhuma chamada real a LLM nem OCR no código.**

### Evidência
- Nenhum pacote de IA nos `.csproj`. Os únicos SDKs externos são `AWSSDK.S3`, `Stripe.net` e `QuestPDF`.
- `grep` por `anthropic|openai|/v1/messages|chat/completions|x-api-key|gemini|tesseract|ocr` em `nexo-backend/src` e `nexo-main/src` só encontra comentários, enums, o seed e o stub.
- Não há chave de LLM em `appsettings*.json`. As flags `Interpreter:Features:EnableClaudeAnalyzer`/`EnableOpenAIAnalyzer` valem `false` por padrão (`InterpreterFeatureFlags.cs:17-18`).

### Separação pedida

| Categoria | Itens |
|---|---|
| **Ideia / documentação** | `nexo-main/CLAUDE.md` (fluxo com OCR); comentários "Claude (primary LLM) → OpenAI (fallback)" em `IAnalyzerSelector.cs:8`; seed de providers "Claude 3 Haiku" e "GPT-4o Mini" com custos (`DataSeeder.cs:356-386`); seed de `StoredPromptVersions` (`DataSeeder.cs:388`). |
| **UI "falsa" ou sem efeito** | `/platform/ai/providers`: o toggle `isEnabled` e o "default" fazem PATCH no banco (`AiProvidersPage.tsx:142`), mas o runtime ignora o banco (o seletor lê só a configuração). `/platform/ai/prompts`: "ativar" a versão do prompt não muda nada, porque nenhum analyzer lê `StoredPromptVersions`. `/platform/ai/costs`: a própria página diz que são "projeções para quando Claude/OpenAI forem ativados" (`AiCostsPage.tsx:91`). Playground com opções "Claude/OpenAI": Claude devolve o stub; OpenAI com a flag desligada lança erro. |
| **Placeholders no backend** | `ClaudeAnalyzerStub`; `MovementMemoryServiceImpl` no-op; `RuleBasedInterpretationService` com campos nulos; `rotate-key` com "TODO: replace with real AES-256" (`InterpreterAdminController.cs:388`); `TenantAiLimit` e `MonthlyTokenLimit` gravados e exibidos, mas **sem enforcement** (só lidos no endpoint de custos). |
| **Integrações reais funcionando** | **Nenhuma com LLM.** O que é "inteligente" e funciona é **determinístico**: `RuleBasedAnalyzer` (regex), `DescriptionNormalizer`, insights do dashboard e cards do Financeiro do restaurante (regras no cliente: `RecentInsights.tsx`, `restaurante/pages/FinanceiroPage.tsx:561+`). Isso **não deve ser contado como IA**. O botão "Analisar" com ícone `Sparkles` no Build (`BuildExpenseDialog.tsx:164,213`) aciona apenas regex. |

### Chaves e custos
- **Chaves:** `POST /api/platform/interpreter/providers/{id}/rotate-key` grava a chave como **Base64 do texto puro** no campo `ApiKeyEncrypted` (`InterpreterAdminController.cs:388-390`). Hoje não é usada por nada.
- **Controle de custo:** inexistente em runtime. Os custos da telemetria são sempre 0, porque o RuleBased não tem tokens.
- **Fallback:** o seletor rebaixa para RuleBased quando as flags de LLM estão desligadas (`AnalyzerSelectorService.cs:78-79`). Se a flag OpenAI for ligada, a aplicação falha (ver Interpreter).

### Próximos passos
Antes de qualquer LLM real:
- criptografia real das chaves (ou secret do ambiente, sem banco);
- enforcement de `TenantAiLimit`;
- seletor lendo `AiProviders.IsEnabled`;
- timeout, retry e fallback para RuleBased em caso de falha;
- telemetria com custo real.

Só depois implementar o analyzer Claude (texto) e, separadamente, OCR/visão para anexos.

---

## Cockpit AI Operations (plataforma)

**Estado:** 🟠 **PARCIAL**   **Maturidade:** 35%

### Backend
- `InterpreterAdminController` (`[Authorize(Policy="Platform")]`). Endpoints em `/api/platform/interpreter/`:
  - `GET dashboard`, `GET telemetry`, `GET costs`;
  - `GET/PATCH providers`, `POST providers/{id}/rotate-key`;
  - `GET prompts`, `POST prompts/{id}/activate`;
  - `POST playground`.
- Lê dados reais de `InterpreterTelemetry` e `IntMovements`/`IntUserCorrections` com `IgnoreQueryFilters`, o que é aceitável para a plataforma.

### Frontend
- `modules/platform/pages/ai/*`: 6 páginas, rotas em `AppRouter.tsx:317-322`, navegação em `PlatformLayout.tsx:108-113`.

### Avaliação
- **Funciona:** dashboard, telemetria e playground rule-based, com dados reais.
- **Não funciona de verdade:** providers, prompts e custos não têm efeito em runtime.
- **Dados em produção:** `AiProviders` e `StoredPromptVersions` só existem via seed fora de produção, então em produção essas telas provavelmente aparecem vazias (não verificável em modo somente leitura).
- **Playground:** sem `tenantId`, usa o "primeiro tenant por nome" (`InterpreterAdminController.cs:482-486`). Não há impacto de dados, porque não persiste.

---

## Storage (Cloudflare R2)

**Objetivo:** upload de imagens por contexto (produto, logo/capa de restaurante, diário de obra, prontuário do Service, logo/capa do portal Service).

**Estado:** 🟡 **FUNCIONAL / INCOMPLETO**   **Maturidade:** 65%
- O upload real funciona quando configurado.
- Faltam:
  - validação de conteúdo (magic bytes);
  - objetos privados;
  - limpeza de órfãos;
  - gate por módulo.

### Backend
- **`StorageController`:**
  - `POST /api/integrations/storage/upload`, com contextos em `ContextPaths` (`StorageController.cs:17-27`);
  - `DELETE /api/integrations/storage/{*key}`, com checagem do prefixo `tenants/{tenantId}/` (linhas 135-137).
- **`CloudflareR2Provider`:**
  - singleton com **client lazy** (`Lazy<AmazonS3Client>`, linhas 22-31), o que corrige o antigo 500 no construtor;
  - `PutObject` com `DisablePayloadSigning`;
  - URL pública = `R2.PublicUrl/key`.
- **`StoragePublicUrlResolver`:** monta a URL na leitura e retorna `null` se não houver base configurada.
- **Flag:** `Integrations:Features:StorageEnabled` (padrão `false`). Desligada, a API responde 404 controlado (teste de integração `StorageUploadTests.Upload_BuildDailyLog_WhenStorageDisabled_Returns404_NotServerError`).
- **Validação:**
  - content-type do cliente checado contra a whitelist `image/jpeg|png|webp`;
  - tamanho de 10 MB (`RequestSizeLimit` de 20 MB);
  - arquivo vazio rejeitado;
  - nome original descartado (a chave é GUID).

### Frontend
- `services/storage.api.ts` (`uploadFile`/`deleteFile`) e `components/shared/ImageUploadButton.tsx`.
- Usado em Produtos, Build (diário) e logo/capa do portal Service.

### Problemas
- **Sem verificação de magic bytes:** confia no `Content-Type` declarado. Comparar com `AttachmentsController`, que faz a verificação.
- **Tudo é público:**
  - URL pública sem autenticação para qualquer contexto.
  - Inclui `service-record`, que são anexos de prontuário (clínica, estética, pet) via `SvcRecordEntryService.cs:124`.
  - A chave é um GUID, portanto não enumerável, mas um link vazado expõe o conteúdo indefinidamente.
  - Não há URL assinada.
- **Sem gate por módulo:** qualquer usuário autenticado sobe arquivo em qualquer contexto (por exemplo, `build-daily-log` sem o módulo Build).
- **Órfãos:**
  - remover a foto do diário (`RemovePhotoAsync`) não apaga o objeto;
  - `deleteFile` não é usado em nenhuma tela;
  - se fosse usado, `encodeURIComponent(key)` transformaria `/` em `%2F`, e o check de prefixo provavelmente retornaria 403 (não testado).
- **Contexto `service-record` sem consumidor:** existe no backend, mas não está no tipo `StorageContext` do frontend nem é usado por nenhuma tela.
- **`AddPhoto` (Build) aceita qualquer `storageKey`** do cliente, sem validar o prefixo do tenant. Hoje o impacto é baixo, porque a URL resultante é pública de qualquer forma.
- **`PingAsync` usa `ListBuckets`:** em R2, tokens com escopo de bucket costumam não ter essa permissão. Não é usado em health check, então o impacto é baixo.
- **Anexos do Interpreter** ficam em outro storage (disco local, ver acima). Há dois mecanismos de storage.

---

## Integrações externas (Weather, Barcode, Lookup, E-mail, Webhooks)

| Integração | Estado | % | Evidência e observações |
|---|---|---|---|
| **CEP/CNPJ** (BrasilAPI + ViaCEP composite) | 🟡 Funcional | 75 | `LookupController` `GET /api/integrations/cep/{cep}` e `/cnpj/{cnpj}`; resiliência Polly (timeout, retry, circuit breaker) em `Integrations/DependencyInjection.cs`; consumido em Clientes e Fornecedores (`integrations.api.ts`). **As flags `BrasilApiEnabled`/`ViaCepEnabled` são ignoradas**: nenhum uso fora da classe de flags. Sem rate limit por tenant. O portal público faz `fetch` direto do ViaCEP no cliente (`portal/components/CartSheet.tsx:99`). |
| **Barcode** (Open Food Facts) | 🟡 Funcional, desligado | 60 | `BarcodeController` `GET /api/integrations/barcode/{barcode}`, flag `OpenFoodFactsEnabled=false` por padrão; usado em `ProductMainDataSection`. 8 testes unitários. |
| **Clima** (Open-Meteo) | 🟡 Funcional, desligado | 55 | `WeatherController` `GET /api/integrations/weather/current|history`, com cache (`OpenMeteoProvider.cs:61,90`) e resiliência; flag `WeatherEnabled=false`. **Licença:** Open-Meteo gratuito é não-comercial; `UseCustomerApi`/`ApiKey` existem nas options, mas o provider **não usa ApiKey** (grep). O próprio plano exige resolver "BK-WEATHER-1" antes de ativar (`ORKEN_INTEGRATIONS_PLAN.md:589`). Sem gate de módulo Build. |
| **E-mail** (Resend) | 🟡 Funcional e restrito | 60 | `ResendEmailService` só implementa `SendVerificationEmailAsync` (registro e reenvio, `RegistrationService.cs:131,241`). Com a chave vazia, cai em `ConsoleEmailService` (`DependencyInjection.cs:231-241`). Falhas são logadas e engolidas. Não há reset de senha, convite nem notificações por e-mail. Detalhe de DI: o typed client está registrado, mas `IEmailService` é resolvido via `AddScoped<IEmailService, ResendEmailService>` (pede um `HttpClient` cru); vale confirmar em runtime que o `HttpClient` injetado é o do factory. |
| **PDF** (QuestPDF) | 🟡 | — | Usado em Caixa, Vendas e Produtos. A flag `PdfEnabled` é ignorada. Fora do foco desta auditoria. Não há PDF de orçamento de obra. |
| **Webhooks** | — | — | Só o Stripe: `POST /api/billing/webhook` (`BillingController.cs:170`) + `StripeWebhookService`. Coberto pela auditoria de billing. Não há webhooks de entrada para Interpreter (`InputSourceType.Webhook` sem implementação) nem webhooks de saída. |
| **Mercado Pago, WhatsApp, iFood, Fiscal (NFe), ReceitaWS, HG Weather** | ⚪ Não desenvolvido | 0 | Só existem flags (`MercadoPagoEnabled`, `WhatsAppEnabled`, `FiscalEnabled` em `IntegrationFeatureFlags.cs`) ou nada. Os `grep` por `whatsapp` encontram apenas campos de telefone de cliente e portal. |

### Comparação com `docs/ORKEN_INTEGRATIONS_PLAN.md`
O "✅" da matriz (linhas 363-377) significa **"selecionado no plano"**, não "implementado".

| Fase | Situação real |
|---|---|
| 1 — Infraestrutura (HttpClientFactory, Polly, flags, logging) | ✅ Feito. |
| 2 — BrasilAPI e ViaCEP | ✅ Feito; o fallback ReceitaWS não foi feito. |
| 3 — R2 | ✅ Upload feito; orphan cleanup e URLs privadas pendentes. |
| 4 — Open Food Facts | ✅ Feito. |
| 5 — QuestPDF | ✅ Feito parcialmente (sem Build e sem Restaurante). |
| 6 — Open-Meteo | ✅ Código feito; desligado e com pendência de licença; snapshot estruturado não persistido; fallback HG não feito. |
| 7 — Stripe | Feito (outra auditoria). |
| 7 — Mercado Pago | ❌ |
| 8 — WhatsApp | ❌ |
| 9 — iFood | ❌ |
| 10 — Fiscal | ❌ |

---

## Backend sem frontend
- `POST /api/v1/movements/{id}/void` e `/reprocess`, e `GET /api/v1/movements/{id}` (detalhe completo).
- `POST /api/v1/interpreter/attachments` (upload de comprovante).
- `GET/POST/DELETE /api/v1/tenants/stopwords`, `GET /api/v1/tenants/memory-profile` e `POST …/rebuild`.
- `POST /api/v1/build/budgets/{id}/convert`, `PUT /api/v1/build/projects/{id}/stages/reorder` e `PUT /api/v1/build/daily-logs/{id}`.
- `GET /api/v1/build/budgets` sem `projectId` (orçamentos pré-venda).
- Contexto de storage `service-record` (fora do tipo `StorageContext` do frontend).
- `DELETE /api/integrations/storage/{*key}` (`deleteFile` existe, mas não é chamado).

## Frontend sem backend/fluxo real
- `/platform/ai/providers`: os toggles enable/default não afetam o runtime; o rotate-key guarda uma chave que nunca é usada.
- `/platform/ai/prompts`: "ativar versão" sem efeito.
- `/platform/ai/costs`: projeções de custo para LLM inexistente.
- `/platform/ai/playground`: opções "Claude/OpenAI" (stub e erro, respectivamente).
- Cards de projeto em `/build`: contagem de etapas, progresso e registros sempre 0 (bug de `Include`).
- Ícone `Sparkles`/"Interpreter extrai automaticamente" no Build: é regex, não IA. O texto do diálogo não chega a ser falso, mas sugere mais do que entrega.

## Código legado/morto
- `ClaudeAnalyzerStub`, `MovementMemoryServiceImpl` (no-op), `RuleBasedInterpretationService` (constantes).
- `BuildValidators.cs` (6 classes) e `Validators/Interpreter/*` (3 classes): registrados e nunca executados.
- Flags `BrasilApiEnabled`, `ViaCepEnabled`, `PdfEnabled`, `MercadoPagoEnabled`, `WhatsAppEnabled` e `FiscalEnabled`: lidas e nunca consultadas.
- `AiProvider`, `TenantAiLimit` e `StoredPromptVersion`: persistidos e exibidos, sem uso em runtime.
- `LocalAttachmentStorage.GetPresignedUrlAsync`: sem chamadores; retornaria caminho não servido.
- Hooks não usados: `useConvertBudget`, `useReorderStages`, `useUpdateDailyLog`, `useDailyLog`.
- `BuildDailyLogService.MapToDtoNoPhotos`.

## Achados de segurança

| # | Severidade | Achado | Cenário |
|---|---|---|---|
| S1 | **Média** | Build e Interpreter sem autorização por papel no backend (`[Authorize]` apenas). Interpreter também sem `RequireModule`. | Um usuário "Vendedor" chama direto `POST /api/v1/build/budgets/{id}/approve`, `POST /api/v1/build/projects/{id}/cancel` ou `POST /api/v1/movements/{id}/confirm`, alterando orçamento aprovado e despesas, mesmo que a UI esconda `/build`. Qualquer tenant sem o módulo Build pode gravar movimentos. |
| S2 | **Média** | Chave de provider de IA "criptografada" com Base64 (`InterpreterAdminController.cs:388-390`). | Quem tiver leitura do banco ou de um backup obtém a chave Anthropic/OpenAI em texto puro. Hoje nenhuma chave é usada, mas a tela convida a cadastrar uma. |
| S3 | **Média** | Todos os objetos do R2 são públicos, inclusive anexos de prontuário (`service-record`) e fotos de obra. | Link vazado ou compartilhado dá acesso permanente sem autenticação a fotos de pacientes ou pets. Não é enumerável (GUID). |
| S4 | **Baixa** | `StorageController` não valida magic bytes; aceita qualquer conteúdo com `Content-Type` permitido. | Arquivo arbitrário hospedado no bucket público com `Content-Type` de imagem. Mitigado porque é servido como imagem. |
| S5 | **Baixa** | `AddDailyLogPhoto` aceita `storageKey` arbitrário (sem checar o prefixo `tenants/{id}/`). | Um tenant referencia a chave de outro tenant no próprio diário. O impacto é nulo hoje, porque as URLs já são públicas, mas vira vazamento se o storage passar a ser privado. |
| S6 | **Baixa** | `ConfirmMovement` não valida `ContextId`/`SupplierId`; validators não executados (`amount=0` e data futura aceitos). | Integridade dos dados do ledger de obra, não do isolamento. |
| S7 | **Baixa** | Proxies de lookup, clima e barcode sem rate limit por tenant. | Abuso de cota em APIs de terceiros com um único token autenticado. |

## Testes existentes para esta área

| Arquivo | Testes (aprox.) |
|---|---|
| `nexo-backend/tests/Nexo.IntegrationTests/Build/BuildFlowTests.cs` | 4 |
| `nexo-backend/tests/Nexo.IntegrationTests/Interpreter/InterpreterAnalyzeTests.cs` | 1 |
| `nexo-backend/tests/Nexo.IntegrationTests/Storage/StorageUploadTests.cs` | 1 |
| `Nexo.UnitTests/Interpreter/AnalyzeMovementUseCaseTests.cs` | 10 |
| `Nexo.UnitTests/Interpreter/ConfirmMovementUseCaseTests.cs` | 10 |
| `Nexo.UnitTests/Interpreter/ReprocessMovementUseCaseTests.cs` | 10 |
| `Nexo.UnitTests/Interpreter/RuleBasedAnalyzerTests.cs` | 18 |
| `Nexo.UnitTests/Interpreter/DescriptionNormalizerTests.cs` | 13 |
| `Nexo.UnitTests/Integrations/StorageControllerTests.cs` | 14 |
| `Nexo.UnitTests/Integrations/CloudflareR2ProviderTests.cs` | 1 |
| `Nexo.UnitTests/Integrations/WeatherControllerTests.cs` | 8 |
| `Nexo.UnitTests/Integrations/BarcodeControllerTests.cs` | 8 |
| `Nexo.UnitTests/Integrations/LookupControllerTests.cs` | 9 |
| `Nexo.UnitTests/Integrations/CompositeCepLookupProviderTests.cs` | 6 |
| `Nexo.UnitTests/Integrations/PdfControllerTests.cs` | 8 |
| `Nexo.UnitTests/Integrations/IntegrationFeatureFlagsTests.cs` + `IntegrationOptionsDefaultsTests.cs` + `Phase2OptionsTests.cs` | 4 + 5 + 6 |

**Lacunas de teste:**
- **Build:** nenhum teste unitário de domínio ou service; 4 testes de integração.
- **Interpreter:** confirm, void, attachments e o cockpit da plataforma (`InterpreterAdminController`) sem teste de integração.
- **Frontend:** nenhum teste para `modules/build` nem para `platform/pages/ai`.
