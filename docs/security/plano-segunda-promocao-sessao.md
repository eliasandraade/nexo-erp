# Segunda promoção: sessão em cookie

Dossiê da promoção que leva a sessão em cookie para a produção. **Nada aqui foi executado na produção, e nenhuma promoção está autorizada.** A promoção
espera as correções em revisão, a homologação nos navegadores, a homologação humana e a autorização explícita do proprietário.

## 1. Commit candidato

**Candidato: `834737db3f9c5a5db32b7a495f353d4400e5b2a4`** (`master`, merge do PR #186), com `master-gate` verde em 2026-11-13 03:47Z e
staging em `SUCCESS` nesse SHA. Ele contém todas as correções P0 de código desta remediação. A promoção continua **não autorizada**:
ser candidato só quer dizer que é este SHA que os ensaios e a homologação precisam validar.

A regra do login foi decidida pelo proprietário em 2026-11-12: o login é único em todo o Orken ERP.

`0b9a398`, `c6c2cfc` e `633a301` não são candidatos. Nenhum commit é promovido automaticamente.

**Candidato final:** o commit da `master` que fechar as correções do SonarCloud (PRs #147, #165, #166 e #167, que só acrescentam `void` e
testes, e os PRs que só acrescentam testes). Nenhum deles traz migração nem muda comportamento. O SHA final entra aqui quando existir, com:

- `master-gate` verde nesse SHA;
- Quality Gate da `master` no SonarCloud em OK nesse SHA;
- staging em `SUCCESS` nesse SHA.

Se a decisão for promover sem essas correções, o candidato continua `834737d`.

**Candidato final, em 2026-11-14: `10397e6`** (merge do #167). Cumpre as três condições acima: `master-gate` verde, Quality Gate OK
e staging em `SUCCESS` (backend e main) nesse SHA. A promoção continua dependendo das condições da seção 5 e da autorização do proprietário.

| Commit da `master` | Origem | `master-gate` | Quality Gate |
|---|---|---|---|
| `d529674` | #188 | Verde | ERROR: confiabilidade C, cobertura 79,8% |
| `09c6233` | #189 | Verde | ERROR: só confiabilidade C, cobertura 81,5% |
| `8112852` | #165 | **Vermelho**, pela falha conhecida do `auth-e2e` (seção 2) | ERROR: só confiabilidade C, cobertura 81,9% |
| `4983330` | #147, com a correção do `auth-e2e` | Verde | ERROR: só confiabilidade C, cobertura 82,2% |
| `83f3616` | #166 | Verde | ERROR: só confiabilidade C, cobertura 82,6% |
| `10397e6` | #167 | Verde | **OK**: confiabilidade A, cobertura 83,9% |

Cada PR foi atualizado contra a `master` do momento e mesclado só com todos os checks verdes no SHA novo (#147 em `75ec780`, #166 em
`0355af9`, #167 em `0cec4c1`). O #147 entrou antes do #166 e do #167, por decisão do proprietário, para levar a correção do `auth-e2e`.

De `834737d` para `10397e6` não entra migração nem mudança em `.github`, `global.json` ou no script de promoção. Fora testes e
documentação, só mudam arquivos do frontend, pelos `void`. O ensaio da API anterior sobre o esquema novo (seção 8.0) continua valendo.

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
| Quality Gate geral do SonarCloud em `master` em `834737d` | ERROR em 2 condições do código novo: confiabilidade C (o limite é A) e cobertura de 77,4% (o mínimo é 80%). Os limites não foram reduzidos |
| Por que o `master-gate` passava com o Quality Gate em ERROR | No push para a `master`, o job `sonarcloud` enviava a análise com `sonar.qualitygate.wait=false` e terminava com sucesso; o `master-gate` exige o job, não o veredito. O check separado "SonarCloud Code Analysis" carrega o veredito e não é exigido por nenhuma proteção. A correção está no PR #191 (rascunho), que só é mesclado depois do Quality Gate OK na `master` |
| Confiabilidade C | 138 bugs abertos: 137 `S9383` (promessas sem tratamento declarado; nenhuma rejeita, ver `docs/audits/2026-11-06-sonarcloud-confiabilidade-por-achado.md`) e 1 `S8733` (consulta com dois `Include`). Os `void` dos PRs #147, #165, #166 e #167 tiram os 137. O `S8733` foi decidido pelo proprietário em 2026-11-13: aceitar no painel como risco conhecido, com a issue #190 para o limite de pagamentos por venda. Em `10397e6` a confiabilidade do código novo é A (`new_bugs` = 0) com o `S8733` ainda aberto: a medida do código novo não o conta, mas ele segue como o único bug do projeto (confiabilidade geral C). O aceite no painel continua com o proprietário |
| Cobertura | Com os PRs de teste #188 (`d529674`) e #189 (`09c6233`), a cobertura do código novo na `master` foi de 77,4% para 79,8% e depois **81,5%**. Condição cumprida |
| PRs do SonarCloud, mesclados um por vez | #188 → `d529674` e #189 → `09c6233`, `master-gate` verde nos dois. #165 → `8112852`: **`master-gate` vermelho** pela falha conhecida do `auth-e2e` (cookie) descrita na linha abaixo, cuja correção ainda não está na `master`. Cobertura em 81,9% nesse commit. Por decisão do proprietário, o #147 entrou em seguida: #147 → `4983330`, #166 → `83f3616`, #167 → `10397e6`, com `master-gate` verde nos três e Quality Gate OK no último |
| `auth-e2e` instável no bloco "The hub of the kitchen" | Causa medida: o host de teste copiava corpo num 204, e o Kestrel fechava a conexão que o cliente reaproveitava. Corrigido no PR #147 (`566a793`), sem mudar nenhuma verificação. Apareceu no PR #147 (13/11 12:53Z) e no `master-gate` de `8112852` (13/11 19:18Z). Com a correção na `master`, os dois `auth-e2e` passaram em todos os commits seguintes, nos PRs e na `master`, sem nova execução |

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
- **Quality Gate:** situação atual na seção 2.
- **Fora da sessão, já conhecidos:** a chave do Stripe é de produção e nunca recebeu webhook, e `api/billing/webhook` lê o corpo inteiro antes
  da assinatura. Nada disso muda com esta promoção.

## 7. Plano

### Condições para começar

Todas precisam estar cumpridas. Qualquer uma em falta bloqueia a promoção:

1. PRs do candidato mesclados, um por vez, cada um com o CI verde no SHA exato.
2. `master-gate` verde no SHA do candidato, e staging em `SUCCESS` nesse SHA.
3. Bateria de navegadores completa nos domínios próprios do staging (`erp.staging.orken.com.br`), sem falha em aberto.
4. [Checklist humana](checklist-homologacao-humana-sessao.md) preenchida por uma pessoa, em Chrome ou Edge, Firefox e Safari.
5. API anterior sobre o esquema novo: **cumprida** no ensaio local de 2026-11-13 (seção 8.0), com o candidato `834737d`. Se o candidato
   final trouxer migração nova, o ensaio é repetido.
6. Backup verificado (abaixo) e autorização explícita do proprietário, para cada etapa.
7. Contenção do GitHub aplicada: **cumprida** em 2026-11-13. O ruleset de `production` está ativo, com só a conta do proprietário na
   exceção, e a proteção clássica não mudou. Os apps instalados ficam, por decisão do proprietário. Os riscos que sobram estão registrados
   no documento privado da contenção.
8. Quality Gate da `master` no SonarCloud em OK no candidato final: **cumprida** em `10397e6` (confiabilidade A, cobertura 83,9%).
   - Em `834737d` ele estava em ERROR: confiabilidade do código novo C (exigido A) e cobertura do código novo 77,4% (exigido 80%).
   - O `master-gate` ainda não depende dele (seção 2). O PR #191 passa a exigir o veredito; com o gate em OK, o merge dele depende só da
     decisão do proprietário.
   - Os limites não serão reduzidos. Se o proprietário decidir promover com ele em ERROR, a decisão fica registrada aqui.

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

### Critérios de aprovação de cada etapa

Cada etapa tem uma decisão antes (pode publicar?) e uma depois (pode seguir?). As duas são do proprietário, por escrito.

**Etapa 1: candidato sem `VITE_SESSION_MODE`**

| Antes de publicar | Exigido |
|---|---|
| Candidato | SHA final fixado na seção 1, `master-gate` verde e staging em `SUCCESS` nele |
| Condições | 1, 2, 5, 6, 7 e 8 da lista acima. A 3 e a 4 (navegadores e checklist humana) valem para a etapa 2, porque a etapa 1 não liga o cookie |
| Regressão do modo de hoje no staging, no SHA final | Login, renovação e logout de empresa; login da plataforma com a segunda etapa; criação de usuário com login repetido recusada com 409 `login_taken` |
| Ensaios | 8.0 cumprido; 8.2 (restauração do volume em ambiente descartável) cumprido |
| Backup | Backup manual do volume de produção com id anotado e cópia lógica verificada, no mesmo dia |
| Produção antes | `production` em `8a164ce`; `Billing__NewCheckoutsEnabled` ausente; Tax Intelligence sem variáveis; nenhuma variável de sessão |

| Depois de publicar (24 h) | Para seguir |
|---|---|
| Implantações | `backend` e `main` em `SUCCESS` no SHA final; 79 migrações no log de início, sem erro |
| Uso real | Login, recarga, logout e upload feitos por uma pessoa; login da plataforma com o código; portais públicos abrem |
| Sinais | Nenhum item da tabela de bloqueio abaixo disparou |
| Dados | Contagens de referência iguais, salvo o uso normal explicado |

**Etapa 2: ligar o modo cookie**

| Antes de publicar | Exigido |
|---|---|
| Etapa 1 | No ar há pelo menos 7 dias, sem incidente aberto |
| Navegadores | Condição 3 cumprida no SHA da etapa 1 |
| Checklist humana | Condição 4: as 19 linhas com **Sim** em Chrome ou Edge, Firefox e Safari, ou **—** justificado, assinadas pelo proprietário |
| Backup | Novo backup manual do volume e cópia lógica verificada, no mesmo dia |
| Variável | Só `VITE_SESSION_MODE=cookie` no `main` de produção; nada no `backend` |

| Depois de publicar (24 h) | Para seguir |
|---|---|
| Migração | Pessoas logadas passam ao cookie na recarga, sem novo login (amostra conferida pela auditoria e pelo log de `auth/refresh`) |
| Renovação | Nenhuma série de 401 em `auth/refresh` para sessões ativas |
| Limites | Nenhuma loja inteira recebendo 429 em uso normal |
| CSP | Nenhuma violação nova em fluxo do produto |
| Volta atrás | Pronta e testada: retirar a variável e publicar o `main` de novo |

**Qualquer critério em falta:** a etapa não começa ou é revertida pela seção "Volta atrás", e a decisão volta ao proprietário.

### Backup e restauração, antes de cada etapa

1. Backup manual do volume do Postgres de produção no Railway, com o id anotado.
2. Cópia lógica (`pg_dump`) lida de volta por inteiro, com as contagens das tabelas principais comparadas com o banco ao vivo. Depois, a cópia
   é apagada do contêiner.
3. Contagens de referência: empresas, usuários, sessões ativas, assinaturas e auditoria. Elas são comparadas depois da publicação.
4. **Restauração:** o caminho principal é restaurar o backup do volume pelo Railway. Ele foi ensaiado em 2026-11-13 num ambiente
   descartável (seção 8.2): o banco voltou idêntico ao momento do backup, e a API subiu sobre ele. A restauração cria um volume novo e
   só vale depois de aplicada a troca pendente ("Deploy" no painel). Nunca ensaiar sobre o staging ou a produção em funcionamento.
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
- **Restauração do banco:** só se uma migração falhar ou houver perda de dados. É a última opção. O ensaio da seção 8.2 passou: restaurar
  o backup do dia, aplicar a troca de volume pendente e confirmar que a API sobe e aplica as migrações que faltarem.

## 8. Ensaios antes da etapa 1

Decisão do proprietário em 2026-11-13: os ensaios rodam só em ambiente descartável. Nenhuma restauração é feita sobre o staging ou a produção
em funcionamento. Nenhum ensaio toca a produção.

### 8.0 Resultado: API anterior sobre o esquema novo, em ambiente local descartável (2026-11-13)

**Ambiente:**

- Postgres 18 local, criado só para o ensaio, com a cópia lógica do staging de 2026-11-12 10:51Z (`sha256 446ae51b…`). Ela tem 77
  migrações, o mesmo esquema da produção.
- Redis descartável em contêiner.
- As APIs de `834737d` (candidato) e `8a164ce` (produção) compiladas em Release, uma de cada vez, na mesma base.
- Tudo foi apagado no fim. Nada saiu da máquina.

**Sequência:** candidato aplica as migrações → API `8a164ce` sobre o esquema novo → candidato de novo.

| Fase | Verificação | Resultado |
|---|---|---|
| Antes | Migrações; linhas de `users` | 77; 29 linhas, impressão digital registrada |
| Candidato | Sobe e aplica as duas migrações | 79 migrações, índice `IX_users_login` e coluna `mfa_attempts` presentes, sem erro no log |
| Candidato | Linhas de `users` depois da migração | Idênticas às de antes |
| Candidato | Login, `auth/me` e renovação de empresa, no modo de hoje e no modo cookie | 200 em todos. No modo cookie, o cookie é emitido e o corpo não traz `refreshToken` |
| Candidato | Login da plataforma; ativar a segunda etapa; sessão pendente | Entra; `mfa_attempts = 0` na sessão pendente |
| Candidato | Código logo depois da ativação | 401: o passo de 30 s já tinha sido gasto na ativação, e o servidor recusa reutilizar o passo (`MfaLastUsedStep`). É a proteção contra reutilização. Com o passo seguinte, entrou (fases 2 e 3) |
| `8a164ce` | Sobe sobre 79 migrações | Sobe em 30 s, sem tentar aplicar nem desfazer nada |
| `8a164ce` | Sessão aberta no candidato, no modo de hoje | Renova (200) |
| `8a164ce` | Login, `auth/me`, renovação e logout de empresa | 200, 200, 200 e 204 |
| `8a164ce` | Criar usuário com login novo | 201 |
| `8a164ce` | Criar usuário com o login de outra empresa | **409 `duplicate`**, nunca 500. O índice recusa, e o log registra o erro do banco (2 linhas de nível `error` do EF), que é a recusa esperada |
| `8a164ce` | Cadastro público com e-mail já cadastrado | 409 `email_already_registered` |
| `8a164ce` | Login da plataforma com a segunda etapa | Entra; a sessão pendente grava `mfa_attempts = 0` pelo padrão do banco |
| Candidato de novo | Sessão que atravessou a API anterior | Renova (200) |
| Candidato de novo | Login de outra empresa | 409 `login_taken` |
| Candidato de novo | Plataforma com a segunda etapa | Entra |
| Fim | Tabelas alteradas | Só as que os passos tocam: `users` +2, `user_sessions` +4, `platform_sessions` +4, `platform_users` +1 (operador do ensaio), `audit_records` +11 |

**Conclusão:** a volta da API para `8a164ce` sobre o esquema do candidato funciona, e a sessão no modo de hoje atravessa a ida e a volta sem
novo login. Na volta, um login repetido entre empresas recebe 409 `duplicate` em vez de `login_taken`. O log da API mostra duas linhas de
erro do banco para cada tentativa dessas: a seção "Observabilidade" não deve tratar esse caso como incidente durante uma volta atrás.

**O que este ensaio não cobre:** a publicação real no Railway, o bundle e as abas abertas durante as publicações. Isso foi exercitado na 2ª
rodada (seção 3), com `c6c2cfc` no lugar do candidato. A única diferença do candidato para essa rodada no caminho de volta é o esquema, e o
esquema é o que este ensaio provou.

### 8.1 Volta atrás completa com o candidato, no Railway

Opcional depois do 8.0. Se o proprietário quiser repeti-lo com a publicação real, ele roda no staging com o candidato publicado e com
autorização própria. Ele publica versões, mas não restaura banco. Prova que o caminho da seção "Volta atrás" funciona com o esquema novo: a
API de `8a164ce` roda sobre as migrações do candidato.

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

### 8.2 Restauração de um backup do volume: resultado (2026-11-13, ambiente descartável)

**Cumprido.** O ensaio rodou num ambiente descartável do Railway (`ensaio-restauracao`), autorizado pelo proprietário, e foi apagado no fim.

**Como a restauração do Railway funciona de fato.** Ela não grava sobre o volume em uso, ao contrário do que este plano supunha:
1. O pedido de restauração cria um **volume novo** a partir do backup e deixa uma troca pendente no ambiente: o serviço passa a montar o
   volume novo, e o antigo fica desmontado.
2. Nada muda até essa troca ser aplicada. No painel, ela aparece como mudança pendente e precisa de "Deploy"; pela API,
   `environmentPatchCommitStaged`.
3. Ao aplicar, o Postgres reinicia já sobre o volume restaurado. O volume antigo continua no projeto até ser apagado, o que dá uma volta
   da própria restauração.

**Isolamento:**
- O ambiente foi criado **sem origem**, sem copiar nenhuma variável do staging, e com publicações iniciais desligadas.
- Ele teve só três serviços próprios: Postgres, Redis e backend. Todos os segredos foram gerados no momento: senha do banco, senha do
  Redis, `Jwt__Secret` e senhas do seed. Sem `Secrets__MasterKey`, as integrações ficaram desligadas, sem domínio público e sem gatilho de
  branch.
- Antes de cada operação, o script conferiu que o ambiente e a instância de volume eram os do ensaio.
- Antes e depois de cada etapa, o script comparou a produção e o staging com uma fotografia inicial: implantações, volumes, serviços e
  ambientes.
  - A produção ficou idêntica o tempo todo.
  - No staging, os únicos movimentos foram as implantações normais de `backend` e `main` com commits da `master` (merges do #188 e do
    #189). Postgres, Redis e volumes ficaram idênticos.
- No fim, o ambiente, os três serviços e os dois volumes do ensaio foram apagados. O projeto voltou a 2 ambientes, 4 serviços e 2 volumes,
  como antes.

| # | Passo | Resultado |
|---|---|---|
| 1 | Carregar a cópia lógica do staging de 13/11 01:27Z no Postgres descartável | 161 tabelas, 1.145 linhas, 78 migrações. Assinatura D0: contagem e hash de todas as linhas de cada tabela |
| 2 | Backup B1 do volume (`dd5bda27`, 18:35:49Z) | Listado no Railway |
| 3 | Dano depois de B1: tabela de marcação criada e a migração mais recente apagada do histórico | D1 diferente de D0 (162 tabelas, 77 migrações) |
| 4 | Restaurar B1 e aplicar a troca de volume | Postgres de volta em cerca de 8 s depois de aplicada a troca (implantação de 18:48:35 a 18:48:43Z) |
| 5 | Comparar com D0 | **Idêntico**: 161 tabelas, mesmas contagens e mesmos hashes, marcação ausente, 78 migrações, nenhuma constraint ou índice inválido |
| 6 | Publicar a API da `master` (`09c6233`) sobre o banco restaurado | `SUCCESS` com o health check `/health` do Railway. A API aplicou a migração que faltava ao backup (`UsersLoginUniqueEverywhere`, 79 no total, com o índice `IX_users_login`) e só criou o usuário de plataforma do próprio seed do ensaio. Nenhum erro no log |

**Não coberto:** login de empresa, renovação e logout contra a API restaurada. Ela não teve domínio público, por isolamento, e o critério
do ensaio era integridade e início da API. Esses fluxos seguem cobertos pelo ensaio local da seção 8.0 e pela regressão no staging.

**Para a produção:**
- Restaurar exige aplicar a troca pendente. Até lá, nada muda.
- O tempo fora do ar medido foi o reinício do Postgres sobre o volume novo, alguns segundos com este tamanho de banco. A API reconecta
  sozinha ou é publicada de novo.
- Tudo o que foi gravado depois do backup se perde, como já estava previsto.
- O volume anterior fica desmontado no projeto. Ele só é apagado depois de a restauração ser aceita.

**Na produção:** além do backup manual antes de cada etapa, o volume tem backups automáticos diários, retidos por 6 dias, e semanais,
retidos por 27 dias. A restauração na produção continua sendo a última opção, porque perde tudo o que foi gravado depois do backup.
