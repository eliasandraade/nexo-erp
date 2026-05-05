# Spec: CMV Restaurante — Fase 1
**Separação Estoque/Cardápio + Ficha Técnica Completa**
Data: 2026-05-05

---

## Contexto

O módulo restaurante do NexoERP usa a entidade `Product` tanto para itens do cardápio (o que é vendido) quanto para ingredientes/insumos (o que é comprado e usado para produzir pratos). Essa mistura impede a gestão correta de estoque e custo.

O backend já possui `RestRecipeCard` com cálculo básico de CMV (custo de ingredientes / rendimento / preço de venda). Faltam: foto do prato, modo de preparo estruturado, tempo de preparo, montagem, embalagem e custo operacional (gás, mão de obra).

Esta Fase 1 resolve a separação e completa a ficha técnica. As Fases 2 e 3 (painel Financeiro, funcionários, despesas) dependem desta.

---

## Decisões de Design

| Decisão | Escolha | Motivo |
|---|---|---|
| Separação ingrediente/cardápio | Flag `IsIngredient` no `Product` existente | Reusa StockItem, StockMovement, migrations mínimas |
| Histórico de preços | Nova tabela `ProductPurchasePrice` | Independente do módulo Compras; média simples das últimas 5 |
| Etapas de preparo | JSONB no `RestRecipeCard` | Sem query individual por etapa; ordem imutável dentro da ficha |
| Custo gás/MO | Configurável em `FoodServiceSettings` | Centralizado; muda para todos os pratos ao ajustar |
| Ficha Técnica no frontend | Página dedicada `/produtos/:id/ficha` | Formulário muito grande para modal |

---

## Modelo de Dados

### 1. `Product` — novo campo

```sql
ALTER TABLE products
  ADD COLUMN is_ingredient BOOLEAN NOT NULL DEFAULT FALSE;
```

- `is_ingredient = false` → item do cardápio (comportamento atual de todos os produtos existentes)
- `is_ingredient = true` → insumo/ingrediente do estoque do restaurante

Sem breaking change. Produtos existentes continuam com `false`.

### 2. `ProductPurchasePrice` — nova tabela

```sql
CREATE TABLE product_purchase_prices (
  id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id     UUID NOT NULL REFERENCES tenants(id),
  product_id    UUID NOT NULL REFERENCES products(id) ON DELETE CASCADE,
  price         NUMERIC(18,4) NOT NULL CHECK (price >= 0),
  purchased_at  DATE NOT NULL,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX ix_ppp_tenant_product_date
  ON product_purchase_prices (tenant_id, product_id, purchased_at DESC);
```

O backend retorna a média das 5 entradas mais recentes por `(tenant_id, product_id)`. O campo `last_price` no DTO é o registro mais recente.

### 3. `RestRecipeCard` — novos campos

```sql
ALTER TABLE rest_recipe_cards
  ADD COLUMN image_url              TEXT,
  ADD COLUMN has_prep               BOOLEAN NOT NULL DEFAULT TRUE,
  ADD COLUMN prep_steps             JSONB NOT NULL DEFAULT '[]',
  ADD COLUMN total_prep_time_min    INTEGER,
  ADD COLUMN assembly_notes         TEXT,
  ADD COLUMN requires_packaging     BOOLEAN NOT NULL DEFAULT FALSE,
  ADD COLUMN packaging_product_id   UUID REFERENCES products(id);
```

Schema do JSONB `prep_steps`:
```json
[
  { "order": 1, "description": "Temperar a carne com sal e pimenta", "durationMinutes": 5 },
  { "order": 2, "description": "Selar na chapa quente", "durationMinutes": 8 },
  { "order": 3, "description": "Deixar descansar", "durationMinutes": 2 }
]
```

`total_prep_time_min` é calculado pelo backend na gravação como `Σ durationMinutes` dos passos. Campo readonly para o cliente.

### 4. `FoodServiceSettings` — novos campos

```sql
ALTER TABLE food_service_settings
  ADD COLUMN cost_per_minute_gas    NUMERIC(18,4) NOT NULL DEFAULT 0,
  ADD COLUMN cost_per_minute_labor  NUMERIC(18,4) NOT NULL DEFAULT 0;
```

---

## Cálculo de CMV

