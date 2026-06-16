# Orken Menu — Revisão completa de UX (rodada `ux/orken-menu-complete-review`)

Revisão profunda da experiência do **Orken Menu** (restaurante/bar/delivery):
navegação, fluxos (salão, cozinha, entregas, portal público), configurações,
financeiro e microcopy. Complementa `backlog-ui-flow-audit.md` (rodada anterior).

Premissas: nada de feature fake, nada de mock, não esconder erro real, não quebrar
performance validada, não mexer em Auth/Cookies/Stripe/Redis, não renomear namespaces
de backend. Branch baseada no HEAD validado (portal sem tela branca + workspace +
rebrand Orken), **não** na `master` crua — a `master` ainda não tem essas correções.

## Implementado nesta rodada (frontend, seguro)

| # | Severidade | Página/área | Mudança |
|---|-----------|-------------|---------|
| 1 | P1 | Sidebar | `Config. Mesas` → **`Mesas e áreas`** (nome honesto do que a página faz) |
| 2 | P1 | Entregas | título da página `Hub de Deliveries` → **`Entregas`** (consistente com sidebar + trilha); subtítulo com estado vazio ("Nenhum pedido em andamento") |
| 3 | P1 | Salão | estado vazio real quando **não há mesa nenhuma**: CTA "Configurar mesas" p/ gestor, orientação de balcão p/ garçom; corrige texto "nesta área" quando não há filtro |
| 4 | P1 | Relatórios | corrige layout: usava `h-screen` + header próprio dentro do `MainAppLayout` (sidebar) → duplo chrome/scroll aninhado no desktop. Agora usa `PageHeader` padrão |
| 5 | P1 | Header (global) | remove **sino de notificações sem função** (botão morto visível nas telas de gestão do Menu) |
| 6 | P2 | Dashboard | blocos "Mesas abertas" e "Cozinha" agora **clicáveis** → Salão / Cozinha (caminho claro para a operação ao entrar no Menu) |
| 7 | P2 | Cardápio online | `Portal` → **`Cardápio online`** (sidebar + título); `PageHeader` + eyebrow "Orken Menu" |
| 8 | P2 | Mesas e áreas / Financeiro | `PageHeader` + eyebrow "Orken Menu" (contexto "onde estou" consistente nas telas de gestão, que não têm trilha) |
| 9 | P2 | Cozinha (KDS) | copy dos estados vazios por coluna: "Sem novos pedidos" / "Nada em preparo" / "Nada pronto ainda" |

## Já estava correto (verificado, sem ação)

- **Portal público** (`PortalMenuPage`/`PortalTrackingPage`): loading, erro
  ("Restaurante não encontrado"), estado fechado e tracking com passos — todos bons.
  Branding `app.orken.com.br`. Sem "Nexo" visível em lugar nenhum da UI.
- **Fluxo de entregas**: triagem real (Aceitar/Rejeitar com motivo) antes de seguir,
  cores por tempo de espera, progressão de status clara. Operacional.
- **Parâmetros de custo (CMV)** já morava no Financeiro (não em Mesas/áreas), com
  rótulo e explicação próprios — gestor sabe onde configurar.
- **Trilha "Orken Menu › …"** + botão voltar role-aware nas telas operacionais
  (Salão/Entregas/Cozinha/comanda) que não têm sidebar.

## Backlog (P3 — decisão de produto / refactor maior, fora do escopo seguro)

- **B1 (herdado)** `/estoque` mostra cópia "Ingredientes" (Menu) numa rota compartilhada
  rotulada "Estoque" — precisa cópia ciente do workspace. Decisão de domínio.
- **B2 (herdado)** `FinanceiroPage` muito longa num scroll — abas/seções colapsáveis
  (Resultado · CMV · Pessoal & Despesas · Parâmetros). Refactor de layout.
- **N1 — Primeira tela do Menu para gestão.** Hoje o workspace Menu abre no `/dashboard`
  genérico (com `RestauranteBlocks`). Avaliar um "home operacional" do Menu (ex.: Salão)
  ou um dashboard dedicado. Mudaria a landing de gerente/diretoria → decisão de produto.
- **N2 — Notificações.** O sino foi removido por ser inerte. Se houver demanda, implementar
  notificações reais (item pronto, novo pedido de delivery) com backend/WS — já existe
  `useKitchenSocket`/SignalR como base.
- **N3 — Headers ainda inconsistentes** em telas fora do Menu (Platform Admin usa
  "Carregando..." cru; ~20 ocorrências). Fora do app operacional do tenant.

## Critérios de aceite (status)

- [x] sem tela branca (fix de chunk preservado na base)
- [x] sem "Nexo" visível na UI
- [x] sem rota fake / sem botão sem função (sino removido)
- [x] páginas operacionais com contexto (trilha) e gestão com PageHeader
- [x] gestor sabe onde configurar (Cardápio online, Mesas e áreas, Parâmetros CMV) e
      acompanhar (Financeiro, Relatórios)
- [x] build TypeScript passa (`tsc --noEmit` limpo)
