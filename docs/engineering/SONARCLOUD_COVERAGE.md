# SonarCloud: cobertura de testes

## Por que aparecia `0.0% Coverage on New Code`

Duas causas, ambas fora do código de produto:

1. **Nenhum teste gerava relatório de cobertura.** Os projetos de teste .NET não tinham coletor configurado, e o frontend não tinha provider de cobertura do Vitest instalado.
2. **O SonarCloud roda como _Automatic Analysis_** (o app do GitHub analisa o código sozinho; não há scanner no CI nem `sonar-project.properties`). Esse modo **não importa relatórios de cobertura** — por desenho do SonarCloud. A API confirma: o PR #78 tinha `new_lines_to_cover = 0` (nenhum dado de cobertura recebido), não "0% de linhas cobertas".

## O que é gerado agora

| Parte | Ferramenta | Relatório | Onde |
|---|---|---|---|
| Backend | Microsoft Code Coverage (vem com `Microsoft.NET.Test.Sdk`; nenhum pacote novo) | XML do Visual Studio — o formato que o SonarCloud importa para C# (`sonar.cs.vscoveragexml.reportsPaths`) | `nexo-backend/TestResults/{unit,integration}/**/*.xml`; artefato `backend-coverage` e tabela no resumo do job `backend-tests` |
| Frontend | `@vitest/coverage-v8` (mesma versão do Vitest) | `lcov` (`sonar.javascript.lcov.reportsPaths`) + resumo em texto | `nexo-main/coverage/lcov.info`; artefato `frontend-coverage` no `frontend-ci` |

- Backend: `nexo-backend/coverage.runsettings` mede só os assemblies de produto (`Nexo.Api`, `Application`, `Domain`, `Infrastructure`, `Shared`), nunca os de teste; migrations do EF ficam fora (são scripts gerados).
- Frontend: `npm run test:coverage`; ficam fora os próprios testes, `src/test` e o kit `components/ui` (shadcn).
- `nexo-backend/tools/coverage_summary.py` junta unit + integração por linha e imprime a cobertura por assembly e a do Tax Intelligence.

Nenhum teste foi criado para subir percentual, nenhum limite foi reduzido e nenhum módulo foi excluído da medição.

## O que falta — ação do dono (bloqueio)

Mandar a cobertura ao SonarCloud exige **análise pelo CI**, e isso depende de duas configurações que só o dono pode fazer (não são código):

1. **SonarCloud** → projeto `nexo-erp` → *Administration* → *Analysis Method* → **desligar Automatic Analysis**.
2. **GitHub** → repositório → *Settings* → *Secrets and variables* → *Actions* → **criar `SONAR_TOKEN`** (token de um usuário da organização `eliasandraade` no SonarCloud, *My Account → Security*).

Nessa ordem. Com o token mas a análise automática ligada, o SonarCloud recusa a análise do CI.

O workflow `.github/workflows/sonarcloud.yml` já está pronto: roda os testes com cobertura e envia tudo com o SonarScanner for .NET 11.3.0. **Sem o `SONAR_TOKEN`, todos os passos são pulados e o job passa** — nada muda até o dono fazer os dois passos acima.

### Configurações que hoje estão na interface do SonarCloud

A exclusão das migrations do cálculo de duplicação foi feita na interface (a análise automática ignora arquivos do repositório). O workflow repete essa exclusão em `sonar.cpd.exclusions`, para o resultado não mudar na troca.

### Como conferir depois da troca

- O job `sonarcloud` deixa de mostrar o aviso "SONAR_TOKEN not set" e termina com "ANALYSIS SUCCESSFUL".
- No PR, o comentário do SonarCloud passa a mostrar cobertura diferente de 0,0% e `new_lines_to_cover > 0`.
- Na primeira análise, conferir se os arquivos TypeScript do `nexo-main` aparecem no projeto (o scanner do .NET analisa arquivos fora dos `.csproj` a partir do `sonar.projectBaseDir`). Se não aparecerem, ajustar `sonar.sources`/`sonar.inclusions` no passo *Begin analysis*.

## Cobertura medida (26/10/2026, branch `feat/tax-intelligence-v1`)

Ver o relatório final do PR #78 e o resumo do job `backend-tests`.
