# Roteiro de homologação: sessão em cookie

Para a revisão humana no staging, exigida antes de a mudança chegar à produção
(`docs/security/sessao-auditoria-e-migracao.md`, seções 6 a 8). Cada fluxo diz o que fazer, onde olhar nas ferramentas do navegador e o
que precisa acontecer. Se um resultado não bater, o fluxo reprovou: anote o passo, o horário e o `X-Request-Id` da resposta, e não siga
para a produção.

## 1. Antes de começar

### Variáveis do staging

| Serviço | Variável | Valor | Observação |
|---|---|---|---|
| `main` (frontend) | `VITE_SESSION_MODE` | `cookie` | Entra na compilação: mudar exige novo build e nova publicação |
| `backend` | `Auth__Session__CookieSameSite` | `None` | Só enquanto o staging estiver em `*.up.railway.app` (seção 7 do contrato). Em `*.staging.orken.com.br`, remova: o padrão é `Lax` |
| `backend` | `Auth__Session__AllowedOrigins__0` | a origem exata do frontend do staging, com `https://` e sem barra no fim | É a lista de quem pode agir sobre o cookie. Sem isso, o login responde `403 origin_not_allowed`. A origem entra sozinha na lista do CORS |
| `backend` | `Cors__AllowedOrigins__0` | a mesma origem | Para o modo padrão continuar funcionando se o bundle voltar atrás (seção 5). Não dá autoridade sobre o cookie |

Produção não muda nada nesta etapa.

### Navegador

- Chrome ou Edge atualizados. Em `*.up.railway.app`, frontend e API são sites diferentes, e Safari e Firefox podem bloquear o cookie:
  isso não é reprovação enquanto o staging estiver nesse domínio. Para valer em qualquer navegador, é preciso o DNS de
  `erp.staging.orken.com.br` e `api.erp.staging.orken.com.br`.
- Janela anônima nova para cada fluxo, salvo quando o passo diz o contrário.
- Abra as ferramentas (F12) e deixe marcado **Network > Preserve log**.

Onde olhar:

- **Application > Cookies:** escolha o host da **API**, não o do frontend. O cookie é daquele host e só aparece lá.
- **Application > Local Storage / Session Storage:** o host do frontend.
- **Network:** clique na requisição e veja **Headers** (`Cookie`, `X-Orken-Session`, `Origin`, `Set-Cookie`) e **Response**.

### Contas

Diretoria e gerente de uma empresa com duas lojas e com o módulo de restaurante; um usuário bloqueado e um inativo; um e-mail sem
verificar; um administrador da plataforma com verificação em duas etapas; outra pessoa com verificação em duas etapas numa empresa. Só
dados de teste.

### Conferência do modo

Faça login. Em **Network**, a chamada `auth/login` precisa ter `X-Orken-Session: cookie` no pedido. Se não tiver, o bundle é o do modo
antigo: pare e publique de novo com a variável.

## 2. Cookie e armazenamento (vale em todos os fluxos)

Depois de qualquer login, confira uma vez:

| Onde | O que precisa estar |
|---|---|
| Cookies do host da API | `__Host-nexo_rt`, **HttpOnly** e **Secure** marcados, Path `/`, Domain igual ao host da API, SameSite `Lax` (`None` em `*.up.railway.app`), expira em cerca de 7 dias |
| Cookies do host da API | Nenhum `nexo_access` nem `nexo_refresh` novos |
| Local Storage | `nexo:session` com nome e papel. **Nenhum** `nexo:access_token` nem `nexo:refresh_token`, e nenhum valor que comece com `eyJ` |
| Session Storage | Nenhum token (a única exceção é a aba de impersonação) |
| Console: `document.cookie` | Não mostra `__Host-nexo_rt` |
| Corpo de `auth/login` | Tem `accessToken` e `session`. **Não** tem `refreshToken` |

## 3. Fluxos

### Login

1. Entre como gerente e como vendedor. Cada um vai para o destino do seu papel (`/workspaces`, `/pdv`).
2. Senha errada: "Login ou senha incorretos.", sem cookie novo.
3. Conta bloqueada e inativa: "Conta bloqueada. Entre em contato com o suporte."
4. E-mail sem verificar: "E-mail não verificado. Verifique sua caixa de entrada."
5. Em **Network**, `auth/refresh` pode aparecer uma vez, com 401, na primeira visita de um navegador: é a pergunta "o cookie tem sessão?". Não
   aparece de novo enquanto não houver login.

