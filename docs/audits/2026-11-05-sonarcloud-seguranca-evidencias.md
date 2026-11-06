# SonarCloud: evidências de segurança, item por item (05/11/2026)

Exame individual das 2 vulnerabilidades e dos 17 hotspots abertos no projeto `eliasandraade_nexo-erp`. Em cada item, a evidência vem antes da classificação. As respostas em lote de `2026-11-04-sonarcloud-revalidacao.md` foram tratadas como hipóteses a testar.

## Resumo

**Nenhum dos 19 itens é um defeito no ponto que o analisador acusa.** A recomendação é 2 False Positive e 17 Safe (tabela na seção 9). Mas a resposta curta "seguro" escondia três coisas que as medições mostraram:

1. **As flags dos cookies estão certas, e os cookies não protegem a sessão.** O SPA nem guarda esses cookies: chama a API com `fetch` sem `credentials`, e o navegador descarta o `Set-Cookie` (M3). Os mesmos tokens vão no corpo da resposta e ficam em `localStorage`, legíveis por qualquer script, inclusive o refresh de 7 dias. O app não tem CSP com `script-src` (M2). Marcar os 6 `S3330` como Safe é correto sobre a flag, e não quer dizer que a sessão resista a XSS. **É decisão do dono** (seção 6, D1).
2. **O limite de upload é real, mas vale depois de o corpo estar em disco.** `[RequestSizeLimit]` segura `Content-Length` e `chunked`, e o arquivo acima de 10 MiB é recusado (M6). Só que até 20 MiB são gravados em disco antes de olhar a flag de armazenamento, o contexto, o papel fino e o limite de 10 MiB, sem limite de taxa: 8 pedidos paralelos chegaram a 150 MiB em disco temporário (M7).
3. **Achado fora da lista, corrigido nesta branch: o limite `credential-check` era um só para todo mundo.** O limitador rodava antes da autenticação, então a partição "por usuário" sempre via `anonymous`. Dez chamadas sem token bloqueavam `verify-manager`, `validate-manager` e `mfa/*` para todos os usuários e empresas por 5 minutos (M8).

**Mudanças nesta branch:**

| Commit | O quê |
|---|---|
| `test(auth)` | Testes que faltavam: flag `Secure` (o único teste dela não tinha asserção), `switch-store`, flags por ambiente na subida real, guarda do segredo JWT na subida real. Passam sem mudar produção |
| `fix(auth)` | `UseAuthentication` antes de `UseRateLimiter`, com 2 testes que falharam antes da correção (M9) |
| `docs(sonar)` | Este documento, as correções em outros documentos e um comentário falso em `StorageController.cs:106` |

`AuthController.cs`, `JwtTokenService.cs`, `CreateTenantDrawer.tsx` e a linha 91 de `StorageController.cs` não mudaram, para as chaves e linhas abaixo continuarem valendo no painel. Nada foi silenciado, e nada foi alterado no SonarCloud.

## 1. Referência

| Item | Valor |
|---|---|
| Data do exame | 05/11/2026 (medições entre 00:37 e 01:30 UTC de 06/11) |
| Commit | `440b08406a32b73c12ef00b056df5be2bc930678`, o mesmo de produção |
| Análise | `b38ee41b-847a-4635-a13c-882fa5aff1eb`, de 05/11/2026 19:40:06 UTC, ramo `master` |
| Vulnerabilidades abertas | **2**, como esperado |
| Hotspots `TO_REVIEW` | **17**, como esperado (`S2092` ×10, `S3330` ×6, `S5693` ×1) |
| Quality Gate | `ERROR`. Hotspots revisados: 0% contra 100%. Segurança no período: A |

## 2. Como reproduzir

Consultas públicas, sem token:

```bash
P=eliasandraade_nexo-erp
curl -s "https://sonarcloud.io/api/project_analyses/search?project=$P&ps=1"
curl -s "https://sonarcloud.io/api/issues/search?componentKeys=$P&types=VULNERABILITY&resolved=false"
curl -s "https://sonarcloud.io/api/hotspots/search?projectKey=$P&status=TO_REVIEW&ps=100"
curl -s "https://sonarcloud.io/api/hotspots/show?hotspot=<chave>"
```

Medições locais, com o build `Release` deste commit, em Kestrel de verdade e contra contêineres descartáveis:

```bash
docker run -d --name evid-pg -e POSTGRES_USER=nexo_evid -e POSTGRES_PASSWORD=<local> -e POSTGRES_DB=nexo_evid -p 127.0.0.1:55433:5432 postgres:15.1
docker run -d --name evid-redis -p 127.0.0.1:56380:6379 redis:7-alpine
cd nexo-backend/src/Nexo.Api/bin/Release/net8.0
ASPNETCORE_ENVIRONMENT=Staging ASPNETCORE_URLS=http://127.0.0.1:5077 ASPNETCORE_TEMP=<pasta> \
ConnectionStrings__Default='Host=127.0.0.1;Port=55433;Database=nexo_evid;Username=nexo_evid;Password=<local>' \
ConnectionStrings__Redis=127.0.0.1:56380 Jwt__Secret="$(openssl rand -base64 48)" \
Seed__AdminPassword=<valor de TestCredentials> Seed__PlatformEmail=<idem> Seed__PlatformPassword=<idem> \
Integrations__Features__StorageEnabled=true RateLimiting__AuthLogin__PermitLimit=1000 \
dotnet Nexo.Api.dll
```

Os corpos de upload são `multipart/form-data` montados à mão: campo `context`, campo `file` com a assinatura PNG e zeros até o tamanho do caso. Enviados com `curl -H 'Expect:' --data-binary @corpo`, com ou sem `-H 'Transfer-Encoding: chunked'`.

Testes filtrados:

```bash
dotnet test nexo-backend/tests/Nexo.IntegrationTests -c Release --filter "FullyQualifiedName~RateLimitingTests|FullyQualifiedName~CookieSecurityTests|FullyQualifiedName~JwtStartupGuardTests"
```

## 3. Medições

Valores de token nunca aparecem: só o tamanho. Segredos locais foram criados para a medição e descartados.

### M1. `Set-Cookie` real, por ambiente (Kestrel local)

| Ambiente | Cookie e fluxo | Atributos medidos |
|---|---|---|
| `Production` e `Staging` | `nexo_access`, em login, refresh e switch-store | `expires=+15 min; path=/; secure; samesite=lax; httponly` (valor de 943 caracteres) |
| `Production` e `Staging` | `nexo_refresh`, em login, refresh e switch-store | `expires=+7 dias; path=/; secure; samesite=lax; httponly` (valor de 489 caracteres) |
| `Production` e `Staging` | Os dois, no logout | Valor vazio, `expires` no passado, mesmas flags |
| `Development` | Os dois, em todos os fluxos acima | `path=/; samesite=strict; httponly`, **sem `secure`** |
| Todos | Os dois, em `sessions/revoke-all` | `secure; samesite=lax; httponly`, constante no código (linhas 520 a 524) |

