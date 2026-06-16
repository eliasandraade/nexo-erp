# Backlog — Auditoria de fluxo do usuário (P2)

Itens encontrados na auditoria de fluxo pós-E2E que **não** são quick-wins seguros.
São melhorias reais, mas exigem decisão de produto e/ou refactor maior — fora do
escopo "apenas melhorias objetivas e seguras" desta rodada. Nada aqui é fake/mock;
nada remove funcionalidade.

## B1 — Estoque com cópia restaurante-específica numa página compartilhada
`modules/inventory/pages/EstoquePage.tsx`: título **"Ingredientes"**, descrição
"Insumos e ingredientes usados nos pratos" e botão **"Novo ingrediente"** — mas a
sidebar (grupo `inventario`, compartilhado) rotula a rota `/estoque` como **"Estoque"**.
Para um tenant **Orken Store (varejo)** isso confunde: ele vende produtos, não ingredientes.
**Ação sugerida:** cópia ciente do workspace ativo — Store → "Estoque / Produtos";
Menu → "Ingredientes". Precisa decisão: `/estoque` é genérico ou específico de cardápio?
**Severidade:** média (clareza). **Risco:** baixo, mas é decisão de domínio.

## B2 — Página Financeiro (Orken Menu) muito longa num só nível
`modules/restaurante/pages/FinanceiroPage.tsx` empilha num scroll: KPIs, Insights,
CMV por prato, Parâmetros de custo, Funcionários e Despesas. É completa (bom), mas
densa. **Ação sugerida:** abas ou seções colapsáveis (ex.: *Resultado · CMV · Pessoal
& Despesas · Parâmetros*) — progressive disclosure, sem remover informação.
**Severidade:** média (hierarquia). **Risco:** médio (refactor de layout).

## B3 — Headers de página inconsistentes
A maioria usa `PageHeader` (régua de acento + Syne), mas algumas telas de restaurante
têm header inline próprio (`RestauranteSetupPage`, `DeliveryPage`). **Ação:** padronizar
no `PageHeader` para consistência visual. **Severidade:** baixa. **Risco:** baixo, porém
muitos arquivos.

## B4 — "Carregando..." inline (sobretudo no Platform Admin)
~20 ocorrências de `Carregando...` como texto cru (em sua maioria `modules/platform/*`,
ferramenta interna). **Ação:** trocar por skeletons/loader consistente. **Severidade:**
baixa. **Risco:** baixo. (Fora do app operacional do tenant.)

## B5 — Revisão de labels de navegação
Ex.: "Config. Mesas" agora é só áreas/mesas (custos saíram) → poderia ser "Mesas e áreas";
revisar nomes para clareza por workspace. **Severidade:** baixa. **Risco:** baixo.

---

### Já corrigido nesta rodada (não é backlog)
- **P0** 500 em `/api/stock/paged` (computed `AvailableQuantity` em LINQ) + teste de regressão.
- **P1** breadcrumb/voltar "Orken Menu › Salão/Entregas/Cozinha" (operacional, sem sidebar).
- **P1** "Custos operacionais" movido de *Config. Mesas* → *Financeiro* (junto do CMV que alimenta).
- **P2** `EstoquePage`: estado de erro agora usa `ErrorState` com "Tentar novamente".
