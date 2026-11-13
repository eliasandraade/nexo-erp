# Segunda promoção: sessão em cookie

Dossiê da promoção que leva a sessão em cookie para a produção. **Nada aqui foi executado na produção, e nenhuma promoção está autorizada.** A promoção
espera as correções em revisão, a homologação nos navegadores, a homologação humana e a autorização explícita do proprietário.

## 1. Commit candidato

**Candidato: `834737db3f9c5a5db32b7a495f353d4400e5b2a4`** (`master`, merge do PR #186), com `master-gate` verde em 2026-11-13 03:47Z e
staging em `SUCCESS` nesse SHA. Ele contém todas as correções P0 de código desta remediação. A promoção continua **não autorizada**:
ser candidato só quer dizer que é este SHA que os ensaios e a homologação precisam validar.

A regra do login foi decidida pelo proprietário em 2026-11-12: o login é único em todo o Orken ERP.

`0b9a398`, `c6c2cfc` e `633a301` não são candidatos. Nenhum commit é promovido automaticamente.

O que o candidato leva além da produção atual (`8a164ce`):

| PR | O que muda | Migração de banco | Estado |
|---|---|---|---|
| #176 | Política de renovação: teto por sessão, 409 com carência de 30 s, 429 e falhas de rede sem encerrar a sessão | Não | Na `master` |
| #178 | Sessão em cookie em modo duplo: a API aceita os dois modos, e o bundle escolhe na compilação por `VITE_SESSION_MODE` | Não | Na `master` |
| #181 | `Cache-Control: no-cache` no HTML de todas as rotas do app | Não | Na `master` |
| #183 | Limites de autenticação contados pela rota casada | Não | Na `master` (`0b9a398`, 2026-11-12) |
| #184 | Login da plataforma: contagem da segunda etapa e teto de códigos por desafio | **Sim:** coluna `mfa_attempts` em `platform_sessions` | Na `master` (`9e5a653`, 2026-11-12) |
| #185 | Login único em todo o sistema: uma normalização, uma verificação nos três caminhos que criam conta e índice único. Traz também o `global.json` que fixa o SDK .NET 8 no CI | **Sim:** índice único `IX_users_login` em `users (login)` | Na `master` (`038040f`, 2026-11-13) |
| #187 | Estabilidade do teste de saída da impersonação: os cabeçalhos são lidos no envio, e a verificação de que a requisição não leva `Cookie` continua | Não | Na `master` (`6b909ae`, 2026-11-13) |
| #186 | Script de promoção: confere a origem do `master-gate` e mostra mudanças nos workflows | Não | Na `master` (`834737d`, 2026-11-13). Não muda o que é publicado |

**Variáveis de produção:** nenhuma nova, além de `VITE_SESSION_MODE=cookie` no `main`, na etapa 2. Os padrões do código já servem à produção:
- a lista da sessão é `erp.orken.com.br`;
- o CORS aceita `erp` e `www`;
- `SameSite` é `Lax`.

### Migrações e compatibilidade do esquema

A API aplica as migrações ao iniciar, e voltar a API não as desfaz. Por isso cada migração do candidato precisa ser compatível com a versão
anterior (`8a164ce`):

| Migração | O que faz | Compatível com `8a164ce` |
|---|---|---|
| `PlatformMfaChallengeAttempts` (#184) | Acrescenta `mfa_attempts integer not null default 0` | Sim. A API anterior insere sessões sem conhecer a coluna, e o banco preenche 0 |
| `UsersLoginUniqueEverywhere` (#185) | Cria o índice único `IX_users_login`. Não altera nenhuma linha. Para antes do índice se houver login repetido | Sim para leitura e login. Ao criar um login repetido, a API anterior recebe a recusa do banco e responde 409, porque já trata violação de unicidade |

Nenhuma dessas migrações apaga ou reescreve dados. Restaurar um backup só seria necessário se uma migração falhasse no meio; as duas são
instruções únicas e transacionais.

O que foi conferido para a migração do login, em 2026-11-12:

| Verificação | Resultado |
|---|---|
| Logins repetidos em produção e no staging (exatos, e sem diferenciar maiúsculas ou espaços) | 0 nos dois |
| Logins fora da forma normalizada, ou iguais ao e-mail de um operador da plataforma | 0 nos dois |
| Scripts reais da migração (gerados pelo EF) numa cópia descartável do staging | Aplicou; as linhas de `users` ficaram idênticas; o mesmo login em outra empresa passou a ser recusado pelo índice |
| Reversão na mesma cópia | Removeu o índice e o registro da migração; linhas idênticas; aplicou de novo em seguida |
| Mesma migração sobre uma cópia com um login repetido plantado | Recusou antes de criar o índice, sem alterar nada |

No staging real, a migração foi aplicada no merge do #185 (2026-11-13), depois de um backup verificado. O resultado:

- 79 migrações e o índice criado;
- as 29 linhas de `users` idênticas às de antes;
- contas existentes entram em qualquer grafia do login;
- um login de outra empresa e o `admin` do seed são recusados com 409 `login_taken`;
- oito criações simultâneas do mesmo login, por duas empresas, criam uma conta e recebem sete 409 `login_taken`, sem 500.

Quando o login e o e-mail colidem juntos, na mesma empresa, o índice do e-mail pode responder primeiro, e quem perde recebe 409
`duplicate`. Também nunca 500.

## 2. CI e Quality Gate

| Item | Resultado |
|---|---|
| `master-gate` em `c6c2cfc` | Verde (2026-11-11 04:02Z) |
| PR #184 no SHA exato do merge (`b5759b8`) | Tudo verde |
| `master-gate` em `9e5a653` (merge do #184) | Verde (2026-11-12 11:29Z). Staging em `SUCCESS` nesse commit, com a migração aplicada (78 migrações) |
| PR #183 no SHA exato do merge (`9fe337c`, já com o #184) | Tudo verde; cobertura do código novo em 100% |
| `master-gate` em `0b9a398` (merge do #183) | Verde (2026-11-12). Staging em `SUCCESS` nesse commit |
| Regressão no staging depois de cada merge | Passou: login, renovação e logout de empresa; login da plataforma; segunda etapa da plataforma com o contador gravado por desafio; outra grafia de uma rota limitada gasta o mesmo orçamento |
| PR #185 no SHA exato do merge (`b614b94`) | Tudo verde: 1.254 testes de unidade e 1.821 de integração, cobertura do código novo em 100% |
| `master-gate` em `038040f` (#185), `6b909ae` (#187) e `834737d` (#186) | Verde nos três. Staging em `SUCCESS` em `834737d` |
| SDK do CI | Fixado em .NET 8 pelo `global.json`. Uma imagem nova do runner trazia o SDK 11, que não compila o host dos testes |
| PR #181 no SHA exato do merge (`7e7ae73`) | Tudo verde: `ci-gate`, `frontend`, `auth-e2e` cookie (101 passaram) e storage, `csp-e2e` (31 passaram, incluindo o teste novo), `sonarcloud`, SonarCloud Code Analysis e GitGuardian |
| Quality Gate geral do SonarCloud em `master` | **ERROR em 2 condições do código novo**: confiabilidade C (o limite é A) e cobertura de 77,3% (o mínimo é 80%). É o mesmo estado de `633a301`; o PR #181 não mudou nenhuma das duas. Os limites não foram reduzidos. Levar o gate a verde depende das decisões já abertas sobre os PRs de `S9383` e o período do código novo |

## 3. Volta atrás no staging

- **1ª rodada (2026-11-10):** a volta do bundle reprovou por causa do cache do HTML.
- **2ª rodada (2026-11-11):** depois do PR #181, a volta completa passou. Primeiro o bundle, depois a API em `8a164ce`, depois a
  restauração, com abas e sessões abertas durante as publicações. Nenhuma sessão se perdeu sem que o comportamento previsto mandasse:
  - só quem estava no modo cookie e recarregou durante a volta foi ao login;
  - a sessão do modo de hoje atravessou tudo;
  - depois da restauração, todas voltaram sem novo login;
  - uma aba aberta com o token vencido na API anterior ficou com o aviso, sem perder a sessão, e renovou sozinha quando a API voltou.

  Depois da última publicação, a validação final passou: 33 verificações do modo cookie e 109 rotas com `no-cache`.

Detalhes no [registro](homologacao-sessao-staging-2026-11-10.md), seção 2.1.

## 4. Navegadores

| Navegador | Situação |
|---|---|
| Chromium | Passou: as três rodadas automatizadas, incluindo os domínios próprios com `SameSite=Lax`, mais as suítes `auth-e2e` e `csp-e2e` |
| WebKit do Playwright | Passou no que executou nos domínios próprios. Cinco itens não rodaram (CSRF, isolamento e expiração), por limite do motor no Windows. Não substitui o Safari |
| Firefox | **Pendente.** Sem Firefox na máquina de teste; fica para a checklist humana |
| Safari | **Pendente.** Só em aparelho Apple; fica para a checklist humana |

## 5. Testes humanos

**Pendentes.** A lista está na [checklist](checklist-homologacao-humana-sessao.md), e nenhuma linha está marcada.

## 6. Riscos residuais

- **Aba aberta durante uma publicação.** Ela continua com o bundle que já estava na memória até ser recarregada. Na volta do bundle, segue no
  modo cookie até recarregar. Se o token vencer depois que a API também voltou, ela não renova: mostra o aviso de sessão ativa, sem perder a
  sessão. Ao recarregar, vai ao login. Se a API for restaurada antes disso, renova sozinha. Se navegar para uma tela que nunca abriu, pede um arquivo do bundle anterior, que não existe mais. A tela falha
  até a pessoa recarregar. Isso vale para qualquer publicação, não só para esta.
- **Arquivo do bundle que não existe.** Ele cai no `index.html` com o cabeçalho de imutável. É raro, e ficou fora do PR #181.
- **Riscos aceitos no contrato** (`sessao-auditoria-e-migracao.md`, seção 9): a queda dura do Redis, e os tetos compartilhados por endereço.
  O limite de login é de 5 por 15 minutos por IP; numa rede com muitas pessoas atrás do mesmo endereço, isso aparece primeiro.
- **CSP em modo de relatório.** O cookie HttpOnly impede levar a sessão para fora, mas um XSS ainda age enquanto a página está aberta, até o
  `script-src` passar a bloquear.
- **Quality Gate** em ERROR no código novo (seção 2).
- **Fora da sessão, já conhecidos:** a chave do Stripe é de produção e nunca recebeu webhook, e `api/billing/webhook` lê o corpo inteiro antes
  da assinatura. Nada disso muda com esta promoção.

## 7. Plano

### Condições para começar

Todas precisam estar cumpridas. Qualquer uma em falta bloqueia a promoção:

1. PRs do candidato mesclados, um por vez, cada um com o CI verde no SHA exato.
2. `master-gate` verde no SHA do candidato, e staging em `SUCCESS` nesse SHA.
3. Bateria de navegadores completa nos domínios próprios do staging (`erp.staging.orken.com.br`), sem falha em aberto.
4. [Checklist humana](checklist-homologacao-humana-sessao.md) preenchida por uma pessoa, em Chrome ou Edge, Firefox e Safari.
5. Volta atrás repetida no staging com o candidato, incluindo a API anterior sobre o esquema novo.
6. Backup verificado (abaixo) e autorização explícita do proprietário, para cada etapa.
7. Contenção do GitHub aplicada: só o administrador atualiza `production`, e nenhum app desnecessário tem escrita em conteúdo e em
   workflows no repositório.

### Duas etapas

1. **Etapa 1: promover o candidato sem `VITE_SESSION_MODE`.**
   - O bundle continua no modo de hoje, e ninguém migra de sessão.
   - Entram a política de renovação, a API em modo duplo, o `no-cache` do HTML e as correções dos PRs #183, #184 e #185.
   - No staging, uma sessão no modo de hoje funcionou e renovou nessa combinação (bundle sem o modo, API nova), e a suíte `auth-e2e` no modo
     storage está verde.
2. **Etapa 2: ligar o modo cookie.** Só depois da homologação e de um período sem incidente na etapa 1:
   - definir `VITE_SESSION_MODE=cookie` no `main` de produção;
   - publicar de novo o mesmo commit;
   - cada pessoa migra na próxima recarga, sem novo login.

### Backup e restauração, antes de cada etapa

1. Backup manual do volume do Postgres de produção no Railway, com o id anotado.
2. Cópia lógica (`pg_dump`) lida de volta por inteiro, com as contagens das tabelas principais comparadas com o banco ao vivo. Depois, a cópia
   é apagada do contêiner.
3. Contagens de referência: empresas, usuários, sessões ativas, assinaturas e auditoria. Elas são comparadas depois da publicação.
4. **Restauração:** o caminho principal é restaurar o backup do volume pelo Railway, e ele nunca foi ensaiado. Ensaiar uma vez no
   staging (restaurar um backup do volume e conferir as contagens) antes da etapa 1.
   - Em 2026-11-12 foi ensaiada no staging a restauração da cópia lógica: o `pg_dump` foi restaurado num banco descartável e comparado
     com o banco ao vivo, tabela por tabela (161 tabelas, 1.125 linhas, idênticas).

### Publicação

1. Conferir:
   - `production` em `8a164ce`;
   - as condições da seção acima;
   - `Billing__NewCheckoutsEnabled` ausente;
   - Tax Intelligence sem variáveis.
2. Etapa 1: `scripts/promote-production.sh <SHA completo do candidato>`.
   - Com o PR #186 na `master`, o script lista o que mudou em `.github/`, nele próprio e no `global.json` desde `8a164ce`, e recusa.
     Ler essa diferença e repetir com `--ci-changes-reviewed`.
3. Etapa 2, quando autorizada: a variável no `main` de produção e o novo deploy do `main` no mesmo commit.
4. Depois de cada etapa, verificar:
   - backend e frontend em `SUCCESS` no SHA certo, e as migrações aplicadas (log de início da API);
   - `/health`;
   - `Cache-Control: no-cache` em todas as rotas de `erp.orken.com.br` e `www.orken.com.br`, e assets imutáveis;
   - login real feito por uma pessoa, recarga, logout e upload;
   - login do administrador da plataforma com o código;
   - portais públicos;
   - contagens de referência.

### Observabilidade e critérios de bloqueio

Acompanhar por 24 horas depois de cada etapa. Qualquer item abaixo interrompe a sequência e aciona a volta:

| Sinal | Onde ver | Bloqueia quando |
|---|---|---|
| Erros da API | `error_events` e log do backend | Aparece um tipo de erro novo em rota de autenticação, ou qualquer erro de migração |
| Logins | Auditoria: logins com sucesso e com falha | Pessoas que entravam deixam de entrar, ou as falhas sobem sem explicação |
| Renovação de sessão | Respostas de `auth/refresh` no log | 401 em série para sessões que estavam ativas |
| Limites | Respostas 429 nas rotas de autenticação | Uma loja inteira recebe 429 em uso normal |
| Relatórios de CSP | Log da API | Violação nova em fluxo do produto |
| Cockpit | Métricas do ERP | A coleta falha, ou as contagens divergem das de referência |
| Dados | Contagens de referência | Qualquer diferença não explicada |

### Volta atrás

- **Etapa 2:** retirar `VITE_SESSION_MODE` e publicar de novo o `main` no mesmo commit.
  - Com o `no-cache`, a próxima navegação de cada pessoa já recebe o bundle sem o modo.
  - Quem migrou entra de novo.
  - O cookie fica no navegador. Se o modo voltar, ele vale de novo, como o staging mostrou.
- **Etapa 1, ou as duas:** no painel do Railway, "Redeploy" das implantações de `8a164ce`, **primeiro o `main`, depois o `backend`**.
  - O banco fica com as migrações do candidato. Elas são compatíveis com `8a164ce` (seção 1), e isso precisa ser provado no staging antes:
    a API de `8a164ce` sobre o esquema do candidato, com login, renovação, login da plataforma e criação de usuário (o índice único
    do login).
  - `production` só anda para a frente. A próxima promoção publica de novo o que estiver nela, então uma volta definitiva é um `git revert`
    promovido.
- **Restauração do banco:** só se uma migração falhar ou houver perda de dados. É a última opção, e depende do ensaio de restauração acima.

## 8. Ensaios no staging antes da etapa 1

Os dois ensaios rodam só no staging, com o candidato já publicado nele, e cada um precisa de autorização antes de começar. Nenhum toca a
produção.

### 8.1 Volta atrás completa com o candidato

Prova que o caminho da seção "Volta atrás" funciona com o esquema novo: a API de `8a164ce` roda sobre as migrações do candidato.

**Antes:**

- Staging em `SUCCESS` no SHA do candidato, com `VITE_SESSION_MODE=cookie`.
- Backup verificado, como na seção 7: cópia lógica restaurada num banco descartável e comparada tabela por tabela, mais um backup manual
  do volume.
- Duas sessões abertas na mesma máquina, em janelas separadas:
  - S1, no modo cookie, com o bundle atual;
  - S2, aberta no passo 2, no modo de hoje.

**Passos e o que precisa acontecer:**

| # | Passo | O que precisa acontecer |
|---|---|---|
| 1 | Retirar `VITE_SESSION_MODE` do `main` e publicar o `main` de novo no mesmo commit | S1, ao recarregar, vai ao login. Um login novo funciona no modo de hoje (é a S2) |
| 2 | Publicar o `main` de `8a164ce` (primeiro o `main`) | S2 continua logada ao recarregar |
| 3 | Publicar o `backend` de `8a164ce` | A API sobe sem erro de migração. Ela encontra no histórico migrações que não conhece e não desfaz nenhuma |
| 4 | Com a API de `8a164ce` sobre o esquema do candidato | Ver a tabela seguinte |
| 5 | Publicar o `backend` do candidato, depois o `main` do candidato com `VITE_SESSION_MODE=cookie` | S2 passa ao cookie ao recarregar, sem novo login. Um login novo cria o cookie |

O passo 4 confere a API de `8a164ce` sobre o esquema do candidato:

| Verificação | Resultado esperado |
|---|---|
| `/health` | 200 |
| Login, renovação e logout de empresa | 200, 200 e 204 |
| Login do administrador da plataforma, sem e com a segunda etapa | Entra. A sessão pendente grava `mfa_attempts = 0` pelo padrão do banco |
| Criar usuário com um login novo | Criado |
| Criar usuário com um login que existe em outra empresa | O índice recusa e a API responde 409. Nunca 500 |
| Cadastro público com um e-mail já cadastrado | 409, como hoje |
| Linhas de `users` e contagens das tabelas principais | Iguais às de antes do ensaio, mais o que os passos criaram |

**Critério de aprovação:** todos os passos e verificações, sem nenhuma resposta 500 e sem perda de sessão fora das previstas (S1 no
passo 1).

**Se falhar:** publicar de volta o candidato (passo 5), registrar o passo e o horário, e manter a promoção bloqueada até a causa ser
entendida.

### 8.2 Restauração de um backup do volume

O caminho principal de restauração é o backup do volume pelo Railway, e ele nunca foi ensaiado. A restauração no Railway é feita **sobre o
próprio volume**: o banco volta inteiro ao momento do backup, e tudo o que foi gravado depois se perde. No staging isso é aceitável,
porque os dados são sintéticos.

O Railway também oferece a restauração para um ponto no tempo, que cria um serviço de banco novo. Ela exige trocar a conexão da API e
fica fora deste ensaio.

**Antes:**

1. Registrar as contagens de todas as tabelas, a impressão digital das linhas de `users` e o número de migrações.
2. Backup B0 do volume e cópia lógica D0 verificada. São a volta do próprio ensaio.
3. Backup B1 do volume e, logo em seguida, a cópia lógica D1. As contagens de D1 são o resultado esperado da restauração.
4. Uma gravação de marcação **depois** de B1: por exemplo, um usuário sintético novo. Ela precisa sumir com a restauração.

**Passos:**

| # | Passo | O que medir ou conferir |
|---|---|---|
| 1 | Restaurar B1 pelo painel do Railway, no serviço Postgres do staging | O horário do início e o fim do reinício do Postgres |
| 2 | Esperar o Postgres aceitar conexões e a API responder `/health` | O tempo total fora do ar |
| 3 | Comparar com D1, tabela por tabela | Contagens idênticas |
| 4 | Procurar a marcação do passo 4 de "Antes" | Ausente: prova que o banco voltou ao momento de B1 |
| 5 | Conferir o histórico de migrações | Igual ao de B1. Se B1 for anterior a uma migração, a API a aplica de novo ao reiniciar, e isso também é conferido |
| 6 | Login de empresa, renovação, logout e login da plataforma | Funcionam |
| 7 | Publicar a API de novo, se ela não tiver se reconectado sozinha | Registrar se foi preciso |

**Critério de aprovação:**

- banco idêntico a D1;
- marcação ausente;
- API funcionando;
- tempo fora do ar medido.

**Se falhar:** restaurar B0 e, se B0 também falhar, recriar o banco a partir de D0. O resultado e o tempo medido entram neste
documento, e a seção "Volta atrás" passa a citar o tempo real de restauração.

**Na produção:** além do backup manual antes de cada etapa, o volume tem backups automáticos diários, retidos por 6 dias, e semanais,
retidos por 27 dias. A restauração na produção continua sendo a última opção, porque perde tudo o que foi gravado depois do backup.