Sem cookie nenhum: login errado (401), refresh inválido (401), login de operador da plataforma, nos dois caminhos (`/api/auth/login` e `/api/platform/auth/login`).

No mesmo login, em `Staging`:

- o corpo traz `accessToken` e `refreshToken`, **idênticos** aos valores dos cookies (comparação exata: verdadeiro nos dois);
- `GET /api/auth/me` só com o cookie, sem `Authorization`, responde 200: a API aceita o cookie quando o cliente o envia;
- o `Swagger` responde 404 em `Production`, 200 em `Staging` e `Development`.

`mfa/verify` não foi executado: usa o mesmo método `SignedIn` do login (linhas 83 e 157).

### M2. Staging, sem autenticação (`backend-staging-d8d0`, `main-staging-22f4`)

| Pedido | Resultado |
|---|---|
| `GET /` no app | `content-security-policy: frame-ancestors 'none'`, **sem `script-src`**, igual ao `Caddyfile`. HSTS, `nosniff` e `X-Frame-Options: DENY` presentes |
| `GET /health` na API | 200 |
| `GET /swagger/index.html` na API | 200, logo o ambiente não é `Production` (coerente com `Staging`, mas não distingue de `Development`) |
| `GET /api/auth/me` sem token | 401, `www-authenticate: Bearer`, sem `Set-Cookie` |
| `POST /api/auth/login` com usuário inexistente (**1 tentativa das 2 permitidas**) | 401, **sem `Set-Cookie`** |
| Pacote JS principal do app (`index-BtejPHnr.js`, 200.897 bytes) | `nexo:refresh_token` presente, 18 usos de `localStorage`, zero `credentials:"include"` |

Um `Set-Cookie` real de staging exigiria login bem-sucedido, fora do combinado. Por isso M1 é a medição do caminho de código.

### M3. O navegador guarda esses cookies? (Chrome headless, API real local)

Página servida de `http://localhost:8080`, chamando a API em `http://localhost:5077`, em modo `Staging`. É a mesma relação de produção: origens diferentes, mesmo site (`erp.orken.com.br` e `api.erp.orken.com.br`).

| Passo | Resultado |
|---|---|
| A. Login com `fetch` sem `credentials`, como `api-client.ts:313` | 200, tokens no corpo |
| B. `GET /me` com `credentials:"include"`, sem `Authorization` | **401: o navegador não guardou o cookie** |
| C. `document.cookie` contém `nexo_` | Não |
| C2. `GET /me` com `Authorization` do token do corpo | 200: é assim que o SPA autentica |
| D. Controle: login com `credentials:"include"` | 200 |
| E. `GET /me` com `credentials:"include"`, sem `Authorization` | **200: agora o cookie foi guardado e enviado** |
| E2. `GET /me` com `fetch` sem `credentials` | 401 |
| F. `document.cookie` contém `nexo_` depois do controle | Não: `HttpOnly` funciona |
| G. Refresh token em `localStorage` lido por script | Sim |

Em código, em 440b084: nenhum `credentials: "include"` em `nexo-main/src` fora de `src/test`; os tokens vão para `localStorage` em `setTokens` (`api-client.ts:162-166`), chamado por `authService.ts:76`, e o comentário em `api-client.ts:143` diz que não há cópia em memória.

`document.cookie` só é escrito em `components/ui/sidebar.tsx:68`, com o estado aberto ou fechado da barra lateral. Não há credencial ali, e não está entre os 17 hotspots.

### M4. Subida real em modo estrito (`dotnet Nexo.Api.dll`, sem banco alcançável)

| Caso | Ambiente | Resultado |
|---|---|---|
| Sem `Jwt__Secret` (cai no valor de `appsettings.json`) | `Production` | **Recusa:** "is a value committed to the repository" |
| Sem `Jwt__Secret` e sem `ASPNETCORE_ENVIRONMENT` | (padrão: `Production`) | **Recusa**, mesma mensagem |
| Sem `Jwt__Secret` | `Staging` | **Recusa**, mesma mensagem |
| `Jwt__Secret` vazio, ou só espaços | `Production` | **Recusa:** "is not configured" |
| 31 caracteres aleatórios | `Production` | **Recusa:** "must be at least 32 characters" |
| Valor de desenvolvimento versionado | `Production` | **Recusa:** "committed to the repository" |
| Valor dos testes versionado | `Production` | **Recusa:** "committed to the repository" |
| Marcador `CHANGEME` | `Production` | **Recusa:** "looks like a placeholder" |
| 40 caracteres de pouca variedade | `Production` | **Recusa:** "too little variety" |
| Aleatório de 64 caracteres | `Production`, `Staging` | Passa da guarda (chega em "Applying database migrations") |
| `correct-horse-battery-staple-correct-horse` | `Production` | **Passa:** frase adivinhável, com variedade suficiente |
| Sem `Jwt__Secret` | `Development` | **Passa, com o valor público** |
| Sem `Jwt__Secret` | `Testing` | **Passa, com o valor público** |

Nenhuma mensagem contém o segredo.

Histórico do Git: `git log --all -G` nas chaves de segredo, em todas as branches, achou 3 segredos JWT, e os 3 estão na lista `KnownPublicSecrets`. Os demais candidatos são chaves de teste do Stripe, textos curtos e caminhos de arquivo em documentos.

### M5. Tokens forjados contra host local em `Staging` (segredo aleatório)

| Token | Resposta de `GET /api/auth/me` |
|---|---|
| Controle: token verdadeiro | 200 |
| Refresh token usado como access | 401, `The audience 'nexo-refresh' is invalid` |
| Payload verdadeiro assinado com o valor de `appsettings.json` | 401, `The signature key was not found` |
| Idem, com o valor de desenvolvimento | 401, idem |
| Idem, com o valor dos testes | 401, idem |
| `alg=none` | 401, `The signature is invalid` |

**Não enviei token forjado a staging nem a produção.** O combinado para staging era GET não autenticado ou login falho, e um token forjado é tentativa de entrar. A verificação equivalente em produção é a de 09/10/2026, em `docs/security/2026-10-dumps-and-jwt-secret.md`.

### M6. Limites de upload, em Kestrel de verdade

