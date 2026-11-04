# Orken Build — relatório final (Waves 1–6)

Branch `feat/build-complete` · PR #90 (rascunho) · base `master` 6064c7a · 2026-10-30.
**Não mergeado e não publicado**: o merge na `master` publica em produção e depende de decisão do Elias.

## 1. Arquitetura

- Mesmo padrão do código novo do repositório (Tax Intelligence): entidades `TenantEntity` em `Nexo.Domain/Modules/Build`,
  DTOs/validadores/cálculos puros em `Nexo.Application/Modules/Build`, serviços em `Nexo.Infrastructure/Modules/Build`
  com `NexoDbContext`, controllers internos em `Controllers/Modules/Build` (`Management` + módulo `build`) e públicos em
  `Controllers/Public/PublicBuildPortalController.cs`.
- **Três verdades separadas** no gêmeo digital: geometria (arquivo do modelo + derivado de visualização), operação (etapas,
  cronograma, diário, medições, eventos) e interpretação (vínculos, sugestões de IA).
- **Livro financeiro único**: `FinancialMovement` com `ContextType=Obra`/`ContextId=obra` ganhou `StageId`; nenhum
  ledger paralelo. Custo por etapa, CPI, EAC e 5D saem daí.
- **Progresso nunca digitado no total**: pesos por etapa, eventos de progresso append-only (manual, medição, IA
  confirmada), snapshots diários imutáveis, baseline/revisões.
