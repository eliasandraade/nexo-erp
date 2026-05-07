# Release: CMV + Financeiro Restaurante — Fase 3

**Data:** 2026-05-06
**Branch:** master
**Commits:** `849188f` → `ed7baa3` (7 commits de feature + 1 fix)

---

## O que foi entregue

Fase 3 completa o painel financeiro do módulo Restaurante com gestão de pessoal, despesas operacionais e KPIs avançados de rentabilidade.

---

## Backend

### Novas entidades de domínio

**`RestEmployee`** (`nexo.rest_employees`)
- Campos: `Name`, `Role`, `AdmissionDate (DateOnly)`, `MonthlySalary`, `Notes?`, `IsActive`
- Factory `Create(...)` com validação de nome, função e salário
- `Update(...)` com `SetUpdatedAt()`
- Soft delete via toggle de `IsActive` (sem DELETE físico)

**`RestExpense`** (`nexo.rest_expenses`)
- Campos: `Description`, `Category`, `Amount`, `CompetenceDate (DateOnly)`, `PaymentDate? (DateOnly)`, `IsRecurring`
- Factory `Create(...)` com validação de descrição, categoria e valor
- `Update(...)` com `SetUpdatedAt()`
- Hard delete suportado (despesas são registros de período)

Ambas estendem `StoreEntity` — filtro automático por `TenantId` + `StoreId`, sem `IgnoreQueryFilters`.

### Novos endpoints

**`GET /api/restaurante/employees?includeInactive=false`**
Lista funcionários. Por padrão retorna apenas ativos.

**`POST /api/restaurante/employees`** → 201 Created
**`PUT /api/restaurante/employees/{id}`** → 200 OK
Suporta ativar/desativar via campo `IsActive` no body.

**`GET /api/restaurante/expenses?from=yyyy-MM-dd&to=yyyy-MM-dd`**
Lista despesas filtradas por `CompetenceDate` no período. Ambos os parâmetros são opcionais.

**`POST /api/restaurante/expenses`** → 201 Created
**`PUT /api/restaurante/expenses/{id}`** → 200 OK
**`DELETE /api/restaurante/expenses/{id}`** → 204 No Content

### Financeiro summary estendido

`GET /api/restaurante/financeiro/summary?from=&to=` passou de 7 para 11 campos:

| Campo novo | Cálculo |
|---|---|
| `TotalPersonnelCost` | Soma de `MonthlySalary` de todos os funcionários ativos (sem filtro de período) |
| `TotalFixedExpenses` | Soma de `Amount` das despesas com `CompetenceDate` dentro do período |
| `OperationalProfit` | `GrossMargin − TotalPersonnelCost − TotalFixedExpenses` |
| `BreakEvenRevenue` | `(TotalPersonnelCost + TotalFixedExpenses) / (1 − CMV%/100)`, retorna 0 se CMV ≥ 100% |

Queries COGS são puladas inteiramente quando não há pedidos no período (`ordersCount == 0`), evitando 5 queries pesadas desnecessárias.

### Migration

`20260506203512_CreateRestEmployeesAndExpenses` — aplicada automaticamente via `MigrateAsync()` no startup.

### Integration tests (7 passando)

| Teste | Verifica |
|---|---|
| `CmvReport_ReturnsItemWithCorrectCmvMetrics` | CMV% e margem calculados corretamente pela ficha técnica |
| `FinanceiroSummary_ReturnsZeroRevenue_WhenNoPaidOrdersInPeriod` | Summary zerado para período sem pedidos |
| `CreateEmployee_ReturnsCreatedWithCorrectFields` | Criação retorna 201 com campos corretos e `IsActive = true` |
| `UpdateEmployee_CanDeactivate` | PUT com `IsActive: false` desativa o funcionário |
| `CreateExpense_ReturnsCreatedWithCorrectFields` | Criação retorna 201 com campos corretos |
| `ListExpenses_FiltersByPeriod` | GET com `from`/`to` filtra por `CompetenceDate` corretamente |
| `FinanceiroSummary_IncludesPersonnelAndExpenses` | Summary inclui custo de pessoal e despesas do período |