| Limite | Valor | Onde | Quando vale |
|---|---|---|---|
| Borda do Railway | Desconhecido | | Não medido |
| Kestrel `MaxRequestBodySize` | 30.000.000 bytes (padrão) | Nenhuma configuração em `Program.cs` | Até o filtro da ação rodar |
| `[RequestSizeLimit]` | 20.971.520 bytes | `StorageController.cs:91` | Na leitura do corpo, com ou sem `Content-Length` |
| `FormOptions.MultipartBodyLengthLimit` | 134.217.728 bytes (padrão) | Sem `[RequestFormLimits]` | Nunca é o que segura |
| `MaxFileSizeMb` | 10 (`appsettings.json` e padrão da classe) | `StorageController.cs:114-116` | Dentro da ação, depois do corpo inteiro |

| Caso | HTTP | Bytes enviados | Resposta |
|---|---|---|---|
| Anônimo, 50 MiB, `Content-Length` | 401 | 1,4 MB (buffer do socket) | Corpo não lido |
| Anônimo, 50 MiB, `chunked` | 401 | 1,7 MB | Corpo não lido |
| Autenticado, 1 MiB | 503 | Tudo | Passa dos limites. Sem R2 local, para em `UploadAsync` |
| Autenticado, 10 MiB exatos | 503 | Tudo | Idem |
| Autenticado, 10 MiB + 1 byte, `Content-Length` | 400 | Tudo | "Arquivo muito grande. Máximo: 10MB." |
| Autenticado, 10 MiB + 1 byte, `chunked` | 400 | Tudo | Idem |
| Autenticado, 19 MiB, `Content-Length` | 400 | Tudo | Idem |
| Autenticado, 19 MiB, `chunked` | 400 | Tudo | Idem |
| Autenticado, 21 MiB, `Content-Length` | 400 | 1,4 MB | "Request body too large. The max request body size is 20971520 bytes." |
| Autenticado, 21 MiB, `chunked` | 400 | 22 MB | Idem |
| Autenticado, 50 MiB, `Content-Length` | 400 | 1,4 MB | Idem |
| Autenticado, 50 MiB, `chunked` | 400 | **22,6 MB de 52,4 MB** | Idem: cortado perto do limite |

**Omitir o `Content-Length` não burla o limite:** o Kestrel conta os bytes do `chunked` enquanto lê. A recusa acima de 20 MiB vem como 400 de validação, sem o campo `error`. O SPA mostraria só o texto do status.

### M7. O que chega ao disco antes das verificações (`ASPNETCORE_TEMP` observado a cada 50 ms)

| Caso, a 4 MB/s | Resposta | Tempo | Pico no disco | Depois |
|---|---|---|---|---|
| 19 MiB, contexto inválido | 400 "Contexto inválido" | 4,8 s | 18,9 MiB | 0 |
| 19 MiB, contexto válido | 400 "Arquivo muito grande" | 4,8 s | 19,0 MiB | 0 |
| 19 MiB, `StorageEnabled=false` | 404 | 4,9 s | Não observado | 0 |
| 8 pedidos paralelos de 19 MiB | 400, todos | 4,8 s | **149,9 MiB** | 0 |

O tempo igual ao do envio completo mostra que a resposta só sai depois do corpo inteiro: o `IFormFile` é ligado antes de a ação rodar. O disco é limpo no fim do pedido. Nenhuma política de taxa cobre este endpoint.

### M8. O limite `credential-check` (achado fora da lista)

`POST /api/auth/verify-manager`, com 10 permissões em 5 minutos:

| Passo | Antes (440b084) | Depois da correção |
|---|---|---|
| Usuário A, 11 chamadas | 200 ×10, depois 429 | 200 ×10, depois 429 |
| Usuário B, de outro login, primeira chamada | **429** | 200 |
| Sem token nenhum | **429** | 401 |

### M9. Testes

| Execução | Resultado |
|---|---|
| Testes novos e existentes de cookies e guarda, mais os 2 de `credential-check`, com o código de produção sem mudança | 26 no total, **24 aprovados, 2 com falha**: exatamente os 2 de `credential-check` (`Expected ... not to be 429`) |
| Mesmos testes com a correção, mais `RateLimitingTests`, `StripeStartupGuardTests`, `AuthorizationTests`, `PlatformSecurityTests` e `AuthenticationLifecycleTests` | 120 no total, **120 aprovados, 0 com falha** |

## 4. Vulnerabilidades

### V1. `AZ4VBIEnlnS-lTm-6YCd`: `S2068` em `CreateTenantDrawer.tsx:64`

- **Regra e severidade:** `typescript:S2068`, MAJOR, impacto de segurança MEDIUM. Palavras da regra: `password,pwd,passwd,passphrase`.
- **Local:** `nexo-main/src/modules/platform/components/CreateTenantDrawer.tsx:64`, colunas 54 a 67.
- **Trecho:**
  ```ts
  if (!form.adminPassword.trim()) e.adminPassword = "Obrigatório";
  ```
- **O que a regra teme:** uma senha escrita no código, que vai junto para quem lê o repositório ou o pacote.
- **Fluxo de dados:** o literal é a mensagem de erro do campo. `e` é `typeof errors`, um `Partial<Record<keyof CreateTenantInput, string>>` (linhas 46 e 57). `setErrors(e)` guarda em outro estado, e o único uso é `error={errors.adminPassword}` no rótulo do campo (linha 182). A senha real é `form.adminPassword`, digitada em `<input type="password">` (linhas 183 e 184). Só `form` vai para a API (`createMut.mutateAsync(form)`, linha 73), e o formulário começa vazio (linha 42). A tela é da plataforma, e o endpoint exige `PlatformAdmin`.
- **Evidência:** leitura de código. As colunas 54 a 67 da linha são exatamente `"Obrigatório"`, 13 caracteres. O alvo da atribuição se chama `adminPassword`, e é isso que dispara a regra. A linha 65 atribui "Mínimo 6 caracteres" ao mesmo campo e não foi acusada; provavelmente porque tem espaços, o que não confirmei no analisador. Não rodei teste de frontend: esta árvore não tem `node_modules`, e `CreateTenantDrawer.test.tsx` não cobre este campo.
- **Recomendação:** **False Positive.** É uma mensagem de validação e nunca é usada como credencial.
- **Confiança:** alta. **O que mudaria:** só alguém passar a enviar `errors` no lugar de `form`.
- **Observação à parte:** `CreateTenant` não valida o tamanho de `AdminPassword` no servidor; o mínimo de 6 é só da tela (`PlatformController.cs:269`). O risco é baixo: só `PlatformAdmin` chega lá, e o usuário nasce com `requirePasswordChange: true`.

### V2. `AZ4VBId4lnS-lTm-6YJ3`: `S6781` em `JwtTokenService.cs:192`

