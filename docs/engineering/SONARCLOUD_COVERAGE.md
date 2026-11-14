# SonarCloud: cobertura de testes

## Por que aparecia `0.0% Coverage on New Code`

Duas causas, ambas fora do código de produto:

1. **Nenhum teste gerava relatório de cobertura.** Os projetos de teste .NET não tinham coletor configurado, e o frontend não tinha provider de cobertura do Vitest instalado.
2. **O SonarCloud rodava como _Automatic Analysis_** (o app do GitHub analisava o código sozinho, sem scanner no CI). Esse modo **não importa relatórios de cobertura**, por desenho do SonarCloud. A API confirmava: o PR #78 tinha `new_lines_to_cover = 0` (nenhum dado de cobertura recebido), não "0% de linhas cobertas".

## O que é gerado agora

| Parte | Ferramenta | Relatório | Onde |
|---|---|---|---|
| Backend | Microsoft Code Coverage (vem com `Microsoft.NET.Test.Sdk`; nenhum pacote novo) | XML do Visual Studio — o formato que o SonarCloud importa para C# (`sonar.cs.vscoveragexml.reportsPaths`) | `nexo-backend/TestResults/{unit,integration}/**/*.xml`; artefato `backend-coverage` e tabela no resumo do job `backend-tests` |
| Frontend | `@vitest/coverage-v8` (mesma versão do Vitest) | `lcov` (`sonar.javascript.lcov.reportPaths`) + resumo em texto | `nexo-main/coverage/lcov.info`; artefato `frontend-coverage` no `frontend-ci` |

- Backend: `nexo-backend/coverage.runsettings` mede só os assemblies de produto (`Nexo.Api`, `Application`, `Domain`, `Infrastructure`, `Shared`), nunca os de teste; migrations do EF ficam fora (são scripts gerados).
- Frontend: `npm run test:coverage`; ficam fora os próprios testes, `src/test` e o kit `components/ui` (shadcn).
- `nexo-backend/tools/coverage_summary.py` junta unit + integração por linha e imprime a cobertura por assembly e a do Tax Intelligence.

Nenhum teste foi criado para subir percentual, nenhum limite foi reduzido e nenhum módulo foi excluído da medição.

## Como a análise roda hoje

A análise é **pelo CI**: `.github/workflows/sonarcloud.yml` roda os testes com cobertura e envia tudo com o SonarScanner for .NET 11.3.0. O workflow é chamado por `ci.yml` em toda PR e por `master.yml` em todo commit que chega ao `master`. A etapa que dependia do dono (desligar a _Automatic Analysis_ e criar o `SONAR_TOKEN`) está concluída.

- **Na PR**, o job espera o Quality Gate do código novo e falha se ele estiver vermelho (`sonar.qualitygate.wait`). Como `ci-gate` depende desse job, um gate vermelho bloqueia o merge.
- **No `master`**, o job também espera o Quality Gate e falha se ele estiver vermelho. Como o `master-gate` depende desse job e a
  branch `production` só aceita commit com `master-gate` verde, nenhum commit com o gate vermelho chega à produção.
- **Sem `SONAR_TOKEN`**, numa PR (de fork, por exemplo) os passos são pulados e o job passa, sem análise. Num push para a `master`
  o job falha: sem token não há veredito, e o `master-gate` não pode passar sem ele.

### Quando o gate da `master` fica vermelho

O gate da `master` julga o código novo do projeto inteiro, no período configurado no SonarCloud, e não só o último commit. Um
achado aberto nesse período bloqueia todas as promoções seguintes, inclusive a de um commit que não tocou nele.

1. Ver qual condição falhou: `https://sonarcloud.io/api/qualitygates/project_status?projectKey=eliasandraade_nexo-erp&branch=master`.
2. Corrigir num PR comum (o PR também espera o gate) ou, se o achado for um risco conhecido, revisar e aceitar no painel com a
   justificativa registrada numa issue. Nenhuma condição do gate é reduzida para destravar uma promoção.
3. Depois do merge da correção, o `master-gate` do novo commit da `master` reflete o veredito.
4. Uma emergência em produção que não pode esperar usa o "Redeploy" da versão anterior no Railway (seção Rollback de
   `docs/engineering/staging-e-promocao.md`), que não depende do gate. Um revert por código passa pelo gate como qualquer commit:
   com o gate da `master` vermelho, ele também fica sem `master-gate` verde até o gate voltar a aprovar.

O que ainda depende do painel está em `docs/audits/2026-11-04-sonarcloud-revalidacao.md`: período de "código novo", falsos positivos e hotspots.

### Configurações que ficam na interface do SonarCloud

A exclusão das migrations do cálculo de duplicação foi feita na interface, quando a análise era automática, e o workflow a repete em `sonar.cpd.exclusions`.

### Como conferir

- O job `sonarcloud` termina com "ANALYSIS SUCCESSFUL".
- Na PR, a API mostra `new_lines_to_cover > 0` quando há código novo coberto por medição.
- Os arquivos TypeScript de `nexo-main` aparecem no projeto: os achados `typescript:*` confirmam.

### Atenção ao nome das propriedades

C# usa `sonar.cs.vscoveragexml.reportsPaths` (plural); TypeScript usa `sonar.javascript.lcov.reportPaths` (singular). Com o nome errado o sensor de cobertura TS nem é ativado e o frontend aparece com 0%. O `lcov` do Vitest traz caminhos relativos a `nexo-main`; o workflow os prefixa porque a análise roda na raiz do repositório.

## Cobertura medida (26/10/2026, branch `feat/tax-intelligence-v1`)

Ver o relatório final do PR #78 e o resumo do job `backend-tests`.
