# ERP: staging e promoção para produção

Como um commit chega à produção, o que bloqueia uma promoção prematura e o que existe no staging.

## O caminho de um commit

```
PR ──(ci-gate)──> master ──(CI completo)──> staging ──(validação + master-gate)──> production
```

| Etapa | O que acontece | Quem garante |
|---|---|---|
| PR para `master` | Roda `ci.yml`: o que o diff tocou, mais o SonarCloud. O merge exige `ci-gate` verde | Proteção da branch `master` |
| Push em `master` | Roda tudo, sem pular nada. Railway publica no **staging** depois do CI | `ci.yml` e "Wait for CI" do Railway |
| Promoção | `production` avança para um SHA com `master-gate` verde. Railway publica em **produção** | Proteção da branch `production` |

## Os dois checks

- **`ci-gate`:** veredito de uma execução, em PR e em `master`. Sempre reporta, então nunca fica pendente por causa de um
  job que o diff pulou. Um job pulado só vale se o diff não tocou aquela área; falha ou cancelamento reprova.
- **`master-gate`:** só existe em commit que chegou à `master` e passou em tudo (backend, frontend e SonarCloud), sem nada
  pulado. Execuções de PR não o produzem, então a ponta de uma branch não mesclada não chega à produção.

## O que resolve os problemas antigos

| Problema visto | Causa | Agora |
|---|---|---|
| Deploy em produção antes do fim do CI (788a0a6, 04/11) | "Wait for CI" do Railway reagia ao push na `master` | Produção não sai mais da `master`. A branch `production` só aceita um SHA que já tem `master-gate` verde |
| Push seguinte cancelava a verificação do anterior (a8303f8, 04/11) | `concurrency` por branch com `cancel-in-progress` | Em `master` o grupo é o próprio SHA e nada é cancelado. Só PR substitui a própria execução antiga |
| Commit só de docs ganhava verde sobre um commit quebrado | Filtros de caminho nos workflows | Em `master` tudo roda sempre (o repositório é público, sem custo de minutos) |
| Bundle do staging chamaria a API de produção | `Dockerfile` sem `ARG VITE_API_BASE_URL`: a variável do Railway não chegava ao build | `ARG` declarado. Sem a variável, vale o `.env.production` |

## Como promover

1. Espere o merge publicar no staging e valide o que mudou.
2. Pegue o SHA completo do commit validado e rode:

```bash
scripts/promote-production.sh <sha-completo>
```

O script recusa um SHA fora da `master`, atrás de `production` ou sem `master-gate` verde, e mostra os commits que vão
entrar. Ele não é a trava: quem recusa de fato é o GitHub. Um `git push origin <sha>:production` sem o check também é
recusado, inclusive para administradores.

Depois da promoção, confira no Railway que `backend` e `main` estão em `SUCCESS` com esse SHA, e rode o production-watch
da Platform.

## Rollback

- **Mais rápido:** no painel do Railway, em Deployments do serviço, use "Redeploy" na versão anterior. Não muda nenhuma
  branch. Depois disso, a próxima promoção volta a publicar o que estiver em `production`.
- **Por código:** reverta com uma PR normal (`git revert`) e promova o novo SHA. `production` só anda para a frente: a
  proteção recusa force push.
- **Migrações:** o backend aplica as migrações ao subir, e o redeploy da versão anterior não as desfaz. Mudança de esquema
  em produção precisa ser compatível com a versão anterior.

## Staging

Ambiente `staging` no projeto `nexoerp` do Railway, descrito em [`.railway/railway.ts`](../../.railway/railway.ts).

| Item | Staging | Produção |
|---|---|---|
| Origem | `master` | `production` |
| Postgres e Redis | Próprios, vazios na criação | Próprios (fora do IaC) |
| `ASPNETCORE_ENVIRONMENT` | `Staging` | `Production` |
| Dados | Sintéticos, do `DataSeeder` (rodam fora de produção) | Reais |
| Swagger | Ligado | Desligado |
| Stripe | Desligado até haver chave `sk_test_` | Chave live |
| Storage (R2) e e-mail (Resend) | Desligados | Ligados |
| Orken ID | `account.staging.orken.com.br` | `account.orken.com.br` |

**Não copie nada de produção para o staging:** nem dados, nem variáveis. O ambiente não foi criado por duplicação
justamente para não herdar a chave live do Stripe, a chave do Resend ou o R2.

### Segredos

Gerados só para o staging, guardados só no Railway: `Jwt__Secret`, `Secrets__MasterKey`, `Seed__AdminPassword`,
`Seed__PlatformPassword` e as senhas do Postgres e do Redis. Os usuários sintéticos do `DataSeeder` têm senhas fixas no
código; valem só para dados de teste, e por isso nada real pode entrar neste ambiente.

### Segredos externos que dependem do dono

| O quê | Para quê | Sem isso |
|---|---|---|
| Registros DNS (CNAME e TXT) no GoDaddy | Domínios próprios do staging | Veja "Domínios" |
| `sk_test_...` e preços de teste do Stripe | Checkout e webhooks de verdade em modo de teste | Só os testes automatizados com eventos assinados, ver `stripe-homologacao.md` |
| Bucket de teste | Upload de arquivos | Upload fica desligado |

### Domínios

Staging roda nos domínios gerados pelo Railway. `*.up.railway.app` é um sufixo público, então o `main` e o `backend` são
sites diferentes para o navegador e o cookie `nexo_refresh` (`SameSite=Lax`) não acompanha as chamadas: o login funciona
com o token em memória, mas recarregar a página encerra a sessão. Isso é limite do domínio, não defeito do produto.

Para o staging se comportar como produção, os dois hosts precisam ser do mesmo site:
`erp.staging.orken.com.br` e `api.erp.staging.orken.com.br`. Depois de criar os registros DNS:

1. inclua os domínios em `.railway/railway.ts` (fora do ramo de produção) e rode o `apply` no staging;
2. troque `App__FrontendUrl`, `Cors__AllowedOrigins__0` e `VITE_API_BASE_URL` para os novos hosts;
3. publique de novo o `main`, porque a API fica fixada no bundle durante o build.

### Custo

Estimativa do próprio Railway para o projeto em produção neste ciclo: cerca de 330 vCPU·min, 21.869 GB·min de memória e
16.966 GB·min de disco. Pela tabela pública (US$ 20 por vCPU/mês, US$ 10 por GB/mês de memória, US$ 0,15 por GB/mês de
disco) isso dá **cerca de US$ 5 por mês**. O staging tem a mesma forma, com o mesmo sono por inatividade, e deve custar o
mesmo ou menos. Não é um recurso pago significativo, e o plano Pro inclui US$ 20 de uso. Confira o valor real em
Workspace → Usage depois do primeiro ciclo.

## Infraestrutura como código

Veja [`.railway/README.md`](../../.railway/README.md). O arquivo é um `partial`: Postgres, Redis e volumes de produção não
aparecem no ramo de produção, e o `apply` nunca os altera. Variáveis não são gerenciadas pelo arquivo.
