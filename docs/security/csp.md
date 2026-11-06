# Content-Security-Policy do app

**Estado:** a política completa está em `Content-Security-Policy-Report-Only`. Ela não bloqueia nada para nenhum cliente. O único
cabeçalho aplicado continua sendo `Content-Security-Policy: frame-ancestors 'none'`. A API tem a sua própria política
(`default-src 'none'`, em `nexo-backend/src/Nexo.Api/Program.cs`), que não muda.

**Por que isto existe:** o token de sessão e o de renovação, de 7 dias, ficam no `localStorage` (decisão D1 em
`docs/audits/2026-11-05-sonarcloud-seguranca-evidencias.md`). Sem `script-src`, qualquer XSS lê os dois. A troca do lugar dos tokens é
outra frente. Esta reduz a chance de o script injetado rodar, e passar para o modo que bloqueia é o passo que dá o efeito.

`www.orken.com.br` e `erp.orken.com.br` são o mesmo serviço, o mesmo bundle e o mesmo `Caddyfile`, então têm a mesma política.

## A política

Em `nexo-main/Caddyfile`, com os valores de produção:

```text
default-src 'none';
script-src 'self' 'wasm-unsafe-eval';
style-src 'self' 'unsafe-inline';
img-src 'self' data: blob: https://*.r2.cloudflarestorage.com https://*.r2.dev https://images.openfoodfacts.org;
font-src 'none';
connect-src 'self' blob: https://api.erp.orken.com.br wss://api.erp.orken.com.br https://viacep.com.br https://*.r2.cloudflarestorage.com https://*.r2.dev;
worker-src 'self';
frame-src blob:;
object-src blob:;
media-src https://*.r2.cloudflarestorage.com https://*.r2.dev;
base-uri 'none';
form-action 'self';
frame-ancestors 'none';
manifest-src 'self';
report-uri https://api.erp.orken.com.br/api/csp-reports
```

Sem `unsafe-eval`, sem `unsafe-inline` em scripts e sem `*` ou `https:` sozinhos.

### Variáveis por ambiente

Lidas quando o Caddy sobe. O padrão de cada uma é o valor de produção, então **produção não precisa de nenhuma**.

| Variável | Padrão | Para quê |
|---|---|---|
| `CSP_API_ORIGIN` | `https://api.erp.orken.com.br` | Chamadas à API e destino dos relatórios |
| `CSP_HUB_ORIGIN` | `wss://api.erp.orken.com.br` | WebSocket do hub da cozinha |
| `CSP_STORAGE_ORIGINS` | `https://*.r2.cloudflarestorage.com https://*.r2.dev` | Object store: arquivos públicos e URLs assinadas. Um ou mais endereços separados por espaço |

**Staging** precisa destas duas, no serviço `main`:

```text
CSP_API_ORIGIN=https://backend-staging-d8d0.up.railway.app
CSP_HUB_ORIGIN=wss://backend-staging-d8d0.up.railway.app
```

Sem elas, as chamadas de staging à própria API viram relatórios, e **os relatórios de staging vão para a API de produção**. Nada
quebra, mas defina as duas antes do merge, porque `master` publica em staging. O R2 fica desligado em staging, então
`CSP_STORAGE_ORIGINS` não faz falta lá.

### Os dois curingas

A conta R2 de produção (`Integrations__Storage__R2__AccountId`) e a URL pública (`Integrations__Storage__R2__PublicUrl`) estão só no
Railway, e o `assets.orken.com.br` citado nos planos antigos não existe no DNS. Por isso o padrão é `*.r2.cloudflarestorage.com` e
`*.r2.dev`. Eles deixam passar o bucket de qualquer pessoa, inclusive em `connect-src`. É aceitável enquanto nada é bloqueado, e **sair
deles é condição para aplicar** (ver abaixo): basta definir `CSP_STORAGE_ORIGINS` com os dois endereços reais.

## Inventário: o que o app carrega de verdade

Levantado no código e no bundle de produção. Atenção ao repetir: o `.gitignore` da raiz ignora `build/`, e por isso o `ripgrep` não
entra em `nexo-main/src/modules/build`, onde estão o conversor IFC e o PDF. Use `grep -r` ou `rg --no-ignore`.

