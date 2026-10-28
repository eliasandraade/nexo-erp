# Triagem de dependências vulneráveis antes do piloto controlado (28/10/2026)

Fontes da varredura:

- `dotnet list package --vulnerable --include-transitive` em `nexo-backend`;
- `npm audit --omit=dev` em `nexo-main`.

A auditoria e o OSV não foram silenciados. Nenhum upgrade cego foi feito.

| Pacote (versão) | Advisory | Afetadas → corrigida | Direta / transitiva e caminho | Alcançável pelo ORKEN? | Condição de exploração presente? | Classificação |
|---|---|---|---|---|---|---|
| `Microsoft.Extensions.Caching.Memory` 8.0.0 | GHSA-qj66-m88j-hmgj / CVE-2024-43483 (high, DoS por hash flooding) | ≤ 8.0.0 → 8.0.1 | Transitiva, só no grafo do projeto de biblioteca `Nexo.Application` (EF Core 8.0.8 → Caching.Memory 8.0.0) | **Não no binário implantado.** O host `Nexo.Api` resolve 8.0.1 (EF Core 8.0.11); `dotnet list` não acusa o `Nexo.Api`, e a DLL publicada tem file version 8.0.1024. O único `IMemoryCache` próprio (`ModuleAccessService`) usa chaves do servidor | Não. Exigiria chaves de cache controladas pelo atacante no processo implantado | **Accepted temporarily**: alinhar a versão do EF Core em `Nexo.Application` no PR de dependências |
| `SSH.NET` 2023.0.0 | GHSA-q939-rpr3-3284 / CVE-2026-48798; GHSA-mggc-4xg6-vcxf / CVE-2026-85756 (high, `ScpClient`) | < 2026.0.0 → 2026.0.0 | Transitiva, só em `Nexo.IntegrationTests` (Testcontainers 4.0.0 → SSH.NET) | **Não.** Pacote de teste, nunca publicado; o ORKEN não usa `ScpClient` | Não. Exigiria um servidor SCP malicioso consultado pela suíte de testes | **Not applicable** ao produto. Subir o Testcontainers no PR de dependências |
| `zod` 3.25.76 | GHSA-mrhx-c7xc-m3w5 (high, memória em coleções restritas muito grandes) | < 4.6.6 → 4.6.6 (major) | Direta em `package.json` | **Não.** Nenhum arquivo de `nexo-main/src` importa `zod` e ele não entra no bundle | Não | **Not applicable**: dependência declarada e não usada. Remover ou atualizar no PR de dependências |
| `react-router` / `react-router-dom` 6.30.6 | GHSA-wrjc-x8rr-h8h6 (moderate, open redirect por barra invertida em `<Link>`/`useNavigate`); GHSA-337j-9hxr-rhxg (moderate, desserialização na hidratação SSR) | ≥ 6.0.0 < 7.18.0 → 7.18.x (major) | Direta (`react-router-dom` → `react-router`) | Open redirect: **um ponto** recebia parte da URL e a repassava a `<Navigate>`, o redirecionamento legado `/menu/:slug`. Os demais `navigate()` usam caminhos internos ou montados com ids. SSR/hidratação: **não usado** (SPA sem SSR nem data router) | Para o redirect legado, um link forjado como `/menu/%5Cexemplo.com` | **Corrigido pontualmente** (commit separado): o slug vira um único segmento de caminho (`legacyMenuTarget`), com teste. A atualização para o v7 fica no PR de dependências. Parte SSR: **Not applicable** |
| `postcss-selector-parser` 6.0.10 / 6.1.4 | GHSA-rj75-hqrm-r3gf (moderate, complexidade quadrática) | < 6.1.5 → 6.1.5 | Transitiva (`tailwindcss`, `postcss-nested`, `@tailwindcss/typography`) | **Não em tempo de execução.** Só é usado no build do CSS, sobre o código-fonte do próprio repositório | Não. Exigiria seletores CSS hostis no código-fonte | **Not applicable** ao runtime |

## Conclusão

Nenhum advisory **HIGH** é alcançável em fluxo exposto do piloto. O único ponto alcançável, o open redirect moderado no redirecionamento legado de cardápio, foi corrigido neste ciclo.

As atualizações de versão continuam no PR de dependências separado:

- EF Core em `Nexo.Application`;
- Testcontainers;
- remoção ou atualização do `zod`;
- React Router v7.