### Verificação em duas etapas

1. Entre com a senha. Nenhum cookie é gravado nesse passo: a resposta traz `mfaChallenge`.
2. Código errado: "Código inválido.", e o passo continua aberto.
3. Código certo: entra, e o cookie aparece.
4. Em outra janela anônima, use um código de recuperação. Funciona uma vez; na segunda, "Código inválido.".
5. Deixe a tela do código aberta e termine o mesmo desafio em outro lugar. Na primeira janela: "A verificação expirou.", e volta para a senha.

### Recarregar

1. Logado, vá a `/produtos` e pressione F5.
2. A tela aparece na hora, sem passar pelo login. Em **Network**, a ordem é `auth/refresh` (200, com o cookie no pedido e um novo `Set-Cookie` na
   resposta), depois `auth/me` (200) com `Authorization: Bearer`. Nenhum 401.
3. Pressione F5 várias vezes em sequência, sem esperar. A sessão continua, e nenhum `auth/refresh` responde 401.
4. **Reload no meio da renovação:** em **Network**, escolha **Slow 3G**, recarregue e recarregue de novo enquanto `auth/refresh` estiver pendente. Volte
   para **No throttling**. A sessão continua. Até dois 409 por carregamento no `auth/refresh` são aceitáveis (a aba espera 3 s e olha mais uma vez). Mais que isso reprova, e também reprova um 401 ou qualquer `auth/logout`.
5. **Resposta que demora:** uma renovação lenta segura as outras abas na fila da trava do navegador. Quando a resposta chega, todas continuam,
   sem aviso, sem login e sem `auth/logout`. O aviso de sessão ativa só aparece quando a renovação fica sem veredito: um 409 que se repete
   depois de a aba esperar 3 s pela vencedora, um 429, um erro do servidor ou falta de conexão. Ele fica até o fim da carência ou do `Retry-After`. No
   caso do 409, isso leva no máximo 30 s. Não vai para o login.

### Várias abas

1. Abra quatro abas em `/dashboard`.
2. Recarregue todas ao mesmo tempo (clique direito na aba > "Recarregar todas", ou Ctrl+clique e F5). Todas continuam logadas. Nenhum 401 em
   `auth/refresh`, em nenhuma aba.
3. **Login propaga:** com uma aba em `/login`, entre por outra. A primeira sai do login sozinha, sem recarregar.
4. **Logout propaga:** clique em "Sair da conta" numa aba. As outras vão sozinhas para o login, com o aviso de sessão encerrada.

### Troca de loja

1. Abra duas abas. Na primeira, troque de loja pelo seletor do topo.
2. Em **Network**, `auth/switch-store` tem o cookie no pedido, um novo `Set-Cookie` e nenhum `refreshToken` no corpo.
3. Na segunda aba, sem recarregar, o seletor mostra a loja nova, e os dados vêm dela.
4. Espere 15 minutos, ou recarregue. A loja continua a nova nas duas abas.

### Logout

1. Clique em "Sair da conta". Em **Network**, `auth/logout` responde 204, com `Set-Cookie: __Host-nexo_rt=; expires=...` no passado.
2. Em **Application > Cookies**, o cookie sumiu. O Local Storage fica sem `nexo:session`.
3. O botão Voltar e o endereço de `/dashboard` levam ao login. Nenhum `auth/refresh` é enviado.
4. Em **Network**, as respostas de `auth/login`, `auth/refresh`, `auth/logout`, `auth/me` e `auth/verify-email` têm `Cache-Control: no-store`, inclusive um 401 e um 403.
5. **Logout logo depois de recarregar:** em **Network**, bloqueie `auth/refresh` (clique direito > **Block request URL**) e recarregue. Desbloqueie e clique
   em "Sair da conta" sem recarregar. `auth/logout` vai sem `Authorization`, responde 204 e o cookie some.
6. **Sem conexão:** coloque em **Offline** e clique em "Sair da conta". A tela vai para o login, e o Local Storage fica com `nexo:pending_sign_out`. O cookie ainda está lá, porque o servidor não soube da saída. Volte
   para **No throttling** e recarregue: `auth/logout` vai sozinho e responde 204, o cookie some e a marca sai. Continua no login, sem nenhum `auth/refresh`. Recarregue de novo: nenhum `auth/logout`.
