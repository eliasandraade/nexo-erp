# Hosts públicos do frontend (serviço `main`)

O serviço `main` do projeto `nexoerp` (Railway, produção) atende dois hosts com o mesmo build e o mesmo `Caddyfile`:

| Host | O que serve |
|---|---|
| `erp.orken.com.br` | O Orken ERP. A raiz é a entrada do produto: o login, ou o destino de quem já entrou. Links de e-mail saem com este host (`App__FrontendUrl`) |
| `www.orken.com.br` | Links públicos de clientes (abaixo). A raiz leva ao Portal Orken |

A topologia de toda a Orken (conta, portal, produtos) está em `orken-platform/docs/estado.md`, seção "Domínios". A
única página institucional é o Portal Orken em `app.orken.com.br`. A landing antiga do ERP foi removida do app e não
existe mais em nenhum endereço.

## Raiz de `www`

A raiz de `www` responde 307 para `https://app.orken.com.br/`, mantendo a query string (`?utm_source=…`).

- **Servidor:** o `Caddyfile` faz o redirecionamento só em visitas (`GET` e `HEAD`) ao host `www.orken.com.br` no
  caminho `/`, antes do fallback da SPA. O Caddy normaliza o caminho antes, então `//` e `/./` também contam como raiz.
  O destino é sempre `https://app.orken.com.br/`. Um `POST` na raiz continua com o 405 de antes. Os seis cabeçalhos de
  segurança saem também no 307.
- **Cliente:** links internos para `/` não passam pelo servidor (o logo das telas de login). Para eles, o roteador faz a
  mesma coisa (`src/app/router/institutionalEntry.ts`).

## Raiz de `erp`

A raiz de `erp` responde 307 para `/login`, mantendo a query string. Isso também vale só para `GET` e `HEAD`, com os
mesmos cabeçalhos.

- **Sessão:** a sessão do ERP fica no navegador (no `localStorage` ou, no modo cookie, num cookie do host da API), e o servidor do frontend não a vê. Por
  isso o servidor manda todos ao login. O `AuthLayout` leva quem já tem sessão ao mesmo destino de logo depois de
  entrar (`resolvePostLogin`: `/platform`, a última área de trabalho guardada, a área única, `/workspaces` ou a casa do
  papel). Nenhum desses destinos é `/`, então não há laço.
- **Cliente:** no roteador, `/` em qualquer host que não seja `www` é `/login` com a query (`ErpEntry`). O logo das telas
  de login leva ao login.
- **Rastreio de pedido:** "← Voltar ao cardápio" aparece quando o carrinho abriu a página e leva ao cardápio da loja.
  Aberta por um link compartilhado, a página não tem link de volta.

## Caminhos que continuam como estavam

| Caminho | O que é |
|---|---|
| `/{empresa}/{obra}` | Portal do cliente do Orken Build (`resolveBuildPortal`) |
| `/{slug}` e `/menu/{slug}` | Cardápio público do restaurante |
| `/agendar/{slug}` e `/agendar/{slug}/reserva/{token}` | Agendamento e gerenciamento da reserva |
| `/rastrear/{token}` | Acompanhamento do pedido |
| `/login`, `/redefinir-senha`, `/verify-email` e as demais rotas do app | O app do ERP, nos dois hosts |

A raiz de qualquer outro host continua sendo servida pela SPA. Isso inclui o healthcheck do Railway, que chama `/` com
outro host e precisa de 200.

## Testes

- **`scripts/check-public-hosts.sh <origem>`:** matriz de rotas por host contra o Caddy de produção, incluindo:
  - a cadeia de redirecionamentos (um salto, sem laço);
  - os cabeçalhos nos dois 307.

  O CI (`frontend-ci`, passo "Public hosts") roda a matriz sobre o build do PR, com `caddy:2-alpine`, como o Dockerfile.
- **`e2e/experience.spec.ts`:**
  - `/` sem sessão abre o login com a query;
  - `/` e `/login` com sessão levam ao destino do ERP (estoquista, diretoria com e sem área guardada, plataforma).
- **`src/app/router/institutionalEntry.test.ts`:** a regra de `www` no roteador.
- **`src/modules/portal/pages/PortalTrackingPage.test.tsx`:** o link de volta ao cardápio.

## Reverter

Reverta os commits do `Caddyfile` e do roteador. Como o código é 307, nenhum navegador fica preso ao redirecionamento.