| Diretiva | Fonte | Por quê | Onde no código |
|---|---|---|---|
| `script-src` | `'self'` | Módulos do Vite e os `import()` dos chunks. O `index.html` construído não tem script inline. Não há analytics nem script de terceiros | `nexo-main/index.html`, `vite.config.ts` |
| `script-src` | `'wasm-unsafe-eval'` | Compilar o `web-ifc` dentro do worker da conversão IFC → GLB. Não libera `eval` | `modules/build/components/twin/viewer/ifc-convert.worker.ts` |
| `worker-src` | `'self'` | O worker da conversão vem de `/assets`. O modo de uma linha de execução é forçado, então o `new Worker(blob:)` do `web-ifc` não roda | `modules/build/components/twin/viewer/convert-ifc.ts` |
| `style-src` | `'self'` | A folha do Tailwind | `dist/assets/index-*.css` |
| `style-src` | `'unsafe-inline'` | Necessário hoje, ver abaixo | `nexo-main/index.html`, `sonner`, `react-remove-scroll`, `modules/restaurante/utils/print.ts` |
| `img-src` | `'self'` | Logo, favicon, ícones do manifesto | `nexo-main/public` |
| `img-src` | `data:` | QR code da verificação em duas etapas | `modules/profile/components/MfaSection.tsx`, `modules/platform/pages/PlatformSecurityPage.tsx` |
| `img-src` | `blob:` | Imagem da planta aberta do computador; textura embutida em GLB (Safari) | `modules/build/components/twin/SheetMapper.tsx`, `GLTFLoader` |
| `img-src` | storage | Logo, capa, produto (bucket público); foto de obra, panorama, captura da realidade (URL assinada) | `components/shared/ImageUploadButton.tsx`, `modules/build-portal/PortalView.tsx`, `modules/build/components/twin/RealityCaptureSection.tsx`, `viewer/Pano360Viewer.tsx` |
| `img-src` | `https://images.openfoodfacts.org` | Sugestão de imagem pelo código de barras, que o produto pode guardar | `modules/products/components/ProductMainDataSection.tsx`, `Nexo.Infrastructure/Integrations/OpenFoodFacts` |
| `font-src` | `'none'` | Só fontes do sistema. O bundle não tem fonte nem `url()` em CSS | `nexo-main/tailwind.config.ts` |
| `connect-src` | `'self'` | O worker busca o `.wasm` em `/assets` | `ifc-convert.worker.ts` |
| `connect-src` | API | Todas as chamadas, inclusive upload por XHR, negociação e long polling do hub | `services/api-client.ts`, `services/pdf.api.ts`, `modules/portal/api`, `modules/service-portal/api`, `modules/build-portal/api` |
| `connect-src` | hub | WebSocket da cozinha e do salão | `modules/restaurante/hooks/useKitchenSocket.ts` |
| `connect-src` | `https://viacep.com.br` | Consulta de CEP no carrinho do cardápio. É a única integração que o navegador chama direto | `modules/portal/components/CartSheet.tsx` |
| `connect-src` | storage | O `GLTFLoader` baixa o GLB e a conversão baixa o IFC, ambos por URL assinada | `TwinViewer.tsx`, `viewer/ModelViewer3D.tsx`, `IfcBrowserConvert.tsx` |
| `connect-src` | `blob:` | O `GLTFLoader` lê a textura embutida com `fetch(blob:)` | `three` (`ImageBitmapLoader`) |
| `media-src` | storage | Vídeo de obra e de captura da realidade, por URL assinada | `modules/build-portal/PortalView.tsx`, `RealityCaptureSection.tsx` |
| `object-src` | `blob:` | PDF da planta aberto do computador, em `<object>` | `modules/build/components/twin/SheetMapper.tsx` |
| `frame-src` | `blob:` | O Chromium abre esse mesmo PDF como um frame, e confere esta diretiva além de `object-src`. Não há `<iframe>` no código, nem mapa ou vídeo embutido | `SheetMapper.tsx` |
| `manifest-src` | `'self'` | `site.webmanifest` | `nexo-main/index.html` |
| `base-uri` | `'none'` | Nenhum `<base>` | |
| `form-action` | `'self'` | Todo formulário é tratado em JS, nenhum envia para outro host | |
| `frame-ancestors` | `'none'` | Igual ao cabeçalho aplicado | |

**Não entram na política porque são navegação ou download, que a CSP deixa passar:** Stripe (`window.location.href` para o checkout e o
portal, sem `js.stripe.com`), links do WhatsApp (`wa.me`), documentos do portal abertos em nova aba, e os PDFs, CSV, ICS e códigos de
recuperação entregues por `<a download>` com `blob:` (`services/pdf.api.ts`, `modules/service-portal/lib/ics.ts`,
`PlatformSecurityPage.tsx`). A janela de impressão da cozinha nasce sem endereço e herda a política de quem a abriu.

### `style-src 'unsafe-inline'`