7. **Sem conexão, e login na mesma página:** repita o passo anterior até o Offline, volte a rede e, sem recarregar, entre de novo. Em **Network**, `auth/logout` (204) vem antes de `auth/login`, e a sessão anterior não renova mais.

### Impersonação

1. Entre como administrador da plataforma e abra uma empresa. Clique em "Entrar como cliente". Uma nova aba abre com a faixa do acesso.
2. Nessa aba, em **Network**, nenhum pedido tem `Cookie` nem `X-Orken-Session`. Todos têm `Authorization`.
3. O Session Storage dessa aba tem o token curto (risco residual aceito no contrato). O Local Storage dela continua sem tokens.
4. Clique em "Sair do acesso". A aba fecha. Se o navegador não deixar fechar, ela vai para "acesso encerrado".
5. Na aba do administrador, recarregue. Continua logado como administrador. O valor do cookie só muda pela renovação da própria aba dele.

### Plataforma

1. Entre como administrador: senha, depois o código. O valor do cookie começa com `p.`. Vai para `/platform`.
2. Recarregue: continua em `/platform`. O cookie muda de valor e continua começando com `p.`.
3. Em **Segurança**, encerre todas as sessões. A tela vai para o login, e o cookie some.
4. Em outro navegador, encerre as sessões desse administrador e volte ao primeiro. Na próxima tela, vai para o login sem laço.

### Recuperação de senha e verificação de e-mail

1. Peça a redefinição, siga o link e crie a nova senha. A tela vai para o login com o aviso. Entre com a nova.
2. Em outra janela que já estava logada com a mesma pessoa, recarregue: vai para o login com "Seu acesso foi alterado". O cookie antigo não renova mais.
3. Crie uma conta e siga o link do e-mail. Entra sozinho, com o cookie gravado e sem `refreshToken` no corpo de `auth/verify-email`.
4. Abra o mesmo link de novo. A tela mostra "Link expirado ou inválido", e `auth/verify-email` responde 400 ("Link inválido ou expirado.")
   sem `Set-Cookie`.

### Cozinha (tempo real)

1. Abra `/restaurante/cozinha` e recarregue. O indicador fica em "Tempo real". Em **Network > WS**, a conexão tem `access_token` na URL.
2. Em `hubs/restaurant/negotiate`, o pedido **não** tem `Cookie`.
3. Fique 15 minutos na tela sem clicar em nada. O servidor fecha a conexão quando o token expira, e ela volta sozinha com outro token, depois de um `auth/refresh`.
4. Mude o papel dessa pessoa por outra conta. A tela vai para o login, com "Seu acesso foi alterado".

### Falhas que não encerram a sessão

1. **Rede fora:** recarregue em **Offline**. A tela continua, com o aviso "Sua sessão continua ativa". Volte a rede e navegue: renova sem novo login.
2. **429:** no staging, repita `auth/refresh` sem esperar, mais de 300 vezes em 15 minutos, recarregando a mesma sessão (um laço no **Console** da aba resolve). O aviso aparece, com `Retry-After` na resposta, e a sessão
   continua. Depois do tempo indicado, volta sozinha.
3. **401 encerra sem laço:** apague o cookie em **Application > Cookies** e recarregue. Um `auth/refresh` com 401, depois o login. Nenhum pedido repetido.

4. **409:** um 409 nunca leva ao login sozinho e nunca dispara `auth/logout`. A resposta traz `Retry-After`, com o que falta dos 30 s de carência.

### Migração de quem já está logado

1. Antes de publicar o bundle novo, entre pelo bundle atual e não saia. Confira `nexo:refresh_token` no Local Storage.
2. Publique o bundle com o modo cookie e recarregue a mesma aba.
3. Em **Network**, o primeiro `auth/refresh` tem o token no corpo e `X-Orken-Session: cookie`. Responde 200, com `Set-Cookie` e sem `refreshToken`.
4. A pessoa continua logada, sem novo login. O Local Storage fica sem os dois tokens, e o cookie aparece.
5. Recarregue: agora `auth/refresh` vai sem corpo.
6. **Aba antiga aberta:** uma aba do bundle anterior, aberta durante a publicação, vai para o login quando outra migra, porque os tokens saíram do armazenamento que ela lê. Recarregar resolve.

### CSRF

Em um terminal, com o valor do cookie copiado de **Application > Cookies**, numa variável (`RT=...`), sem colar o valor em lugar nenhum além desse terminal. A API é o host do staging:

