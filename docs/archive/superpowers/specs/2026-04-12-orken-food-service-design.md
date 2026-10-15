# Orken Food Service — Design Spec

**Date:** 2026-04-12  
**Status:** Approved  
**Author:** Lead Product Architect + Full-Stack Engineer  
**Scope:** Food Service module for restaurant, bar, and pub operations within Orken ERP

---

## 1. Overview

Orken Food Service extends the existing Orken platform with a complete food-service operational layer. It is not a separate application — it is a new module family (`moduleKey: "restaurante"`) that reuses the core Orken engine (auth, multi-tenant, products, stock, sales, cash, customers) and adds restaurant-specific domain on top.

**Product promise to the owner:**
> "I don't need to guess anymore. I know what sells, what costs, and where my money goes."

**Target users:** restaurant owners, bar/pub owners, waiters, kitchen staff, cashiers, delivery operators.

**Store types covered:** `restaurant` | `bar` | `pub` (configured per-store, same codebase).

---

## 2. Scope

### In Scope (v1 — Operational Core)

- Multi-area floor plan with visual table management
- Command (tab/comanda) lifecycle: open → items → kitchen → payment
- Kitchen Display System (KDS) with SignalR real-time + polling fallback
- Product modifiers (flat price adjustment only)
- Couvert and service fee: admin-defined, explicit in payment breakdown
- Multiple order history per table (operational rule: max 1 active per table)
- Order types: DineIn, Counter, Takeaway
- Recipe cards (ficha técnica) with CMV calculation and ingredient stock deduction
- Multi-store isolation via StoreId on all restaurante entities
- Waiter assignment at command open; visible only to assigned waiter (configurable)
- Split bill suggestion (party size → suggested per-person amount)
- FoodServiceSettings per store (lean config only)

### In Scope (v2 — Channels + Bar/Pub)

- Delivery order intake (manual channel tracking: iFood, WhatsApp, Rappi, own)
- Bar/pub event registration and performance tracking
- Reservation calendar
- QR code / digital menu ordering

### In Scope (v3 — Integrations + Intelligence)

- iFood Partner API (order webhook, menu sync, status callbacks)
- WhatsApp Business API (order flow)
- Menu Engineering classification (Star / Plow Horse / Puzzle / Dog)
- CMV alerts and cost increase notifications
- Channel profitability dashboard

### Out of Scope (all versions)

- Fiscal emission (NF-e, SAT, MFE) — separate compliance module
- Inventory purchasing for food ingredients (uses existing Compras module)
- Loyalty / points programs
- Table reservations with deposit payment

---

## 3. Architectural Decisions

### 3.1 Reuse vs Create

| Component | Decision | Rationale |
|-----------|----------|-----------|
| Auth / session / roles | **Reuse** | Works; includes module array |
| Multi-tenant isolation | **Reuse** | Add StoreId to restaurante entities |
| Product catalog | **Reuse** | Restaurant items are Products |
| Stock engine | **Reuse** | Ingredient deduction already implemented |
| Sales / cash engine | **Reuse** | Order.Pay → SaleService.ConfirmAsync |
| Customer model | **Reuse** | Optional on comanda |
| ProductModifier | **Create** | Does not exist in current model |
| FoodServiceSettings | **Create** | Per-store operational config |
| RestDeliveryOrder | **Create (v2)** | Delivery channel intake |
| RestEvent | **Create (v2)** | Bar/pub events |
| SignalR Hub | **Create** | Real-time KDS |

### 3.2 RestTable ↔ RestOrder: Historical N:1

A table has **many orders over its lifetime** but at most **one active order** at any given time.

- `RestOrder` has `TableId` (nullable — null for Counter and Takeaway orders)
- Active = status NOT IN (`Closed`, `Paid`, `Cancelled`)
- Operational enforcement: partial unique index `(tenant_id, store_id, table_id) WHERE status NOT IN ('Closed','Paid','Cancelled')`
- Application guard: `OrderService.OpenAsync` checks for existing active order before opening
- Table status `Occupied` is set by `OrderService.OpenAsync`; `Available` restored on `PayAsync` or `CancelAsync`
- History queries: `GET /api/restaurante/tables/{id}/orders` returns all historical orders

