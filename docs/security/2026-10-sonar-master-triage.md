# Triagem do SonarCloud no `master` (issue #84)

Data: 29/10/2026. Fonte: API pública do SonarCloud (`branch=master`, issues não resolvidos e hotspots `TO_REVIEW`).
Nada foi ocultado: o Quality Gate não foi alterado e nenhum arquivo foi excluído da análise.

## Por que o gate do `master` falhou

O período de *new code* do `master` engloba o trabalho do ORKEN 1.0 (outubro), anterior ao Tax Intelligence. As
condições que falharam — confiabilidade C, segurança D, cobertura 56,6% e 0% dos hotspots revisados — vêm desse
código, não do módulo tributário (a PR #78 passou no gate próprio com 92,1% de cobertura e 0 hotspots).

## Vulnerabilidades (5)

| Regra | Local | Veredito | Ação |
|---|---|---|---|
| S6680 (loop com entrada do usuário) | `FinancialService.cs:407` | **Falso positivo.** `Installments` é validado em 1–60 antes do laço (`FinancialService.cs:371`); a análise de fluxo não enxerga a guarda de domínio | Marcar como *safe* no SonarCloud com este link |
| S8707 (path traversal por argumento de CLI) | `nexo-main/scripts/capture-experience.mjs:6` | **Real, baixo impacto** (script de captura local, nunca roda no servidor) | **Corrigido:** o argumento só aceita `[a-z0-9-]{1,40}` |
| S7039 (CSP pouco restritiva) | `Program.cs:467` | **Aceito com ressalva.** A API responde JSON; a CSP já tem `default-src 'self'`, `frame-ancestors 'none'`, `form-action 'self'`. `style-src 'unsafe-inline'` e `img-src https:` existem para a UI do Swagger | Avaliar CSP `default-src 'none'` fora das rotas do Swagger numa PR de plataforma |
| S2068 (senha fixa) | `CreateTenantDrawer.tsx:62` | **Falso positivo.** É a validação do campo `adminPassword` digitado pelo operador; não há senha no código | Marcar como *safe* |
| S6781 (segredo JWT em configuração) | `JwtTokenService.cs:168` | **Mitigado.** `appsettings*.json` só trazem placeholders, e `JwtSecretPolicy` recusa esses valores fora de Development (ver `2026-10-dumps-and-jwt-secret.md`) | Marcar como *safe* com a referência |

## Hotspots (24)

| Grupo | Qtde | Veredito | Ação |
|---|---|---|---|
| Regex sem timeout (`Store.cs:72-74`, `BarcodeController.cs:39`, `LookupController.cs:46,97`) | 6 | Padrões lineares, mas sem limite de tempo | **Corrigido:** os três controllers usam filtro de dígitos sem regex; o slug usa timeout de 250 ms |
| Tamanho de requisição (`StorageController.cs:89`, 20 MB) | 1 | Upload de imagens de catálogo, autenticado e com limite de arquivo em `Storage:MaxFileSizeMb` | Revisar o limite com o dono do módulo de mídia (fora do escopo tributário). Os uploads tributários já foram limitados a 8 MB nesta PR |
| Cookie sem `Secure` (`AuthController.cs`) | 10 | **Seguro.** `Secure = true` em todo ambiente que não seja Development; o proxy do Railway termina o TLS (comentário em `AuthController.cs:105`) | Marcar como revisado/seguro |
| Cookie sem `HttpOnly` (`AuthController.cs`) | 6 | **Seguro.** As opções derivam de `cookieOptions` com `HttpOnly = true` (`new CookieOptions(cookieOptions)`); o analisador não segue a cópia | Marcar como revisado/seguro |
| Imagem `caddy` como root (`nexo-main/Dockerfile:20`) | 1 | Risco aceito por ora: o Caddy precisa da porta 80 no container do Railway | Avaliar `USER caddy` com `setcap` ou porta alta numa PR de infraestrutura |

## Bugs (185)

| Regra | Qtde | Natureza | Plano |
|---|---|---|---|
| `typescript:S9383` | 145 | Promises não tratadas, quase todas `invalidateQueries`/`refetch` do React Query em hooks dos módulos Build, Menu e Core | PR dedicada: `void` explícito onde o fogo-e-esquece é intencional, `await` onde o fluxo depende do resultado. Os arquivos do Tax Intelligence já usam `void` |
| `csharpsquid:S8949` | 31 | `CancellationToken` não repassado (middlewares e serviços antigos) | PR dedicada repassando `RequestAborted` |
| `csharpsquid:S8733`, `typescript:S1082`, `S2871`, `S3923` | 9 | Pontuais | Mesma PR de limpeza |

## Definição de *new code*

A definição atual do `master` mede desde o início do ORKEN 1.0, então o gate do branch acumula dívida antiga e não
diz nada sobre a mudança mais recente. Recomendação: **"Previous version"** (cada `sonar.projectVersion` novo abre o
período) ou uma **data de referência** igual ao merge da #78 (26/10/2026).

A mudança é feita em *Project Settings → New Code* no SonarCloud e exige permissão de administrador do projeto; esta
sessão não tem token de administrador, então **não foi aplicada**. Fica como ação do dono do projeto.

## O que esta PR corrigiu

- Path traversal do script de captura (S8707).
- 6 hotspots de regex sem timeout.
- Limite de requisição dos uploads tributários reduzido de 26 MB para 8 MB (S5693 apontado na própria PR).