```bash
# origem fora da lista: 403 origin_not_allowed
curl -i -X POST "$API/api/auth/refresh" -H "Origin: https://evil.example" -H "X-Orken-Session: cookie" -H "Cookie: __Host-nexo_rt=$RT"

# sem Origin: 403 origin_not_allowed
curl -i -X POST "$API/api/auth/refresh" -H "X-Orken-Session: cookie" -H "Cookie: __Host-nexo_rt=$RT"

# sem o header: 400, e o cookie não vale nada
curl -i -X POST "$API/api/auth/refresh" -H "Origin: $APP" -H "Content-Type: application/json" -H "Cookie: __Host-nexo_rt=$RT"

# access token velho no cookie antigo: 401
curl -i "$API/api/auth/me" -H "Cookie: nexo_access=$ACCESS"
```

```bash
# outra grafia do nome: 401, e o cookie não vale nada
curl -i -X POST "$API/api/auth/refresh" -H "Origin: $APP" -H "X-Orken-Session: cookie" -H "Cookie: __HOST-nexo_rt=$RT"

# o nome exato duas vezes: 401, e nenhum dos dois é gasto
curl -i -X POST "$API/api/auth/refresh" -H "Origin: $APP" -H "X-Orken-Session: cookie" -H "Cookie: __Host-nexo_rt=$RT; __Host-nexo_rt=$RT"

# um site só da lista do CORS, com o header: 403 origin_not_allowed
curl -i -X POST "$API/api/auth/refresh" -H "Origin: https://www.orken.com.br" -H "X-Orken-Session: cookie" -H "Cookie: __Host-nexo_rt=$RT"
```

Depois de cada um, a aba continua funcionando: recarregue, e `auth/refresh` responde 200. Se responder 409 ou 401, a tentativa tocou na sessão, e o fluxo reprovou.

**Página de outro site:** em **Console**, numa aba de qualquer site, com `$API` no lugar do host do staging, mande pedidos simples, sem preflight:

```js
for (let i = 0; i < 30; i++) fetch(`${API}/api/auth/login`, { method: "POST", mode: "no-cors", credentials: "include", headers: { "Content-Type": "text/plain" }, body: "x" });
```

Em **Network**, cada um responde 403 (a linha aparece como `opaque`, sem detalhes). Sem esperar a janela do limite, entre na aplicação pelo mesmo navegador: o login funciona, porque nenhum pedido daquele site gastou o limite de login do endereço.

**Corpo grande:** com 20 KB sem declarar o tamanho, cada uma das rotas responde 413, sem linha nova em `error_events`:

```bash
head -c 20480 /dev/zero | tr '\\0' x | curl -i -X POST "$API/api/auth/refresh" -H "Content-Type: application/json" -H "Transfer-Encoding: chunked" --data-binary @-
```

**Ladrão que renovou primeiro:** com o valor do cookie na variável (`RT=...`), renove pelo terminal, como faria quem copiou o token: `curl -i -X POST "$API/api/auth/refresh" -H "Origin: $APP" -H "X-Orken-Session: cookie" -H "Cookie: __Host-nexo_rt=$RT"`. Guarde o novo valor do `Set-Cookie`. Recarregue a aba:

1. `auth/refresh` responde 409 com `Retry-After`, no máximo duas vezes. A tela continua, com o aviso de sessão ativa. Nenhum `auth/logout`.
2. Espere passar o tempo e navegue. `auth/refresh` responde 401, a tela vai para o login, e a auditoria tem `refresh_reuse` crítico.
3. O valor guardado, apresentado de novo pelo terminal, responde 401: o ladrão ficou sem o par.

### Isolamento

Só faz sentido com os domínios `*.staging.orken.com.br`. Com eles:

1. De outro subdomínio do mesmo site, tente gravar o cookie: em **Console** num host irmão, `document.cookie = "__Host-nexo_rt=x; Domain=orken.com.br; Path=/; Secure"`. O navegador recusa, e o cookie do host da API continua com o mesmo valor.
2. De um host irmão, `fetch` da renovação com `credentials: "include"` e o header: o navegador barra na preflight, e nada muda.

### Host público

Só faz sentido onde `www.orken.com.br` servir um bundle com `VITE_SESSION_MODE=cookie`, ou seja, não dá para conferir em `*.up.railway.app`: fica para a primeira publicação com o modo ligado. A suíte prova o mesmo caminho com o host interceptado ("The host of the public portals"). Lá:

1. `https://www.orken.com.br/login` vai para `https://erp.orken.com.br/login`, com a consulta. O mesmo com `/dashboard`, `/verify-email?token=...` e `/redefinir-senha?token=...`.
2. `/agendar/<empresa>`, `/rastrear/<token>` e um cardápio ficam em `www`. Em **Network**, nenhum `auth/refresh` nem `auth/me`, e nenhum pedido com `X-Orken-Session`.
3. **Sessão antiga:** com o bundle atual, entre por `www` e não saia. Depois da publicação, abra `www` de novo: `auth/logout` vai com o token no corpo, sem cookie, responde 204 e o Local Storage de `www` fica sem os tokens.

### Limpar o armazenamento

1. Logado, em **Application > Storage**, limpe só o Local Storage e o Session Storage do host do frontend (não os cookies).
2. Sem recarregar, navegue: tudo funciona, porque o token está em memória.
3. Recarregue: a mesma tela abre, sem login. Um `auth/refresh` 200 e um `auth/me` 200, e `nexo:session` volta ao Local Storage.
4. Em uma página pública (cardápio, agendamento), nenhum `auth/refresh` é enviado.

Esse é o comportamento definido: enquanto o cookie vale, limpar o armazenamento não encerra a sessão. Quem quer sair usa "Sair da conta". Limpar também os cookies do host da API encerra.

## 4. Resultado

A rodada automatizada de 2026-11-10 no staging está registrada em
[`homologacao-sessao-staging-2026-11-10.md`](homologacao-sessao-staging-2026-11-10.md), com o que ainda falta para a revisão humana.

| Fluxo | Aprovado | Quem | Quando |
|---|---|---|---|
| Login | | | |
| Verificação em duas etapas | | | |
| Recarregar | | | |
| Várias abas | | | |
| Troca de loja | | | |
| Logout | | | |
| Impersonação | | | |
| Plataforma | | | |
| Recuperação de senha e e-mail | | | |
| Cozinha | | | |
| Falhas | | | |
| Migração | | | |
| CSRF | | | |
| Isolamento | | | |
| Host público | | | |
| Limpar o armazenamento | | | |

## 5. Voltar atrás

**A ordem importa: primeiro o bundle, depois a API, se ela também voltar.** A API anterior responde 400 a uma renovação sem corpo e volta a
gravar `nexo_access`, então um bundle no modo cookie falando com ela perderia todas as sessões.

Publique o bundle sem `VITE_SESSION_MODE`. A API continua aceitando os dois modos, então nada muda nela. Quem migrou precisa entrar de novo,
porque o bundle antigo não enxerga o cookie. O cookie que ficou no navegador expira sozinho, e nenhum pedido do bundle antigo o usa.

**A volta do bundle só alcança quem recebe o HTML novo.** Até o PR #181, o servidor do frontend mandava `no-cache` apenas para `/` e
`/index.html`. Nas rotas do cliente (`/login`, `/dashboard`, …), o navegador guardava o HTML por horas sem consultar o servidor. Antes de
voltar a API, confira:

- `curl -I` numa rota do cliente responde `Cache-Control: no-cache`;
- um navegador que já tinha o app aberto, ao abrir `/dashboard` de novo, deixa de mandar `X-Orken-Session`.

A volta foi executada no staging em 2026-11-10 ([registro](homologacao-sessao-staging-2026-11-10.md), seção 3.1):

- A volta da API (`8a164ce`) manteve a sessão do modo de hoje, e o login continuou entregando os tokens no corpo.
- A volta do bundle não chegou ao navegador que já tinha o app aberto, pelo motivo acima.
- A restauração voltou o staging ao modo cookie, com a conferência completa passando.

## 6. Riscos aceitos

Não são reprovação. A lista completa, com as condições, está na seção 9 do contrato:

- Uma queda dura do Redis pode reviver uma sessão encerrada no último minuto antes dela, ou fazer um logout durante a queda não valer se a linha estiver defasada. Fechar isso exige uma coluna com a família na linha da sessão, e portanto uma migração. A base tem a mesma exposição.
- Login durante uma queda do Redis não tem trava por conta, e a sessão aberta nesse período termina na primeira renovação depois da volta.
- Quem está no mesmo endereço gasta os mesmos tetos. No modo cookie, o logout conta na partição anônima.
- Um subdomínio comprometido pode bloquear um navegador com cookies grandes (431).