- **Regra e severidade:** `csharpsquid:S6781`, BLOCKER, impacto de segurança BLOCKER, marca `symbolic-execution`.
- **Local:** `nexo-backend/src/Nexo.Infrastructure/Auth/JwtTokenService.cs:192`, colunas 15 a 71.
- **Trecho:**
  ```csharp
  var secret = _config["Jwt:Secret"] ?? throw new InvalidOperationException(...);
  if (secret.Length < 32) throw new InvalidOperationException(...);
  return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
  ```
- **O que a regra teme:** a chave que assina os JWT estar em código ou em arquivo de configuração. Quem a lê forja tokens de qualquer usuário, inclusive super-admin da plataforma.
- **Fluxo de dados:** `Jwt:Secret` vem de `IConfiguration`, com a variável `Jwt__Secret` acima de `appsettings.json`. O arquivo versionado traz um placeholder de 68 caracteres que começa com `CHANGE_THIS_IN_PRODUCTION`; é o que o analisador enxerga. `Program.cs:73-77` lê a chave, chama `JwtSecretPolicy.EnsureSafe` antes de `builder.Build()` e valida os tokens recebidos com `ValidateIssuerSigningKey`, emissor, audiência e validade. Roda em todo login, refresh e token de plataforma, em produção. `MfaSecretProtector` também deriva dela por HKDF, se `Mfa:EncryptionKey` não existir.
- **Evidência:**
  - medição M4: em `Production` e `Staging` a API não sobe sem segredo, nem com ele vazio, curto, versionado, com marcador ou de pouca variedade;
  - medição M5: tokens assinados com os valores versionados são recusados por um host com segredo próprio; `alg=none` e refresh usado como access também;
  - histórico do Git: os 3 segredos JWT já versionados estão na lista de recusa;
  - teste novo `JwtStartupGuardTests`: a recusa em `Program.cs`, na subida real, e não só a regra isolada;
  - documento de 09/10/2026: produção recusou tokens assinados com os valores versionados;
  - staging: `/health` responde 200, e o documento de staging diz `ASPNETCORE_ENVIRONMENT=Staging`, em que a guarda vale.
- **Recomendação:** **False Positive.** O valor no arquivo é um placeholder público, que a API se recusa a usar em ambiente real.
- **Confiança:** alta sobre o mecanismo; média sobre o valor atual em produção, que não li nem testei aqui. **O que mudaria:**
  - `ASPNETCORE_ENVIRONMENT=Development` ou `Testing` em host real: a guarda não vale, e a API sobe com o valor público (M4). Sem a variável, o padrão é `Production`, que recusa;
  - um segredo real adivinhável: a guarda só testa tamanho, variedade e valores conhecidos, e `correct-horse-battery-staple-correct-horse` passa (M4). Endurecer tem custo: uma recusa falsa derruba a API, como diz o comentário em `JwtSecretPolicy.cs:27-28`.
- **Alternativa que não adotei:** tirar o placeholder de `appsettings.json` atacaria a causa do achado, mas não sei se a regra sairia (o texto completo dela não está na API pública). E quebraria um fluxo: o projeto não tem `IDesignTimeDbContextFactory`, então `dotnet ef` passa pelo `Program.cs`, e uma árvore nova, sem `appsettings.Development.json` (fora do Git), deixaria de criar migrações.
- **Atenção operacional:** se o segredo vazar e for trocado sem `Mfa:EncryptionKey` definida, os segredos de MFA ficam ilegíveis e os códigos de recuperação não batem mais (`MfaSecretProtector.cs:9-15`). Vale definir `Mfa__EncryptionKey` antes de precisar.

## 5. Hotspots

Os 16 hotspots de cookie estão em `nexo-backend/src/Nexo.Api/Controllers/AuthController.cs`, e todos rodam em produção. `isSecure` é sempre o mesmo cálculo:

```csharp
var isSecure = !string.Equals(HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().EnvironmentName,
    "Development", StringComparison.OrdinalIgnoreCase);
```

Ele não depende do esquema do pedido: o TLS termina no Railway, e a API sempre vê HTTP. O caso de ambiente ausente é seguro: o padrão é `Production`.

### 5.1 `S2092`: flag `Secure` (probabilidade LOW)

O que a regra teme, nos 10: o navegador enviar o cookie por HTTP sem criptografia, e alguém na rede capturar a sessão. O analisador acusa porque `Secure = isSecure` não é uma constante, e nas cópias ele não acompanha `new CookieOptions(cookieOptions)`.

#### H1. `AZ4a6msSGNX4r2SMPRKM`: linha 110

- **Trecho:** `var cookieOptions = new CookieOptions { HttpOnly = true, Secure = isSecure, SameSite = isSecure ? SameSiteMode.Lax : SameSiteMode.Strict, Path = "/", Domain = null };`
- **Fluxo de dados:** método `SignedIn`. Este objeto não é enviado diretamente: é a base copiada nas linhas 121 e 125. Quem chega: `POST /api/auth/login` (anônimo, limite `auth-login`) e `POST /api/auth/mfa/verify`. Login de plataforma não passa aqui (linhas 146 e 217).
- **Evidência:** M1, medida em `Production` e `Staging`: `secure` presente nos dois cookies derivados; ausente só em `Development`. Testes novos `Login_Cookies_AreSecure_EvenOverPlainHttp` e `Login_Cookie_Flags_FollowTheEnvironment`.
- **Recomendação:** **Safe.** A base leva `Secure` em todo ambiente que não seja `Development`.
- **Confiança:** alta. **O que mudaria:** `ASPNETCORE_ENVIRONMENT=Development` em host real.

#### H2. `AZ4a6msSGNX4r2SMPRKN`: linha 121

- **Trecho:** `var accessCookieOptions = new CookieOptions(cookieOptions) { Expires = response.AccessTokenExpiresAt };`
- **Fluxo de dados:** opções do `nexo_access` no login (linha 131). O valor é o JWT de acesso de 15 minutos.
- **Evidência:** M1: `nexo_access=<943>; expires=+15 min; path=/; secure; samesite=lax; httponly`. A cópia preserva `Secure` de fato, o que o analisador não enxerga.
- **Recomendação:** **Safe.** O `Set-Cookie` real sai com `secure`.
- **Confiança:** alta. **O que mudaria:** ambiente `Development` em host real.

#### H3. `AZ4a6msSGNX4r2SMPRKO`: linha 125

- **Trecho:** `var refreshCookieOptions = new CookieOptions(cookieOptions) { Expires = DateTimeOffset.UtcNow.AddDays(7) };`
- **Fluxo de dados:** opções do `nexo_refresh` no login (linha 132). O valor é o refresh token de 7 dias, o mais sensível dos dois.
- **Evidência:** M1: `nexo_refresh=<489>; expires=+7 dias; path=/; secure; samesite=lax; httponly`.
- **Recomendação:** **Safe.** O `Set-Cookie` real sai com `secure`.
- **Confiança:** alta. **O que mudaria:** ambiente `Development` em host real.

