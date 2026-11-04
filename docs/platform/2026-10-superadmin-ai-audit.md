# Auditoria SuperAdmin / Platform + IA — 2026-10-30

Escopo: todas as rotas `/api/platform/*`, telas `/platform/*`, autenticação do SuperAdmin e toda a infraestrutura de
IA/Interpreter, na `master` 7dfe843. Cada item foi rastreado UI → API client → controller → domínio → banco → efeito real.

Estado de produção lido só como metadado (nunca a chave): `ai_providers` tem RuleBased **desabilitado e default**, Claude
**habilitado** com chave no formato legado Base64 e modelo `claude-haiku-20240307` (ID inexistente, vindo do seed), OpenAI
desabilitado; três prompts ativos que nenhum código lê; zero linhas de telemetria nos últimos 30 dias.

## P0 — segurança / integridade

| # | Achado | Causa raiz | Correção (PR) |
|---|---|---|---|
| 1 | Chave de API "criptografada" é Base64 | `rotate-key` faz `Convert.ToBase64String` com `// TODO` | `ISecretProtector` AES-256-GCM com chave mestra fora do banco; upgrade em memória do formato legado (PR 1) |
| 2 | Configuração do SuperAdmin não controla o runtime | Seletor lê `Interpreter:Features:*` (appsettings); `AiProvider`, prompts e limites só são lidos pela tela | Runtime lê `ai_providers`; kill switch da implantação documentado (PR 1) |
| 3 | Impersonação sobrescreve a sessão do próprio admin | Tokens do tenant gravados nas mesmas chaves do `localStorage` | Tokens de impersonação só na aba (`sessionStorage`) (PR 2) |
| 4 | Ações durante impersonação não são atribuíveis ao operador | Token comum do Diretor, sem claim de ator; sem evento de fim | Claim `act`, campo de impersonador na auditoria, motivo obrigatório, fim auditado (PR 2) |
| 5 | SuperAdmin sem MFA, sem logout, sem revogação | `PlatformUser` só tem email/hash/role; token de 8 h sem stamp | TOTP + códigos de recuperação reaproveitando os componentes do Auth; security stamp; token curto + refresh; logout (PR 2) |
| 6 | Login do SuperAdmin não é auditado | — | `platform_login` / `platform_login_failed` (PR 2) |
| 7 | Papel `support` pode tudo (impersonar, resetar senha, trocar chave de IA) | Política `Platform` só exige `type=platform` | Política `PlatformAdmin` (`super_admin`) nas ações destrutivas e de IA (PR 1 para IA, PR 2 para o resto) |
| 8 | Arquivo local com chave fora do `.gitignore` | `api_anthropic.txt` na raiz do checkout, não rastreado e não ignorado | Ignorado no `.gitignore`; recomenda-se rotacionar (PR 1) |

## P1 — funcionalidade quebrada

| # | Achado | Correção |
|---|---|---|
| 9 | Provider desabilitado vira HTTP 500 (`InvalidOperationException`) | Erros de domínio tipados (`ai_provider_unavailable`), status coerente (PR 1) |
| 10 | Claude é stub; OpenAI não existe e quebra se ligado | `ClaudeAnalyzer` real (SDK oficial, saída estruturada); OpenAI aparece como "não disponível" (PR 1) |
| 11 | Toda extração RuleBased é gravada como Claude | `ExtractionResult` deduzia o provider pelo `PromptType` (PR 1) |
| 12 | Falha do analyzer não tem fallback nem telemetria | Execução com cadeia de fallback explícita e telemetria por tentativa (PR 1) |
| 13 | Playground engole erros, nunca envia `source`, não grava telemetria | Status por provider, erros visíveis, telemetria (PR 1) |
| 14 | Limites por tenant e por provider não têm efeito | Reserva antes da chamada paga, hard limit determinístico, soft limit sinaliza (PR 3) |
| 15 | Prompts versionados não são usados | Prompt ativo lido pelo runtime, versão/hash na telemetria, um ativo por tipo (PR 1/PR 3) |
| 16 | Explorador de endpoints monta `/api/api/...` | Corrigir caminho, sem "Run" em rota com parâmetro (PR 2) |
| 17 | "Resetar senha" promete troca obrigatória que não acontece | Exigir troca no próximo login (PR 2) |
| 18 | Sessão de impersonação não renova e some do painel | Token próprio, curto e sem refresh, explícito na UI (PR 2) |
| 19 | CNPJ/módulo duplicado → 500 | Pré-checagem + violação única → 409 (PR 2) |

## P2 / P3 — inconsistências e melhorias (resumo)

Custos por token em micros inteiros não representam preços reais (seed ~1000× acima); PATCH de provider não altera modelo
nem prioridade; dois prompts/defaults ativos possíveis sob concorrência; IDs de outro tenant aceitos no confirm do movimento;
texto do usuário duplicado em `ExtractionResult`; suspensão/revogação de módulo não limpam cache (5 min); revogação de sessão
pode sobreviver 60 s; health "api" fixo em verde; notas da plataforma não auditadas; listas sem paginação; UI promete
auditoria, fallback automático e "inativo" sem respaldo no backend.

## Ordem de execução

1. **PR 1 — runtime de IA:** segredo real, runtime controlado pelo SuperAdmin, Claude real, seleção/fallback, erros tipados,
   Playground e Testar conexão, tela de providers honesta.
2. **PR 2 — segurança do Platform:** impersonação, MFA, sessões, papéis, auditoria de login, matriz de autorização, correções
   P1/P2 do Platform.
3. **PR 3 — governança de IA:** limites com reserva, prompts, telemetria completa, painéis.