---

## Frontend

### Novos arquivos

**`src/modules/restaurante/api/employees-expenses.api.ts`**
- `EXPENSE_CATEGORIES` — 10 categorias pré-definidas: Energia, Gás, Água, Internet, Impostos, Manutenção, Aluguel, Embalagem, Publicidade, Outros
- DTOs: `EmployeeDto`, `CreateEmployeeRequest`, `UpdateEmployeeRequest`, `ExpenseDto`, `CreateExpenseRequest`
- Fetch functions: `fetchEmployees`, `createEmployee`, `updateEmployee`, `fetchExpenses` (com `URLSearchParams`), `createExpense`, `updateExpense`, `deleteExpense`

**`src/modules/restaurante/hooks/use-employees-expenses.ts`**
- TanStack Query v5 (object-options API)
- `useEmployees(includeInactive?)` — staleTime 60s
- `useCreateEmployee()`, `useUpdateEmployee()` — invalidam `["restaurante", "employees"]`
- `useExpenses(from?, to?)` — `enabled: !!from && !!to`
- `useCreateExpense()`, `useUpdateExpense()`, `useDeleteExpense()` — invalidam `["restaurante", "expenses"]`

### `FinanceiroSummaryDto` estendida

Interface TypeScript espelha exatamente os 11 campos do backend, incluindo os 4 novos: `totalPersonnelCost`, `totalFixedExpenses`, `operationalProfit`, `breakEvenRevenue`.

### `FinanceiroPage.tsx` — nova estrutura

**Linha 1 de KPIs (4 cards):** Faturamento bruto · CMG · CMV% ponderado · Margem bruta

**Linha 2 de KPIs (4 cards):** Custo de pessoal · Despesas do período · Lucro operacional · Ponto de equilíbrio

**InsightCards (3 painéis):**
- Contagem de pratos com CMV > 35%
- Delta de lucro operacional vs mês anterior (via segunda chamada `useFinanceiroSummary` com `prevMonthBounds`)
- Funcionário ativo com maior salário

**EmployeesSection:**
- Lista todos (ativos + inativos)
- Formulário inline de adição (nome, função, salário)
- Edição inline por linha (lápis → fields → confirmar/cancelar)
- Toggle ativar/desativar por funcionário
- Total mensal dos ativos calculado no cliente

**ExpensesSection:**
- Lista despesas do período selecionado
- Formulário inline de adição (descrição, categoria via Select, valor, checkbox recorrente)
- Edição inline por linha (lápis → fields → confirmar/cancelar)
- Delete com toast de confirmação
- Total do período calculado no cliente

---

## Arquitetura — decisões relevantes

- **Sem DELETE de funcionários:** desativação via `IsActive` preserva histórico de custo
- **Custo de pessoal sem filtro de período:** salário é custo mensal fixo, não varia por data de competência
- **CompetenceDate como DateOnly:** funciona nativamente com EF Core 8 + Npgsql sem converter customizado
- **COGS guard:** `if (ordersCount > 0)` evita 5 queries em meses sem movimento
- **Divisão por zero no break-even:** protegida com `cmvRatio < 1m`
- **Invalidação ampla no TanStack Query:** usa prefixo `["restaurante", "employees"]` sem parâmetros para invalidar todas as variantes do query key

---

## Commits

| SHA | Descrição |
|---|---|
| `849188f` | feat(restaurante): add RestEmployee and RestExpense entities with EF config and migration |
| `bfd49e7` | fix(restaurante): add role and category blank validation in RestEmployee and RestExpense |
| `c544379` | feat(restaurante): add EmployeesController, ExpensesController with CRUD and integration tests |
| `04bca19` | feat(restaurante): extend financeiro summary with personnel cost, expenses, operational profit and break-even |
| `d56e113` | feat(restaurante): add employees-expenses API layer and hooks, extend FinanceiroSummaryDto |
| `ad9a462` | feat(restaurante): extend FinanceiroPage with employees section, expenses section, expanded KPIs and insights |
| `ed7baa3` | feat(restaurante): add inline edit to ExpensesSection |