### 3.3 Multi-Store Isolation

All restaurante entities must carry `StoreId`. This requires:

1. Migration adding `store_id UUID NOT NULL` to **`rest_areas`, `rest_tables`, `rest_orders`, `rest_recipe_cards`**
2. Global Query Filters updated: `WHERE tenant_id = {tenantId} AND store_id = {storeId}`
3. All services receive `ICurrentStore` (already exists in core) alongside `ICurrentTenant`
4. Existing data migration: assign to the primary/first active store of each tenant

`rest_recipe_cards` joins the migration because recipes are per-store: a bar's caipirinha recipe is independent from a sister restaurant's recipe for the same product.

### 3.4 Order Types

```
DineIn    — table required, standard restaurant flow
Counter   — no table, like a bar counter or fast counter (no floor plan)
Takeaway  — no table, customer picks up (v1: manual intake)
Delivery  — no table, external channel (v2: manual; v3: API integration)
```

`TableId` is nullable. Required only for `DineIn`.  
`PartySize` is nullable. **Required only when the operational rule demands it:**
- `CouvertEnabled = true AND CouvertAutomatic = true` → required at order open (422 if missing)
- `CouvertEnabled = true AND CouvertAutomatic = false` → required before payment (waiter sets at payment step)
- `CouvertEnabled = false` → fully optional; can be set for split suggestion only

### 3.5 Payment Breakdown — Official Formula

When closing/paying a comanda, the summary is always:

```
Items Subtotal  = Σ (item.Quantity × item.UnitPrice)
                  + Σ (modifier.PriceAdjustment × item.Quantity)   ← per each item's modifiers

Couvert         = settings.CouvertPricePerPerson × order.PartySize
                  (0 if CouvertEnabled = false OR PartySize = null)

Service Fee     = Items Subtotal × (settings.ServiceFeePercent / 100)
                  (0 if ServiceFeeEnabled = false)
                  ← applied to Items Subtotal ONLY, never to Couvert

Total           = Items Subtotal + Couvert + Service Fee
```

Payment screen always shows all four lines, even when Couvert and Service Fee are zero (displayed as "—" or hidden if both disabled):

```
Subtotal dos itens         R$ 120,00
Couvert (4 pessoas)        R$  20,00
Taxa de serviço (10%)      R$  14,00
──────────────────────────────────────
Total                      R$ 154,00
```

Rules:
- Service fee base is **Items Subtotal only** — couvert is excluded
- `CouvertAmount` and `ServiceFeeAmount` are stored on `RestOrder` and passed to `SaleService.ConfirmAsync` as part of the total
- Admin defines both in FoodServiceSettings; waiter can view but not override
- When `CouvertAutomatic = false`: waiter sets party size at payment step; system recalculates

### 3.6 SignalR + Polling Fallback

This is split into two explicit checkpoints: backend infrastructure and frontend client.

**Checkpoint B-14 — Backend (Hub infrastructure):**
- `RestaurantHub` registered at `/hubs/restaurant`
- `IRestaurantNotificationService` abstraction injected into `OrderService`
- All `OrderService` mutations emit events after database commit
- Groups: `$"store:{tenantId}:{storeId}"`
- Railway: single instance is sufficient for v1; Redis backplane documented for scale

**Checkpoint F-05 — Frontend (Client + fallback):**
- `useKitchenSocket` hook manages connection lifecycle
- Primary: SignalR WebSocket, auto-reconnect with exponential backoff (3 retries)
- Fallback: activated silently after 3 failed retries → `useQuery` with `refetchInterval: 10_000`
- On reconnect: polling stops, SignalR resumes, full query invalidation fires
- UI indicator: green dot "Tempo real" / amber dot "Atualizando (10s)" in KitchenHeader
- Fallback is transparent to the user — kitchen is never stuck, no manual refresh needed