- **IA sobre o runtime da plataforma** (#87–#89): mesmo registro de provedores, chave cifrada, reserva de uso, telemetria
  e prompts versionados no SuperAdmin; toda saída vira `BuildAiSuggestion` e só muda algo com aceite humano.
- **Determinístico primeiro**: previsão (`productivity/1`), riscos (SPI/CPI/EAC) e regras de fallback não dependem de LLM.
- **Flags por empresa** (`BuildTenantSettings`): Build3D, BuildAI, BuildVision, BuildPredict — controladas no SuperAdmin.

Documentos: `PLAN.md`, `internal-api.md`, `client-portal-contract.md` (v1.6), `digital-twin.md`, `ai.md`, `forecast.md`,
`openapi-portal.json`, `portal-types.ts`.

## 2. Funcionalidades por Wave

| Wave | Entregue |
|---|---|
| 1 Base + portal | local/área/modo de acompanhamento na criação; pesos, eventos, baseline/revisões, snapshots, curvas, "como estava em X"; custo por etapa; portal (empresa + obra, visibilidade, senha, sessão, branding), atualizações ao cliente, diário/fotos visíveis ao cliente |
| 2 Comercial | orçamento com tipos, totais por tipo e m², validade, notas cliente/internas, duplicar, versões, modelos, PDF com marca, conversão pela UI; aditivos com decisão do cliente no portal; escolhas do cliente → aditivo; materiais (pedido→cotação→compra→entregas→despesa); medições que atualizam o progresso |
| 3 Qualidade e pós-obra | checklists, inspeções (fotos obrigatórias, resultado calculado), não conformidades (crítica aberta bloqueia 100%); vistoria com cliente, entrega, garantias (CC art. 132 §3), assistência técnica aberta pelo cliente com fotos; "Meu Imóvel" |
| 4 Digital Twin | upload privado e versionado (IFC, GLB/glTF, DXF, PDF, imagem; RVT/DWG recusados com instrução), fila assíncrona, IFC via xBIM, zonas e elementos, derivado GLB gerado no navegador (web-ifc), vínculos etapa↔elemento/zona/categoria, estado 4D por data, preparo 5D, representação simplificada, mapeamento 2D, captura de realidade (foto, 360, vídeo, drone, nuvem de pontos armazenada) |
| 5 IA | copiloto do diário, interpretador de progresso (visão), mapeador de modelo, atualização ao cliente, resumo semanal, riscos de prazo e custo explicados, inteligência de documentos; repositório de documentos da obra |
| 6 Previsões e operação | motor de previsão por produtividade com calendário e feriados, clima por atividade (Open-Meteo), painel de carteira, observabilidade e reprocessamento no SuperAdmin, OpenAPI + tipos do portal, revisão de segurança |

## 3. Migrations (todas aditivas)

`BuildWave1Portal`, `BuildWave2Commercial`, `BuildWave3Quality`, `BuildWave4DigitalTwin`, `BuildWave5Ai`,
`BuildWave6Forecast`. Verificado: nenhum `Up` contém `DropTable`, `DropColumn`, `Rename*`, `AlterColumn`, `DropIndex` ou
SQL bruto. Linhas existentes recebem padrões neutros (modo Standard, peso 1, tipo de item Outros, versão 1). Nada é
apagado nem reescrito.

## 4. Endpoints

~205 rotas internas em `/api/v1/build/*`, 22+ rotas públicas em `/api/public/build/{empresa}/{obra}/*` e rotas de
plataforma `/api/platform/build/*` e `/api/platform/tenants/{id}/build-settings`. Lista completa: `internal-api.md`
(por Wave) e `client-portal-contract.md` + `openapi-portal.json` (público).

## 5. Frontend interno

Abas da obra: Geral (clima), Etapas (peso, sensibilidade ao clima), Progresso (curvas, baseline, "como estava em", IA),
Orçamento, Aditivos, Escolhas, Materiais, Medições, Diário (visibilidade + copiloto), Financeiro (custo por etapa),
Qualidade, Entrega e pós-obra, Gêmeo digital (onboarding 3D, visualizador lazy, conversão IFC, vínculos, zonas, captura),
Riscos e previsões, Documentos, Portal do cliente. Painel de carteira em `/build`. SuperAdmin: card "Orken Build" por
empresa e página `/platform/build`. `three` e `web-ifc` só em chunks sob demanda (bundle principal +1,3 kB).

## 6. IA

8 capacidades, prompts `build-*` versionáveis no SuperAdmin, entrada não confiável em bloco delimitado, saída validada
contra JSON Schema, IDs inventados descartados. Cada sugestão guarda confiança, proveniência, provedor/modelo, versão e
hash do prompt, tokens e custo, e é auditada. Aceite parcial; nada automático. Sem provedor: regras determinísticas
(copiloto, mapeador, atualização ao cliente) ou 422 claro (interpretador, documentos, explicação de risco).
BuildAI e BuildVision nascem **desligados** por empresa.

## 7. Digital Twin

Prioridade IFC > GLB/glTF > RVT/DWG (recusados, exportar IFC) > DXF > PDF > imagem. Estados de geometria NativeBim,
Converted3D, AssistedMapping, SimplifiedRepresentation; nada de geometria inventada (caixas só com medidas digitadas e
rotuladas). Estados de elemento NotStarted, InProgress, Completed, Delayed, Blocked, NeedsReview, calculados para qualquer
data a partir do histórico (4D); custo por quantidade vinculada (preparo 5D). O arquivo BIM original nunca sai pelo portal;
o cliente recebe só o GLB derivado por URL assinada de 15 min. Detalhes: `digital-twin.md`.

## 8. Segurança

Isolamento por tenant/obra em toda consulta pública; 404 idêntico para inexistente/não publicado/reservado; senha BCrypt,
sessão opaca (SHA-256), expiração e revogação ao trocar senha; contagem atômica de falhas, bloqueio por (portal, IP) +
teto global, limite de BCrypt simultâneo, rate limit por IP com origem configurável; escrita só em portal com senha;
portal sem senha não serve dados residenciais (pós-obra, aditivos, escolhas, documentos, captura); custos só com resumo
financeiro visível; URLs assinadas só para itens visíveis e na pasta da seção; uploads com magic bytes, limites por tipo,
nome de arquivo ignorado, autorização antes de ler o corpo; 64 KB nos JSON públicos; senha/configuração do portal
bloqueadas durante impersonação; ações de plataforma com MFA e auditoria. Revisão independente: 1 alta, 4 médias, 3 baixas
e 2 informativas — todas corrigidas exceto a diferença de tempo de resposta entre slug de empresa inexistente e de obra
inexistente (baixa, registrada em §13).

## 9. Testes

| Suite | Resultado |
|---|---|
| Backend unit | 980/980 |
| Backend integração (PostgreSQL real, Testcontainers) | 711/711 |
| Vitest (inclui o portal "Minha Obra") | 917/917 |
| Playwright (app) | 76/76 |
| Playwright do portal com fixtures (DEV) | 11/11 |
| Portal "Minha Obra" contra a API real (CORS real, PostgreSQL descartável) | 19/19 |

Caminhos críticos 1–9 cobertos por testes de integração (portal publicado; portal com senha sem/errada/certa/expirada;
financeiro oculto ausente do JSON; IFC → processamento → elementos → vínculos → estado → portal; movimento → etapa →
custo; orçamento → aprovação → conversão; aditivo → decisão do cliente → impacto; inspeção → entrega → garantia →
assistência; isolamento entre empresas). SonarCloud: quality gate verde; detalhamento dos 543 itens em §16.

## 10. Performance

Painel sem N+1 (teste conta comandos SQL); listas paginadas; processamento de modelos em fila com `SKIP LOCKED`, fora da
requisição; elementos em lote; estado 4D com ETag; chunks 3D sob demanda e conversão IFC em Web Worker; clima com cache.
Limite conhecido: o motor de estado carrega todos os elementos da versão por requisição (adequado à v1; revisar para
modelos muito grandes).

## 11. Custos externos opcionais

Nenhum obrigatório. Opcionais: chamadas de IA (Claude, cobradas por token, limitadas no SuperAdmin; BuildAI desligado por
padrão), armazenamento R2 de modelos/vídeos/nuvens de pontos, IfcConvert no servidor (gratuito, só se instalado), serviço
externo de conversão RVT/DWG (não incluído). Open-Meteo e xBIM/web-ifc/three são gratuitos (xBIM: licença CDDL-1.0,
usado sem modificação).

## 12. Degradação

Sem IA → regras determinísticas ou mensagem clara; sem 3D habilitado → upload bloqueado (403) e portal esconde o 3D; sem
WebGL → aviso amigável; sem IfcConvert → conversão no navegador; sem coordenadas ou clima → resposta 200 com motivo; sem
storage → 503 `storage_unavailable`; previsões desligadas → método ponderado anterior.

## 13. Pendências reais

- Domínio do portal: resolvido em 2026-10-31. `www.orken.com.br` ativo na Railway com DNS e TLS válidos (§16).
- **Merge e deploy** (decisão do Elias); depois, smoke em produção das rotas novas.
- CORS: `https://www.orken.com.br` e `https://app.orken.com.br` já estão na lista fixa; outra origem do site entra em
  `Cors:AllowedOrigins`. Desde a v1.6 `ETag` e `Retry-After` são expostos ao navegador.
- Smoke pós-deploy (não bloqueia o merge): confirmar em produção qual cabeçalho traz o IP real na Railway e ajustar
  `RateLimiting:ClientIpSource` se preciso.
- Ligar BuildAI/BuildVision por empresa e revisar limites de custo antes de liberar IA aos clientes.
- QA manual com arquivos IFC/GLB reais e grandes num navegador com WebGL (os testes usam um IFC pequeno de exemplo).
- Uploads grandes passam pela API (sem PUT pré-assinado direto no R2); vídeos e nuvens de pontos grandes pesam no servidor.
- Tabela de feriados móveis (Sexta-feira Santa) cobre 2026–2028; atualizar anualmente.
- Não feitos: nota fiscal aceita → despesa em rascunho; miniaturas/redimensionamento de imagens; processamento de nuvem
  de pontos; etapas paralelas na previsão; certificado PDF de aceite da entrega; flags "em breve" do portal (vídeos,
  categorias/lançamentos/comprovantes financeiros, riscos, equipe).
- Reaprovar/converter um orçamento depois de aditivos aplicados redefine `BudgetApproved` (documentado).
- Diferença pequena de tempo de resposta entre slug de empresa inexistente e de obra inexistente (achado baixo).

## 14. Commits

`b171273` Wave 1 · `5333a03` correções Wave 1 · `65abf8c` Wave 2 · `7dab57e`/`d8e21a0` Wave 3 · `6b58fc6`/`13fe668`
Wave 4 · `54ec134`/`19de292`/`2084730` Wave 5 · `df7d934` Sonar · `ce0ac36`/`b66733c` Wave 6 · `6cde485` testes ·
`de4b39b` revisão de segurança · `ab57a36` contrato v1.6 (ETag no CORS, `elementIndex`) · `7793aea` merge do frontend
"Minha Obra" (`codex/minha-obra`) · `bfc6f9d` achados de confiabilidade do Sonar · este relatório.

## 15. Instruções para o Astra (site público)

1. Leia `client-portal-contract.md` (v1.6). Use `openapi-portal.json` e `portal-types.ts` como fonte dos tipos.
2. URL pública: `https://www.orken.com.br/{empresa}/{obra}`; chame `GET /api/public/build/{empresa}/{obra}`.
   404 = não existe (não diferencie). 401 `portal_password_required` → tela de senha → `POST …/session` → envie o token no
   cabeçalho `X-Build-Portal-Session` em todas as chamadas; 401 `portal_session_expired` → peça a senha de novo.
   Não envie `Authorization`.
3. Renderize só as seções listadas em `sections`; campos ausentes significam "não visível" — nunca mostre zeros no lugar.
4. Mídia vem como URL assinada de 15 min: não guarde em cache longo; recarregue a rota ao expirar.
5. 3D: `GET …/twin` (three.js `GLTFLoader`; o nome do nó é o `externalId`; cores pelos estados de `GET …/twin/state?date=`,
   use `ETag`; etapa, categoria e zona de cada elemento em `elementIndex`, v1.6). Respeite o aviso de `geometrySource` (representação simplificada não é o projeto).
6. Ações do cliente (aprovar aditivo, escolher opção, aceitar entrega, abrir assistência, enviar fotos, avaliar) só existem
   em portal com senha; trate 409 (já decidido), 429 (`Retry-After`), 413 e 422 com as mensagens do servidor.
7. Se o site não for servido por `www.orken.com.br` ou `app.orken.com.br`, peça a inclusão da origem em
   `Cors:AllowedOrigins`.
8. O frontend "Minha Obra" faz parte deste PR (`nexo-main/src/modules/build-portal`, rota `/{empresa}/{obra}`; merge de
   `codex/minha-obra`). Detalhes em `docs/build/client-portal-frontend.md`, inclusive a suíte que roda contra esta API
   real (`npx playwright test --config playwright.build-portal.integration.config.ts`, em `nexo-main`).

## 16. Auditoria final de release (HEAD `aa15b12`)

### SonarCloud

O comentário do bot ("543 New issues") e o "0 itens" reportado antes medem coisas diferentes: o 0 era só de
**confiabilidade**. A análise do PR (só linhas novas ou alteradas: 62.135) tem:

| Tipo | Abertos |
|---|---|
| BUG | 0 |
| VULNERABILITY | 0 |
| SECURITY_HOTSPOT | 0 (100 % revisados) |
| CODE_SMELL (manutenibilidade) | 543 |

Nenhum item aceito, todos `OPEN`. Dívida estimada de 56 h, razão de dívida 0,28 % (nota A, limite 5 %): por isso o gate passa.

| Área | Itens | CRITICAL |
|---|---|---|
| Backend (src) | 208 | 39 |
| Backend (testes) | 8 | 0 |
| Frontend do Build (admin, plataforma e roteador) | 258 | 7 |
| Portal "Minha Obra" | 69 | 6 |

Os 52 CRITICAL são 44 de complexidade cognitiva (S3776), 5 laços de nova tentativa sem condição de parada explícita
(S1994), 2 nomes de parâmetro (S927) e 1 aninhamento profundo (S2004). Os maiores grupos restantes: props não
`readonly` (144), ternários aninhados (110), `static readonly` para arrays constantes (56), métodos com muitos
parâmetros (47), literais repetidos (21), `<output>` em vez de `role="status"` (21).

Conferidos um a um os que poderiam esconder defeito:
- **S1994:** os 5 laços repetem só quando `attempt == 0`; a segunda falha sobe ou vira 409. Terminam.
- **S2681:** `if (ok) toast.success(ok); return r;` numa linha; o `return` incondicional é o pretendido.
- **S6551:** `FormData.get` de campos de texto devolve string.
- **S6479:** 13 listas com índice como `key`. Todas renderizam texto estático e são trocadas inteiras a cada recarga; os
  pontos da planta só crescem ou encolhem no fim.

Nenhum é defeito funcional nem risco de segurança; ficam como dívida de manutenção documentada. A `master` tem 1.527
itens abertos (184 BUG, 5 VULNERABILITY) que não são deste PR.

### Migrations

`Up()` das 6 migrations, sem nenhum `Drop`, `Rename`, `AlterColumn` ou `Sql()` bruto.

| Migration | Operações | Em tabelas existentes | Risco |
|---|---|---|---|
| BuildWave1Portal | 7 tabelas, 9 colunas, 16 índices | `int_movements.stage_id` (nulo) + índice não concorrente; `bld_stages.weight` (default 1); `bld_projects` área/lat/long (nulos), `tracking_mode` (default 1 = Standard); `bld_daily_logs`/`_photos` `client_visible` (default false) | LOW |
| BuildWave2Commercial | 10 tabelas, 17 colunas, 30 índices, 1 FK | `bld_budgets` 15 colunas (nulas, `version` default 1), índice único filtrado por `root_budget_id IS NOT NULL` (sem conflito: coluna nova toda nula), FK `SetNull` para modelos; `bld_budget_items.kind` default 5 = Other | LOW |
| BuildWave3Quality | 8 tabelas, 27 índices | nenhuma | LOW |
| BuildWave4DigitalTwin | 9 tabelas, 30 índices | nenhuma | LOW |
| BuildWave5Ai | 2 tabelas, 6 índices | nenhuma | LOW |
| BuildWave6Forecast | 1 tabela, 1 coluna, 3 índices | `bld_stages.weather_sensitive` (nula) | LOW |

Observações:
- **Evidência de produção (2026-10-31, consulta somente leitura, sessão com `default_transaction_read_only=on`):**
  - PostgreSQL 18.6;
  - `nexo.int_movements`: 2 linhas, cerca de 128 kB;
  - nenhuma transação aberta há mais de 1 min;
  - nenhuma Wave do Build aplicada; última migration em produção: `20261030063024_AiGovernance`;
  - as 6 Waves são exatamente as próximas migrations, na ordem.
- **Wave 1 reavaliada de MEDIUM para LOW:** `int_movements` é o livro financeiro compartilhado entre módulos, e o
  `CREATE INDEX` não concorrente bloqueia escrita nela enquanto o índice é construído. As migrations rodam no boot
  (`MigrateAsync`, em transação, sem `CONCURRENTLY`). Com 2 linhas, isso leva milissegundos.
- **Defaults:** `NOT NULL` com default constante só altera metadado no PostgreSQL 11 ou mais novo, sem reescrever a
  tabela; produção está no 18.6. Os testes usam o 16, e as migrations só usam recursos presentes nas duas versões.
- **Efeito dos defaults nos dados existentes:**
  - etapas existentes com peso 1 (peso igual);
  - diário e fotos antigos ocultos do portal até a construtora liberar;
  - itens de orçamento antigos como "Outro".
- **FKs:**
  - Para etapa: `SetNull` (aditivos, escolhas, inspeções, não conformidades, materiais, capturas) ou `Cascade` só no
    vínculo modelo↔etapa.
  - `int_movements.stage_id` não tem FK, de propósito: livro entre módulos. Lançamento de etapa excluída cai em
    "Sem etapa" no custo por etapa.
  - Para projeto: `Cascade`, mas projeto não tem exclusão física (só mudança de status).
- Durante o deploy, o código antigo convive bem com o esquema novo: só colunas nulas ou com default.

### Rota pública `/{empresa}/{obra}`

- O resolver só aceita exatamente 2 segmentos `[a-z0-9-]` (3–60 caracteres), nenhum deles reservado.
- Os 37 primeiros segmentos usados por rotas e navegação do app estão todos reservados.
- Arquivos estáticos não casam: o ponto do nome do arquivo e o `.well-known` ficam fora do padrão.
- O backend recusa salvar slug reservado (`BuildPortalSlug.Reserved`) e tem a mesma lista do frontend, conferida
  programaticamente.

Lista final (50): `acesso-encerrado admin agendar api app assets assinatura auditoria build caixa check-email clientes
compras configuracoes dashboard esqueci-senha estoque financeiro fiscal forgot-password fornecedores health impersonate
listas-de-preco login logout menu obra obras orken pdv perfil platform portal produtos public rastrear redefinir-senha
register relatorios reset-password restaurante service static tributos usuarios vendas verify-email workspaces www`.

### Configuração e ambiente

- **Domínio:** `www.orken.com.br` está ativo na Railway, com DNS e TLS válidos (conferido em 2026-10-31; em
  2026-10-30 ainda não existia no DNS). É a origem dos links que o admin gera (`PORTAL_PUBLIC_ORIGIN`, em
  `nexo-main/src/modules/build/api/build-portal.api.ts`).
  - `https://www.orken.com.br/` e `https://www.orken.com.br/solutie/shoppingeusebio` respondem 200 com certificado
    válido e o mesmo bundle do `app`: a rota de dois segmentos chega ao SPA. Antes do deploy deste PR ela mostra o 404
    interno do Orken, como esperado.
  - A API de produção responde ao preflight CORS da origem `https://www.orken.com.br` com 204 e
    `Access-Control-Allow-Origin: https://www.orken.com.br`.
- **CORS:** `https://app.orken.com.br` e `https://www.orken.com.br` estão fixos no código para todos os ambientes;
  `Cors:AllowedOrigins` só acrescenta. Observação antiga, não deste PR: as origens `localhost` também valem em produção.
- **IP do cliente:**
  - A API de produção responde direto da borda da Railway (`Server: railway-hikari`, sem CDN).
  - O padrão do código é o primeiro valor de `X-Forwarded-For`, com base em resposta de fórum.
  - A documentação oficial da Railway aponta `X-Real-IP`, e o fórum tem respostas conflitantes.
  - Não confirmado sem ambiente: item do **smoke pós-deploy**, não bloqueia o merge. Registrar os dois headers numa
    requisição de IP conhecido e, se preciso, ajustar `RateLimiting:ClientIpSource=RealIp`.
  - Mesmo com o header errado, o bloqueio global por portal (`Too_many_wrong_passwords_from_anywhere_lock_the_whole_portal`)
    limita a força bruta.
- **IA:**
  - `BuildAiEnabled` e `BuildVisionEnabled` nascem `false`, também para empresa sem registro de configuração.
  - Desligada: 403. Sem provider ou com o limite mensal atingido: sugestão por regras ou 422 `ai_provider_unavailable`.
  - Resposta inválida do modelo: 503 sem gravar.
  - O uso é contabilizado no runtime da plataforma. Os valores dos limites são configuração do SuperAdmin e devem ser
    conferidos manualmente.
- **Storage:**
  - Contexts `build-daily-log`, `build-portal-logo`, `build-portal-cover`, `build-selection-option`, `build-quality`,
    `build-handover`, `build-assistance`, `build-reality`.
  - URL assinada de 15 min (`PrivateFileUrls.Ttl`), chave sempre conferida contra o tenant.
  - Limites de upload: modelos 150 MB (`Build:Models:MaxUploadMb`), imagens de captura 25 MB, documentos 20 MB,
    upload público 8 MB, JSON público 64 KB.
  - Uploads passam pela API (sem PUT direto no R2).
- **Clima:** `WeatherEnabled` é `false` no `appsettings.json`. A rota de clima responde 404 e a previsão segue com o
  motivo `weather_disabled` (teste em `BuildForecastTests`).

### Controles críticos presentes no HEAD (evidência: testes)

| Controle | Evidência |
|---|---|
| Isolamento de tenant e projeto, IDOR | `Another_tenant_cannot_touch_this_tenants_portal_or_costs`, `Another_tenant_gets_404_for_every_wave2_resource`/`wave3`, `Suggestions_and_documents_of_another_company_are_404`, `Change_orders_cannot_be_decided_on_a_public_portal_nor_across_projects_or_tenants`; E2E real de isolamento |
| Sessão: emissão, expiração, revogação | `Password_portal_needs_a_valid_session`: token de 256 bits aleatórios gerado pelo servidor, só o hash gravado, escopo empresa+portal; expira; cai com troca de senha e no logout (sem *session fixation*) |
| Força bruta e rate limit | `Wrong_passwords_lock_only_the_guessing_client_not_the_real_one`, `Too_many_wrong_passwords_from_anywhere_lock_the_whole_portal`, `Parallel_wrong_passwords_each_count`, `RateLimitResponse_IncludesRetryAfterHeader` |
| CORS | `CORS_PublicPortalOrigin_CanReadETagAndRetryAfter_AndSendTheSessionHeader` e preflights |
| CSRF | O portal usa header e `credentials: "omit"`, sem credencial implícita, então CSRF não se aplica; o admin mantém os cookies e testes existentes |
| XSS | Sem `dangerouslySetInnerHTML`, `innerHTML` ou `eval` no portal e no admin do Build; URLs de mídia só `https:` (`safeUrl`); CSP sem `unsafe-inline` em script (`CSP_AllowsStylesButNotUnsafeInlineScripts`) |
| Uploads, MIME e assinatura de bytes | `Reality_capture_checks_the_magic_and_portal_sections_stay_off_by_default`; E2E real: PNG falso recusado com 422 e 8 MB+1 com 413; `Public_json_bodies_are_capped_at_64_kb` |
| Escopo de URL assinada e caminhos | `Site_photos_only_take_this_tenants_private_daily_log_keys`, `Source_url_is_for_the_company_only_audited_and_never_in_the_portal`; o caminho crítico 4 sobe `../../etc/Projeto Casa.ifc` |
| Enumeração de portal | `Unknown_and_unpublished_portals_answer_the_same_404` |
| Dados antes da autenticação | O mesmo teste de sessão: o teaser não tem `project` nem `stages`; `A_public_portal_never_serves_the_personal_sections` |
| Financeiro oculto | `Financial_data_is_absent_unless_enabled`, `Change_order_money_reaches_the_client_only_with_the_financial_summary` |
| Notas internas | `Documents_check_the_content_and_the_portal_lists_only_client_visible_ones_without_internal_fields`, `Timeline_and_photos_only_show_what_was_marked_for_the_client` |
| BIM bruto | Caminho crítico 4: o JSON público não contém `.ifc`, `/source.`, `private/` nem o id do modelo; só a chave do GLB derivado vira URL assinada |
| Impersonation | `An_impersonating_administrator_cannot_change_how_the_portal_is_opened`, `An_impersonation_needs_a_reason_names_who_acted_never_refreshes_and_ends_at_once` |
| MFA do SuperAdmin | `Admin_acts_need_two_step_verification_with_codes_that_work_once`, `MfaTests` |

### Digital Twin no HEAD

- GLB derivado com URL assinada.
- `elementIndex` a partir de vínculos reais, sem inferência por nome de nó (teste `never reads metadata from node
  names`).
- Snapshots por data com ETag e 304.
- Modos Projeto, Agora e Comparar; aviso de `geometrySource` para representação simplificada e mapeamento assistido.
- Limite conhecido: `Build:Models:MaxElements` = 200.000 e upload de 150 MB. O maior modelo testado é o IFC de exemplo
  e o GLB sintético da suíte. Desempenho com modelos grandes não foi medido.

### Manual / smoke pós-deploy (não testados)

- Validação do IP real na Railway (`X-Forwarded-For` × `X-Real-IP`).
- iPhone físico.
- Android físico.
- Leitor de tela.
- IFC e GLB grandes.
- Storage real (R2).
- Provider real de IA.
- Core Web Vitals.

### Tabela final de release

| Item | Status | Evidência | Blocker? |
|---|---|---|---|
| Backend build | ✅ | 0 erros | Não |
| Backend unit | ✅ | 980/980 | Não |
| Backend integração | ✅ | 711/711 (local e CI) | Não |
| Typecheck | ✅ | limpo | Não |
| Lint | ✅ | 0 erros (22 avisos antigos) | Não |
| Vitest | ✅ | 917/917 | Não |
| Build do frontend | ✅ | OK; `three` só ao abrir o modelo | Não |
| Playwright (app) | ✅ | 76/76 | Não |
| Portal visual | ✅ | 11/11 | Não |
| E2E com backend real | ✅ | 19/19 | Não |
| Sonar | ✅ | gate aprovado; 0 bug, vulnerabilidade ou hotspot; 543 code smells explicados acima | Não |
| GitGuardian | ✅ | check verde | Não |
| Conversas de revisão | ✅ | 0 | Não |
| Migrations | ✅ LOW | as 6 só acrescentam; evidência de produção acima | Não |
| Domínio do portal | ✅ | `www` com DNS e TLS ativos, SPA na rota de dois segmentos | Não |
| CORS | ✅ | origens fixas no código; preflight de produção válido para `www` | Não |
| Rate limit / IP | ⏳ | header real só observável em produção | Não (smoke pós-deploy) |
| Storage | ✅ código / ⏳ R2 real | URL de 15 min com escopo por tenant | Não (smoke pós-deploy) |
| Digital Twin | ✅ | testes e E2E; modelos grandes não medidos | Não (manual) |
| IA | ✅ | desligada por padrão; falta de provider tratada | Não |
| Produção | **READY FOR MERGE** | merge e deploy aguardam a autorização do Elias | — |
