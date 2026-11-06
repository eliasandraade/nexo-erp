# Sessão: auditoria e migração para cookie

Estado em `bd34d08`. Este documento é o contrato das duas mudanças de autenticação que vêm depois dele: a política de renovação
e o cookie de refresh. Nenhuma das duas vai para a produção sem revisão humana específica.

## 1. O que existe hoje

| Item | Onde fica | Duração | Observação |
|---|---|---|---|
| Access token (JWT) | `localStorage`, `nexo:access_token` | 15 min | Qualquer script da página lê |
| Refresh token (JWT, com família e rotação) | `localStorage`, `nexo:refresh_token` | 7 dias | Qualquer script da página lê. É o maior risco |
| Sessão (nome, papel, módulos) | `localStorage`, `nexo:session` | Junto com o token | Não é credencial, mas tem dados pessoais |
| Access da impersonação | `sessionStorage` da aba | Curta, sem refresh | A entrega entre abas usa `BroadcastChannel` com nonce |
| Cookies `nexo_access` e `nexo_refresh` | O navegador descarta | — | A API envia `HttpOnly`, `Secure`, `SameSite=Lax`, mas o `fetch` do app não usa `credentials` |

**O que a API aceita:** `Authorization: Bearer`; o `access_token` na query só em `/hubs`; e, sem header, o cookie `nexo_access`
(`Program.cs`, `OnMessageReceived`). `refresh`, `switch-store` e `logout` aceitam o token no corpo ou no cookie `nexo_refresh`.

**Proteção da rotação:** cada refresh gera um par novo na mesma família. O token anterior vale como corrida entre abas por
`RotationGrace` (409 `refresh_already_rotated`). Depois disso, reapresentá-lo revoga a família inteira e grava auditoria crítica.

## 2. Riscos encontrados

1. **XSS leva a sessão de 7 dias embora.** O refresh está em `localStorage` e a página não tem `script-src`. O token roubado
   funciona fora do navegador da vítima.
2. **O fallback de `nexo_access` é uma armadilha.** Hoje é inofensivo só porque o navegador descarta o cookie. No dia em que o app
   usar `credentials: "include"`, toda a API ganha autenticação ambiente e passa a depender de proteção CSRF que não existe. **Sai
   antes de qualquer cookie ser guardado.**
3. **`SameSite` não basta.** `erp.orken.com.br` e `api.erp.orken.com.br` são o mesmo site, e o mesmo vale para todo
   `*.orken.com.br`: Hub, COND, RST, PREC e Cockpit. Uma falha em qualquer um deles envia requisições "do mesmo site" e também
   pode gravar cookie em `.orken.com.br` (*cookie tossing*).
4. **Origens de desenvolvimento em produção.** O CORS inclui `http://localhost:*` em qualquer ambiente, com `AllowCredentials`.
5. **O refresh divide o limite do login.** `auth-login` (5 por 15 min por IP e rota) também protege `refresh`. Seis terminais atrás
   do mesmo IP derrubam a sessão de alguém a cada ciclo. O 429 ainda encerra a sessão no cliente.
6. **`auth-login` não usa `ClientIpResolver`.** Lê `X-Forwarded-For` por conta própria, então não obedece a
   `RateLimiting:ClientIpSource`.
7. **O login tem o mesmo limite por IP.** Seis pessoas entrando em 15 minutos pelo mesmo IP ficam bloqueadas, mesmo com a trava por
   conta (`LoginAttemptGuard`) já existindo. Fora do escopo destas mudanças: é decisão do dono (seção 8).

## 3. Sessão em cookie

### Modo

O servidor aceita os dois modos ao mesmo tempo, por requisição. O modo cookie só vale quando o cliente manda
`X-Orken-Session: cookie`. Quem não manda segue exatamente como hoje, então o cliente antigo continua funcionando durante toda a
transição. O cliente escolhe na compilação, por `VITE_SESSION_MODE=cookie`, ligado **só no staging**. O padrão é o modo atual.

### Cookie

`__Host-nexo_rt`: `HttpOnly`, `Secure`, `Path=/`, sem `Domain`, com a validade do refresh.

- **`__Host-`** impede outro subdomínio de sobrescrever o cookie, o que resolve o risco 3.
- **`SameSite=Lax`** por padrão, por `Auth:Session:CookieSameSite`. O staging usa `None` enquanto estiver em `*.up.railway.app`,
  porque esse sufixo é público e cada serviço conta como um site à parte (seção 7).
- **O access token nunca vai em cookie**, nos dois modos: `nexo_access` deixa de ser enviado e o fallback do JWT é removido.