The two checkpoints are independent: backend hub can be shipped and tested before the frontend client exists (test via Postman/WebSocket client).

---

## 4. Domain Model

### 4.1 Entities (new)

#### `ProductModifierGroup`
```
Id               UUID PK
TenantId         UUID FK → Tenant
ProductId        UUID FK → Product (indexed)
Name             VARCHAR(100)   — "Ponto da carne", "Adicionais"
IsRequired       BOOLEAN        — must select at least one option
MaxSelections    SMALLINT       — 1 = radio, N = multi-select
SortOrder        SMALLINT
IsActive         BOOLEAN
```

#### `ProductModifier`
```
Id               UUID PK
TenantId         UUID FK → Tenant
GroupId          UUID FK → ProductModifierGroup (cascade delete)
Name             VARCHAR(100)   — "Ao ponto", "Sem cebola", "Extra queijo"
PriceAdjustment  NUMERIC(18,2)  — flat delta only: +2.50, 0, -1.00
SortOrder        SMALLINT
IsActive         BOOLEAN
```
No conditional logic. No combo dependencies. Flat adjustment only (including zero).

#### `RestOrderItemModifier` (snapshot)
```
Id               UUID PK
TenantId         UUID FK → Tenant
OrderItemId      UUID FK → RestOrderItem (cascade delete)
ModifierId       UUID FK → ProductModifier
LabelSnapshot    VARCHAR(100)   — name at time of order
PriceSnapshot    NUMERIC(18,2)  — price at time of order
```
Snapshots prevent historical data corruption when modifier prices change.

#### `FoodServiceSettings` (lean)
```
Id                     UUID PK
TenantId               UUID FK → Tenant
StoreId                UUID FK → Store UNIQUE
StoreType              VARCHAR(20)    — "restaurant" | "bar" | "pub"

CouvertEnabled         BOOLEAN DEFAULT false
CouvertPricePerPerson  NUMERIC(18,2) NULLABLE
CouvertAutomatic       BOOLEAN DEFAULT false  — true: auto-add on open; false: manual

ServiceFeeEnabled      BOOLEAN DEFAULT false
ServiceFeePercent      NUMERIC(5,2) NULLABLE  — e.g. 10.00 for 10%

OrderTypesEnabled      VARCHAR[]     — ["DineIn","Counter","Takeaway"]

CreatedAt              TIMESTAMPTZ
UpdatedAt              TIMESTAMPTZ
```

Only operational switches. No per-item overrides. No complex pricing logic.

### 4.2 Entities (modified)

#### `RestArea` — add StoreId
```
+ StoreId   UUID FK → Store NOT NULL
```
Index: `(tenant_id, store_id, name)` UNIQUE

#### `RestTable` — add StoreId
```
+ StoreId   UUID FK → Store NOT NULL
```
Index: `(tenant_id, store_id, number)` UNIQUE

#### `RestOrder` — add StoreId + new fields
```
+ StoreId        UUID FK → Store NOT NULL
+ OrderType      VARCHAR(20) NOT NULL DEFAULT 'DineIn'
+ PartySize      SMALLINT NULLABLE
+ CouvertAmount  NUMERIC(18,2) NOT NULL DEFAULT 0
+ ServiceFeeAmount NUMERIC(18,2) NOT NULL DEFAULT 0
  TableId        nullable (was NOT NULL before)
```

`Total` on RestOrder = `Subtotal + CouvertAmount + ServiceFeeAmount`

The partial unique index is updated to include StoreId:
```sql
UNIQUE (tenant_id, store_id, table_id)
WHERE status NOT IN ('Closed', 'Paid', 'Cancelled')
```

