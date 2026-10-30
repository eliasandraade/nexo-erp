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

Documentos: `PLAN.md`, `internal-api.md`, `client-portal-contract.md` (v1.5), `digital-twin.md`, `ai.md`, `forecast.md`,
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
| Backend integração (PostgreSQL real, Testcontainers) | 710/710 |
| Vitest | 871/871 |
| Playwright | 76/76 |

Caminhos críticos 1–9 cobertos por testes de integração (portal publicado; portal com senha sem/errada/certa/expirada;
financeiro oculto ausente do JSON; IFC → processamento → elementos → vínculos → estado → portal; movimento → etapa →
custo; orçamento → aprovação → conversão; aditivo → decisão do cliente → impacto; inspeção → entrega → garantia →
assistência; isolamento entre empresas). SonarCloud: quality gate verde (cobertura ≥ 80% no código novo).

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

- **Merge e deploy** (decisão do Elias); depois, smoke em produção das rotas novas.
- Adicionar a origem do site do Astra em `Cors:AllowedOrigins`.
- Confirmar em produção qual cabeçalho traz o IP real na Railway e ajustar `RateLimiting:ClientIpSource` se preciso.
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
`de4b39b` revisão de segurança · este relatório.

## 15. Instruções para o Astra (site público)

1. Leia `client-portal-contract.md` (v1.5). Use `openapi-portal.json` e `portal-types.ts` como fonte dos tipos.
2. URL pública: `https://www.orken.com.br/{empresa}/{obra}`; chame `GET /api/public/build/{empresa}/{obra}`.
   404 = não existe (não diferencie). 401 `portal_password_required` → tela de senha → `POST …/session` → envie o token no
   cabeçalho `X-Build-Portal-Session` em todas as chamadas; 401 `portal_session_expired` → peça a senha de novo.
   Não envie `Authorization`.
3. Renderize só as seções listadas em `sections`; campos ausentes significam "não visível" — nunca mostre zeros no lugar.
4. Mídia vem como URL assinada de 15 min: não guarde em cache longo; recarregue a rota ao expirar.
5. 3D: `GET …/twin` (three.js `GLTFLoader`; o nome do nó é o `externalId`; cores pelos estados de `GET …/twin/state?date=`,
   use `ETag`). Respeite o aviso de `geometrySource` (representação simplificada não é o projeto).
6. Ações do cliente (aprovar aditivo, escolher opção, aceitar entrega, abrir assistência, enviar fotos, avaliar) só existem
   em portal com senha; trate 409 (já decidido), 429 (`Retry-After`), 413 e 422 com as mensagens do servidor.
7. Antes do lançamento, peça a inclusão da origem do site em `Cors:AllowedOrigins`.
