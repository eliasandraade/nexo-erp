# Infraestrutura no Railway (IaC)

`railway.ts` descreve o projeto `nexoerp` do Railway. Processo completo em
[`docs/engineering/staging-e-promocao.md`](../docs/engineering/staging-e-promocao.md).

| Ambiente | Recursos no arquivo | Origem |
|---|---|---|
| `staging` | `backend`, `main`, `Postgres`, `Redis` e os volumes | branch `master` |
| `production` | `backend`, `main` | branch `production` |

Regras do arquivo:
- **É um `partial`** (`nexoerp`). O Postgres e o Redis de produção não aparecem no ramo de produção, e o `apply` nunca os
  altera nem os apaga.
- **Variáveis fora** (`variables: { managed: false }`): segredos ficam no Railway, nunca aqui.
- **Deploy só depois do CI:** `checkSuites: true` faz o Railway esperar o GitHub Actions no commit.
- **Ambientes:** só staging e produção. Qualquer outro é recusado.

## Uso

```bash
cd .railway && npm ci && cd ..
railway config plan --environment staging
railway config apply --environment staging
```

Sempre revise o `plan` antes do `apply`. Em sessão não interativa, o `apply` pede `--yes`.

No Windows com a CLI instalada pelo npm, a SDK não acha o `railway.cmd`. Aponte `$_` para o binário nativo. No Git Bash:

```bash
export MSYS_NO_PATHCONV=1
export _="$APPDATA/npm/node_modules/@railway/cli/bin/railway.exe"
"$_" config plan --environment staging
```