#### `RestRecipeCard` — add StoreId
```
+ StoreId   UUID FK → Store NOT NULL
```
Index: `(tenant_id, store_id, product_id)` UNIQUE — one recipe per product **per store**.  
This replaces the previous `(tenant_id, product_id)` unique index.

#### `RestOrderItem` — add modifiers navigation
```
+ Navigation: IReadOnlyList<RestOrderItemModifier> Modifiers
  Total = (Quantity × UnitPrice) + Σ(modifier.PriceAdjustment × Quantity)
```

### 4.3 Relationship Diagram (v1)

```
Tenant
  └── Store (N)
        ├── FoodServiceSettings (1:1)
        ├── RestArea (N)
        │     └── RestTable (N)
        │           └── RestOrder (N, historical)  ← ONE active at most
        │                 └── RestOrderItem (N)
        │                       └── RestOrderItemModifier (N)
        └── RestOrder (N) [Counter/Takeaway: no table]

Product
  └── ProductModifierGroup (N)
        └── ProductModifier (N)
              └── RestOrderItemModifier (N) [snapshot]

Store (N)
  └── RestRecipeCard (N, scoped per store, 1:1 with Product within a store)
        └── RestRecipeIngredient (N)
```

---

## 5. API Design

### 5.1 New Endpoints

```
# Settings
GET    /api/restaurante/settings
PUT    /api/restaurante/settings

# Modifier Groups (product-scoped)
GET    /api/restaurante/modifier-groups?productId={id}
POST   /api/restaurante/modifier-groups
PUT    /api/restaurante/modifier-groups/{id}
POST   /api/restaurante/modifier-groups/{id}/modifiers
PUT    /api/restaurante/modifier-groups/{id}/modifiers/{modId}
DELETE /api/restaurante/modifier-groups/{id}/modifiers/{modId}

# Table order history
GET    /api/restaurante/tables/{id}/orders

# SignalR Hub
WS     /hubs/restaurant
```

### 5.2 Modified Endpoints

```
# Open order — add orderType, partySize; tableId becomes nullable
POST /api/restaurante/orders
{
  "tableId": "uuid | null",
  "orderType": "DineIn | Counter | Takeaway",
  "partySize": 4,
  "customerId": "uuid | null",
  "notes": "string | null"
}
→ applies couvert automatically if CouvertAutomatic = true
→ sets CouvertAmount = settings.CouvertPricePerPerson × partySize

# Add item — add modifiers
POST /api/restaurante/orders/{id}/items
{
  "productId": "uuid",
  "quantity": 2,
  "notes": "string | null",
  "modifiers": [
    { "modifierId": "uuid" },
    { "modifierId": "uuid" }
  ]
}
→ validates required groups have a selection
→ snapshots label and price of each modifier
→ item.Total = (qty × unitPrice) + Σ(modifier.priceAdjustment × qty)

# Pay order — expose full breakdown
POST /api/restaurante/orders/{id}/pay
{
  "payments": [{ "method": "Cash", "type": "Cash", "amount": 160.00 }],
  "partySize": 4
}
→ backend recalculates: serviceFeAmount = subtotal × (settings.ServiceFeePercent / 100)
→ validates payments sum ≥ (subtotal + couvertAmount + serviceFeeAmount)
→ passes total to SaleService.ConfirmAsync
```

### 5.3 SignalR Hub Contract

```
// Client → Server
JoinStore(storeId: string): void
LeaveStore(storeId: string): void

// Server → Client (group: "store:{tenantId}:{storeId}")
OrderItemStatusChanged(orderId: string, itemId: string, newStatus: string): void
NewItemAdded(orderId: string, item: OrderItemDto): void
OrderStatusChanged(orderId: string, newStatus: string): void
TableStatusChanged(tableId: string, newStatus: string): void
```

The hub emits events after every `OrderService` mutation. No client-to-server mutations through the hub (REST only).

---

## 6. Frontend Design

### 6.1 Layouts