```
custo_ingredientes = Σ (ingrediente.quantity × ingrediente.costPrice)
custo_unitario_ing = custo_ingredientes / card.Yield

custo_gas   = total_prep_time_min × settings.CostPerMinuteGas
custo_mo    = total_prep_time_min × settings.CostPerMinuteLaborRate

custo_total = custo_unitario_ing + custo_gas + custo_mo
CMV%        = (custo_total / product.SalePrice) × 100
```

O `RecipeCardDto` expõe o breakdown completo:
```
ingredientCost  — custo unitário dos ingredientes
gasCost         — custo de gás
laborCost       — custo de mão de obra
calculatedCost  — total
cmvPercent      — percentual sobre o preço de venda
```

Faixas de CMV para colorização na UI:
- Verde: CMV% < 30%
- Amarelo: 30% ≤ CMV% ≤ 40%
- Vermelho: CMV% > 40%

---

## API

### `Product` (mudanças mínimas)

`POST /api/products` e `PUT /api/products/:id` — adicionar `isIngredient: bool` no request/response.

`GET /api/products` — novo query param opcional:
```
?isIngredient=true   → apenas ingredientes
?isIngredient=false  → apenas itens do cardápio
(omitido)            → todos (comportamento atual)
```

### `ProductPurchasePrice` (novo)

```
POST /api/products/:id/purchase-prices
Body:    { price: decimal, purchasedAt: date (ISO) }
Returns: { id, productId, price, purchasedAt }
Roles:   estoquista, gerente, diretoria

GET /api/products/:id/purchase-prices
Returns: {
  lastPrice: decimal | null,
  averagePrice: decimal | null,
  history: [{ id, price, purchasedAt }]  ← até 5, ordem desc
}
Roles:   estoquista, gerente, diretoria
```

### `RecipeCard` (extensão dos endpoints existentes)

`PUT /api/restaurante/recipe-cards/:id` passa a aceitar todos os campos novos:
```json
{
  "imageUrl": "string | null",
  "hasPrep": true,
  "prepSteps": [
    { "order": 1, "description": "...", "durationMinutes": 5 }
  ],
  "assemblyNotes": "string | null",
  "requiresPackaging": false,
  "packagingProductId": "uuid | null",
  "yield": 1,
  "yieldUnit": "porção",
  "notes": "string | null"
}
```

Upload de imagem (novo endpoint):
```
POST /api/restaurante/recipe-cards/:id/image
Content-Type: multipart/form-data
Field: file (image/jpeg, image/png, image/webp — max 5MB)
Returns: { imageUrl: string }
```

### `FoodServiceSettings` (extensão)

`GET` e `PUT /api/restaurante/settings` passam a incluir:
```json
{
  "costPerMinuteGas": 0.020,
  "costPerMinuteLaborRate": 0.100
}
```

---

## Frontend

### `/estoque` — Ingredientes do Estoque

**Mudanças:**
- Continua usando `useStockItems()` + join com `useProducts()` para obter saldos (o `StockItem` é a fonte de verdade do saldo). O filtro passa a ser: exibir apenas itens onde o `Product` relacionado tem `isIngredient = true`.
- Alternativa de backend: adicionar query param `?isIngredient=true` no endpoint `/api/stock-items` para o servidor já filtrar. Preferível para evitar over-fetch.
- Header: "Ingredientes" | botão "Novo ingrediente"
- Tabela adiciona colunas: Última Compra (R$), Preço Médio/5 (R$)
- "Novo ingrediente" abre `ProductFormPage` com `isIngredient=true` pré-setado e oculto

**Formulário de ingrediente** (campos focados, sem seções comerciais):
```
Nome *           texto
Unidade *        select: kg / g / L / ml / unidade / porção
Estoque mínimo   número
Estoque máximo   número
Rastrear estoque toggle (default true)
─── Histórico de Preços ───
Última compra    R$ ____  (ao salvar, grava em ProductPurchasePrice)
Preço médio      R$ ____  [readonly] [ⓘ "Média das últimas 5 compras"]
```

### `/produtos` — Cardápio

**Mudanças:**
- Query `useProducts({ isIngredient: false })`
- Toggle no topo do formulário: `[ Ingrediente ] ←→ [ Item do cardápio ]`
- Seção "Ficha Técnica" aparece no final do formulário (apenas para tenants com módulo `restaurante`):
  - Sem ficha: botão "Criar Ficha Técnica" → navega para `/produtos/:id/ficha`
  - Com ficha: resumo (CMV%, custo, preço) + botão "Editar Ficha Técnica"

