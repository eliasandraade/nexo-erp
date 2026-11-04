# Hosts públicos do frontend (serviço `main`)

O serviço `main` do projeto `nexoerp` (Railway, produção) atende dois hosts com o mesmo build e o mesmo `Caddyfile`:

| Host | O que serve |
|---|---|
| `erp.orken.com.br` | O Orken ERP inteiro, inclusive a raiz. Links de e-mail saem com este host (`App__FrontendUrl`) |
| `www.orken.com.br` | Links públicos de clientes (abaixo). A raiz leva ao Portal Orken |

A topologia de toda a Orken (conta, portal, produtos) está em `orken-platform/docs/estado.md`, seção "Domínios".

## Raiz de `www`

O Portal Orken em `app.orken.com.br` é a entrada institucional. Antes, `www.orken.com.br/` mostrava a landing antiga
do ERP, uma segunda página institucional com conteúdo diferente. Agora a raiz de `www` responde 307 para
`https://app.orken.com.br/`, mantendo a query string (`?utm_source=…`).

- **Servidor:** o `Caddyfile` faz o redirecionamento só para o host `www.orken.com.br` e o caminho exato `/`, antes do
  fallback da SPA.
- **Cliente:** links internos para `/` não passam pelo servidor (o logo das telas de login, a página de rastreio de
  pedido). Para eles, o roteador faz a mesma coisa (`src/app/router/institutionalEntry.ts`).
- **307 por enquanto:** o navegador não guarda o redirecionamento, então a raiz pode voltar a `www` se o portal
  institucional passar a ser servido lá.

## Caminhos de `www` que continuam aqui

| Caminho | O que é |
|---|---|
| `/{empresa}/{obra}` | Portal do cliente do Orken Build (`resolveBuildPortal`) |
| `/{slug}` e `/menu/{slug}` | Cardápio público do restaurante |
| `/agendar/{slug}` e `/agendar/{slug}/reserva/{token}` | Agendamento e gerenciamento da reserva |
| `/rastrear/{token}` | Acompanhamento do pedido |
| `/login`, `/redefinir-senha`, `/verify-email` e as demais rotas do app | O mesmo app do ERP, como antes |

`erp.orken.com.br/` não muda, assim como a raiz de qualquer outro host. Isso inclui o healthcheck do Railway, que chama
`/` com outro host.

## Testes

- `scripts/check-public-hosts.sh <origem>`: matriz de rotas por host contra o Caddy de produção. O CI
  (`frontend-ci`, passo "Public hosts") roda a matriz sobre o build do PR, com `caddy:2-alpine`, como o Dockerfile.
- `src/app/router/institutionalEntry.test.ts`: a mesma regra no roteador.

## Reverter

Reverta o commit do `Caddyfile` e do roteador: o próximo deploy volta a servir a landing em `www.orken.com.br/`. Como
o código é 307, nenhum navegador fica preso ao redirecionamento.