**`WaiterLayout`** — used for FloorPage and OrderPage
- Mobile-first (375px minimum)
- Minimal top bar: store name + waiter name + back
- No sidebar — full-screen for operational speed
- Touch targets ≥ 44px
- Bottom action bar for primary actions (open order, add item, pay)

**`KitchenLayout`** — used for KitchenPage
- Full-screen, no nav
- High-contrast dark background
- Large font (≥ 18px for item names)
- Landscape-optimized for tablets
- Auto-fullscreen prompt on first visit
- Connection status indicator (SignalR health)

### 6.2 Pages

#### `FloorPage` (`/restaurante`)
- Area tabs at top (filter by section)
- Responsive grid of TableCards
- TableCard states: available (muted), occupied (primary), reserved (amber), maintenance (red)
- Occupied card shows: table number + order number + time open + item count

**Navigation rule — no heavy modal on the critical path:**
- **Tap occupied table** → navigate immediately to `OrderPage` (the active order for that table)
- **Tap available table** → open a lightweight bottom sheet (not a dialog) with 3 fields only: order type (DineIn/Counter/Takeaway as chips), party size (optional numeric input), confirm button — on submit: creates order, navigates to `OrderPage` in the same action
- "+ Balcão" button in header → same lightweight bottom sheet, table pre-set to null (Counter type)
- **No intermediate state** — the floor plan should never feel like an admin form

Refresh on SignalR `TableStatusChanged` event

#### `OrderPage` (`/restaurante/mesa/:tableId` or `/restaurante/comanda/:orderId`)
- Header: table number + order number + time open + party size
- Scrollable item list with inline kitchen status badge
- Each item: name + modifiers + quantity + total + status chip
- FAB or bottom bar: "Adicionar item" → opens AddItemDrawer
- Bottom: Totals summary (subtotal + couvert + service fee + total)
- "Fechar conta" CTA → opens PaymentDrawer
- "Cancelar comanda" (danger, requires confirmation)

#### `AddItemDrawer` (bottom sheet, mobile-friendly)
- PosProductSearch reused (product + stock)
- On product select: shows ModifierSelector if product has modifier groups
- ModifierSelector: required groups show error if not selected
- Quantity input + notes field
- "Adicionar" confirms and sends to kitchen (status: Pending)

#### `PaymentDrawer` (bottom sheet)
- Breakdown display:
  ```
  Subtotal             R$ 120,00
  Couvert (4 pessoas)  R$  20,00
  Taxa de serviço 10%  R$  14,00
  ─────────────────────────────
  Total                R$ 154,00
  ```
- Split suggestion: "Sugestão: R$ 38,50 por pessoa" (if partySize set)
- PosPaymentPanel reused for method selection + amount input + change
- "Pagar" → `POST /orders/{id}/pay` → success → clears order, table → Available

#### `KitchenPage` (`/restaurante/cozinha`)
- Three columns: **Pendente** | **Preparando** | **Pronto**
- Each column shows KitchenCards
- KitchenCard: table/order number + item name + modifiers + notes + elapsed time
- Elapsed time indicator: green <5min, amber 5–10min, red >10min
- Tap card to advance status (Pending → Preparing → Ready → Delivered)
- Connection status: green "Tempo real" / amber "Polling (10s)"

### 6.3 Key Hooks

```typescript
// useKitchenSocket.ts
// Manages SignalR connection with polling fallback
{
  items: OrderItemDto[];         // kitchen-relevant items (Pending + Preparing + Ready)
  connectionMode: "realtime" | "polling";
  updateItemStatus(itemId, status): void;  // optimistic + API call
}

// useActiveOrder.ts
// Fetches the active (non-closed) order for a table
useQuery(["orders", "active", tableId])

// useOpenOrder.ts
// Mutation: open a new order
useMutation → POST /restaurante/orders → invalidates ["tables"], ["orders"]

// useAddItem.ts
// Mutation: add item with modifiers
useMutation → POST /restaurante/orders/{id}/items → invalidates ["orders", orderId], emits via SignalR

// usePayOrder.ts
// Mutation: pay comanda
useMutation → POST /restaurante/orders/{id}/pay → invalidates CASH_KEY, STOCK_KEY, ["tables"], ["orders"], ["sales"]

// useFoodSettings.ts
// Fetches FoodServiceSettings for current store
useQuery(["food-settings", storeId])
```

