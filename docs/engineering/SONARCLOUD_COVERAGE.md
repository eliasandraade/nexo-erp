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
- **No `master`**, a análise é enviada sem esperar o gate, que cobre todo o histórico.
- **Sem `SONAR_TOKEN`** (PR de fork, por exemplo), todos os passos são pulados e o job passa, sem análise.

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