É necessário e está provado na suíte: sem ele, a página de login já gera violação. As causas são o `<style>` do esqueleto de carregamento no
`index.html`, os `<style>` que `sonner` e `react-remove-scroll` escrevem em tempo de execução e o `<style>` e os atributos `style=` do
HTML da impressão. O `style={{...}}` do React não conta, porque passa pelo CSSOM.

Tirar daqui exigiria hash do esqueleto, `nonce` em tempo de execução, que o Caddy não gera, e substituir ou configurar as duas bibliotecas.
O risco que sobra é injeção de CSS, bem menor do que a de script: ela não executa código, mas pode vazar dados por seletores em
`url()`, que `img-src` já limita.

### `report-uri` e não `report-to`

A política usa só `report-uri`. Testamos no Chromium da suíte: com `report-uri` sozinho, o relatório chega à API. Acrescentando `report-to`,
com ou sem `Reporting-Endpoints`, ele para de chegar nos servidores locais, em HTTP, porque o navegador troca um caminho pelo outro. A Reporting
API também agrupa os relatórios e faz pré-voo de CORS, e nada disso foi exercitado aqui.

Com `report-uri`, o caminho testado é o mesmo que o Chromium usa em produção. O endpoint já aceita `application/reports+json`, com testes e
pré-voo de CORS. Ligar depois é acrescentar `report-to` e o cabeçalho `Reporting-Endpoints`, e conferir em staging, que tem HTTPS.

## O endpoint de relatórios

`POST /api/csp-reports` (`Nexo.Api/Controllers/CspReportsController.cs`):

- anônimo, porque o navegador não manda sessão em relatório;
- responde **sempre 204**, inclusive sem corpo, com corpo inválido, com tipo errado, acima do limite e acima do rate limit;
- aceita `application/csp-report` e `application/reports+json`, até 16 KB;
- rate limit por IP, pelo `ClientIpResolver`: 30 por minuto (`RateLimiting:CspReport:{Enabled,PermitLimit,WindowSeconds}`). Acima disso o
  relatório é descartado em silêncio;
- não toca no banco, não grava cookie e escreve uma linha por requisição.

### O que vai para o log

Pouco, de propósito. URLs assinadas e tokens moram na query e em alguns caminhos (`/rastrear/{token}`, `/agendar/{slug}/reserva/{token}`).

| Campo | Exemplo | Regra |
|---|---|---|
| `EffectiveDirective` | `script-src-elem` | Só a palavra da diretiva |
| `BlockedOrigin` | `https://x.example`, `inline`, `eval`, `data` | Origem de um endereço web, só o esquema nos outros, palavra-chave do navegador |
| `Document` | `https://erp.orken.com.br/agendar/*` | Origem e primeiro segmento do caminho. O resto vira `*` |
| `SourceFile` | `https://erp.orken.com.br/assets/index-Abc.js` | Nome do arquivo em `/assets`; nos demais, só a origem |
| `Line` | `12` | Número da linha |
| `Disposition` | `report` | `report` ou `enforce` |

Nunca entram: query, fragmento, trecho de código (`script-sample`), `referrer`, a política, user agent e IP. Os campos têm tamanho
máximo, e quebras de linha não sobrevivem.

### Como ler

Nos logs do serviço `backend` no Railway, procure por `CSP report`. É uma linha JSON, em nível `warn`:

```json
{ "level": "warn", "message": "CSP report: 1 violation(s) [...]", "ViolationCount": 1,
  "Violations": [{ "EffectiveDirective": "img-src", "BlockedOrigin": "https://cdn.example", "Document": "https://www.orken.com.br/cantina-da-ana",
                   "SourceFile": "", "Line": null, "Disposition": "report" }] }
```

| O que aparece | O que costuma ser | O que fazer |
|---|---|---|
| `BlockedOrigin` é `chrome-extension`, `moz-extension` ou `safari-web-extension`, ou `SourceFile` é de extensão | Extensão do navegador do usuário | Ignorar |
| `script-src-elem` com `inline`, sem `SourceFile` em `/assets` | Extensão ou script injetado | Se for repetido na mesma área, **investigar como possível XSS** |
| `script-src` com `eval` | Biblioteca nova ou extensão | Se `SourceFile` estiver em `/assets`, é do app: corrigir antes de aplicar |
| `img-src` com um host desconhecido, em `/{slug}` ou no portal | URL externa em logo, capa ou produto, que a API ainda aceita | Decidir: copiar a imagem para o R2 ou liberar o host |
| `connect-src` ou `img-src` com o host da API ou do storage | Variável do ambiente errada ou ausente | Conferir as variáveis acima |

Exemplo de contagem, a partir de um arquivo de logs:

```bash
grep '"CSP report' logs.jsonl | jq -r '.Violations[] | [.EffectiveDirective, .BlockedOrigin, .Document] | @tsv' | sort | uniq -c | sort -rn
```