### 6.4 Dashboard Integration — No Fake Placeholders

The two dashboard blocks ("Mesas abertas" and "Pedidos na cozinha") follow the same empty-state pattern used in the existing dashboard:

- Block is **only visible** when `session.modules.includes("restaurante")` — no empty block shown to non-restaurant tenants
- When data is zero (no open tables, no kitchen items): show empty state with meaningful message, **not** "0 mesas abertas" as if it were a KPI
  - "Nenhuma mesa aberta agora." / "Tudo em ordem na cozinha."
- Loading state: skeleton, not spinner
- No static/hardcoded numbers — blocks are not rendered until real query resolves

### 6.5 Query Keys

```typescript
["food-settings", storeId]
["tables", storeId]
["tables", storeId, "active"]
["areas", storeId]
["orders", storeId]
["orders", "active", tableId]
["orders", orderId]
["kitchen-items", storeId]
["modifier-groups", productId]
```

---

## 7. Execution Phases

### Phase 1 — Backend Foundation (Week 1–2)

1. **StoreId migration** — `rest_areas`, `rest_tables`, `rest_orders` + update query filters + update services
2. **`ProductModifierGroup` + `ProductModifier` + `RestOrderItemModifier`** domain, migration, configuration
3. **`FoodServiceSettings`** entity, migration, service, controller (`GET`, `PUT`)
4. **`RestOrder` schema update** — `OrderType`, `PartySize`, `CouvertAmount`, `ServiceFeeAmount`, nullable `TableId`
5. **`RestOrderItem.Total` recalculation** — include modifier price adjustments
6. **Update `OrderService`** — couvert auto-apply on open, modifier validation on add, service fee on pay
7. **`ModifierGroupService` + `ModifierGroupsController`**
8. **Update `TablesController`** — `GET /tables/{id}/orders` for history
9. **SignalR Hub** — basic infrastructure, JoinStore/LeaveStore, event emission from OrderService
10. **Update integration tests** — cover modifier validation, couvert calculation, store isolation

### Phase 2 — Frontend Core (Week 3–5)

11. **`src/modules/restaurante/types/index.ts`** — all DTOs
12. **API clients** — tables, areas, orders, modifiers, settings
13. **Base hooks** — useTables, useAreas, useActiveOrder, useFoodSettings, useOrderMutations
14. **`FloorPage`** — TableCard, AreaTabs, open order modal
15. **`OrderPage`** — OrderPanel, OrderItemRow, AddItemDrawer, ModifierSelector
16. **`PaymentDrawer`** — breakdown (subtotal + couvert + service fee), split suggestion, payment
17. **`useKitchenSocket`** — SignalR + polling fallback
18. **`KitchenPage`** — KitchenBoard (3-column kanban), KitchenCard, connection indicator
19. **Routing** — WaiterLayout, KitchenLayout, module gate (`moduleKey: "restaurante"`)
20. **Sidebar entry** — "Restaurante" in nav, visible when module active
21. **Dashboard integration** — "Mesas abertas" + "Pedidos na cozinha" blocks

### Phase 3 — Intelligence + Bar/Pub + Delivery Manual (Week 6–8)

**CMV and recipes ship before events** — they deliver immediate owner value and complete the stock/cost loop opened by recipe cards in Phase 1.