### `/produtos/:id/ficha` — Ficha Técnica

Layout two-column no desktop, stacked no mobile.

**Coluna esquerda:**

*Dados gerais:*
```
Foto do prato     — dropzone/upload, preview imediato
Rendimento        — [número] [unidade: texto livre]
Tem preparo?      — toggle (default true)
```

*Se hasPrep = false (ex: Coca-Cola):*
As seções de Ingredientes e Modo de Preparo ficam ocultas. O backend armazena `prepSteps = []`, `totalPrepTimeMinutes = null`, e o CMV é calculado apenas com base no `costPrice` do próprio produto (sem ingredientes). O custo de gás e MO ficam zerados.

*Se hasPrep = true:*

Seção Ingredientes:
```
[ Dropdown ingredientes (isIngredient=true) ] [ Qtd ] [ Unidade readonly ] [ R$ linha ] [ ✕ ]
[ + Adicionar ingrediente ]
```

Seção Modo de Preparo:
```
Passo 1  [ descrição...                    ] [ min ] [ ↑ ] [ ↓ ] [ ✕ ]
Passo 2  [ descrição...                    ] [ min ] [ ↑ ] [ ↓ ] [ ✕ ]
[ + Adicionar passo ]
Tempo total: XX min  (calculado, readonly)
```

**Coluna direita:**
```
Montagem do prato   — textarea
Requer embalagem?   — toggle
  └ se sim: [ Dropdown ingredientes ] (qualquer isIngredient=true)
Observações gerais  — textarea
```

**Barra de CMV sticky no rodapé:**
```
Custo ingredientes   R$ XX,XX
Custo gás            R$ XX,XX   (X min × R$/min)
Custo mão de obra    R$ XX,XX   (X min × R$/min)
─────────────────────────────────
Custo total          R$ XX,XX
Preço de venda       R$ XX,XX
CMV                  XX,X%      [badge: verde/amarelo/vermelho]
```

Atualiza em tempo real (sem debounce — cálculo é local, não vai ao backend).

### `/restaurante/configurar` — Seção "Custos Operacionais"

Nova seção (ou aba) na `RestauranteSetupPage`:
```
Custo por minuto de gás          R$ [0,020]
Custo por minuto de mão de obra  R$ [0,100]
[ⓘ] Usados no cálculo de CMV de todas as fichas técnicas.
[ Salvar ]
```

---

## Migrations (ordem de execução)

1. `AddIsIngredientToProducts`
2. `CreateProductPurchasePrices`
3. `ExtendRestRecipeCard` (image_url, has_prep, prep_steps, total_prep_time_min, assembly_notes, requires_packaging, packaging_product_id)
4. `AddOperationalCostsToFoodServiceSettings`

Todas retrocompatíveis. Nenhuma migration altera dados existentes.

---

## Pontos de Atenção na Implementação

1. **Upload de imagem**: O backend precisa de uma estratégia de storage. Usar o campo `ImageUrl` (string) já existente no `Product` como referência — a mesma abordagem (URL externa) deve ser usada para `RestRecipeCard.ImageUrl`. Definir se o storage é local (wwwroot) ou S3/R2 na implementação.
2. **Toggle isIngredient com ficha existente**: Se um produto já tem `RecipeCard` e o usuário marca `isIngredient = true`, o backend deve rejeitar (400) ou a UI deve alertar e desvincular a ficha antes.
3. **PackagingProductId**: Deve referenciar apenas produtos com `isIngredient = true`. Validar no backend.
4. **CMV com Yield = 0**: Já tratado no domínio (`Yield > 0` obrigatório). Manter.
5. **FoodServiceSettings pode não existir**: Se o tenant nunca acessou as configurações, `GetSettingsAsync` deve retornar defaults (0,0) em vez de 404, para o cálculo não quebrar.

---

## Fora de Escopo (Fase 1)

- Página Financeiro com KPIs de faturamento e margem → Fase 2
- Folha de funcionários e despesas gerais → Fase 3
- Integração automática com módulo Compras para atualizar preço → decisão futura
- Controle de versão de fichas técnicas (histórico de alterações)
- Impressão/exportação de ficha técnica em PDF