### Endpoints de sessão

Só eles usam `credentials: "include"`: `auth/login`, `auth/mfa/verify`, `auth/verify-email`, `auth/refresh`, `auth/switch-store`,
`auth/logout`, `auth/sessions/revoke-all`, `platform/auth/login`, `platform/auth/mfa/verify`, `platform/auth/refresh`. Os outros
continuam sem cookie: a API segue autenticando só por `Bearer`, que CSRF não alcança.

No modo cookie, o corpo da resposta não traz `refreshToken`.

### CSRF, nos endpoints que leem o cookie

Três barreiras independentes, todas obrigatórias no modo cookie:

1. O header `X-Orken-Session` força o preflight de CORS, que só passa para origens da lista.
2. O servidor confere `Origin` contra a mesma lista e responde `403 origin_not_allowed`, sem ler o cookie. Sem `Origin`, recusa.
3. `SameSite`, onde couber.

### CORS

`localhost` só em `Development`. Produção: `erp.orken.com.br`, `www.orken.com.br` e `Cors:AllowedOrigins`. A lista vira uma fonte
só, usada pelo CORS e pela conferência de `Origin`.

### Access token em memória

- Cada aba guarda o access token numa variável. Um recarregamento o perde, e o boot renova pelo cookie antes de chamar a API.
- `nexo:session` continua no `localStorage`: é o que permite desenhar a tela na hora e avisar as outras abas de login e logout.
- **Saber se há sessão** deixa de ser `getAccessToken() !== null`. Tudo que usa esse teste passa a usar `hasSession()`.

### Várias abas

- `navigator.locks` serializa a renovação entre as abas, o que evita apresentar um cookie já trocado depois do prazo de carência.
- `BroadcastChannel("orken-session")` entrega o access novo às outras abas, e também login e logout. O evento `storage` em
  `nexo:session` fica como reserva.
- Sem `navigator.locks`, um 409 não encerra nada: a aba espera o token de outra aba ou o fim da carência.

### Migração de quem já está logado

Com o modo ligado, e havendo refresh no `localStorage`, o boot chama `auth/refresh` com esse token no corpo e o header do modo. A
API rotaciona, grava o cookie e não devolve o refresh. O cliente apaga os dois tokens do `localStorage` só depois de receber 200. O
caminho antigo continua no código até a equivalência estar comprovada (seção 6).

### Impersonação

Não muda. A aba usa `sessionStorage`, não tem refresh e **nunca** envia `credentials` nem o header do modo, então não toca no cookie
do administrador. O access curto em `sessionStorage` continua sendo risco residual, aceito nesta etapa.

### Falha na renovação

Só uma recusa do servidor encerra a sessão (401). Rede fora, 409, 429 e 5xx não encerram: a sessão fica, a
requisição falha com erro temporário e o cliente respeita `Retry-After`. Isso resolve os dois `test.fixme` de `auth.e2e.spec.ts`.

## 4. Política de renovação

Rota própria, `auth-refresh`, separada do login, com três camadas:

| Camada | Chave | Padrão | Em que ajuda |
|---|---|---|---|
| Teto por IP | IP de `ClientIpResolver` | 900 por 15 min | Inundação. Folga para uma loja grande: 100 terminais com renovação a cada 15 min, mais recarregamentos |
| Orçamento de falha por IP (Redis) | IP | 30 assinaturas inválidas por 15 min | Tentativa e erro. Acima disso, só o token inválido recebe 429; token válido nunca é afetado |
| Orçamento por sessão (Redis) | família do token | 300 por 15 min | Token copiado em laço. Acima disso, 429 para aquela sessão e nenhuma outra. Folga para o modo cookie, em que cada carregamento de página renova |

- **Token só vencido não conta como falha.** Conta apenas assinatura inválida ou token malformado. Senão, terminais que voltam depois
  do fim de semana gastariam a cota e, como 429 não encerra sessão, ficariam travados.
- **A reutilização continua como está:** token gasto fora da carência revoga a família e grava auditoria crítica.
- **Redis fora do ar:** os orçamentos deixam de contar (o refresh já depende do Redis para funcionar); o teto por IP é em memória e
  continua valendo.
- **`platform/auth/refresh`** usa a mesma política.
- Tudo configurável em `RateLimiting:AuthRefresh:*`.

## 5. O que muda em cada PR

1. **Política de renovação.** Backend, tratamento de 429 e rede no cliente, os dois `fixme` e `auth-login` passando a usar
   `ClientIpResolver`. Funciona sem o modo cookie.