22. **`RecipesPage`** — recipe card management in main app layout (list, create/edit, add ingredients, view CMV per dish)
23. **CMV dashboard blocks** — top 5 dishes by CMV%, alert when CMV exceeds configurable threshold; uses data already tracked in StockMovement.CostPriceSnapshot
24. **FoodServiceSettings UI** — admin config page for couvert, service fee, store type, order types enabled
25. **Couvert manual flow** — when `CouvertAutomatic = false`, waiter sets party size in PaymentDrawer; system recalculates breakdown before confirming
26. **`RestDeliveryOrder`** entity + service + controller
27. **`DeliveryPage`** — manual order intake, channel tracking (iFood / WhatsApp / Rappi / próprio)
28. **`RestEvent`** entity + service + controller
29. **`EventsPage`** — cadastrar show/atração, custo da atração, faturamento no período vs custo

### Phase 4 — External Integrations (Week 9–14, parallel track)

30. **iFood Partner API** — requires external registration (~2 weeks); webhook ingestion, menu sync
31. **WhatsApp Business API** — requires Meta approval; structured order flow
32. **Menu Engineering dashboard** — Star/Plow Horse/Puzzle/Dog classification
33. **Channel profitability** — revenue by channel minus fees minus packaging

---

## 8. Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| iFood Partner registration takes weeks | High | High | Start process immediately; v1 works without it |
| WhatsApp Business API Meta approval | High | Medium | Fallback: manual WhatsApp intake in v2 |
| StoreId migration on existing prod data | Low (new module) | High | Migration script assigns primary store as default |
| SignalR on Railway with multiple instances | Medium | High | Single instance for now; Redis backplane spec ready for scale |
| Modifier scope creep beyond v1 spec | Medium | Medium | Hard rule: flat price adjustment only in v1; complex modifier logic is v2 |
| Kitchen tablet performance on low-end hardware | Medium | Medium | KitchenPage avoids heavy re-renders; virtual list if >50 items |
| Service fee rounding on split | Low | Low | Always round to 2 decimal places; last payer absorbs rounding diff |

---

## 9. Non-Goals (explicit)

- No NF-e / fiscal document emission
- No multi-currency
- No loyalty points or discount vouchers
- No table reservations with deposit
- No ingredient purchasing flow (uses existing Compras module)
- No complex modifier logic (combos, conditional selections) in v1
- No offline mode / PWA
- No native mobile app (responsive web only)

---

## 10. Open Questions (resolved)

| Question | Decision |
|----------|----------|
| 1 or N orders per table? | N historical; max 1 active enforced by partial unique index |
| Couvert per person or per table? | Per person × party size |
| Couvert mandatory or optional? | Optional, admin-configured, auto or manual |
| Service fee base? | Applies to subtotal only (not to couvert) |
| Modifier complexity v1? | Flat price adjustment only |
| Multi-store in v1? | Yes — StoreId on all restaurante entities |
| KDS real-time or polling? | SignalR primary, 10s polling fallback |
| OrderType Counter? | Yes — balcão, no table required |
| Split bill? | Suggested amount only; actual payment is flexible sum |
| Waiter assignment? | Assigned at order open; WaiterId = opener |
| Delivery v1? | Manual intake with channel tracking |

---

## 11. Definition of Done

The v1 is complete when the following flow works end-to-end in production:

1. Admin opens FoodServiceSettings, enables 10% service fee and R$ 8,00 couvert per person
2. Waiter opens FloorPage, sees table map, taps an available table
3. Waiter opens a DineIn comanda for 4 people — couvert R$ 32,00 applied automatically
4. Waiter adds "Frango Grelhado" (modifier: "Bem passado"), quantity 2
5. Kitchen receives the item in real time on KitchenPage
6. Kitchen marks item as "Preparing", then "Ready"
7. Waiter sees status update inline on OrderPage
8. Waiter taps "Fechar conta":
   - Subtotal: R$ 68,00
   - Couvert: R$ 32,00
   - Taxa de serviço 10%: R$ 6,80
   - Total: R$ 106,80
9. Waiter registers payment (cash R$ 120,00 → troco R$ 13,20)
10. Table returns to Available; sale and stock movement recorded in Orken core