#### H4. `AZ4a6msSGNX4r2SMPRKR`: linha 276

- **Trecho:** `var cookieOptions = new CookieOptions { HttpOnly = true, Secure = isSecure, SameSite = ..., Path = "/", Domain = null };`
- **Fluxo de dados:** método `Refresh`. Base das linhas 285 e 289. Quem chega: `POST /api/auth/refresh`, anônimo, com refresh token válido no corpo ou no cookie. Refresh de plataforma sai antes (linhas 254 a 261), sem cookie. Refresh recusado também não emite cookie (M1).
- **Evidência:** M1, fluxo de refresh, nos dois cookies. O teste `Refresh_RotatedCookies_StillHaveCorrectFlags` passou a afirmar `secure`.
- **Recomendação:** **Safe.** Mesmo cálculo e mesmo resultado medido do login.
- **Confiança:** alta. **O que mudaria:** ambiente `Development` em host real.

#### H5. `AZ4a6msSGNX4r2SMPRKS`: linha 285

- **Trecho:** `var accessCookieOptions = new CookieOptions(cookieOptions) { Expires = result.AccessTokenExpiresAt };`
- **Fluxo de dados:** `nexo_access` novo depois da rotação (linha 294).
- **Evidência:** M1: `nexo_access=<943>; expires=+15 min; path=/; secure; samesite=lax; httponly`.
- **Recomendação:** **Safe.** O `Set-Cookie` real sai com `secure`.
- **Confiança:** alta. **O que mudaria:** ambiente `Development` em host real.

#### H6. `AZ4a6msSGNX4r2SMPRKT`: linha 289

- **Trecho:** `var refreshCookieOptions = new CookieOptions(cookieOptions) { Expires = result.RefreshTokenExpiresAt };`
- **Fluxo de dados:** `nexo_refresh` novo depois da rotação (linha 295). A validade vem de `Jwt:RefreshTokenDays`.
- **Evidência:** M1: `nexo_refresh=<489>; expires=+7 dias; path=/; secure; samesite=lax; httponly`.
- **Recomendação:** **Safe.** O `Set-Cookie` real sai com `secure`.
- **Confiança:** alta. **O que mudaria:** ambiente `Development` em host real.

#### H7. `AZ4bKVyxI4QQ-TKL4Vyf`: linha 382

- **Trecho:** `var cookieOptions = new CookieOptions { HttpOnly = true, Secure = isSecure, SameSite = ..., Path = "/", Domain = null };`
- **Fluxo de dados:** método `SwitchStore`. Base das linhas 391 e 393. Quem chega: `POST /api/auth/switch-store`, com `[Authorize]` e `[DenyDuringImpersonation]`, e refresh token válido da própria sessão.
- **Evidência:** M1, fluxo de switch-store. Antes não havia teste deste fluxo; agora há `SwitchStore_Cookies_CarryEveryFlag`.
- **Recomendação:** **Safe.** Mesmo cálculo, resultado medido, e agora coberto por teste.
- **Confiança:** alta. **O que mudaria:** ambiente `Development` em host real.

#### H8. `AZ4bKVyxI4QQ-TKL4Vyg`: linha 391

- **Trecho:** `new CookieOptions(cookieOptions) { Expires = result.AccessTokenExpiresAt });`
- **Fluxo de dados:** `nexo_access` com a nova loja nas claims (linha 390).
- **Evidência:** M1: `nexo_access=<943>; expires=+15 min; path=/; secure; samesite=lax; httponly`.
- **Recomendação:** **Safe.** O `Set-Cookie` real sai com `secure`.
- **Confiança:** alta. **O que mudaria:** ambiente `Development` em host real.

#### H9. `AZ4bKVyxI4QQ-TKL4Vyh`: linha 393

- **Trecho:** `new CookieOptions(cookieOptions) { Expires = result.RefreshTokenExpiresAt });`
- **Fluxo de dados:** `nexo_refresh` novo da troca de loja (linha 392).
- **Evidência:** M1: `nexo_refresh=<489>; expires=+7 dias; path=/; secure; samesite=lax; httponly`.
- **Recomendação:** **Safe.** O `Set-Cookie` real sai com `secure`.
- **Confiança:** alta. **O que mudaria:** ambiente `Development` em host real.

#### H10. `AZ4a6msSGNX4r2SMPRKU`: linha 456

- **Trecho:** `var cookieOptions = new CookieOptions { HttpOnly = true, Secure = isSecure, ..., Expires = DateTimeOffset.UtcNow.AddSeconds(-1) };`
- **Fluxo de dados:** método `Logout`, com `[Authorize]`. Usado direto nas linhas 465 e 466, com valor vazio e validade no passado. Não há credencial no cookie: o objetivo é apagar o anterior.
- **Evidência:** M1: valor de 0 caracteres, `expires` no passado, `secure; samesite=lax; httponly`. O teste `Logout_DeletedCookies_HaveConsistentFlags_WithLoginCookies` passou a afirmar `secure`.
- **Recomendação:** **Safe.** O cookie vai vazio, com as mesmas flags do que ele apaga.
- **Confiança:** alta. **O que mudaria:** nada realista: mesmo em `Development`, o valor é vazio.

### 5.2 `S3330`: flag `HttpOnly` (probabilidade LOW)

O que a regra teme, nos 6: um script injetado (XSS) ler o cookie e roubar a sessão. O analisador só acusa as cópias: nas linhas 110, 276, 382 e 456 o `HttpOnly = true` é literal, e `S3330` não aparece ali.

**Vale para os 6, e é o ponto mais importante deste documento.** A flag está lá, e funciona (M3, passos C e F). Mas o medo da regra continua de pé por outro caminho: o token que este cookie carregaria está no corpo da resposta e em `localStorage` (M1, M3 passo G). Um script injetado nem precisa do cookie. As 6 recomendações abaixo são sobre a flag; o risco de sessão está em D1 (seção 6).

#### H11. `AZ4a6msSGNX4r2SMPRKK`: linha 121

- **Trecho:** `var accessCookieOptions = new CookieOptions(cookieOptions) { Expires = response.AccessTokenExpiresAt };`
- **Fluxo de dados:** `nexo_access` do login. O cookie guarda um JWT de 15 minutos com `sub`, `tenantId`, nome, papel, `security_stamp`, módulos e lojas (`JwtTokenService.cs:113-139`). Um script que o lesse chamaria a API como essa pessoa por até 15 minutos.
- **Evidência:** M1: `httponly` presente. M3: `document.cookie` não mostra o cookie, mesmo com ele guardado. Teste existente `Login_SetsCookies_WithHttpOnlyFlag`.
- **Recomendação:** **Safe.** A cópia preserva `HttpOnly`, e o navegador o esconde de script.
- **Confiança:** alta sobre a flag. **O que mudaria:** nada no cookie. O risco real é D1.

