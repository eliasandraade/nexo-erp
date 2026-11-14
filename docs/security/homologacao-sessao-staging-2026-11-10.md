# Homologação da sessão em cookie no staging: rodadas de 2026-11-10 a 2026-11-12

Registro da execução do [roteiro](roteiro-homologacao-sessao.md) no staging, antes da segunda promoção para a produção. As rodadas são
automatizadas: um navegador real abriu as telas, e a API e o banco foram lidos onde o roteiro manda olhar. Elas não substituem a parte humana
([checklist](checklist-homologacao-humana-sessao.md)).

- **Primeira rodada (2026-11-10):** o roteiro inteiro, com o staging em `633a301`.
- **Segunda rodada (2026-11-11):** o cache do HTML e a volta atrás completa, com o staging em `c6c2cfc` (o PR #181 já mesclado). Está na
  seção 2.1.
- **Terceira rodada (2026-11-11 e 2026-11-12):** os domínios próprios do staging com `SameSite=Lax`, em Chromium e no WebKit do
  Playwright. Está na seção 2.2.

## Veredito

**A segunda promoção continua bloqueada.** Os fluxos do contrato passaram no Chromium, nos endereços antigos e nos domínios próprios com
`SameSite=Lax`, e a volta atrás passou depois do PR #181. Nenhuma falha de produto ficou em aberto nas três rodadas. Faltam:

1. **Safari e Firefox reais.** O WebKit do Playwright passou no que conseguiu executar, mas não substitui o Safari, e cinco itens não
   rodaram nele (seção 2.2). Não há Firefox nesta máquina.
2. **Homologação humana e aprovação explícita dos resultados** ([checklist](checklist-homologacao-humana-sessao.md)).
3. **O candidato da promoção.** Ele ainda não existe: depende de correções em revisão ([plano](plano-segunda-promocao-sessao.md), seção 1).
   Quando existir, a volta atrás e a conferência do modo cookie precisam ser repetidas nele.

## 1. Ambiente

| Item | Valor |
|---|---|
| Commit no staging | `633a301` no `backend` e no `main` (Railway, ambos `SUCCESS` no fim da rodada) |
| Frontend | `https://main-staging-22f4.up.railway.app`, `VITE_SESSION_MODE=cookie` |
| API | `https://backend-staging-d8d0.up.railway.app`, `Auth__Session__CookieSameSite=None`, origem do frontend em `Auth__Session__AllowedOrigins__0` e em `Cors__AllowedOrigins__0` |
| Produção | `8a164ce` desde 2026-11-07. Nenhuma variável de sessão foi definida lá: `Auth__Session__*`, `Cors__AllowedOrigins__0`, `RateLimiting__ClientIpSource` e `VITE_SESSION_MODE` continuam ausentes, conferidas no fim da rodada |
| Navegador | Chromium do Playwright 1.64.1, sem interface, com perfis persistentes onde o passo pede o mesmo navegador. O motor WebKit do Playwright entrou só como informação |
| Horário | 19:18 a 23:14 UTC |

### Dados

Tudo é sintético e fica no staging. Nenhum dado de produção foi copiado.

- **Contas criadas:** cinco contas `homolog.<papel>.<carimbo>` na empresa de teste `grupo-mix-8f62ac`, com duas lojas e o restaurante:
  - g1: gerente, com verificação em duas etapas;
  - g2: gerente, rebaixado a vendedor no fluxo da cozinha;
  - v1: vendedor, com a senha redefinida no fluxo de recuperação;
  - b1: bloqueado;
  - i1: inativo.
- **Cadastro novo:** uma empresa sintética, para o link de verificação de e-mail.
- **Administrador da plataforma:** a verificação em duas etapas foi ligada por enquanto no administrador semente do staging, porque a
  impersonação exige um login com segundo fator. Ela foi desligada ao fim de cada ciclo, e o banco confirmou `mfa_enabled` em 0
  administradores.
- **Link de redefinição de senha:** a caixa de e-mail do staging não é acessível. Por isso o hash SHA-256 de um token conhecido foi gravado na
  linha da conta v1, com validade de 30 minutos. Do link em diante, o fluxo é o mesmo de quem recebe o e-mail. O pedido pela tela "Esqueci minha senha" fica para a revisão humana.

## 2. Resultado por fluxo do roteiro

As linhas com falha no anexo estão todas explicadas na seção 3. "Passou" quer dizer que o resultado esperado do roteiro foi observado na
rede ou na tela.

| Fluxo | Resultado | Observação |
|---|---|---|
| Cookie e armazenamento | Passou | `__Host-nexo_rt` HttpOnly, Secure, Path `/`, host da API, SameSite `None`, 7 dias. Nenhum `nexo_access` nem `nexo_refresh`, nenhum token no Local Storage ou no Session Storage, e `document.cookie` não mostra o cookie |
| Login | Passou | Senha errada, conta bloqueada, conta inativa e e-mail sem verificar recusam sem cookie. A primeira visita sem sessão pergunta uma vez e não pergunta de novo. Os destinos por papel apareceram pela própria navegação (`/workspaces`, `/pdv`) |
| Verificação em duas etapas | Passou (pela API) | Desafio sem cookie, código errado mantém o desafio, código certo grava o cookie sem `refreshToken`, código de recuperação vale uma vez, desafio terminado em outro lugar responde "A verificação expirou". As telas do código ficam para a revisão humana |
| Recarregar | Passou | F5 e F5 seguidos sem 401. Recarga no meio da renovação, em Slow 3G: nenhum 409 a mais, nenhum 401 nem `auth/logout`. O item 5 foi reescrito no roteiro (seção 3.4) |
| Várias abas | Passou em parte | Quatro abas recarregadas juntas: nenhum 401. O logout se propaga. O login que se propaga entre abas exige digitar a senha numa tela e fica para a revisão humana |
| Troca de loja | Passou | `auth/switch-store` leva o cookie, recebe `Set-Cookie` novo, sem `refreshToken`. A outra aba muda sem recarregar, e as duas continuam na loja nova depois de recarregar |
| Logout | Passou em parte | 204 com o cookie vencido, Local Storage limpo e as outras abas no login. `Cache-Control: no-store` nas respostas de auth. O logout sem `Authorization` logo depois de recarregar e o logout sem conexão (a marca, depois o 204 sozinho) também passaram. O item 7 (entrar de novo na mesma página depois do logout sem conexão) fica para a revisão humana |
| Impersonação | Passou | A aba nova leva só `Authorization`, nunca `Cookie` nem `X-Orken-Session`. O token curto fica só no Session Storage dela. "Sair do acesso" fecha a aba, e o cookie do administrador não mudou |
| Plataforma | Passou | Senha e código, cookie com `p.`, que muda a cada recarga. "Encerrar todas" em Segurança leva ao login sem o cookie. Outro navegador encerra as sessões, e o primeiro vai ao login sem laço |
| Recuperação de senha e e-mail | Passou (pelo link) | A janela que estava logada vai ao login com "Seu acesso foi alterado", e o cookie antigo recebe 401. O link de verificação entra sozinho, com o cookie e sem `refreshToken`. O mesmo link de novo recebe 400, sem cookie (seção 3.3) |
| Cozinha | Passou | "Tempo real" depois de recarregar. O WebSocket leva `access_token` na URL, e `negotiate` vai sem `Cookie`. Depois de 16 minutos parada, a conexão volta sozinha com outro token. O papel mudado por outra conta leva ao login |
| Falhas | Passou | 429 na 300ª renovação, com `Retry-After` e `no-store`: a sessão continua e volta sozinha. Sem rede, o aviso aparece e a renovação acontece ao voltar. Cookie apagado: um 401 e o login, sem laço |
| Migração | Passou em parte | Itens 1 a 5: a sessão do bundle de hoje migra no primeiro `auth/refresh` (token no corpo, depois só o cookie), sem novo login. O item 6 (aba do bundle anterior aberta durante a publicação) não foi exercido |
| CSRF | Passou | Todos os `curl` do roteiro e os 30 pedidos de outro site: 403, sem gastar o limite de login. 20 KB sem tamanho declarado: 413 nas três rotas, sem `error_events`. Ladrão que renovou primeiro: dois 409 com `Retry-After`, depois 401, uma linha crítica `refresh_reuse`, e o valor do ladrão recebe 401 |
| Limpar o armazenamento | Passou em parte | Itens 1 a 3. O item 4 (página pública sem `auth/refresh`) não entrou nesta rodada |
| Isolamento | Não executado | Exige os domínios `*.staging.orken.com.br` |
| Host público | Não executado | Só existe na primeira publicação com o modo ligado em `www` (roteiro) |
| Voltar atrás | Reprovou na 1ª rodada; passou na 2ª | Na 1ª, a volta da API e a restauração passaram, mas a volta do bundle não chegou ao navegador que já tinha o app aberto (seção 3.1). Na 2ª, depois do PR #181, a volta completa passou (seção 2.1) |

Depois da restauração, o teste de fumaça (o bundle nunca chama a API de produção) e a conferência completa do modo cookie passaram de novo no
staging, às 22:55 e às 23:11 UTC: 33 verificações, incluindo seis renovações simultâneas (uma vence, cinco recebem 409), a carência, o reuso
auditado, o logout e as três abas.

## 2.1 Segunda rodada: cache do HTML e volta atrás, depois do PR #181 (2026-11-11)

Staging em `c6c2cfc`, publicado às 04:02 UTC, depois de o `master-gate` passar nesse SHA.

### Cache do HTML

| Verificação | Resultado |
|---|---|
| Antes do PR #181 (linha de base, staging em `633a301`) | 107 de 109 caminhos sem `Cache-Control`. Só `/` e `/index.html` tinham `no-cache` |
| Depois: as 100 rotas do roteador do app, mais `/index.html`, consultas, uma rota inexistente e os arquivos da raiz (`favicon.ico`, `robots.txt`, `site.webmanifest`, imagens) | 109 de 109 com `200` e `Cache-Control: no-cache` |
| Assets com hash citados pelo HTML | 5 de 5 com `public, max-age=31536000, immutable` |
| Segunda visita a `/dashboard` num Chromium com perfil persistente | O HTML veio do servidor, não do cache do disco |
| Depois da volta atrás e da restauração | O mesmo: 109 com `no-cache` e 5 imutáveis |

### Volta atrás com abas e sessões abertas

Quatro pessoas em perfis persistentes do Chromium, mantidos através das publicações:
- **A:** uma aba aberta antes de tudo e não recarregada;
- **B:** uma sessão que ninguém usa até o fim;
- **C:** uma sessão cujo navegador reabre o app durante a volta;
- **D:** uma sessão aberta no modo de hoje durante a volta e mantida até o fim.

| Passo | Verificação | Resultado |
|---|---|---|
| Antes | A, B e C entram no modo cookie. A segunda visita de C pede o HTML ao servidor | Passou |
| Bundle sem o modo (`c6c2cfc`, variável desligada) | Publicado | Passou |
| | A, sem recarregar: continua com o bundle que já estava na memória e segue funcionando (6 chamadas, todas 200 ou 204, nenhum arquivo falhou) | Passou |
| | C reabre o app: recebe o HTML novo do servidor, vai ao login, e o bundle não manda o header do modo nem usa o cookie | Passou. **Era a falha da 1ª rodada** |
| | D entra no modo de hoje: tokens no corpo, renova com o token no corpo | Passou |
| API em `8a164ce` | Publicada | Passou |
| | D, aberta durante toda a volta da API: renova na API anterior, sem novo login | Passou |
| | A, com o token ainda válido: segue funcionando na API anterior | Passou |
| | A recarregada durante a volta: HTML novo do servidor e vai ao login, como quem migrou. O cookie não é tocado | Passou |
| Restauração (API e depois bundle no modo cookie, `c6c2cfc`) | Publicadas | Passou |
| | B, que ninguém usou: abre logada pelo cookie, sem novo login | Passou |
| | C, que foi ao login durante a volta e não entrou de novo: abre logada pelo cookie, sem novo login | Passou |
| | A recarregada: entra pelo cookie, que a volta não tocou | Passou |
| | D: migra para o cookie sem novo login (token no corpo e o header, depois só o cookie), e o Local Storage fica sem tokens | Passou |
| Depois | Teste de fumaça (o bundle nunca chama a API de produção) e as 33 verificações do modo cookie | Passou |
| Estado final | Staging em `c6c2cfc` nos dois serviços, com `VITE_SESSION_MODE=cookie`. Produção em `8a164ce`, sem nenhuma variável de sessão | Conferido |

**Conclusão:** a volta não perdeu nenhuma sessão fora do previsto. Durante a volta do bundle, só quem estava no modo cookie e recarregou foi
ao login. A sessão do modo de hoje atravessou a volta e a restauração. Depois da restauração, todas voltaram sem novo login, porque o cookie
continua válido e nada na volta o gasta.

### Aba aberta com o token vencido enquanto a API está na versão anterior

Na rodada acima, a navegação da aba A depois da espera não fez nenhuma chamada à API. Por isso a renovação contra a API anterior não chegou a
acontecer, e a verificação passou sem ter sido exercida. O caso foi repetido à parte, com uma aba E:

1. A aba E abre no modo cookie e fica aberta.
2. A API vai para `8a164ce`.
3. Depois que o token vence, a aba E abre três telas que ela ainda não tinha aberto.
4. A API volta para `c6c2cfc`.

As duas primeiras tentativas falharam por causa do instrumento:

- **1ª tentativa:** esperou 15,5 minutos, mas a API aceita o token por 15 minutos mais 1 minuto de tolerância de relógio (`ClockSkew` em
  `Program.cs`). As chamadas ainda receberam 200. O token do CLI do Railway também venceu durante a espera, e a API do staging ficou em
  `8a164ce` por 17 minutos, até ser restaurada à mão.
- **2ª tentativa:** a renovação foi exercida. Mas o aviso é um toast que some em segundos, e a tela só foi lida no fim.

A 3ª tentativa registrou o toast no instante em que ele apareceu:

| Verificação | Resultado |
|---|---|
| Com o token vencido, a aba chama a API anterior e a renovação pelo cookie acontece | Passou: `auth/refresh` 400 três vezes, com o header do modo e o cookie |
| A API anterior não dá veredito (nenhum 401 na renovação). A aba fica, com o aviso, sem ir ao login e sem `auth/logout` | Passou: o aviso foi "Sua sessão continua ativa: tente de novo em instantes" |
| A API volta: a mesma aba, sem recarregar e sem novo login, renova pelo cookie | Passou: `auth/refresh` 200 |

Depois disso, o staging ficou em `c6c2cfc` nos dois serviços. As 33 verificações do modo cookie passaram de novo (05:55 UTC), e as 109 rotas
continuaram com `no-cache`.

## 2.2 Terceira rodada: domínios próprios com `SameSite=Lax` (2026-11-11 e 2026-11-12)

Staging em `c6c2cfc`, em `https://erp.staging.orken.com.br` e `https://api.erp.staging.orken.com.br`.

### Preparo

| Verificação | Resultado |
|---|---|
| CNAME e TXT de verificação dos dois nomes, nos servidores autoritativos e em três resolvedores públicos | Valores exatos |
| Railway: domínios verificados e certificados emitidos | `verified: true`; certificado válido para cada nome (Let's Encrypt, TLS 1.3) |
| Variáveis do staging | Origens da sessão, do CORS e do frontend em `https://erp.staging.orken.com.br`; API do bundle em `api.erp.staging`; `Auth__Session__CookieSameSite` removida (vale `Lax`); `VITE_SESSION_MODE=cookie` |
| `Cache-Control` no host novo | 109 de 109 rotas com `no-cache`; 5 de 5 assets imutáveis; segunda visita pedida ao servidor |
| Conferência do modo cookie | 33 verificações passaram; o cabeçalho `Set-Cookie` traz `SameSite=Lax` |
| Volta do bundle no host novo, com abas e sessões abertas | Passou, incluindo a migração de volta para o cookie sem novo login |

### Bateria por motor

O login, o código da segunda etapa e a nova senha foram digitados nas telas, com as contas de teste. O administrador da plataforma entrou
pela API. Cada linha vale para os dois motores, salvo quando indicado.

| Fluxo | Chromium | WebKit do Playwright |
|---|---|---|
| Login pela tela: destino do papel, cabeçalho do modo, cookie, sem `refreshToken` | Passou | Passou |
| Cookie: HttpOnly, Secure, Path `/`, só no host da API, cerca de 7 dias | Passou, com `SameSite=Lax` | Passou; o motor reporta `SameSite=None` (ver abaixo) |
| Sem cookie antigo, sem token no armazenamento, cookie invisível ao script | Passou | Passou |
| Renovação ao recarregar e em recargas seguidas | Passou | Passou |
| Quatro abas recarregadas juntas | Passou | Passou |
| Troca de loja em duas abas | Passou | Passou |
| Logout: 204, cookie some, as outras abas saem | Passou | Passou |
| Login numa aba tira a outra da tela de login | Passou | Passou |
| Logout sem conexão: marca de saída pendente, cookie ainda presente | Passou | Passou |
| De volta à rede, novo login na mesma página: `auth/logout` vai antes de `auth/login`, e a sessão antiga não renova | Passou | Passou |
| Segunda etapa pela tela: passo sem cookie, código errado mantém o passo, código certo entra | Passou | Passou |
| Redefinição de senha pelo formulário do link; a janela antiga vai ao login e o cookie antigo recebe 401 | Passou | Passou |
| Impersonação: faixa do acesso, só `Authorization`, token curto na aba, sair fecha a aba, administrador intacto | Passou | Passou (o motor não mostra o cabeçalho `Cookie` dos pedidos) |
| Chamada com o cabeçalho do modo a partir de outro site: barrada pelo CORS | Passou | **Não executado** |
| A mesma chamada a partir de `www.orken.com.br` (irmão, na lista do CORS): 403 `origin_not_allowed`, sessão intacta | Passou | **Não executado** |
| Subdomínio irmão não grava `__Host-nexo_rt` com `Domain=orken.com.br` | Passou | **Não executado** |
| Aba parada com o token de acesso vencido renova sozinha | Passou | **Não executado** |
| Sessão encerrada no servidor: as abas vão ao login com o motivo, sem laço | Passou | **Não executado** |

Os cinco itens não executados no WebKit pararam no mesmo ponto: o WebKit do Playwright no Windows recusou o certificado de sites externos
(`example.com` e `www.orken.com.br`) com erro de TLS. É limite do motor nesta máquina. Esses itens ficam para o Safari real.

### Falhas desta rodada e a causa de cada uma

Todas foram do roteiro de teste. Cada uma foi repetida com o roteiro corrigido, e passou.

| Falha | Causa | Repetição |
|---|---|---|
| Logout sem conexão: a aba ficava em `/workspaces`, sem marca de saída | O roteiro procurava o botão "Sair da conta" na tela de escolha de módulo, que não tem a barra lateral, e ignorava o erro do clique. Nenhum logout era pedido | Clicando na barra lateral de `/dashboard`: passou nos dois motores |
| `SameSite=None` no WebKit | O servidor envia `Lax` (lido no cabeçalho) e o Chromium guarda `Lax`. Só o WebKit do Playwright no Windows reporta `None` | Atributo registrado como informação nesse motor; o Safari real decide |
| Impersonação: botão não encontrado | O roteiro abria o link da empresa direto num navegador que só tinha o cookie, e a plataforma cai na tela inicial (observação da seção 3.6) | Entrando pela tela inicial: passou nos dois motores |
| Chamada a partir de `www` "lida" | O critério esperava bloqueio do CORS. `www.orken.com.br` está na lista do CORS, então a resposta é legível, e ela é a recusa definida no contrato (403) | Conferindo o status e o código: passou no Chromium |
| Código da segunda etapa recusado no complemento | O roteiro reusou o passo de tempo que o preparo tinha acabado de gastar | Esperando o passo seguinte: passou |

Durante os complementos, o limite de 5 logins por 15 minutos da rota da plataforma segurou o desligamento da verificação em duas etapas
temporária do administrador do staging por cerca de 20 minutos. Ela foi desligada às 06:12 UTC, conferida no banco.

## 3. Falhas e o que cada uma é

### 3.1 Volta do bundle: o HTML do app fica no cache do navegador (falha real, fora do código da sessão)

**O que aconteceu:**

1. Às 22:34, uma pessoa entrou no bundle do modo cookie e abriu `/dashboard` num perfil persistente.
2. O `main` foi publicado de novo no mesmo commit, com `VITE_SESSION_MODE` desligado.
3. O mesmo perfil abriu `/dashboard` e continuou no modo cookie. Ele mandou o header do modo e o cookie, quando o esperado era ir ao login.

Um perfil novo, no mesmo momento, recebeu o bundle sem o modo e funcionou como o de hoje.

**Causa:**

- O `Caddyfile` manda `Cache-Control: no-cache` só para `/` e `/index.html`, porque os matchers veem o caminho pedido antes do `try_files`.
- Por isso `/dashboard`, `/login` e as demais rotas do cliente recebem o mesmo `index.html` sem `Cache-Control` e com `Last-Modified`.
- O navegador guarda esse HTML por um décimo da idade do arquivo sem consultar o servidor.
- Medido no staging: a segunda visita a `/dashboard` veio do cache do disco, e `/` sempre revalidou.

**Alcance:**

- Em produção, a raiz de `erp.orken.com.br` redireciona para `/login`. Então todo carregamento do app passa por uma rota do cliente.
- O problema já existe em produção e vale para qualquer publicação. Não é regressão de `8a164ce`.
- Para a sessão em cookie ele pesa mais, por causa da ordem da volta (seção 5 do roteiro): primeiro o bundle, depois a API. Se a API voltar enquanto navegadores ainda rodam o bundle do modo cookie guardado no cache, essas sessões caem.

**Correção:** PR #181, mesclado em `c6c2cfc` em 2026-11-11. Tudo o que não é asset com hash passa a receber `no-cache`: o navegador
revalida com o `ETag` e recebe 304 quando nada mudou. O teste novo fica na suíte `csp-e2e`, que serve o `Caddyfile` real. A volta foi
repetida com perfis que já tinham aberto o app, e passou (seção 2.1).

### 3.2 Firefox não abriu (limite do equipamento, sem conclusão sobre o produto)

O motor Firefox do Playwright não inicia nesta máquina: o Windows recusa o executável por configuração lado a lado incorreta. Nada foi
alterado no sistema para contornar.

O teste que vale é em Firefox e Safari reais, com os domínios `*.staging.orken.com.br` (seção 4). Em `*.up.railway.app` o roteiro nem exige esses
navegadores. O motor WebKit do Playwright entrou logado pelo cookie entre sites, mas ele não equivale ao Safari, que tem regras próprias de
cookies de terceiros.

### 3.3 O mesmo link de verificação: texto da tela (falha do instrumento)

A API respondeu 400 com "Link inválido ou expirado.", sem `Set-Cookie`, como o roteiro pede. A tela mostra o estado de erro com o título
"Link expirado ou inválido" e "Este link já foi usado ou expirou.". A busca procurava a frase da API.

O roteiro passa a citar o texto da tela. A troca de ordem das palavras entre a API e a tela não muda o comportamento.

### 3.4 Item 5 de "Recarregar": o roteiro descrevia o aviso no caso errado

O roteiro dizia que uma resposta de renovação com mais de 3 s mostra o aviso de sessão ativa. Pelo código (`renewThroughCookie` em
`nexo-main/src/services/api-client.ts`), não é assim:

- Uma resposta lenta segura as outras abas na fila da trava do navegador.
- Quando ela chega, todas continuam, e não há aviso.
- O aviso aparece quando a renovação fica sem veredito: um 409 que se repete depois de a aba esperar 3 s pela vencedora, um 429, um erro do
  servidor ou falta de conexão.

O medido bate com o código:

- Resposta segurada por 6 s numa aba enquanto a outra recarregava: as duas continuaram, sem aviso, sem login e sem `auth/logout`.
- O aviso apareceu nos casos de 409 (ladrão), 429 e sem rede.

O roteiro foi corrigido.

### 3.5 Falhas do instrumento, repetidas com o instrumento certo

| Primeira leitura | O que era | Repetição |
|---|---|---|
| Cozinha: nenhum WebSocket | O evento `websocket` do Playwright não viu a conexão do SignalR | Pelo protocolo do Chromium: um WebSocket, com `access_token`. Passou |
| 429 não chegou (`after: 299`) | A leitura do status estava no lugar errado | Lido na própria resposta: 429 na 300ª, `Retry-After: 815`, `no-store`. Passou |
| Desafio terminado em outro lugar: 429 | Era a 6ª chamada de `mfa/verify` na janela do limite da rota | Repetido fora da janela: 401 `invalid_challenge`, "A verificação expirou". Passou |
| Impersonação recusada (403) | O administrador semente do staging não tem segundo fator, e a impersonação exige login com ele | Com a verificação ligada por enquanto: passou, e ela foi desligada no fim |
| Segurança: botão não encontrado | `/platform/seguranca` aberto direto, num navegador sem sessão local, cai em `/platform` | Pelo link "Segurança" do menu: passou. Ver 3.6 |

### 3.6 Observações que não são reprovação

- **Link profundo da plataforma:** abrir `/platform/seguranca` direto, num navegador que só tem o cookie, leva a `/platform` em vez da tela
  pedida. Isso afeta a usabilidade, não a segurança da sessão. Vale um item próprio fora desta promoção.
- **Saída da impersonação:** "Sair do acesso" fecha a aba. O roteiro dizia "vai para acesso encerrado". Agora diz as duas coisas: fecha, ou
  vai para essa tela quando o navegador não deixa fechar.
- **Volta da API:** a API de `8a164ce` volta a gravar `nexo_access` e `nexo_refresh`, como a produção de hoje. O cookie do modo cookie fica no
  navegador de quem migrou, sem uso, até expirar.

## 4. DNS do staging

**Concluído em 2026-11-11:** os quatro registros abaixo existem, os dois domínios estão verificados no Railway e têm certificado válido.

Os domínios próprios já estão criados no Railway, no ambiente de staging, com a porta 8080. O DNS de `orken.com.br` fica no GoDaddy
(`ns57` e `ns58.domaincontrol.com`). O Railway pede dois tipos de registro por domínio:

- um CNAME, para o tráfego;
- um TXT, que prova a propriedade. A lista de registros de tráfego do Railway não traz o TXT; ele aparece em `verificationDnsHost` e
  `verificationToken` do status do domínio.

Sem o TXT, o domínio fica `verified: false`, o certificado não é emitido e o Railway responde 404 "Application not found" nesse nome.

| Nome | Tipo | Valor | Situação em 2026-11-11 (15h UTC) |
|---|---|---|---|
| `erp.staging` | CNAME | `px0521nt.up.railway.app` | Criado: responde nos autoritativos, no Google, no Cloudflare e no OpenDNS |
| `api.erp.staging` | CNAME | `vns3l2lk.up.railway.app` | Criado: responde nos autoritativos, no Google, no Cloudflare e no OpenDNS |
| `_railway-verify.erp.staging` | TXT | `railway-verify=02e65e60aa8e239aef3a7c402011c4eac17b4de053970633d5d72ff7d57b060c` | Criado em 2026-11-11 |
| `_railway-verify.api.erp.staging` | TXT | `railway-verify=0f9d8c238757af613f20be485634b3e254d8ef3a6c67b62a9236a2bb923cb43d` | Criado em 2026-11-11 |

Depois que os quatro resolverem e o certificado sair, faça nesta ordem:

1. Troque no staging a origem em `Auth__Session__AllowedOrigins__0` e em `Cors__AllowedOrigins__0` para `https://erp.staging.orken.com.br`.
2. Aponte o frontend para `https://api.erp.staging.orken.com.br`.
3. Remova `Auth__Session__CookieSameSite`, para valer o padrão `Lax`.
4. Publique de novo e rode os fluxos em Safari e Firefox reais, mais o Isolamento.

## 5. O que fica para a revisão humana

A lista para marcar está na [checklist](checklist-homologacao-humana-sessao.md). São os passos que exigem digitar a senha numa tela, ou um
navegador que estas rodadas não têm:

- Safari e Firefox reais, nos domínios `*.staging.orken.com.br`.
- Isolamento, nos mesmos domínios.
- As telas da verificação em duas etapas: código errado na tela e "A verificação expirou" voltando para a senha.
- Login que se propaga entre abas (Várias abas, item 3).
- Logout sem conexão seguido de login na mesma página (Logout, item 7).
- Pedido de redefinição pela tela, com o e-mail recebido, e o aviso no login depois da nova senha.
- Migração, item 6 (aba do bundle anterior aberta durante a publicação).
- Limpar o armazenamento, item 4 (página pública sem `auth/refresh`).
- A volta atrás observada por uma pessoa, com o operador publicando (linha 18 da checklist). A rodada automatizada passou (seção 2.1).
- Host público, na primeira publicação com o modo ligado.

## 6. Anexo: cada verificação

As tabelas abaixo vêm do registro da execução, uma linha por verificação, em UTC. Na seção "Execução", uma falha marca um processo do
navegador que terminou com alguma verificação reprovada. As causas estão na seção 3.

| Fluxo | Passou | Falhou | Informação |
|---|---:|---:|---:|
| Login | 7 | 0 | 0 |
| Verificação em duas etapas | 8 | 1 | 0 |
| Cookie e armazenamento | 6 | 0 | 0 |
| Recarregar | 5 | 1 | 0 |
| Várias abas | 1 | 0 | 0 |
| Troca de loja | 3 | 0 | 0 |
| Logout | 9 | 0 | 0 |
| Impersonação | 6 | 0 | 1 |
| Plataforma | 18 | 2 | 5 |
| Recuperação de senha e e-mail | 7 | 1 | 0 |
| Cozinha | 10 | 2 | 0 |
| Falhas | 7 | 1 | 0 |
| Migração | 4 | 0 | 0 |
| CSRF | 19 | 0 | 0 |
| Limpar o armazenamento | 2 | 0 | 0 |
| Navegadores | 0 | 1 | 1 |
| Voltar atrás | 9 | 2 | 2 |
| Contas | 7 | 0 | 0 |
| Execução | 3 | 8 | 1 |

### Anexo: Login

| Horário (UTC) | Verificação | Resultado | Detalhe |
|---|---|---|---|
| 11-10 19:18 | Diretoria entra: token só no cookie | passou | 200 |
| 11-10 19:18 | conta bloqueada recusada, sem cookie | passou | (401, None, 'Conta bloqueada. Entre em contato com o suporte.') |
| 11-10 19:18 | conta inativa recusada, sem cookie | passou | (401, None, 'Conta bloqueada. Entre em contato com o suporte.') |
| 11-10 19:18 | senha errada: 401 sem cookie | passou | (401, 'Login ou senha incorretos.') |
| 11-10 19:35 | vendedor entra com o papel de vendedor | passou | (200, 'vendedor') |
| 11-10 21:22 | Diretoria entra pela API, direto no navegador | passou | 200 |
| 11-10 22:19 | e-mail sem verificar: recusado, sem cookie | passou | (401, 'email_not_verified', 'E-mail não verificado. Verifique sua caixa de entrada.') |

### Anexo: Verificação em duas etapas

| Horário (UTC) | Verificação | Resultado | Detalhe |
|---|---|---|---|
| 11-10 19:18 | preparo: segredo criado | passou | 200 |
| 11-10 19:19 | preparo: ativada, com códigos de recuperação | passou | (200, 8) |
| 11-10 19:34 | senha certa: desafio, sem cookie | passou | (200, True) |
| 11-10 19:34 | código errado recusado, sem cookie | passou | (401, 'invalid_code', 'Código inválido.') |
| 11-10 19:34 | o mesmo desafio continua aberto: código certo entra, cookie gravado, sem refreshToken | passou | 200 |
| 11-10 19:34 | código de recuperação entra uma vez | passou | 200 |
| 11-10 19:34 | o mesmo código de recuperação na segunda vez é recusado | passou | (401, 'invalid_code', 'Código inválido.') |
| 11-10 19:35 | desafio terminado em outro lugar: o segundo uso é recusado | **falhou** | (200, 429, 'rate_limited', 'Muitas tentativas. Aguarde um pouco e tente de nov') |
| 11-10 22:18 | desafio terminado em outro lugar: o segundo uso é recusado (repetido sem o limite da rota) | passou | (200, 401, 'invalid_challenge', 'A verificação expirou. Entre com sua senha de novo') |

### Anexo: Cookie e armazenamento

| Horário (UTC) | Verificação | Resultado | Detalhe |
|---|---|---|---|
| 11-10 21:22 | cookie HttpOnly, Secure, Path /, Domain do host da API, SameSite None, cerca de 7 dias | passou | {"httpOnly":true,"secure":true,"path":"/","domainIsApiHost":true,"sameSite":"None","days":7} |
| 11-10 21:22 | nenhum nexo_access nem nexo_refresh | passou | 0 |
| 11-10 21:22 | Local Storage: nexo:session com nome e papel | passou |  |
| 11-10 21:22 | Local Storage sem token e sem valor que comece com eyJ | passou | [] |
| 11-10 21:22 | Session Storage sem token | passou | [] |
| 11-10 21:22 | document.cookie não mostra o cookie | passou |  |

### Anexo: Recarregar

| Horário (UTC) | Verificação | Resultado | Detalhe |
|---|---|---|---|
| 11-10 21:22 | abre logado só com o cookie | passou | /dashboard |
| 11-10 21:23 | F5: auth/refresh 200 com o cookie e Set-Cookie novo, depois auth/me 200 com Bearer, nenhum 401 | passou | refresh:200 me:200 |
| 11-10 21:23 | F5 várias vezes seguidas: a sessão continua e nenhum auth/refresh 401 | passou | refresh:200 |
| 11-10 21:24 | recarga no meio da renovação (Slow 3G): a sessão continua, até dois 409 por carregamento, nenhum 401 nem auth/logout | passou | {"path":"/dashboard","calls":"refresh:200 refresh:200"} |
| 11-10 21:24 | resposta acima de 3 s: aviso de sessão ativa, sem ir ao login | **falhou** | {"notice":false,"path":"/dashboard"} |
| 11-10 21:42 | resposta lenta numa aba enquanto a outra recarrega: aviso de sessão ativa, sem login e sem logout; as duas continuam | passou | {"notice":false,"a":"/dashboard","b":"/dashboard","renewals":"200","logouts":0} |

### Anexo: Várias abas

| Horário (UTC) | Verificação | Resultado | Detalhe |
|---|---|---|---|
| 11-10 21:25 | quatro abas recarregadas ao mesmo tempo continuam logadas, nenhum 401 | passou | refresh:200 refresh:200 |

### Anexo: Troca de loja

| Horário (UTC) | Verificação | Resultado | Detalhe |
|---|---|---|---|
| 11-10 21:25 | auth/switch-store leva o cookie, traz Set-Cookie novo e nenhum refreshToken no corpo | passou | 200 |
| 11-10 21:25 | a outra aba mostra a loja nova sem recarregar | passou |  |
| 11-10 21:25 | depois de recarregar, as duas abas continuam na loja nova | passou |  |

### Anexo: Logout

| Horário (UTC) | Verificação | Resultado | Detalhe |
|---|---|---|---|
| 11-10 21:38 | auth/logout 204 apagando o cookie (Set-Cookie vencido) | passou | {"status":204,"clears":true} |
| 11-10 21:38 | o cookie sumiu e o Local Storage ficou sem nexo:session | passou |  |
| 11-10 21:38 | as outras abas vão sozinhas para o login, com o aviso de sessão encerrada | passou | /login /login?motivo=sessao-encerrada /login?motivo=sessao-encerrada /login?motivo=sessao-encerrada |
| 11-10 21:38 | o endereço de /dashboard leva ao login, sem auth/refresh | passou | /login |
| 11-10 21:38 | Cache-Control: no-store em todas as respostas de auth vistas, inclusive recusas | passou | logout:204 me:200 refresh:200 switch-store:200 |
| 11-10 21:38 | logout logo depois de recarregar sem renovação: vai sem Authorization, 204, cookie some | passou | {"status":204,"auth":false} |
| 11-10 21:38 | sem conexão: vai para o login, marca nexo:pending_sign_out, e o cookie ainda está lá | passou | {"path":"/login","mark":true} |
| 11-10 21:38 | de volta a rede, ao recarregar: auth/logout 204 sozinho, cookie some, a marca sai, nenhum auth/refresh | passou | {"logout":204,"refreshes":0} |
| 11-10 21:39 | recarregar de novo: nenhum auth/logout | passou |  |

### Anexo: Impersonação

| Horário (UTC) | Verificação | Resultado | Detalhe |
|---|---|---|---|
| 11-10 19:37 | a plataforma recusa o acesso à empresa | informação | {"status":403,"code":null,"tabClosed":true,"aviso":"Ações administrativas exigem login com verificação em duas etapas.\nConfigurar segurança"} |
| 11-10 19:51 | uma nova aba abre com a faixa do acesso | passou | /dashboard |
| 11-10 19:52 | nessa aba, nenhum pedido leva Cookie nem X-Orken-Session, e todos levam Authorization | passou | 7 |
| 11-10 19:52 | o token curto fica só no Session Storage dessa aba; o Local Storage dela continua sem tokens | passou | {"ss":1,"ls":0} |
| 11-10 19:52 | sair do acesso: a aba fecha ou vai para acesso encerrado | passou | fechada |
| 11-10 19:52 | nada naquela aba mexeu no cookie do administrador | passou |  |
| 11-10 19:52 | na aba do administrador, recarregar: continua logado como administrador | passou | /platform/tenants/de8b4b6d-fa5c-4f05-85f7-77863b894a7c |

### Anexo: Plataforma

| Horário (UTC) | Verificação | Resultado | Detalhe |
|---|---|---|---|
| 11-10 19:19 | administradores da plataforma com duas etapas ativas no staging | informação | 0 |
| 11-10 19:35 | entrada do administrador pela API: cookie começando com p., sem refreshToken no corpo | passou | {"status":200,"prefix":true} |
| 11-10 19:35 | o administrador entra sem segundo fator no staging | informação | a conta de teste do staging não tem duas etapas ativas; o roteiro pede uma com duas etapas |
| 11-10 19:36 | abre em /platform | passou | /platform |
| 11-10 19:36 | recarregar: continua em /platform, o cookie muda e continua começando com p. | passou | /platform |
| 11-10 19:37 | entrada do administrador pela API: cookie começando com p., sem refreshToken no corpo | passou | {"status":200,"prefix":true} |
| 11-10 19:37 | o administrador entra sem segundo fator no staging | informação | a conta de teste do staging não tem duas etapas ativas; o roteiro pede uma com duas etapas |
| 11-10 19:37 | abre em /platform | passou | /platform |
| 11-10 19:37 | recarregar: continua em /platform, o cookie muda e continua começando com p. | passou | /platform |
| 11-10 19:51 | preparo: duas etapas ativadas por enquanto no administrador do staging | passou | (200, 200, 8) |
| 11-10 19:51 | senha, depois o código: nenhum cookie no passo da senha; depois, cookie começando com p. e sem refreshToken no corpo | passou | {"status":200,"challenge":true} |
| 11-10 19:51 | vai para /platform | passou | /platform |
| 11-10 19:51 | recarregar: continua em /platform; o cookie muda e continua começando com p. | passou | /platform |
| 11-10 19:52 | outro navegador encerra todas as sessões do administrador | passou | {"signIn":200,"revoke":204} |
| 11-10 19:52 | o primeiro navegador vai para o login, sem laço | passou | {"path":"/login?motivo=sessao-encerrada","renewals":1} |
| 11-10 19:53 | Segurança > encerrar todas | **falhou** | TimeoutError: locator.click: Timeout 30000ms exceeded. |
| 11-10 19:54 | fim: duas etapas desativadas de novo, o administrador do staging volta ao que era | passou | 0 |
| 11-10 20:06 | preparo: duas etapas ativadas por enquanto no administrador do staging | passou | (200, 200, 8) |
| 11-10 20:07 | tela Segurança antes do botão | informação | {"path":"/platform","cookie":true,"text":"Pular para o conteúdo ORKEN. Administração da plataforma Plataforma Visão geral Empresas Períodos de teste Atividade S |
| 11-10 20:07 | Segurança > encerrar todas | **falhou** | TimeoutError: locator.click: Timeout 10000ms exceeded. |
| 11-10 20:09 | fim: duas etapas desativadas de novo, o administrador do staging volta ao que era | passou | 0 |
| 11-10 20:22 | preparo: duas etapas ativadas por enquanto no administrador do staging | passou | (200, 200, 8) |
| 11-10 20:22 | abrir /platform/seguranca direto, num navegador sem sessão local, leva a outra tela | informação | /platform |
| 11-10 20:22 | Segurança > encerrar todas: a tela vai para o login e o cookie some | passou | /login?motivo=sessoes |
| 11-10 20:24 | fim: duas etapas desativadas de novo, o administrador do staging volta ao que era | passou | 0 |

### Anexo: Recuperação de senha e e-mail

| Horário (UTC) | Verificação | Resultado | Detalhe |
|---|---|---|---|
| 11-10 22:18 | janela da pessoa aberta e logada antes da redefinição | passou | {"signIn":200,"path":"/pdv"} |
| 11-10 22:18 | nova senha pelo link de redefinição | passou | (200, None) |
| 11-10 22:18 | a janela que estava logada vai para o login com 'Seu acesso foi alterado' | passou | /login?motivo=sessao-revogada |
| 11-10 22:18 | o cookie antigo não renova mais | passou | refresh:401 |
| 11-10 22:18 | entra com a nova senha | passou | 200 |
| 11-10 22:18 | cadastro de empresa sintética | passou | 200 |
| 11-10 22:18 | o link do e-mail entra sozinho, com o cookie gravado e sem refreshToken no corpo | passou | {"status":200,"setCookie":true,"refreshInBody":false,"path":"/dashboard"} |
| 11-10 22:18 | o mesmo link de novo: 'Link inválido ou expirado.', sem cookie novo | **falhou** | verify-email:400 |

### Anexo: Cozinha

| Horário (UTC) | Verificação | Resultado | Detalhe |
|---|---|---|---|
| 11-10 19:49 | gerente sintético entra pela API | passou | 200 |
| 11-10 19:50 | tela da cozinha em Tempo real | passou | /restaurante/cozinha |
| 11-10 19:50 | recarregada, volta a Tempo real | passou |  |
| 11-10 19:50 | WebSocket com access_token na URL | **falhou** | 0 |
| 11-10 21:20 | gerente sintético entra pela API | passou | 200 |
| 11-10 21:20 | tela da cozinha em Tempo real | passou | /restaurante/cozinha |
| 11-10 21:20 | recarregada, volta a Tempo real | passou |  |
| 11-10 21:20 | conexão com access_token na URL (WebSocket ou outro transporte do SignalR) | **falhou** | {"websockets":0,"transports":[]} |
| 11-10 21:20 | hubs/restaurant/negotiate sem Cookie | passou | {"negotiations":1,"headersRead":true} |
| 11-10 21:37 | 16 min parada: a conexão volta sozinha com outro token, depois de um auth/refresh | passou | {"reopened":2,"renewed":1,"live":true} |
| 11-10 21:37 | papel mudado por outra conta: vai para o login com 'Seu acesso foi alterado' | passou | {"put":200,"path":"/login?motivo=sessao-revogada"} |
| 11-10 21:40 | conexão do hub com access_token na URL (visto pelo protocolo do Chromium) | passou | {"live":true,"websockets":1,"longPolling":0,"wsToken":true,"pollToken":false} |

### Anexo: Falhas

| Horário (UTC) | Verificação | Resultado | Detalhe |
|---|---|---|---|
| 11-10 21:22 | renovações demais na mesma sessão: 429 com Retry-After | **falhou** | {"after":299} |
| 11-10 21:22 | depois do 429, a navegação continua | passou | /produtos |
| 11-10 21:22 | recarregar durante o 429: aviso de sessão ativa, sem ir ao login | passou | /produtos |
| 11-10 21:25 | renovação sem rede: aviso de sessão ativa, sem ir ao login | passou | /produtos |
| 11-10 21:25 | com a rede de volta, ao navegar, renova sem novo login | passou | refresh:200 |
| 11-10 21:37 | depois do tempo indicado, volta sozinha | passou | refresh:200 |
| 11-10 21:37 | cookie apagado: um auth/refresh 401, depois o login, sem laço | passou | refresh:401 |
| 11-10 21:41 | renovações demais na mesma sessão: 429 com Retry-After (lido na própria resposta) | passou | {"at":300,"status":429,"retryAfter":"815","cache":"no-store"} |

### Anexo: Migração

| Horário (UTC) | Verificação | Resultado | Detalhe |
|---|---|---|---|
| 11-10 22:01 | entrada como o bundle de hoje: tokens no corpo | passou | 200 |
| 11-10 22:01 | primeiro auth/refresh leva o token no corpo e o header do modo; 200 com Set-Cookie e sem refreshToken | passou | {"status":200,"bodyToken":true,"setCookie":true,"refreshInBody":false} |
| 11-10 22:01 | continua logado, sem novo login; Local Storage sem os dois tokens; o cookie aparece | passou | {"path":"/dashboard","left":[]} |
| 11-10 22:01 | ao recarregar, auth/refresh vai sem corpo | passou | {"status":200,"bodyToken":false} |

### Anexo: CSRF

| Horário (UTC) | Verificação | Resultado | Detalhe |
|---|---|---|---|
| 11-10 22:00 | ladrão renova primeiro, pelo terminal, com o valor copiado | passou | 200 |
| 11-10 22:00 | a aba recebe 409 com Retry-After, no máximo duas vezes, continua, e nenhum auth/logout | passou | refresh:409 refresh:409 |
| 11-10 22:01 | depois da carência: auth/refresh 401 e a tela vai para o login | passou | {"how":"navegando","path":"/login?motivo=sessao-encerrada"} |
| 11-10 22:01 | o valor do ladrão, apresentado de novo, recebe 401 | passou | 401 |
| 11-10 22:01 | 30 pedidos simples de outro site: todos 403 | passou | 403 (30) |
| 11-10 22:01 | logo depois, o login funciona: nenhum pedido daquele site gastou o limite | passou | 200 |
| 11-10 22:01 | origem fora da lista: 403 | passou | 403 |
| 11-10 22:01 | sem Origin: 403 | passou | 403 |
| 11-10 22:01 | sem o header do modo: 400 | passou | 400 |
| 11-10 22:01 | outra grafia do nome (__HOST-): 401 | passou | 401 |
| 11-10 22:01 | o nome exato duas vezes: 401 | passou | 401 |
| 11-10 22:01 | site só da lista do CORS (www), com o header: 403 | passou | 403 |
| 11-10 22:01 | access token no cookie antigo nexo_access: 401 | passou | 401 |
| 11-10 22:02 | depois das tentativas, a aba continua: auth/refresh 200 | passou | refresh:200 |
| 11-10 22:02 | corpo de 20 KB sem tamanho declarado em /api/auth/refresh: 413 | passou | 413 |
| 11-10 22:02 | corpo de 20 KB sem tamanho declarado em /api/auth/logout: 413 | passou | 413 |
| 11-10 22:02 | corpo de 20 KB sem tamanho declarado em /api/platform/auth/refresh: 413 | passou | 413 |
| 11-10 22:02 | ladrão que renovou primeiro: uma linha crítica refresh_reuse na auditoria | passou | 1 |
| 11-10 22:02 | corpos grandes não criaram error_events | passou | 0 |

### Anexo: Limpar o armazenamento

| Horário (UTC) | Verificação | Resultado | Detalhe |
|---|---|---|---|
| 11-10 21:25 | sem recarregar, a navegação continua | passou | /produtos |
| 11-10 21:25 | ao recarregar, a mesma tela abre sem login: auth/refresh 200, auth/me 200 e nexo:session de volta | passou | refresh:200 me:200 |

### Anexo: Navegadores

| Horário (UTC) | Verificação | Resultado | Detalhe |
|---|---|---|---|
| 11-10 22:00 | Firefox: abrir o navegador | **falhou** | Error: browserType.launch: spawn UNKNOWN |
| 11-10 22:00 | WebKit (motor do Playwright 27.2) em *.up.railway.app: abre logado pelo cookie entre sites | informação | {"signIn":200,"signedIn":true,"refresh":"200+cookie"} |

### Anexo: Voltar atrás

| Horário (UTC) | Verificação | Resultado | Detalhe |
|---|---|---|---|
| 11-10 22:34 | antes: uma pessoa logada no bundle do modo cookie | passou | {"signIn":200,"path":"/dashboard"} |
| 11-10 22:35 | bundle publicado de novo sem o modo cookie (mesmo commit, variável desligada) | passou | ('633a301', 'SUCCESS') |
| 11-10 22:35 | bundle sem o modo: quem estava no modo cookie vai para o login (precisa entrar de novo) | **falhou** | /dashboard |
| 11-10 22:35 | o bundle sem o modo nunca manda o header do modo nem usa o cookie | **falhou** | 200+modo+cookie |
| 11-10 22:35 | entrada no modo de hoje: tokens no corpo da resposta, nenhum cookie de sessão | passou | 200 |
| 11-10 22:36 | bundle sem o modo: a sessão do modo de hoje funciona e renova com o token no corpo, sem cookie | passou | {"path":"/dashboard","refresh":"200+corpo"} |
| 11-10 22:37 | API publicada no commit de produção (8a164ce) | passou | ('8a164ce', 'SUCCESS') |
| 11-10 22:38 | API de produção: a sessão aberta na API nova continua e renova | passou | {"path":"/dashboard","refresh":"200+corpo+cookie-antigo"} |
| 11-10 22:38 | API de produção: login funciona e devolve os tokens no corpo | passou | 200 |
| 11-10 22:38 | API de produção volta a gravar nexo_access e nexo_refresh, como hoje em produção | informação | grava |
| 11-10 22:38 | o cookie do modo cookie ficou no navegador de quem migrou, sem uso | informação | ainda no navegador, vence sozinho |
| 11-10 22:39 | restaurado: API de volta a 633a301 | passou | ('633a301', 'SUCCESS') |
| 11-10 22:40 | restaurado: bundle de volta ao modo cookie | passou | ('633a301', 'SUCCESS') |

### Anexo: Contas

| Horário (UTC) | Verificação | Resultado | Detalhe |
|---|---|---|---|
| 11-10 19:18 | conta sintética g1 (gerente) | passou | 201 |
| 11-10 19:18 | conta sintética g2 (gerente) | passou | 201 |
| 11-10 19:18 | conta sintética v1 (vendedor) | passou | 201 |
| 11-10 19:18 | conta sintética b1 (vendedor) | passou | 201 |
| 11-10 19:18 | conta sintética i1 (vendedor) | passou | 201 |
| 11-10 19:18 | b1 passa a blocked | passou | 200 |
| 11-10 19:18 | i1 passa a inactive | passou | 200 |

### Anexo: Execução

| Horário (UTC) | Verificação | Resultado | Detalhe |
|---|---|---|---|
| 11-10 19:36 | erro inesperado no navegador (plataforma) | **falhou** | page.reload: Target page, context or browser has been closed |
| 11-10 19:36 | navegador: plataforma e impersonação | **falhou** | 1 |
| 11-10 19:37 | fluxo de impersonação interrompido: ver a recusa acima | informação |  |
| 11-10 19:37 | navegador: plataforma e impersonação | passou | 0 |
| 11-10 19:53 | navegador: plataforma com duas etapas e impersonação | **falhou** | 1 |
| 11-10 20:07 | navegador: plataforma com duas etapas e impersonação | **falhou** | 1 |
| 11-10 20:22 | navegador: plataforma com duas etapas e impersonação | passou | 0 |
| 11-10 21:39 | navegador Chromium, fluxos principais | **falhou** | 1 |
| 11-10 22:02 | navegadores Firefox e WebKit, ladrão, migração e CSRF | **falhou** | 1 |
| 11-10 22:19 | navegador: recuperação e verificação de e-mail | **falhou** | (0, 0, 1) |
| 11-10 22:36 | navegador: volta do bundle | **falhou** | (0, 1) |
| 11-10 22:38 | navegador: volta da API | passou | 0 |

### Anexo: Voltar atrás (2ª rodada), 2026-11-11

As linhas de `rollback2-expired-tab` são as três tentativas da aba com o token vencido. As falhas das duas primeiras são do instrumento
(seção 2.1).

| Horário (UTC) | Fase | Verificação | Resultado | Detalhe |
|---|---|---|---|---|
| 11-11 04:04 | rollback2 | antes: aba A aberta no bundle do modo cookie (painel e produtos), e fica aberta sem recarregar | passou | {"signIn":200,"path":"/dashboard","refresh":"200+modo+cookie+novo-cookie"} |
| 11-11 04:04 | rollback2 | antes: sessão B aberta no bundle do modo cookie, depois o navegador fecha | passou | {"signIn":200,"path":"/dashboard"} |
| 11-11 04:05 | rollback2 | antes: sessão C aberta no bundle do modo cookie, depois o navegador fecha | passou | {"signIn":200,"path":"/dashboard"} |
| 11-11 04:05 | rollback2 | antes: a segunda visita a /dashboard pede o HTML ao servidor (no-cache), não ao cache do disco | passou | {"status":200,"fromDiskCache":false,"cacheControl":"no-cache"} |
| 11-11 04:06 | rollback2 | bundle publicado de novo sem o modo cookie (c6c2cfc, variável desligada) | passou | {"ok":true,"commit":"c6c2cfc","status":"SUCCESS"} |
| 11-11 04:06 | rollback2 | aba A, sem recarregar: continua no bundle que já estava na memória e segue funcionando (a API aceita os dois modos) | passou | {"path":"/dashboard","calls":6,"statuses":"200 204","assetsFailed":0} |
| 11-11 04:06 | rollback2 | sessão C, o navegador abre o app de novo: recebe o HTML novo do servidor, não o do cache | passou | {"status":200,"fromDiskCache":false,"cacheControl":"no-cache"} |
| 11-11 04:06 | rollback2 | sessão C: vai para o login (quem migrou entra de novo enquanto o bundle está de volta) | passou | /login |
| 11-11 04:06 | rollback2 | sessão C: o bundle sem o modo não manda o header do modo nem usa o cookie | passou | 401 |
| 11-11 04:06 | rollback2 | sessão D, aberta com o bundle de hoje: tokens no corpo, renova com o token no corpo, sem modo e sem cookie | passou | {"signIn":200,"path":"/dashboard","refresh":"200+corpo"} |
| 11-11 04:07 | rollback2 | API publicada no commit de produção (8a164ce) | passou | {"ok":true,"commit":"8a164ce","status":"SUCCESS"} |
| 11-11 04:07 | rollback2 | sessão D, aberta durante toda a volta da API: continua e renova na API anterior, sem novo login | passou | {"path":"/dashboard","refresh":"200+corpo+cookie-antigo"} |
| 11-11 04:07 | rollback2 | aba A, ainda sem recarregar, com o token de acesso ainda válido: segue funcionando na API anterior | passou | {"path":"/produtos","calls":1,"statuses":"200"} |
| 11-11 04:07 | rollback2 | esperando o token de acesso da aba A vencer | informação | 724 s |
| 11-11 04:20 | rollback2 | aba A com o token vencido, na API anterior: a renovação sem corpo é recusada sem veredito sobre a sessão; a aba não vai ao login nem manda auth/logout | passou | {"path":"/dashboard","notice":false,"auth":"nenhum"} |
| 11-11 04:20 | rollback2 | aba A recarregada durante a volta: HTML novo do servidor; vai para o login, como quem migrou (o cookie fica intacto) | passou | {"doc":{"status":200,"fromDiskCache":false,"cacheControl":"no-cache"},"path":"/login","auth":"401"} |
| 11-11 04:20 | rollback2 | restaurado: API de volta a c6c2cfc | passou | {"ok":true,"commit":"c6c2cfc","status":"SUCCESS"} |
| 11-11 04:21 | rollback2 | restaurado: bundle de volta ao modo cookie (c6c2cfc) | passou | {"ok":true,"commit":"c6c2cfc","status":"SUCCESS"} |
| 11-11 04:21 | rollback2 | sessão B, que ninguém usou durante a volta: abre logada pelo cookie, sem novo login | passou | {"path":"/dashboard","refresh":"200+modo+cookie+novo-cookie"} |
| 11-11 04:22 | rollback2 | sessão C, que foi ao login durante a volta e não entrou de novo: abre logada pelo cookie, sem novo login | passou | {"path":"/dashboard","refresh":"200+modo+cookie+novo-cookie"} |
| 11-11 04:22 | rollback2 | aba A recarregada depois da restauração: entra pelo cookie, que a volta não tocou | passou | {"path":"/workspaces","refresh":"200+modo+cookie+novo-cookie"} |
| 11-11 04:22 | rollback2 | sessão D, aberta no bundle de hoje: migra para o cookie sem novo login (token no corpo e o header; 200 com cookie, sem refreshToken) | passou | {"path":"/dashboard","refresh":"200+modo+corpo+novo-cookie","leftInStorage":[]} |
| 11-11 04:22 | rollback2 | sessão D, recarregada de novo: renova só pelo cookie | passou | {"path":"/dashboard","refresh":"200+modo+cookie+novo-cookie"} |
| 11-11 04:22 | rollback2 | arquivos do bundle que falharam ao carregar nas abas abertas durante as publicações | informação | {"a":0,"d":0} |
| 11-11 04:56 | rollback2-expired-tab | aba E aberta no bundle do modo cookie, e fica aberta sem recarregar | passou | {"signIn":200,"path":"/dashboard"} |
| 11-11 04:58 | rollback2-expired-tab | API publicada no commit de produção (8a164ce), com a aba E aberta | passou | {"ok":true,"commit":"8a164ce","status":"SUCCESS"} |
| 11-11 04:58 | rollback2-expired-tab | esperando o token de acesso da aba E vencer | informação | 818 s |
| 11-11 05:12 | rollback2-expired-tab | aba E com o token vencido chama a API anterior: a renovação pelo cookie é exercida | **falhou** | {"renewals":"nenhum","apiCalls":3} |
| 11-11 05:12 | rollback2-expired-tab | aba E: a API anterior não dá veredito (não é 401); a aba fica, com o aviso de sessão ativa, sem login e sem auth/logout | **falhou** | {"path":"/fornecedores","notice":false,"auth":"nenhum","statuses":"200"} |
| 11-11 05:12 | rollback2-expired-tab | erro inesperado no navegador (aba com token vencido) | **falhou** | Error: Command failed: python (caminho local) backend c6c2cfcb9c1a799a4a3025f201042850c703d1f5 |
| 11-11 05:12 | rollback2-expired-tab | restauração de segurança da API depois de um erro | **falhou** | {"ok":false,"status":"Error: Command failed: python (caminho local)"} |
| 11-11 05:15 | rollback2-expired-tab | aba E aberta no bundle do modo cookie, e fica aberta sem recarregar | passou | {"signIn":200,"path":"/dashboard"} |
| 11-11 05:15 | rollback2-expired-tab | API publicada no commit de produção (8a164ce), com a aba E aberta | passou | {"ok":true,"commit":"8a164ce","status":"SUCCESS"} |
| 11-11 05:15 | rollback2-expired-tab | esperando o token de acesso da aba E vencer | informação | 999 s |
| 11-11 05:32 | rollback2-expired-tab | aba E com o token vencido chama a API anterior: a renovação pelo cookie é exercida | passou | {"renewals":"/auth/refresh:400+modo+cookie /auth/refresh:400+modo+cookie /auth/refresh:400+modo+cookie","apiCalls":3} |
| 11-11 05:32 | rollback2-expired-tab | aba E: a API anterior não dá veredito (não é 401); a aba fica, com o aviso de sessão ativa, sem login e sem auth/logout | **falhou** | {"path":"/fornecedores","notice":false,"auth":"/auth/refresh:400+modo+cookie /auth/refresh:400+modo+cookie /auth/refresh:400+modo+cookie","statuses":"401"} |
| 11-11 05:33 | rollback2-expired-tab | API de volta a c6c2cfc | passou | {"ok":true,"commit":"c6c2cfc","status":"SUCCESS"} |
| 11-11 05:34 | rollback2-expired-tab | aba E, sem recarregar e sem novo login: volta a renovar pelo cookie quando a API volta | passou | {"path":"/dashboard","auth":"/auth/refresh:200+modo+cookie"} |
| 11-11 05:35 | rollback2-expired-tab | aba E aberta no bundle do modo cookie, e fica aberta sem recarregar | passou | {"signIn":200,"path":"/dashboard"} |
| 11-11 05:36 | rollback2-expired-tab | API publicada no commit de produção (8a164ce), com a aba E aberta | passou | {"ok":true,"commit":"8a164ce","status":"SUCCESS"} |
| 11-11 05:36 | rollback2-expired-tab | esperando o token de acesso da aba E vencer | informação | 999 s |
| 11-11 05:53 | rollback2-expired-tab | aba E com o token vencido chama a API anterior: a renovação pelo cookie é exercida | passou | {"renewals":"/auth/refresh:400+modo+cookie /auth/refresh:400+modo+cookie /auth/refresh:400+modo+cookie","apiCalls":3} |
| 11-11 05:53 | rollback2-expired-tab | aba E: a API anterior não dá veredito (não é 401); a aba fica, com o aviso de sessão ativa, sem login e sem auth/logout | passou | {"path":"/fornecedores","notice":true,"text":" Sua sessão continua ativa: tente de novo em instantes","auth":"/auth/refresh:400+modo+cookie /auth/refresh:400+mo |
| 11-11 05:53 | rollback2-expired-tab | API de volta a c6c2cfc | passou | {"ok":true,"commit":"c6c2cfc","status":"SUCCESS"} |
| 11-11 05:54 | rollback2-expired-tab | aba E, sem recarregar e sem novo login: volta a renovar pelo cookie quando a API volta | passou | {"path":"/dashboard","auth":"/auth/refresh:200+modo+cookie"} |