## O que prova que funciona

`nexo-main/e2e/csp.e2e.spec.ts` (`npm run test:e2e:csp`, job `csp-e2e`, exigido por `ci-gate` e `master-gate`): o bundle de produção,
servido pelo `Caddyfile` real na imagem de produção, contra a API real em PostgreSQL descartável, no Chromium.

- **Nenhuma violação nos fluxos reais:** login, painel com gráfico, QR code do MFA, cardápio, agendamento, portal do Build, visor com GLB
  texturizado, conversão IFC no navegador até o GLB aceito pelo servidor, PDF em `<object>`, imagem de planta, PDF emitido pela API, panorama 360, WebSocket
  da cozinha, vídeo e janela de impressão. Cada teste lê o evento na página e também os relatórios que a API recebeu, porque só o relatório
  enxerga o que acontece dentro do worker.
- **A política tem dentes:** script inline injetado, `eval`, handler inline, script e fetch para host desconhecido, `iframe`, `<base>`, form
  e worker de `blob:` geram violação, e o relatório chega ao endpoint sem query nem token. O script injetado **ainda roda**, o que prova
  que nada é bloqueado.
- **Cada fonte tem motivo:** tirar `'wasm-unsafe-eval'`, `worker-src 'self'`, `object-src blob:`, `frame-src blob:`, `connect-src blob:`,
  `media-src`, `style-src 'unsafe-inline'` ou `img-src data:` faz o fluxo correspondente ser relatado. Foi a suíte que revelou o `frame-src
  blob:`: pelo código, parecia bastar `object-src`.

`scripts/check-public-hosts.sh`, no `frontend-ci`, confere por `Host` que `www` e `erp` mandam a política com os valores de produção e que
o cabeçalho aplicado continua sendo só `frame-ancestors`.

Alterou o que o app carrega? Atualize a tabela acima e a política. Se o carregamento novo estiver em um fluxo coberto, a suíte falha até
os dois estarem de acordo. Fluxo novo que carrega algo diferente precisa de um teste novo aqui.

## Passar a aplicar

### Critérios objetivos

Todos, ao mesmo tempo:

1. **14 dias corridos** de relatórios em produção, depois do último deploy que mudou o frontend ou a política.
2. **Tráfego real em cada área** nesse período, conferido nos logs de acesso: login e painel, cardápio ou agendamento, um portal do Build
   publicado, e ao menos **uma conversão IFC e um PDF de planta abertos por uma pessoa**. Quando uma área não tiver uso real, o
   prazo não prova nada para essa área, e o critério é fazer o percurso manualmente em produção, com uma obra de teste.
3. **Zero relatório vindo do app** nos últimos 14 dias: nenhum com `SourceFile` em `/assets`, e nenhum com `Document` em área do app e
   origem de bloqueio que não seja de extensão. Toda ocorrência precisa de causa explicada na tabela acima.
4. **Sem curinga de storage:** `CSP_STORAGE_ORIGINS` definida em produção com os endereços reais, e 7 dias de relatórios depois disso.
5. **Decisão tomada sobre imagens externas:** nenhum `img-src` com host desconhecido, ou cada host decidido (copiar para o R2 ou liberar).
6. **`csp-e2e` verde** no commit exato.

### Passos

1. Em um PR, no `Caddyfile`, troque o nome do cabeçalho completo para `Content-Security-Policy` e remova a linha antiga de `frame-ancestors`,
   que já está na política. Mantenha `report-uri`: uma política aplicada também relata.
2. Ajuste `check-public-hosts.sh` e os testes do cabeçalho na suíte. Nos testes de dentes, o script injetado passa a **não** rodar, e o
   `disposition` esperado passa a ser `enforce`.
3. O merge publica em staging. Percorra os fluxos da suíte manualmente em staging e confira os logs.
4. Promova com `scripts/promote-production.sh <sha>`, depois do `master-gate` verde, em horário de pouco movimento.
5. Acompanhe os logs por `CSP report` nas primeiras 24 horas, agora com `Disposition: enforce`. Cada linha é algo que **foi bloqueado**
   para alguém.

### Como voltar

Sem migração de banco, então é seguro em qualquer momento.

- **Imediato:** no Railway, faça o redeploy do deploy anterior do serviço `main`, como descrito em
  `docs/engineering/staging-e-promocao.md#rollback`.
- **Definitivo:** reverta o PR. A política volta para `Report-Only`, e `frame-ancestors` continua aplicado.

Um relatório nunca derruba nada: mesmo com o endpoint fora do ar, o navegador só perde o relatório.