#### H12. `AZ4a6msSGNX4r2SMPRKL`: linha 125

- **Trecho:** `var refreshCookieOptions = new CookieOptions(cookieOptions) { Expires = DateTimeOffset.UtcNow.AddDays(7) };`
- **Fluxo de dados:** `nexo_refresh` do login. Guarda um JWT com `sub`, `jti`, `userId`, `tenantId` e `fid`, a família da sessão (`JwtTokenService.cs:154-163`). Um script que o lesse renovaria a sessão por até 7 dias, até a reutilização derrubar a família.
- **Evidência:** M1 e M3, como em H11. Teste `Login_SetsCookies_WithHttpOnlyFlag`.
- **Recomendação:** **Safe.** A cópia preserva `HttpOnly`.
- **Confiança:** alta sobre a flag. **O que mudaria:** nada no cookie. O risco real é D1: **este mesmo token está em `localStorage["nexo:refresh_token"]`**.

#### H13. `AZ4a6msSGNX4r2SMPRKP`: linha 285

- **Trecho:** `var accessCookieOptions = new CookieOptions(cookieOptions) { Expires = result.AccessTokenExpiresAt };`
- **Fluxo de dados:** `nexo_access` depois da rotação, mesmo conteúdo de H11.
- **Evidência:** M1: `httponly` presente. Teste `Refresh_RotatedCookies_StillHaveCorrectFlags`.
- **Recomendação:** **Safe.** A cópia preserva `HttpOnly`.
- **Confiança:** alta sobre a flag. **O que mudaria:** nada no cookie. O risco real é D1.

#### H14. `AZ4a6msSGNX4r2SMPRKQ`: linha 289

- **Trecho:** `var refreshCookieOptions = new CookieOptions(cookieOptions) { Expires = result.RefreshTokenExpiresAt };`
- **Fluxo de dados:** `nexo_refresh` depois da rotação, mesmo conteúdo de H12.
- **Evidência:** M1: `httponly` presente. Teste `Refresh_RotatedCookies_StillHaveCorrectFlags`.
- **Recomendação:** **Safe.** A cópia preserva `HttpOnly`.
- **Confiança:** alta sobre a flag. **O que mudaria:** nada no cookie. O risco real é D1.

#### H15. `AZ4bKVyxI4QQ-TKL4Vyd`: linha 391

- **Trecho:** `new CookieOptions(cookieOptions) { Expires = result.AccessTokenExpiresAt });`
- **Fluxo de dados:** `nexo_access` da troca de loja, mesmo conteúdo de H11 com a nova loja.
- **Evidência:** M1: `httponly` presente. Teste novo `SwitchStore_Cookies_CarryEveryFlag`.
- **Recomendação:** **Safe.** A cópia preserva `HttpOnly`.
- **Confiança:** alta sobre a flag. **O que mudaria:** nada no cookie. O risco real é D1.

#### H16. `AZ4bKVyxI4QQ-TKL4Vye`: linha 393

- **Trecho:** `new CookieOptions(cookieOptions) { Expires = result.RefreshTokenExpiresAt });`
- **Fluxo de dados:** `nexo_refresh` da troca de loja, mesmo conteúdo de H12.
- **Evidência:** M1: `httponly` presente. Teste novo `SwitchStore_Cookies_CarryEveryFlag`.
- **Recomendação:** **Safe.** A cópia preserva `HttpOnly`.
- **Confiança:** alta sobre a flag. **O que mudaria:** nada no cookie. O risco real é D1.

### 5.3 `S5693`: tamanho do pedido (probabilidade MEDIUM)

#### H17. `AZ7NEKGaGt1PCrxXzOLh`: `StorageController.cs:91`

- **Local:** `nexo-backend/src/Nexo.Api/Controllers/Integrations/StorageController.cs:91`.
- **Trecho:**
  ```csharp
  [HttpPost("upload")]
  [Authorize(Roles = AppRoles.Stock)]   // the widest set; each context narrows it below
  [RequestSizeLimit(20 * 1024 * 1024)]
  public async Task<IActionResult> Upload(IFormFile file, [FromForm] string context, CancellationToken ct)
  ```
- **O que a regra teme:** pedidos grandes esgotarem memória, disco ou conexões antes de a aplicação recusar. O limite recomendado é 8 MB para upload, e o exemplo da própria regra trata 10 MiB como fora do limite.
- **Fluxo de dados:** a controller inteira exige `[Authorize]`, e a ação exige `Gerente`, `Diretoria` ou `Estoquista`. Anônimo recebe 401 sem o corpo ser lido. Autenticado: o `IFormFile` é ligado antes da ação, e o corpo vai para disco temporário acima de 64 KiB. Só então a ação testa flag, contexto, papel fino, bucket privado, tipo, tamanho e assinatura. Roda em produção quando `Integrations:Features:StorageEnabled` está ligado; o arquivo versionado deixa desligado, e o valor real não foi lido. As telas limitam a 10 MiB (`ImageUploadButton`, `PortalImageUpload`, `PrivateUploadButton`, `SelectionOptionDialog`), exceto `handleUploadPhoto` em `BuildProjectDetailPage.tsx:394`.
- **Evidência:** M6 e M7, em Kestrel de verdade.
  - Sem `Content-Length` não burla: um `chunked` de 50 MiB é cortado perto de 20 MiB.
  - O arquivo acima de 10 MiB é recusado com `Content-Length` e com `chunked`.
  - Até 20 MiB são escritos em disco antes de qualquer verificação da ação, inclusive com a flag desligada ou contexto inválido. O disco é limpo no fim do pedido.
  - Sem limite de taxa: 8 pedidos paralelos chegaram a 150 MiB.
  - Nenhum middleware lê o corpo antes do filtro, então o limite não fica `IsReadOnly`: `Request.Body` e `ReadFormAsync` só aparecem em `BillingController` e `PublicBuildPortalController`.
  - O comentário em `StorageController.cs:106` dizia "refused before the file is even read", e isso era falso. Corrigido nesta branch.
- **Recomendação:** **Safe, com teto medido.** O corpo tem teto real de 20 MiB em todos os modos de envio, só depois de autenticar com um papel de operação, e o disco é devolvido no fim do pedido.
- **Confiança:** alta sobre o Kestrel; a borda do Railway não foi medida. **O que mudaria:** muitas contas abusivas ou um disco temporário muito pequeno. Nesse caso, o próximo passo é um limite de taxa por usuário, que agora funciona (M8).
- **Sem confirmar `MaxFileSizeMb` em produção:** a revalidação anterior condicionava o Safe a isso. Não precisa: o teto de 20 MiB não depende dessa configuração, que só muda onde o arquivo é recusado.
- **Por que não reduzi o limite:** 8 MiB é o que satisfaz a regra, mas o produto promete 10 MiB em 4 telas. 11 MiB (10 + 1 de sobrecarga do multipart, o mesmo padrão de `BuildUploadLimitAttribute`) reduziria o pior caso à metade, e o hotspot continuaria aberto. Não posso garantir que não quebra nada: `handleUploadPhoto` não tem limite na tela, e não sei o valor de `MaxFileSizeMb` em produção. Ver D2.