2. **Modo cookie.** Remove o fallback de `nexo_access`, cria o cookie novo, a conferência de `Origin`, a lista única de origens e o
   cliente em modo duplo, com a compilação padrão igual a hoje.

## 6. Prova de equivalência

Mesmos cenários nos dois modos, com a API real, em `auth.e2e.spec.ts`. O modo antigo só sai depois de todos passarem no staging, de
a revisão humana aprovar e de um período em produção sem incidente.

| Fluxo | O que precisa continuar valendo |
|---|---|
| Login | Destino por papel, senha errada, conta bloqueada, inativa, não verificada |
| MFA | Código, código errado, código de recuperação uma só vez, desafio já usado |
| Recarregar | Sessão continua sem nova autenticação; o token não aparece em nenhum storage |
| Várias abas | Login e logout propagam; renovação simultânea em N abas não revoga a família |
| Troca de loja | Loja nova vale nas outras abas e sobrevive à renovação |
| Logout | Encerra no servidor, apaga o cookie, some em todas as abas |
| Impersonação | Aba isolada; sair dela não afeta o administrador, e o cookie dele não é enviado |
| Plataforma | Login, MFA, renovação e revogação de `p.` |
| Recuperação de senha, verificar e-mail | Entram e encerram como hoje |
| SignalR | Reconecta com token novo vindo do cookie; sessão encerrada fecha |
| Falhas | Rede fora, 429 e 5xx mantêm a sessão; 401 encerra sem laço |
| Migração | Sessão antiga vira cookie sem novo login; `localStorage` fica sem tokens |
| CSRF | Origem fora da lista e falta de `Origin` recebem 403 sem tocar na sessão; sem o header, o cookie não vale |
| Isolamento | Outro subdomínio não sobrescreve o cookie; nenhum token no corpo no modo cookie |

## 7. Staging e a revisão humana

Em `*.up.railway.app`, frontend e API são sites diferentes, então o cookie só viaja com `SameSite=None`. O Chrome aceita; Safari e
Firefox podem bloquear. Isso permite validar em Chromium, mas **não é o formato da produção**. Para a revisão humana valer em
qualquer navegador, são necessários os domínios `erp.staging.orken.com.br` e `api.erp.staging.orken.com.br`, cujo DNS depende do
dono, na GoDaddy.

## 8. Decisões do dono

- **Aprovar a produção:** depois da revisão, ligar `VITE_SESSION_MODE=cookie` e promover. Voltar atrás exige novo login de quem já
  migrou, porque o cliente antigo não enxerga o cookie.
- **DNS do staging** (seção 7).
- **Limite de login por IP** (risco 7). Recomendo 30 por 15 min por IP, porque a trava por conta já cobre a tentativa de senha contra
  uma pessoa. Não muda nesta etapa.
- **Sessão de 7 dias:** pode encurtar depois da migração, com renovação deslizante. Não muda nesta etapa.

## 9. O que continua em aberto depois da migração

Cookie `HttpOnly` impede levar o refresh para fora, mas não impede um XSS de agir **enquanto a página está aberta**: o script chama
`auth/refresh` pela própria aba e usa o access. Por isso a CSP (`docs/security/csp.md`) é parte da mesma defesa, e o `script-src`
precisa chegar a modo de bloqueio.

### Riscos aceitos nesta etapa

Nenhum destes muda aqui. Ficam escritos para a revisão decidir sabendo.

- **Uma queda dura do Redis pode reviver uma sessão encerrada.** Condições: o Redis perde o que gravou depois do último snapshot (`--save 60 1`, até 1 min), e o logout aconteceu nesse último minuto. A marca da família some, e um token de antes do snapshot volta a
  valer, porque `IsRevokedAsync` procura um `jti` só e a linha já está no mais novo. O mesmo acontece com um logout durante a queda se a linha estiver defasada, já que ela segue o token em melhor esforço. A base tem a mesma exposição. O que fecharia: uma coluna com a família na linha da sessão
  e a conferência por família, o que exige migração.
- **Login durante uma queda do Redis:** não há trava por conta, porque o contador mora no Redis, e a sessão aberta nesse período termina na primeira renovação depois da volta, porque sua entrada nunca chegou ao cache.
- **Tetos compartilhados:** quem está no mesmo endereço gasta o mesmo teto de renovação, de login e de logout anônimo. No modo cookie, o logout vai sem access, então conta na partição anônima do endereço.
- **Cookies grandes de um irmão comprometido:** um subdomínio de `orken.com.br` comprometido pode encher o navegador de cookies para o domínio pai, e a API responde 431 até eles saírem ou expirarem. O prefixo `__Host-` impede trocar o cookie da sessão, não isso.
