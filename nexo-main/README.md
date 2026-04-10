# Nexo — ERP para Pequenas e Médias Empresas

> **Gestão inteligente para empresas reais.**
>
> Desenvolvido por Andrade Systems.

Nexo é um sistema ERP desktop voltado para o controle operacional e gerencial de pequenas e médias empresas. Centraliza em um único lugar: ponto de venda, estoque, caixa, comissões, relatórios e insights estratégicos.

---

## Módulos

| Módulo | Rota | Descrição |
|---|---|---|
| **Dashboard** | `/dashboard` | Visão geral com KPIs, ranking de vendedores, produtos mais vendidos, alertas de estoque e insights recentes |
| **PDV** | `/pdv` | Ponto de venda otimizado para teclado e leitura de código de barras |
| **Vendas** | `/vendas` | Histórico de vendas com filtros; detalhe com cancelamento total ou por item |
| **Produtos** | `/produtos` | Cadastro e edição de produtos |
| **Estoque** | `/estoque` | Posição de estoque, movimentações, ajustes e transferências |
| **Clientes** | `/clientes` | Cadastro de clientes com dados comerciais |
| **Fornecedores** | `/fornecedores` | Cadastro de fornecedores |
| **Usuários** | `/usuarios` | Gestão de usuários e perfis de permissão |
| **Caixa** | `/caixa` | Controle de sessão de caixa: abertura, movimentos, sangria, suprimento e fechamento |
| **Comissões** | `/comissoes` | Apuração e estorno de comissões por vendedor |
| **Relatórios** | `/relatorios` | Relatórios operacionais: vendas por operador, produtos mais vendidos, cancelamentos e caixa |
| **Insights** | `/insights` | Alertas gerenciais derivados automaticamente dos dados operacionais |
| **Auditoria** | `/auditoria` | Log de todas as ações críticas do sistema |
| **Configurações** | `/configuracoes` | Preferências gerais do sistema |

---

## Perfis de Acesso

- Diretoria
- Gerente
- Vendedor
- Estoquista

---

## Stack

**Frontend**

| Tecnologia | Versão |
|---|---|
| React | 18 |
| TypeScript | 5 |
| Vite | 5 |
| TailwindCSS | 3 |
| shadcn/ui | — |
| TanStack Query | 5 |
| React Router | 6 |
| Recharts | 2 |
| Lucide Icons | — |
| Sonner | — |

**Futuro**

- Electron (wrapper desktop)
- Backend .NET

---

## Arquitetura

O projeto segue uma arquitetura modular. Cada módulo é isolado e organizado em:

```
src/modules/<nome>/
  components/   # Componentes de UI do módulo
  pages/        # Páginas roteadas
  services/     # Camada de acesso a dados (mock → futuramente API)
  types/        # Tipagens TypeScript
  data/         # Dados estáticos ou seeds (quando necessário)
```

**Utilitários compartilhados**

```
src/lib/formatters.ts   # formatCurrency, formatDate, formatDateTime, formatTimeShort
src/components/shared/  # PageHeader, SectionCard, EmptyState, StatusBadge, ...
```

Os serviços atualmente utilizam dados em memória (mock). A interface dos métodos já está preparada para substituição por chamadas à API .NET sem alteração nos componentes.

---

## Instalação e Execução

**Pré-requisitos:** Node.js 18+

```bash
# Instalar dependências
npm install

# Iniciar servidor de desenvolvimento (porta 8080)
npm run dev

# Build de produção
npm run build

# Executar testes
npm test
```

O servidor de desenvolvimento ficará disponível em `http://localhost:8080`.

---

## Scripts Disponíveis

| Script | Descrição |
|---|---|
| `npm run dev` | Servidor de desenvolvimento com HMR |
| `npm run build` | Build de produção |
| `npm run build:dev` | Build em modo development |
| `npm run preview` | Preview do build de produção |
| `npm run lint` | Lint com ESLint |
| `npm test` | Executa os testes com Vitest |
| `npm run test:watch` | Testes em modo watch |

---

## Módulos Críticos

### PDV (`/pdv`)

O PDV é o módulo mais sensível do sistema. Opera em layout dedicado (`PosLayout`) e foi projetado para:

- Busca de produto por código de barras ou descrição
- Gerenciamento de carrinho com ajuste de quantidade e descontos
- Seleção de múltiplas formas de pagamento (dinheiro, PIX, cartão)
- Finalização de venda com atualização automática de estoque e caixa

Ao finalizar uma venda, as seguintes caches são invalidadas automaticamente: sessão de caixa, histórico de vendas, comissões e dados do dashboard.

### Caixa (`/caixa`)

Controle de sessão financeira com:

- Abertura com valor inicial
- Registro de entradas e saídas (suprimento e sangria)
- Fechamento com contagem de caixa e detecção de divergência
- Log completo de movimentos

---

## Decisões de Arquitetura

**TanStack Query**
- `staleTime: 30_000` global — evita refetches desnecessários durante navegação
- Chaves hierárquicas: `["sales", filters]` é invalidado por `invalidateQueries({ queryKey: ["sales"] })`
- Invalidação cruzada explícita após mutações críticas (venda, cancelamento, fechamento de caixa)

**Formatadores centralizados**
- Toda formatação de moeda e data passa por `src/lib/formatters.ts` — um único ponto de ajuste para localização

**Filtragem server-side**
- Filtros de listagem são passados diretamente ao serviço via `queryKey: ["entidade", filters]`, eliminando `useMemo` de filtragem nos componentes de página

---

*Nexo — Andrade Systems*