## 6. Decisões do dono

**D1. Onde a sessão mora.** Hoje: `localStorage`, com refresh de 7 dias, sem `script-src` na CSP (`Caddyfile`: "No full CSP", por causa de PDFs em `<object>` e WebAssembly do visor IFC). Os tokens de operador da plataforma ficam no mesmo lugar; os de acesso assistido, em `sessionStorage`. Uma falha de XSS em qualquer tela entrega a sessão inteira. Atenuantes: rotação com detecção de reuso, e React escapando por padrão (o único `dangerouslySetInnerHTML` é o `chart.tsx` do shadcn).

| Opção | O que muda | Custo |
|---|---|---|
| A. Aceitar e registrar | Nada | Risco continua, agora documentado |
| B. CSP com `script-src` | Reduz a chance de script injetado rodar. Testar antes em `Content-Security-Policy-Report-Only` | Baixo, mas precisa percorrer PDF, IFC e portais em navegador real |
| C. Refresh só em cookie `HttpOnly`, com `credentials: "include"` | É o que dá efeito real às flags | Alto: troca o fluxo de várias abas, exige análise de CSRF, e staging em `*.up.railway.app` deixaria de funcionar sem domínio próprio |

Recomendo B agora e C como trabalho planejado.

**Sobre os cookies hoje:** nenhum navegador usando o SPA os guarda (M3). Se C não for feito, removê-los tira 16 hotspots de verdade e apaga superfície sem uso. Não removi porque depende de D1, e porque precisaria confirmar que nenhum cliente externo os usa.

**D2. Teto de upload.** Manter 20 MiB, baixar para 11 MiB (com limite de tela em `handleUploadPhoto` e depois de conferir `MaxFileSizeMb`) ou para 8 MiB (muda o produto).

**D3. Fixar `Mfa__EncryptionKey`.** Ver V2.

## 7. Achados fora dos 19

| Achado | Situação |
|---|---|
| **`credential-check` global.** `UseRateLimiter` rodava antes de `UseAuthentication` (`Program.cs:516-517`), e `httpContext.User` estava vazio: toda chamada caía em `cred\|anonymous\|<caminho>`. Chamadas sem token gastavam a mesma cota, então bastavam 10 por 5 minutos para qualquer pessoa, sem conta, travar a autorização de gerente em todo o sistema (`verify-manager`, `validate-manager`) e a configuração de MFA. `change-password` tem o id no caminho, então o bloqueio ali é por usuário. `docs/security/rbac-matrix.md` dizia o contrário. Mesmo código de produção; medido em host local (M8), não em staging nem produção | **Corrigido** em `fix(auth)`, com testes que falharam antes. As outras políticas de taxa usam IP e não mudam |
| `RecipeCardsController.UploadImage` não tem `[RequestSizeLimit]` (vale o padrão de 30 MB do Kestrel), confere 5 MB só depois de ler, não confere assinatura e usa a extensão do nome enviado para gravar em `wwwroot`. `UseStaticFiles` não existe no pipeline | Só leitura de código. Não medido, não corrigido |
| `auth-login` prefere o primeiro `X-Forwarded-For` (`Program.cs:232-235`), como o próprio comentário avisa | Não examinado: exigiria mais tentativas de login do que o combinado |
| Acima de 20 MiB, a resposta de upload não traz `error`, e o SPA mostra só o texto do status | Medido em M6. Só alcançável por `handleUploadPhoto` |

## 8. Declarações anteriores que a evidência contradiz

| Onde | O que dizia | O que a evidência mostra | Situação |
|---|---|---|---|
| `2026-11-04-sonarcloud-revalidacao.md`, seções 2 e 5 | `S2068` em `CreateTenantDrawer.tsx:63` | Linha 64 | Corrigido |
| Idem, 7.5 | `S5144`: aguardar a #152 | Já saiu: a análise de 05/11 tem só 2 vulnerabilidades, e `S5144` não é uma delas | Corrigido |
| Idem, 7.5 | `S5693`: Safe "depois de confirmar `MaxFileSizeMb`" | O teto de 20 MiB não depende disso | Corrigido |
| Idem, 7.5 | `S5693`: "arquivo limitado a 10 MB" | Verdade para o que é guardado. O servidor aceita e grava até 20 MiB antes de recusar | Corrigido |
| Idem, seções 2, 5 e 7.5 | Cookies "Safe" sem ressalva | A flag está certa, mas a sessão não depende dela (D1) | Corrigido |
| `docs/engineering/staging-e-promocao.md`, "Domínios" | O cookie `nexo_refresh` "não acompanha as chamadas", "token em memória", recarregar encerra a sessão | O SPA não usa o cookie em nenhum ambiente e guarda os tokens em `localStorage`. Pelo código, recarregar mantém a sessão. Não testei com login real | Corrigido |
| `docs/security/rbac-matrix.md:81-83` | `credential-check` "particionado pelo usuário autenticado" | Falso em 440b084, verdadeiro depois do `fix(auth)` | Código corrigido |
| `CookieSecurityTests.Login_Cookies_SecureFlag_MatchesRequestScheme` | "the code should dynamically detect IsHttps" | Vem do nome do ambiente. O teste não tinha asserção em HTTP, ou seja, nenhum teste cobria `Secure` | Substituído |
| `StorageController.cs:106` | "refused before the file is even read" | O corpo já foi gravado em disco (M7) | Corrigido |
| `nexo-main/Caddyfile`, bloco `@erpEntry` | "The session lives in the API's cookie" | Vive em `localStorage` | **Não mudei:** o texto certo depende de D1 |
| `nexo-main/e2e/auth.e2e.spec.ts:73-77` e `:176` | Espera `nexo_access` no armazenamento do navegador | M3 indica que não estaria lá com origens diferentes. O arquivo não roda no CI (revalidação, 7.4) | **Não mudei:** não executei esse arquivo |

O que a evidência **confirmou:** `HttpOnly` em todo fluxo; `Secure` fora de `Development`; o analisador não acompanha a cópia; `JwtSecretPolicy` recusa a subida em modo estrito; o literal de `S2068` é a mensagem "Obrigatório".

## 9. Tabela para o painel

