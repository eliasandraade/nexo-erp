# Segunda promoção: sessão em cookie

Dossiê da promoção que leva a sessão em cookie para a produção. **Nada aqui foi executado na produção.** A promoção espera a homologação nos
navegadores, a homologação humana e a autorização explícita do proprietário.

## 1. Commit candidato

**`c6c2cfcb9c1a799a4a3025f201042850c703d1f5`**: o merge do PR #181, hoje a ponta de `master` com código.

`633a301` deixou de ser candidato: ele não tem a correção do cache do HTML, e sem ela a volta do bundle não chega a quem está com o app aberto
([registro](homologacao-sessao-staging-2026-11-10.md), seção 3.1). Se um PR só de documentação entrar depois, o candidato pode ser a nova ponta.
O código continua o mesmo, mas o `master-gate` precisa estar verde naquele SHA exato.

O que o candidato leva além da produção atual (`8a164ce`):

| PR | O que muda | Migração de banco |
|---|---|---|
| #176 | Política de renovação: teto por sessão, 409 com carência de 30 s, 429 e falhas de rede sem encerrar a sessão | Não |
| #178 | Sessão em cookie em modo duplo: a API aceita os dois modos, e o bundle escolhe na compilação por `VITE_SESSION_MODE` | Não |
| #181 | `Cache-Control: no-cache` no HTML de todas as rotas do app | Não |

**Variáveis de produção:** nenhuma nova, além de `VITE_SESSION_MODE=cookie` no `main`, na etapa 2. Os padrões do código já servem à produção:
- a lista da sessão é `erp.orken.com.br`;
- o CORS aceita `erp` e `www`;
- `SameSite` é `Lax`.

## 2. CI e Quality Gate

| Item | Resultado |
|---|---|
| `master-gate` em `c6c2cfc` | Verde (2026-11-11 04:02Z) |
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
| Chromium | Passou: as duas rodadas automatizadas, mais as suítes `auth-e2e` e `csp-e2e` |
| Firefox | **Pendente.** Depende do DNS do staging e da troca para `SameSite=Lax` |
| Safari | **Pendente.** Depende do DNS do staging e da troca para `SameSite=Lax` |

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

### Recomendação: duas etapas

1. **Etapa 1: promover `c6c2cfc` sem `VITE_SESSION_MODE`.**
   - O bundle continua no modo de hoje, e ninguém migra.
   - Entram a política de renovação, a API em modo duplo e o `no-cache` do HTML.
   - No staging, uma sessão no modo de hoje funcionou e renovou nessa combinação (bundle sem o modo, API nova), e a suíte `auth-e2e` no modo
     storage está verde.
2. **Etapa 2: ligar o modo cookie.** Depois de um período sem incidente na etapa 1:
   - definir `VITE_SESSION_MODE=cookie` no `main` de produção;
   - publicar de novo o mesmo commit;
   - cada pessoa migra na próxima recarga, sem novo login.

Separadas, a volta de cada etapa é menor. Ligar tudo de uma vez também funciona: é o que o staging validou, com os mesmos passos.

### Backup, antes de cada etapa

1. Backup manual do volume do Postgres de produção no Railway, com o id anotado.
2. Cópia lógica (`pg_dump`) lida de volta por inteiro, com as contagens das tabelas principais comparadas com o banco ao vivo. Depois, a cópia
   é apagada do contêiner.
3. Contagens de referência: empresas, usuários, sessões ativas, assinaturas e auditoria. Elas são comparadas depois da publicação.

### Publicação

1. Conferir:
   - `production` em `8a164ce`;
   - `master-gate` verde no SHA exato do candidato;
   - staging em `SUCCESS` nesse SHA;
   - `Billing__NewCheckoutsEnabled` ausente;
   - Tax Intelligence sem variáveis.
2. Etapa 1: `scripts/promote-production.sh c6c2cfcb9c1a799a4a3025f201042850c703d1f5`.
3. Etapa 2, quando autorizada: a variável no `main` de produção e o novo deploy do `main` no mesmo commit.
4. Depois de cada etapa, verificar:
   - backend e frontend em `SUCCESS` no SHA certo;
   - `/health`;
   - `Cache-Control: no-cache` em todas as rotas de `erp.orken.com.br` e `www.orken.com.br`, e assets imutáveis;
   - login real feito por uma pessoa, recarga, logout e upload;
   - portais públicos;
   - relatórios de CSP e `error_events`;
   - métricas do Cockpit;
   - contagens de referência.

### Volta atrás

- **Etapa 2:** retirar `VITE_SESSION_MODE` e publicar de novo o `main` no mesmo commit.
  - Com o `no-cache`, a próxima navegação de cada pessoa já recebe o bundle sem o modo.
  - Quem migrou entra de novo.
  - O cookie fica no navegador. Se o modo voltar, ele vale de novo, como o staging mostrou.
- **Etapa 1, ou as duas:** no painel do Railway, "Redeploy" das implantações de `8a164ce`, **primeiro o `main`, depois o `backend`**. Não há
  migração de banco entre `8a164ce` e `c6c2cfc`. A API de `8a164ce` rodou sobre o banco do candidato no staging, com login e renovação
  funcionando.
  - `production` só anda para a frente. A próxima promoção publica de novo o que estiver nela, então uma volta definitiva é um `git revert`
    promovido.