Vulnerabilidades em *Issues*; hotspots em *Security Hotspots*. Depois do merge, confira que a análise do `master` mantém estes 19.

| # | Chave | Regra | Local | Ação | Justificativa para colar |
|---:|---|---|---|---|---|
| V1 | `AZ4VBIEnlnS-lTm-6YCd` | `S2068` | `CreateTenantDrawer.tsx:64` | False Positive | Mensagem de validação "Obrigatório" atribuída ao erro do campo; a senha é `form.adminPassword`, digitada pelo operador. |
| V2 | `AZ4VBId4lnS-lTm-6YJ3` | `S6781` | `JwtTokenService.cs:192` | False Positive | O valor em `appsettings.json` é placeholder público; `JwtSecretPolicy` recusa a subida com ele fora de Development/Testing (medido). O segredo real vem de `Jwt__Secret`. |
| H1 | `AZ4a6msSGNX4r2SMPRKM` | `S2092` | `AuthController.cs:110` | Safe | Base dos cookies do login: `Secure` fora de Development, porque o TLS termina no proxy. Medido em Production e Staging. |
| H2 | `AZ4a6msSGNX4r2SMPRKN` | `S2092` | `AuthController.cs:121` | Safe | `nexo_access` do login: a cópia preserva `Secure`. Set-Cookie medido com `secure`. |
| H3 | `AZ4a6msSGNX4r2SMPRKO` | `S2092` | `AuthController.cs:125` | Safe | `nexo_refresh` do login: a cópia preserva `Secure`. Set-Cookie medido com `secure`. |
| H4 | `AZ4a6msSGNX4r2SMPRKR` | `S2092` | `AuthController.cs:276` | Safe | Base dos cookies do refresh: mesmo cálculo do login. Medido em Production e Staging. |
| H5 | `AZ4a6msSGNX4r2SMPRKS` | `S2092` | `AuthController.cs:285` | Safe | `nexo_access` da rotação: a cópia preserva `Secure`. Set-Cookie medido com `secure`. |
| H6 | `AZ4a6msSGNX4r2SMPRKT` | `S2092` | `AuthController.cs:289` | Safe | `nexo_refresh` da rotação: a cópia preserva `Secure`. Set-Cookie medido com `secure`. |
| H7 | `AZ4bKVyxI4QQ-TKL4Vyf` | `S2092` | `AuthController.cs:382` | Safe | Base dos cookies do switch-store: mesmo cálculo do login. Medido e coberto por teste. |
| H8 | `AZ4bKVyxI4QQ-TKL4Vyg` | `S2092` | `AuthController.cs:391` | Safe | `nexo_access` do switch-store: a cópia preserva `Secure`. Set-Cookie medido com `secure`. |
| H9 | `AZ4bKVyxI4QQ-TKL4Vyh` | `S2092` | `AuthController.cs:393` | Safe | `nexo_refresh` do switch-store: a cópia preserva `Secure`. Set-Cookie medido com `secure`. |
| H10 | `AZ4a6msSGNX4r2SMPRKU` | `S2092` | `AuthController.cs:456` | Safe | Logout: valor vazio, validade no passado e `secure` medido. |
| H11 | `AZ4a6msSGNX4r2SMPRKK` | `S3330` | `AuthController.cs:121` | Safe | `nexo_access` do login: a cópia preserva `HttpOnly` (medido). Risco de XSS na sessão é outro: tokens em localStorage, ver D1 em docs/audits/2026-11-05-sonarcloud-seguranca-evidencias.md. |
| H12 | `AZ4a6msSGNX4r2SMPRKL` | `S3330` | `AuthController.cs:125` | Safe | `nexo_refresh` do login: a cópia preserva `HttpOnly` (medido). Risco de XSS na sessão é outro: ver D1 no mesmo documento. |
| H13 | `AZ4a6msSGNX4r2SMPRKP` | `S3330` | `AuthController.cs:285` | Safe | `nexo_access` da rotação: a cópia preserva `HttpOnly` (medido). Risco de XSS na sessão é outro: ver D1 no mesmo documento. |
| H14 | `AZ4a6msSGNX4r2SMPRKQ` | `S3330` | `AuthController.cs:289` | Safe | `nexo_refresh` da rotação: a cópia preserva `HttpOnly` (medido). Risco de XSS na sessão é outro: ver D1 no mesmo documento. |
| H15 | `AZ4bKVyxI4QQ-TKL4Vyd` | `S3330` | `AuthController.cs:391` | Safe | `nexo_access` do switch-store: a cópia preserva `HttpOnly` (medido). Risco de XSS na sessão é outro: ver D1 no mesmo documento. |
| H16 | `AZ4bKVyxI4QQ-TKL4Vye` | `S3330` | `AuthController.cs:393` | Safe | `nexo_refresh` do switch-store: a cópia preserva `HttpOnly` (medido). Risco de XSS na sessão é outro: ver D1 no mesmo documento. |
| H17 | `AZ7NEKGaGt1PCrxXzOLh` | `S5693` | `StorageController.cs:91` | Safe | Teto real de 20 MiB, com e sem Content-Length (medido); só depois de autenticar com papel de operação; arquivo recusado acima de 10 MiB; disco devolvido no fim do pedido. Reduzir é a decisão D2. |

Com os 17 hotspots revisados, a condição "Hotspots revisados" do gate vai a 100%. O gate continua vermelho por confiabilidade e cobertura, que não são assunto deste documento.

## 10. O que não foi verificado

- **`Set-Cookie` real em staging ou produção:** exigiria login bem-sucedido. Só o login falho foi feito (1 tentativa, sem cookie). M1 mede o mesmo código em host local.
- **Valores reais de `Jwt__Secret`, `ASPNETCORE_ENVIRONMENT`, `MaxFileSizeMb`, `StorageEnabled` e `Mfa__EncryptionKey`:** variáveis do Railway ficaram fora deste exame. Nenhum pedido foi feito a produção. Para o ambiente, valem `docs/engineering/staging-e-promocao.md` e o fato de staging expor o Swagger.
- **Token forjado em staging:** não enviado (M5). Para conferir, o dono pode repetir a verificação de 09/10/2026.
- **Limite de corpo na borda do Railway e HTTP/2:** as medições são HTTP/1.1 direto no Kestrel.
- **`mfa/verify` em execução:** só leitura de código, porque usa o mesmo `SignedIn`.
- **Testes de frontend:** não rodados. `S2068` é conclusão de leitura de código.
- **Texto completo de `S6781`:** a API pública não devolve a descrição sem login. Usei nome, mensagem e marcas.
- **Efeito no SonarCloud:** esta branch não foi reanalisada antes de abrir a PR. As linhas acusadas não mudaram.
- **Suíte completa:** só testes filtrados, por causa do espaço em disco.
