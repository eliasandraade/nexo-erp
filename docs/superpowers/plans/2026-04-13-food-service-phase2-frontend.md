# Orken Food Service — Phase 2: Frontend Core

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the complete React frontend for Food Service — types, API clients, hooks, FloorPage, OrderPage, AddItemDrawer with ModifierSelector, PaymentDrawer, KitchenPage with SignalR+polling fallback, and dashboard blocks.

**Architecture:** New module at `nexo-main/src/modules/restaurante/`. WaiterLayout is mobile-first (375px min, no sidebar). KitchenLayout is full-screen dark for tablets. FloorPage uses a lightweight bottom sheet (no heavy modal) for opening orders — tap available table → 3-field sheet → create + navigate immediately. All hooks use TanStack Query v5 with query keys from the spec. useKitchenSocket manages SignalR with transparent 10s polling fallback.

**Tech Stack:** React, TypeScript, Vite, TailwindCSS, shadcn/ui, TanStack Query v5, `@microsoft/signalr`, lucide-react, react-router-dom v6

**Prerequisite:** Phase 1 backend must be deployed and accessible. `@microsoft/signalr` must be installed.

---

## File Map

### New Files
| File | Purpose |
|------|---------|
| `src/modules/restaurante/types/index.ts` | All DTOs mirroring backend |
| `src/modules/restaurante/api/restaurante.api.ts` | All HTTP API functions |
| `src/modules/restaurante/hooks/useRestauranteTables.ts` | Tables query |
| `src/modules/restaurante/hooks/useRestauranteAreas.ts` | Areas query |
| `src/modules/restaurante/hooks/useActiveOrder.ts` | Active order for a table |
| `src/modules/restaurante/hooks/useFoodSettings.ts` | FoodServiceSettings query |
| `src/modules/restaurante/hooks/useModifierGroups.ts` | Modifier groups for a product |
| `src/modules/restaurante/hooks/useOrderMutations.ts` | Open/addItem/pay/cancel mutations |
| `src/modules/restaurante/hooks/useKitchenSocket.ts` | SignalR + polling fallback (CHECKPOINT) |
| `src/modules/restaurante/hooks/useKitchenItems.ts` | Kitchen items query (polling path) |
| `src/app/layouts/WaiterLayout.tsx` | Mobile-first layout for floor + order pages |
| `src/app/layouts/KitchenLayout.tsx` | Full-screen dark layout for kitchen |
| `src/modules/restaurante/components/TableCard.tsx` | Single table tile |
| `src/modules/restaurante/components/AreaTabs.tsx` | Area filter tabs |
| `src/modules/restaurante/components/OpenOrderSheet.tsx` | Lightweight bottom sheet to open order |
| `src/modules/restaurante/components/OrderItemRow.tsx` | Single order item row |
| `src/modules/restaurante/components/ModifierSelector.tsx` | Modifier group selector inside AddItemDrawer |
| `src/modules/restaurante/components/AddItemDrawer.tsx` | Bottom sheet to add an item |
| `src/modules/restaurante/components/PaymentDrawer.tsx` | Payment breakdown + PosPaymentPanel |
| `src/modules/restaurante/components/KitchenCard.tsx` | Single kitchen item card |
| `src/modules/restaurante/components/KitchenBoard.tsx` | 3-column kanban board |
| `src/modules/restaurante/components/KitchenConnectionBadge.tsx` | Green/amber realtime indicator |
| `src/modules/restaurante/pages/FloorPage.tsx` | Table map page |
| `src/modules/restaurante/pages/OrderPage.tsx` | Active order page |
| `src/modules/restaurante/pages/KitchenPage.tsx` | Kitchen display page |
| `src/modules/dashboard/components/RestauranteBlocks.tsx` | Open tables + kitchen status blocks |

### Modified Files
| File | Change |
|------|--------|
| `src/app/router/routes.ts` | Add restaurante route entry |
| `src/app/router/AppRouter.tsx` | Add restaurante routes under ModuleRoute |
| `src/modules/dashboard/pages/DashboardPage.tsx` | Render RestauranteBlocks when module active |

---

## Task F-01: Types — All DTOs

**Files:**
- Create: `nexo-main/src/modules/restaurante/types/index.ts`

- [ ] **Step 1: Install @microsoft/signalr**
```bash
cd nexo-main
npm install @microsoft/signalr
```
Expected: package added, no peer dep errors.

- [ ] **Step 2: Create types/index.ts**

```typescript
// nexo-main/src/modules/restaurante/types/index.ts

// ── Settings ──────────────────────────────────────────────────────────────────
export interface FoodServiceSettingsDto {
  id: string;
  storeType: "restaurant" | "bar" | "pub";
  couvertEnabled: boolean;
  couvertPricePerPerson: number | null;
  couvertAutomatic: boolean;
  serviceFeeEnabled: boolean;
  serviceFeePercent: number | null;
  orderTypesEnabled: string; // "DineIn,Counter,Takeaway"
}

export interface UpdateFoodServiceSettingsRequest {
  storeType: string;
  couvertEnabled: boolean;
  couvertPricePerPerson: number | null;
  couvertAutomatic: boolean;
  serviceFeeEnabled: boolean;
  serviceFeePercent: number | null;
  orderTypesEnabled: string;
}

// ── Areas ─────────────────────────────────────────────────────────────────────
export interface AreaDto {
  id: string;
  name: string;
  description: string | null;
  isActive: boolean;
  tableCount: number;
}

// ── Tables ────────────────────────────────────────────────────────────────────
export type TableStatus = "Available" | "Occupied" | "Reserved" | "Maintenance";

export interface TableDto {
  id: string;
  areaId: string;
  areaName: string;
  number: string;
  capacity: number;
  status: TableStatus;
  isActive: boolean;
}

// ── Orders ────────────────────────────────────────────────────────────────────
export type OrderType = "DineIn" | "Counter" | "Takeaway" | "Delivery";
export type OrderStatus =
  | "Open"
  | "InPreparation"
  | "Ready"
  | "Closed"
  | "Paid"
  | "Cancelled";
export type OrderItemStatus =
  | "Pending"
  | "Preparing"
  | "Ready"
  | "Delivered"
  | "Cancelled";

export interface OrderItemModifierDto {
  modifierId: string;
  labelSnapshot: string;
  priceSnapshot: number;
}

export interface OrderItemDto {
  id: string;
  productId: string;
  productName: string;
  quantity: number;
  unitPrice: number;
  total: number;
  status: OrderItemStatus;
  notes: string | null;
  modifiers: OrderItemModifierDto[];
  sentToKitchenAt: string | null;
  preparedAt: string | null;
  deliveredAt: string | null;
  cancelledAt: string | null;
}

export interface OrderDto {
  id: string;
  orderNumber: number;
  status: OrderStatus;
  orderType: OrderType;
  tableId: string | null;
  tableNumber: string | null;
  partySize: number | null;
  waiterId: string;
  customerId: string | null;
  saleId: string | null;
  itemsSubtotal: number;
  couvertAmount: number;
  serviceFeeAmount: number;
  total: number;
  notes: string | null;
  openedAt: string;
  closedAt: string | null;
  cancelledAt: string | null;
  items: OrderItemDto[];
}

// ── Order requests ────────────────────────────────────────────────────────────
export interface OpenOrderRequest {
  orderType: OrderType;
  tableId?: string | null;
  partySize?: number | null;
  customerId?: string | null;
  notes?: string | null;
}

export interface ApplyModifierRequest {
  modifierId: string;
}

export interface AddOrderItemRequest {
  productId: string;
  quantity: number;
  notes?: string | null;
  modifiers?: ApplyModifierRequest[];
}

export interface PaymentInputDto {
  method: string; // "Cash" | "Pix" | "Debit" | "Credit"
  type: string;   // "Cash" | "Credit"
  amount: number;
  dueDate?: string | null;
}

export interface PayOrderRequest {
  payments: PaymentInputDto[];
  partySize?: number | null;
}

// ── Modifiers ─────────────────────────────────────────────────────────────────
export interface ModifierDto {
  id: string;
  name: string;
  priceAdjustment: number;
  sortOrder: number;
  isActive: boolean;
}

export interface ModifierGroupDto {
  id: string;
  productId: string;
  name: string;
  isRequired: boolean;
  maxSelections: number;
  sortOrder: number;
  isActive: boolean;
  modifiers: ModifierDto[];
}

// ── Kitchen (SignalR events) ───────────────────────────────────────────────────
export type ConnectionMode = "realtime" | "polling";

export interface KitchenItem {
  orderId: string;
  orderNumber: number;
  tableNumber: string | null;
  orderType: OrderType;
  itemId: string;
  productName: string;
  quantity: number;
  notes: string | null;
  modifiers: OrderItemModifierDto[];
  status: OrderItemStatus;
  sentToKitchenAt: string | null;
}
```

- [ ] **Step 3: Commit**
```bash
cd nexo-main
git add src/modules/restaurante/types/index.ts
git commit -m "feat(restaurante): add frontend types mirroring backend DTOs"
```

---

## Task F-02: API Client

**Files:**
- Create: `nexo-main/src/modules/restaurante/api/restaurante.api.ts`

- [ ] **Step 1: Create restaurante.api.ts**

```typescript
// nexo-main/src/modules/restaurante/api/restaurante.api.ts
import { apiClient } from "@/services/api-client";
import type {
  AreaDto, TableDto, OrderDto, OpenOrderRequest,
  AddOrderItemRequest, PayOrderRequest, FoodServiceSettingsDto,
  UpdateFoodServiceSettingsRequest, ModifierGroupDto,
} from "../types";

// ── Settings ──────────────────────────────────────────────────────────────────
export const getFoodSettings = (): Promise<FoodServiceSettingsDto> =>
  apiClient.get("/restaurante/settings");

export const updateFoodSettings = (
  req: UpdateFoodServiceSettingsRequest
): Promise<FoodServiceSettingsDto> =>
  apiClient.put("/restaurante/settings", req);

// ── Areas ─────────────────────────────────────────────────────────────────────
export const listAreas = (): Promise<AreaDto[]> =>
  apiClient.get("/restaurante/areas");

// ── Tables ────────────────────────────────────────────────────────────────────
export const listTables = (): Promise<TableDto[]> =>
  apiClient.get("/restaurante/tables");

export const getTableOrders = (tableId: string): Promise<OrderDto[]> =>
  apiClient.get(`/restaurante/tables/${tableId}/orders`);

// ── Orders ────────────────────────────────────────────────────────────────────
export const listOrders = (): Promise<OrderDto[]> =>
  apiClient.get("/restaurante/orders");

export const getOrder = (orderId: string): Promise<OrderDto> =>
  apiClient.get(`/restaurante/orders/${orderId}`);

export const openOrder = (req: OpenOrderRequest): Promise<OrderDto> =>
  apiClient.post("/restaurante/orders", req);

export const addOrderItem = (
  orderId: string,
  req: AddOrderItemRequest
): Promise<OrderDto> =>
  apiClient.post(`/restaurante/orders/${orderId}/items`, req);

export const updateItemStatus = (
  orderId: string,
  itemId: string,
  status: string
): Promise<OrderDto> =>
  apiClient.patch(`/restaurante/orders/${orderId}/items/${itemId}/status`, { status });

export const closeOrder = (orderId: string): Promise<{ orderId: string; saleId: string; total: number; message: string }> =>
  apiClient.post(`/restaurante/orders/${orderId}/close`, {});

export const payOrder = (
  orderId: string,
  req: PayOrderRequest
): Promise<OrderDto> =>
  apiClient.post(`/restaurante/orders/${orderId}/pay`, req);

export const cancelOrder = (orderId: string): Promise<OrderDto> =>
  apiClient.post(`/restaurante/orders/${orderId}/cancel`, {});

// ── Modifiers ─────────────────────────────────────────────────────────────────
export const getModifierGroups = (productId: string): Promise<ModifierGroupDto[]> =>
  apiClient.get(`/restaurante/modifier-groups?productId=${productId}`);

// ── Kitchen items (polling path) ──────────────────────────────────────────────
/** Returns all orders with Pending/Preparing/Ready items for the kitchen board. */
export const listKitchenOrders = (): Promise<OrderDto[]> =>
  apiClient.get("/restaurante/orders");
```

- [ ] **Step 2: Commit**
```bash
git add src/modules/restaurante/api/restaurante.api.ts
git commit -m "feat(restaurante): add restaurante API client"
```

---

## Task F-03: Base Hooks

**Files:**
- Create: `nexo-main/src/modules/restaurante/hooks/useRestauranteTables.ts`
- Create: `nexo-main/src/modules/restaurante/hooks/useRestauranteAreas.ts`
- Create: `nexo-main/src/modules/restaurante/hooks/useActiveOrder.ts`
- Create: `nexo-main/src/modules/restaurante/hooks/useFoodSettings.ts`
- Create: `nexo-main/src/modules/restaurante/hooks/useModifierGroups.ts`

- [ ] **Step 1: Create base query hooks**

```typescript
// useRestauranteTables.ts
import { useQuery } from "@tanstack/react-query";
import { listTables } from "../api/restaurante.api";

export const TABLES_KEY = (storeId: string) => ["tables", storeId] as const;

export function useRestauranteTables(storeId: string) {
  return useQuery({
    queryKey: TABLES_KEY(storeId),
    queryFn: listTables,
    staleTime: 10_000,
  });
}
```

```typescript
// useRestauranteAreas.ts
import { useQuery } from "@tanstack/react-query";
import { listAreas } from "../api/restaurante.api";

export const AREAS_KEY = (storeId: string) => ["areas", storeId] as const;

export function useRestauranteAreas(storeId: string) {
  return useQuery({
    queryKey: AREAS_KEY(storeId),
    queryFn: listAreas,
    staleTime: 60_000,
  });
}
```

```typescript
// useActiveOrder.ts
import { useQuery } from "@tanstack/react-query";
import { listOrders } from "../api/restaurante.api";
import type { OrderDto } from "../types";

export const ORDERS_KEY = (storeId: string) => ["orders", storeId] as const;
export const ACTIVE_ORDER_KEY = (storeId: string, tableId: string) =>
  ["orders", "active", storeId, tableId] as const;

export function useActiveOrder(storeId: string, tableId: string) {
  return useQuery({
    queryKey: ACTIVE_ORDER_KEY(storeId, tableId),
    queryFn: async (): Promise<OrderDto | null> => {
      const orders = await listOrders();
      return (
        orders.find(
          (o) =>
            o.tableId === tableId &&
            !["Closed", "Paid", "Cancelled"].includes(o.status)
        ) ?? null
      );
    },
    enabled: !!tableId,
  });
}

export function useOrder(storeId: string, orderId: string) {
  return useQuery({
    queryKey: ["orders", storeId, orderId] as const,
    queryFn: () => import("../api/restaurante.api").then((m) => m.getOrder(orderId)),
    enabled: !!orderId,
  });
}
```

```typescript
// useFoodSettings.ts
import { useQuery } from "@tanstack/react-query";
import { getFoodSettings } from "../api/restaurante.api";

export const FOOD_SETTINGS_KEY = (storeId: string) =>
  ["food-settings", storeId] as const;

export function useFoodSettings(storeId: string) {
  return useQuery({
    queryKey: FOOD_SETTINGS_KEY(storeId),
    queryFn: getFoodSettings,
    staleTime: 60_000,
  });
}
```

```typescript
// useModifierGroups.ts
import { useQuery } from "@tanstack/react-query";
import { getModifierGroups } from "../api/restaurante.api";

export function useModifierGroups(productId: string | null) {
  return useQuery({
    queryKey: ["modifier-groups", productId] as const,
    queryFn: () => getModifierGroups(productId!),
    enabled: !!productId,
    staleTime: 30_000,
  });
}
```

- [ ] **Step 2: Commit**
```bash
git add src/modules/restaurante/hooks/
git commit -m "feat(restaurante): add base query hooks for tables, areas, orders, settings, modifiers"
```

---

## Task F-04: Mutation Hooks

**Files:**
- Create: `nexo-main/src/modules/restaurante/hooks/useOrderMutations.ts`

- [ ] **Step 1: Create useOrderMutations.ts**

```typescript
// nexo-main/src/modules/restaurante/hooks/useOrderMutations.ts
import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  openOrder, addOrderItem, closeOrder, payOrder,
  cancelOrder, updateItemStatus,
} from "../api/restaurante.api";
import type { OpenOrderRequest, AddOrderItemRequest, PayOrderRequest } from "../types";
import { TABLES_KEY, ORDERS_KEY } from "./useRestauranteTables";

function useInvalidateRestaurante(storeId: string) {
  const qc = useQueryClient();
  return () => {
    qc.invalidateQueries({ queryKey: TABLES_KEY(storeId) });
    qc.invalidateQueries({ queryKey: ORDERS_KEY(storeId) });
    qc.invalidateQueries({ queryKey: ["kitchen-items", storeId] });
  };
}

export function useOpenOrder(storeId: string) {
  const invalidate = useInvalidateRestaurante(storeId);
  return useMutation({
    mutationFn: (req: OpenOrderRequest) => openOrder(req),
    onSuccess: invalidate,
  });
}

export function useAddItem(storeId: string, orderId: string) {
  const invalidate = useInvalidateRestaurante(storeId);
  return useMutation({
    mutationFn: (req: AddOrderItemRequest) => addOrderItem(orderId, req),
    onSuccess: invalidate,
  });
}

export function useUpdateItemStatus(storeId: string) {
  const invalidate = useInvalidateRestaurante(storeId);
  return useMutation({
    mutationFn: ({ orderId, itemId, status }: { orderId: string; itemId: string; status: string }) =>
      updateItemStatus(orderId, itemId, status),
    onSuccess: invalidate,
  });
}

export function useCloseOrder(storeId: string) {
  const invalidate = useInvalidateRestaurante(storeId);
  return useMutation({
    mutationFn: (orderId: string) => closeOrder(orderId),
    onSuccess: invalidate,
  });
}

export function usePayOrder(storeId: string) {
  const qc = useQueryClient();
  const invalidate = useInvalidateRestaurante(storeId);
  return useMutation({
    mutationFn: ({ orderId, req }: { orderId: string; req: PayOrderRequest }) =>
      payOrder(orderId, req),
    onSuccess: () => {
      invalidate();
      // also invalidate core cash/sales data so dashboard updates
      qc.invalidateQueries({ queryKey: ["sales"] });
      qc.invalidateQueries({ queryKey: ["cash"] });
    },
  });
}

export function useCancelOrder(storeId: string) {
  const invalidate = useInvalidateRestaurante(storeId);
  return useMutation({
    mutationFn: (orderId: string) => cancelOrder(orderId),
    onSuccess: invalidate,
  });
}
```

- [ ] **Step 2: Commit**
```bash
git add src/modules/restaurante/hooks/useOrderMutations.ts
git commit -m "feat(restaurante): add order mutation hooks"
```

---

## Task F-05: useKitchenSocket — SignalR + Polling Fallback (CHECKPOINT ✓)

**Files:**
- Create: `nexo-main/src/modules/restaurante/hooks/useKitchenSocket.ts`
- Create: `nexo-main/src/modules/restaurante/hooks/useKitchenItems.ts`

> **Checkpoint:** This hook is the real-time nerve of the kitchen. Test it against the running backend (B-14 must be done) before building KitchenPage.

- [ ] **Step 1: Create useKitchenItems.ts (polling path)**

```typescript
// useKitchenItems.ts
import { useQuery } from "@tanstack/react-query";
import { listKitchenOrders } from "../api/restaurante.api";
import type { KitchenItem } from "../types";

const KITCHEN_STATUSES = new Set(["Pending", "Preparing", "Ready"]);

export const KITCHEN_KEY = (storeId: string) =>
  ["kitchen-items", storeId] as const;

export function useKitchenItems(storeId: string, refetchInterval?: number) {
  return useQuery({
    queryKey: KITCHEN_KEY(storeId),
    queryFn: async (): Promise<KitchenItem[]> => {
      const orders = await listKitchenOrders();
      const items: KitchenItem[] = [];
      for (const order of orders) {
        for (const item of order.items) {
          if (!KITCHEN_STATUSES.has(item.status)) continue;
          items.push({
            orderId:       order.id,
            orderNumber:   order.orderNumber,
            tableNumber:   order.tableNumber,
            orderType:     order.orderType,
            itemId:        item.id,
            productName:   item.productName,
            quantity:      item.quantity,
            notes:         item.notes,
            modifiers:     item.modifiers,
            status:        item.status,
            sentToKitchenAt: item.sentToKitchenAt,
          });
        }
      }
      return items;
    },
    refetchInterval,
    staleTime: 0,
  });
}
```

- [ ] **Step 2: Create useKitchenSocket.ts**

```typescript
// useKitchenSocket.ts
import { useEffect, useRef, useState, useCallback } from "react";
import * as signalR from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import { KITCHEN_KEY } from "./useKitchenItems";
import { TABLES_KEY } from "./useRestauranteTables";
import { ORDERS_KEY } from "./useRestauranteTables";
import type { ConnectionMode } from "../types";

const RECONNECT_DELAYS = [0, 2000, 5000]; // ms before each retry
const POLLING_INTERVAL = 10_000;
const API_BASE = import.meta.env.VITE_API_URL ?? "http://localhost:5000";

export function useKitchenSocket(storeId: string, token: string | undefined) {
  const qc                      = useQueryClient();
  const connectionRef           = useRef<signalR.HubConnection | null>(null);
  const retryCountRef           = useRef(0);
  const [mode, setMode]         = useState<ConnectionMode>("realtime");
  const pollingTimerRef         = useRef<ReturnType<typeof setInterval> | null>(null);

  const invalidateAll = useCallback(() => {
    qc.invalidateQueries({ queryKey: KITCHEN_KEY(storeId) });
    qc.invalidateQueries({ queryKey: TABLES_KEY(storeId) });
    qc.invalidateQueries({ queryKey: ORDERS_KEY(storeId) });
  }, [qc, storeId]);

  const startPolling = useCallback(() => {
    if (pollingTimerRef.current) return;
    setMode("polling");
    pollingTimerRef.current = setInterval(invalidateAll, POLLING_INTERVAL);
  }, [invalidateAll]);

  const stopPolling = useCallback(() => {
    if (pollingTimerRef.current) {
      clearInterval(pollingTimerRef.current);
      pollingTimerRef.current = null;
    }
  }, []);

  useEffect(() => {
    if (!token || !storeId) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${API_BASE}/hubs/restaurant`, {
        accessTokenFactory: () => token,
      })
      .withAutomaticReconnect(RECONNECT_DELAYS)
      .build();

    connectionRef.current = connection;

    connection.on("NewItemAdded", () => invalidateAll());
    connection.on("OrderItemStatusChanged", () => invalidateAll());
    connection.on("OrderStatusChanged", () => invalidateAll());
    connection.on("TableStatusChanged", () => invalidateAll());

    connection.onreconnecting(() => {
      retryCountRef.current += 1;
      if (retryCountRef.current >= RECONNECT_DELAYS.length) {
        startPolling();
      }
    });

    connection.onreconnected(() => {
      retryCountRef.current = 0;
      stopPolling();
      setMode("realtime");
      invalidateAll(); // full refresh after reconnect
    });

    connection.onclose(() => {
      startPolling();
    });

    connection
      .start()
      .then(() => {
        retryCountRef.current = 0;
        setMode("realtime");
        stopPolling();
        return connection.invoke("JoinStore", storeId);
      })
      .catch(() => {
        startPolling();
      });

    return () => {
      stopPolling();
      connection.stop();
    };
  }, [token, storeId, invalidateAll, startPolling, stopPolling]);

  return { connectionMode: mode };
}
```

- [ ] **Step 3: Manual checkpoint — verify in browser**

Start both backend and frontend. Navigate to `/restaurante/cozinha`. Open browser DevTools → Network → WS tab. Verify:
- WebSocket connection to `/hubs/restaurant` is established
- `JoinStore` message is sent
- When you add an item via `/restaurante/mesa/:tableId`, the kitchen page refreshes without manual reload

Expected console: no SignalR errors, connection status `"Connected"`.

- [ ] **Step 4: Commit**
```bash
git add src/modules/restaurante/hooks/useKitchenSocket.ts
git add src/modules/restaurante/hooks/useKitchenItems.ts
git commit -m "feat(restaurante): add useKitchenSocket with SignalR + 10s polling fallback"
```

---

## Task F-06: Layouts + Routing + Sidebar

**Files:**
- Create: `nexo-main/src/app/layouts/WaiterLayout.tsx`
- Create: `nexo-main/src/app/layouts/KitchenLayout.tsx`
- Modify: `nexo-main/src/app/router/routes.ts`
- Modify: `nexo-main/src/app/router/AppRouter.tsx`

- [ ] **Step 1: Create WaiterLayout**

```tsx
// nexo-main/src/app/layouts/WaiterLayout.tsx
import { Outlet } from "react-router-dom";

/**
 * Mobile-first layout for waiter-facing pages (FloorPage, OrderPage).
 * No sidebar — maximizes screen real estate on phones.
 * Min-width 375px. Touch targets ≥ 44px enforced via Tailwind classes in child components.
 */
export function WaiterLayout() {
  return (
    <div className="min-h-screen bg-background flex flex-col">
      <Outlet />
    </div>
  );
}
```

- [ ] **Step 2: Create KitchenLayout**

```tsx
// nexo-main/src/app/layouts/KitchenLayout.tsx
import { Outlet } from "react-router-dom";

/**
 * Full-screen dark layout for KitchenPage.
 * Landscape-optimized for tablets. No nav chrome.
 * Auto-fullscreen prompt is triggered inside KitchenPage on first visit.
 */
export function KitchenLayout() {
  return (
    <div className="min-h-screen bg-gray-950 text-gray-100 flex flex-col overflow-hidden">
      <Outlet />
    </div>
  );
}
```

- [ ] **Step 3: Add restaurante route to routes.ts**

```typescript
// Add the import at the top:
import { UtensilsCrossed } from "lucide-react";

// Add to appRoutes array, after the PDV entry:
{ path: "/restaurante", label: "Restaurante", icon: UtensilsCrossed, moduleKey: "restaurante" },
```

- [ ] **Step 4: Add restaurante routes to AppRouter.tsx**

Add imports at the top:
```tsx
import { WaiterLayout }  from "@/app/layouts/WaiterLayout";
import { KitchenLayout } from "@/app/layouts/KitchenLayout";
import FloorPage  from "@/modules/restaurante/pages/FloorPage";
import OrderPage  from "@/modules/restaurante/pages/OrderPage";
import KitchenPage from "@/modules/restaurante/pages/KitchenPage";
```

Add routes after the PDV block (before the core routes block):
```tsx
{/* Protected: restaurante — waiter pages use WaiterLayout */}
<Route element={<ProtectedRoute />}>
  <Route element={<ModuleRoute moduleKey="restaurante" />}>
    <Route element={<WaiterLayout />}>
      <Route path="/restaurante" element={<FloorPage />} />
      <Route path="/restaurante/mesa/:tableId" element={<OrderPage />} />
      <Route path="/restaurante/comanda/:orderId" element={<OrderPage />} />
    </Route>
    <Route element={<KitchenLayout />}>
      <Route path="/restaurante/cozinha" element={<KitchenPage />} />
    </Route>
  </Route>
</Route>
```

- [ ] **Step 5: Build check**
```bash
cd nexo-main && npx tsc --noEmit
```
Expected: 0 errors.

- [ ] **Step 6: Commit**
```bash
git add src/app/layouts/WaiterLayout.tsx src/app/layouts/KitchenLayout.tsx
git add src/app/router/routes.ts src/app/router/AppRouter.tsx
git commit -m "feat(restaurante): add WaiterLayout, KitchenLayout, routing, sidebar entry"
```

---

## Task F-07: FloorPage — TableCard + AreaTabs + OpenOrderSheet

**Files:**
- Create: `nexo-main/src/modules/restaurante/components/TableCard.tsx`
- Create: `nexo-main/src/modules/restaurante/components/AreaTabs.tsx`
- Create: `nexo-main/src/modules/restaurante/components/OpenOrderSheet.tsx`
- Create: `nexo-main/src/modules/restaurante/pages/FloorPage.tsx`

- [ ] **Step 1: Create TableCard**

```tsx
// TableCard.tsx
import { cn } from "@/lib/utils";
import type { TableDto } from "../types";

interface TableCardProps {
  table: TableDto;
  onClick: () => void;
}

const statusStyles: Record<TableDto["status"], string> = {
  Available:   "bg-card border-border hover:border-primary",
  Occupied:    "bg-primary/10 border-primary text-primary",
  Reserved:    "bg-amber-500/10 border-amber-500 text-amber-600",
  Maintenance: "bg-red-500/10 border-red-500 text-red-600",
};

const statusLabel: Record<TableDto["status"], string> = {
  Available:   "Livre",
  Occupied:    "Ocupada",
  Reserved:    "Reservada",
  Maintenance: "Manutenção",
};

export function TableCard({ table, onClick }: TableCardProps) {
  return (
    <button
      onClick={onClick}
      className={cn(
        "relative rounded-xl border-2 p-4 text-left transition-all min-h-[88px]",
        "flex flex-col justify-between active:scale-95",
        statusStyles[table.status]
      )}
    >
      <span className="text-xl font-bold">Mesa {table.number}</span>
      <span className="text-xs font-medium opacity-70">
        {statusLabel[table.status]}
      </span>
      {table.status === "Occupied" && (
        <span className="absolute top-2 right-2 h-2 w-2 rounded-full bg-primary animate-pulse" />
      )}
    </button>
  );
}
```

- [ ] **Step 2: Create AreaTabs**

```tsx
// AreaTabs.tsx
import { cn } from "@/lib/utils";
import type { AreaDto } from "../types";

interface AreaTabsProps {
  areas: AreaDto[];
  activeAreaId: string | null;
  onSelect: (areaId: string | null) => void;
}

export function AreaTabs({ areas, activeAreaId, onSelect }: AreaTabsProps) {
  return (
    <div className="flex gap-2 overflow-x-auto pb-1 scrollbar-none">
      <button
        onClick={() => onSelect(null)}
        className={cn(
          "shrink-0 rounded-full px-4 py-1.5 text-sm font-medium transition-colors",
          activeAreaId === null
            ? "bg-primary text-primary-foreground"
            : "bg-muted text-muted-foreground hover:bg-muted/80"
        )}
      >
        Todas
      </button>
      {areas.map((area) => (
        <button
          key={area.id}
          onClick={() => onSelect(area.id)}
          className={cn(
            "shrink-0 rounded-full px-4 py-1.5 text-sm font-medium transition-colors",
            activeAreaId === area.id
              ? "bg-primary text-primary-foreground"
              : "bg-muted text-muted-foreground hover:bg-muted/80"
          )}
        >
          {area.name}
        </button>
      ))}
    </div>
  );
}
```

- [ ] **Step 3: Create OpenOrderSheet**

```tsx
// OpenOrderSheet.tsx
import { useState } from "react";
import {
  Sheet, SheetContent, SheetHeader, SheetTitle,
} from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import type { OrderType } from "../types";

interface OpenOrderSheetProps {
  open: boolean;
  tableNumber?: string; // undefined = Counter (no table)
  onClose: () => void;
  onSubmit: (orderType: OrderType, partySize: number | null) => void;
  isLoading: boolean;
}

const ORDER_TYPES: { value: OrderType; label: string }[] = [
  { value: "DineIn",   label: "Mesa" },
  { value: "Counter",  label: "Balcão" },
  { value: "Takeaway", label: "Retirada" },
];

export function OpenOrderSheet({
  open, tableNumber, onClose, onSubmit, isLoading
}: OpenOrderSheetProps) {
  const defaultType: OrderType = tableNumber ? "DineIn" : "Counter";
  const [orderType, setOrderType] = useState<OrderType>(defaultType);
  const [partySize, setPartySize] = useState("");

  const handleSubmit = () => {
    const ps = partySize ? parseInt(partySize, 10) : null;
    onSubmit(orderType, ps);
  };

  return (
    <Sheet open={open} onOpenChange={(v) => !v && onClose()}>
      <SheetContent side="bottom" className="rounded-t-2xl pb-8">
        <SheetHeader className="mb-4">
          <SheetTitle>
            {tableNumber ? `Abrir comanda — Mesa ${tableNumber}` : "Nova comanda"}
          </SheetTitle>
        </SheetHeader>

        {/* Order type chips */}
        <div className="flex gap-2 mb-4">
          {ORDER_TYPES.map((t) => (
            <button
              key={t.value}
              onClick={() => setOrderType(t.value)}
              className={cn(
                "flex-1 rounded-lg py-2 text-sm font-medium border transition-colors",
                orderType === t.value
                  ? "border-primary bg-primary/10 text-primary"
                  : "border-border text-muted-foreground"
              )}
            >
              {t.label}
            </button>
          ))}
        </div>

        {/* Party size */}
        <div className="mb-6">
          <label className="text-sm text-muted-foreground mb-1 block">
            Pessoas (opcional)
          </label>
          <Input
            type="number"
            min={1}
            placeholder="Ex: 4"
            value={partySize}
            onChange={(e) => setPartySize(e.target.value)}
            className="w-full"
          />
        </div>

        <Button
          className="w-full h-12 text-base"
          onClick={handleSubmit}
          disabled={isLoading}
        >
          {isLoading ? "Abrindo..." : "Abrir comanda"}
        </Button>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 4: Create FloorPage**

```tsx
// FloorPage.tsx
import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Plus } from "lucide-react";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { TableCard } from "../components/TableCard";
import { AreaTabs } from "../components/AreaTabs";
import { OpenOrderSheet } from "../components/OpenOrderSheet";
import { useRestauranteTables } from "../hooks/useRestauranteTables";
import { useRestauranteAreas } from "../hooks/useRestauranteAreas";
import { useOpenOrder } from "../hooks/useOrderMutations";
import type { OrderType, TableDto } from "../types";

export default function FloorPage() {
  const { session } = useAuth();
  const storeId     = session?.storeId ?? "";
  const navigate    = useNavigate();

  const { data: tables = [], isLoading: tablesLoading } = useRestauranteTables(storeId);
  const { data: areas  = [] }                           = useRestauranteAreas(storeId);
  const openOrderMut                                    = useOpenOrder(storeId);

  const [activeAreaId, setActiveAreaId]   = useState<string | null>(null);
  const [selectedTable, setSelectedTable] = useState<TableDto | null>(null);
  const [sheetOpen, setSheetOpen]         = useState(false);
  const [isCounter, setIsCounter]         = useState(false);

  const visibleTables = activeAreaId
    ? tables.filter((t) => t.areaId === activeAreaId)
    : tables;

  const handleTableClick = (table: TableDto) => {
    if (table.status === "Occupied") {
      // Navigate directly to the order — no modal needed
      navigate(`/restaurante/mesa/${table.id}`);
      return;
    }
    setSelectedTable(table);
    setIsCounter(false);
    setSheetOpen(true);
  };

  const handleCounterClick = () => {
    setSelectedTable(null);
    setIsCounter(true);
    setSheetOpen(true);
  };

  const handleOpenOrder = async (orderType: OrderType, partySize: number | null) => {
    const result = await openOrderMut.mutateAsync({
      orderType,
      tableId:   selectedTable?.id ?? null,
      partySize: partySize ?? null,
    });
    setSheetOpen(false);
    navigate(`/restaurante/comanda/${result.id}`);
  };

  return (
    <div className="flex flex-col h-screen overflow-hidden">
      {/* Header */}
      <div className="px-4 pt-5 pb-3 flex items-center justify-between border-b border-border">
        <div>
          <h1 className="text-lg font-semibold">Salão</h1>
          <p className="text-xs text-muted-foreground">
            {tables.filter((t) => t.status === "Occupied").length} mesa(s) ocupada(s)
          </p>
        </div>
        <button
          onClick={handleCounterClick}
          className="flex items-center gap-1.5 rounded-lg bg-primary px-3 py-2 text-sm font-medium text-primary-foreground"
        >
          <Plus className="h-4 w-4" />
          Balcão
        </button>
      </div>

      {/* Area tabs */}
      <div className="px-4 pt-3 pb-2">
        <AreaTabs areas={areas} activeAreaId={activeAreaId} onSelect={setActiveAreaId} />
      </div>

      {/* Table grid */}
      <div className="flex-1 overflow-y-auto px-4 pb-4">
        {tablesLoading ? (
          <div className="grid grid-cols-3 gap-3 mt-2">
            {Array.from({ length: 6 }).map((_, i) => (
              <div key={i} className="h-[88px] rounded-xl bg-muted animate-pulse" />
            ))}
          </div>
        ) : visibleTables.length === 0 ? (
          <p className="text-center text-muted-foreground mt-12 text-sm">
            Nenhuma mesa cadastrada nesta área.
          </p>
        ) : (
          <div className="grid grid-cols-3 gap-3 mt-2">
            {visibleTables.map((table) => (
              <TableCard
                key={table.id}
                table={table}
                onClick={() => handleTableClick(table)}
              />
            ))}
          </div>
        )}
      </div>

      {/* Bottom sheet */}
      <OpenOrderSheet
        open={sheetOpen}
        tableNumber={selectedTable?.number}
        onClose={() => setSheetOpen(false)}
        onSubmit={handleOpenOrder}
        isLoading={openOrderMut.isPending}
      />
    </div>
  );
}
```

- [ ] **Step 5: TypeScript check and commit**
```bash
npx tsc --noEmit
git add src/modules/restaurante/components/TableCard.tsx
git add src/modules/restaurante/components/AreaTabs.tsx
git add src/modules/restaurante/components/OpenOrderSheet.tsx
git add src/modules/restaurante/pages/FloorPage.tsx
git commit -m "feat(restaurante): add FloorPage with TableCard, AreaTabs, OpenOrderSheet"
```

---

## Task F-08: AddItemDrawer + ModifierSelector

**Files:**
- Create: `nexo-main/src/modules/restaurante/components/ModifierSelector.tsx`
- Create: `nexo-main/src/modules/restaurante/components/AddItemDrawer.tsx`

- [ ] **Step 1: Create ModifierSelector**

```tsx
// ModifierSelector.tsx
import { cn } from "@/lib/utils";
import type { ModifierGroupDto } from "../types";

interface ModifierSelectorProps {
  groups: ModifierGroupDto[];
  selected: Record<string, string[]>; // groupId → [modifierId, ...]
  onToggle: (groupId: string, modifierId: string, maxSelections: number) => void;
  errors: Record<string, string>; // groupId → error message
}

export function ModifierSelector({
  groups, selected, onToggle, errors
}: ModifierSelectorProps) {
  if (groups.length === 0) return null;

  return (
    <div className="space-y-4">
      {groups.map((group) => (
        <div key={group.id}>
          <div className="flex items-center gap-1 mb-2">
            <span className="text-sm font-medium">{group.name}</span>
            {group.isRequired && (
              <span className="text-xs text-destructive font-medium">*</span>
            )}
            {group.maxSelections > 1 && (
              <span className="text-xs text-muted-foreground ml-1">
                (até {group.maxSelections})
              </span>
            )}
          </div>
          <div className="flex flex-wrap gap-2">
            {group.modifiers.filter((m) => m.isActive).map((mod) => {
              const isSelected = (selected[group.id] ?? []).includes(mod.id);
              return (
                <button
                  key={mod.id}
                  onClick={() => onToggle(group.id, mod.id, group.maxSelections)}
                  className={cn(
                    "rounded-full border px-3 py-1 text-sm transition-colors",
                    isSelected
                      ? "border-primary bg-primary/10 text-primary font-medium"
                      : "border-border text-muted-foreground"
                  )}
                >
                  {mod.name}
                  {mod.priceAdjustment !== 0 && (
                    <span className="ml-1 text-xs">
                      {mod.priceAdjustment > 0 ? "+" : ""}
                      R$ {Math.abs(mod.priceAdjustment).toFixed(2)}
                    </span>
                  )}
                </button>
              );
            })}
          </div>
          {errors[group.id] && (
            <p className="text-xs text-destructive mt-1">{errors[group.id]}</p>
          )}
        </div>
      ))}
    </div>
  );
}
```

- [ ] **Step 2: Create AddItemDrawer**

```tsx
// AddItemDrawer.tsx
import { useState } from "react";
import {
  Sheet, SheetContent, SheetHeader, SheetTitle,
} from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { useProducts } from "@/modules/products/hooks/useProducts";
import { useStockItems } from "@/modules/inventory/hooks/useStockItems";
import { ModifierSelector } from "./ModifierSelector";
import { useModifierGroups } from "../hooks/useModifierGroups";
import type { AddOrderItemRequest } from "../types";

interface AddItemDrawerProps {
  open: boolean;
  onClose: () => void;
  onAdd: (req: AddOrderItemRequest) => void;
  isLoading: boolean;
}

export function AddItemDrawer({ open, onClose, onAdd, isLoading }: AddItemDrawerProps) {
  const { data: products = [] } = useProducts(false);
  const { data: stockItems = [] } = useStockItems();

  const [search, setSearch]         = useState("");
  const [selectedProduct, setSelectedProduct] = useState<string | null>(null);
  const [quantity, setQuantity]     = useState("1");
  const [notes, setNotes]           = useState("");
  const [selected, setSelected]     = useState<Record<string, string[]>>({});
  const [errors, setErrors]         = useState<Record<string, string>>({});

  const { data: modifierGroups = [] } = useModifierGroups(selectedProduct);

  const stockMap = new Map(stockItems.map((s) => [s.productId, s.currentQuantity]));
  const filtered = products.filter((p) => {
    if (!p.isActive) return false;
    const q = search.toLowerCase();
    return (
      p.name.toLowerCase().includes(q) ||
      p.code.toLowerCase().includes(q) ||
      (p.barcode ?? "").includes(q)
    );
  });

  const handleToggleModifier = (
    groupId: string, modifierId: string, maxSelections: number
  ) => {
    setSelected((prev) => {
      const curr = prev[groupId] ?? [];
      if (curr.includes(modifierId)) {
        return { ...prev, [groupId]: curr.filter((id) => id !== modifierId) };
      }
      if (maxSelections === 1) {
        return { ...prev, [groupId]: [modifierId] };
      }
      if (curr.length >= maxSelections) return prev;
      return { ...prev, [groupId]: [...curr, modifierId] };
    });
    setErrors((e) => ({ ...e, [groupId]: "" }));
  };

  const handleAdd = () => {
    if (!selectedProduct) return;
    // Validate required groups
    const newErrors: Record<string, string> = {};
    for (const g of modifierGroups) {
      if (g.isRequired && !(selected[g.id]?.length)) {
        newErrors[g.id] = `Selecione uma opção para "${g.name}"`;
      }
    }
    if (Object.keys(newErrors).length > 0) {
      setErrors(newErrors);
      return;
    }

    const modifiers = Object.values(selected).flat().map((id) => ({ modifierId: id }));
    onAdd({
      productId: selectedProduct,
      quantity:  parseFloat(quantity) || 1,
      notes:     notes || null,
      modifiers: modifiers.length > 0 ? modifiers : undefined,
    });
    // Reset
    setSearch(""); setSelectedProduct(null); setQuantity("1");
    setNotes(""); setSelected({}); setErrors({});
  };

  const selectedProductName = products.find((p) => p.id === selectedProduct)?.name;

  return (
    <Sheet open={open} onOpenChange={(v) => !v && onClose()}>
      <SheetContent side="bottom" className="rounded-t-2xl max-h-[90vh] overflow-y-auto pb-8">
        <SheetHeader className="mb-4">
          <SheetTitle>Adicionar item</SheetTitle>
        </SheetHeader>

        {/* Product search or modifier selection */}
        {!selectedProduct ? (
          <>
            <Input
              placeholder="Buscar produto por nome ou código..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="mb-3"
              autoFocus
            />
            <div className="space-y-1 max-h-60 overflow-y-auto">
              {filtered.map((p) => {
                const stock = stockMap.get(p.id);
                const lowStock = stock !== undefined && stock <= 0;
                return (
                  <button
                    key={p.id}
                    onClick={() => setSelectedProduct(p.id)}
                    className="w-full flex items-center justify-between rounded-lg px-3 py-2.5 text-left hover:bg-muted transition-colors"
                  >
                    <div>
                      <p className="text-sm font-medium">{p.name}</p>
                      <p className="text-xs text-muted-foreground">{p.code}</p>
                    </div>
                    <div className="text-right">
                      <p className="text-sm font-semibold">
                        R$ {p.salePrice.toFixed(2)}
                      </p>
                      {lowStock && (
                        <p className="text-[10px] text-destructive">Sem estoque</p>
                      )}
                    </div>
                  </button>
                );
              })}
            </div>
          </>
        ) : (
          <>
            <button
              onClick={() => setSelectedProduct(null)}
              className="text-sm text-muted-foreground mb-3 hover:text-foreground"
            >
              ← {selectedProductName}
            </button>

            {/* Modifiers */}
            <ModifierSelector
              groups={modifierGroups}
              selected={selected}
              onToggle={handleToggleModifier}
              errors={errors}
            />

            {/* Quantity */}
            <div className="mt-4 mb-3">
              <label className="text-sm text-muted-foreground mb-1 block">Quantidade</label>
              <Input
                type="number" min={1} value={quantity}
                onChange={(e) => setQuantity(e.target.value)}
              />
            </div>

            {/* Notes */}
            <Textarea
              placeholder="Observação (opcional)"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={2}
              className="mb-5"
            />

            <Button
              className="w-full h-12 text-base"
              onClick={handleAdd}
              disabled={isLoading}
            >
              {isLoading ? "Adicionando..." : "Adicionar à comanda"}
            </Button>
          </>
        )}
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 3: TypeScript check and commit**
```bash
npx tsc --noEmit
git add src/modules/restaurante/components/ModifierSelector.tsx
git add src/modules/restaurante/components/AddItemDrawer.tsx
git commit -m "feat(restaurante): add AddItemDrawer with ModifierSelector"
```

---

## Task F-09: OrderPage

**Files:**
- Create: `nexo-main/src/modules/restaurante/components/OrderItemRow.tsx`
- Create: `nexo-main/src/modules/restaurante/pages/OrderPage.tsx`

- [ ] **Step 1: Create OrderItemRow**

```tsx
// OrderItemRow.tsx
import { cn } from "@/lib/utils";
import type { OrderItemDto } from "../types";

const statusColor: Record<OrderItemDto["status"], string> = {
  Pending:   "bg-muted text-muted-foreground",
  Preparing: "bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-400",
  Ready:     "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400",
  Delivered: "bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400",
  Cancelled: "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400 line-through opacity-50",
};

const statusLabel: Record<OrderItemDto["status"], string> = {
  Pending:   "Pendente",
  Preparing: "Preparando",
  Ready:     "Pronto",
  Delivered: "Entregue",
  Cancelled: "Cancelado",
};

export function OrderItemRow({ item }: { item: OrderItemDto }) {
  return (
    <div className="flex items-start gap-3 py-3 border-b border-border last:border-0">
      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-2">
          <span className="font-medium text-sm">{item.quantity}×</span>
          <span className="font-medium text-sm truncate">{item.productName}</span>
        </div>
        {item.modifiers.length > 0 && (
          <p className="text-xs text-muted-foreground mt-0.5 pl-6">
            {item.modifiers.map((m) => m.labelSnapshot).join(", ")}
          </p>
        )}
        {item.notes && (
          <p className="text-xs text-muted-foreground italic mt-0.5 pl-6">
            "{item.notes}"
          </p>
        )}
      </div>
      <div className="text-right shrink-0">
        <p className="text-sm font-semibold">R$ {item.total.toFixed(2)}</p>
        <span className={cn("text-[10px] font-medium px-1.5 py-0.5 rounded mt-1 inline-block", statusColor[item.status])}>
          {statusLabel[item.status]}
        </span>
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Create OrderPage**

```tsx
// OrderPage.tsx
import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { ArrowLeft, Plus, AlertTriangle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { OrderItemRow } from "../components/OrderItemRow";
import { AddItemDrawer } from "../components/AddItemDrawer";
import { PaymentDrawer } from "../components/PaymentDrawer";
import { useActiveOrder, useOrder } from "../hooks/useActiveOrder";
import { useAddItem, useCloseOrder, useCancelOrder } from "../hooks/useOrderMutations";
import type { AddOrderItemRequest } from "../types";

export default function OrderPage() {
  const { session }  = useAuth();
  const storeId      = session?.storeId ?? "";
  const { tableId, orderId } = useParams<{ tableId?: string; orderId?: string }>();
  const navigate     = useNavigate();

  // Support both /mesa/:tableId (find active order) and /comanda/:orderId (direct)
  const { data: orderByTable }  = useActiveOrder(storeId, tableId ?? "");
  const { data: orderDirect }   = useOrder(storeId, orderId ?? "");
  const order = orderId ? orderDirect : orderByTable;

  const addItemMut    = useAddItem(storeId, order?.id ?? "");
  const closeOrderMut = useCloseOrder(storeId);
  const cancelMut     = useCancelOrder(storeId);

  const [addDrawerOpen, setAddDrawerOpen]   = useState(false);
  const [payDrawerOpen, setPayDrawerOpen]   = useState(false);

  if (!order) {
    return (
      <div className="flex flex-col items-center justify-center h-screen gap-3">
        <p className="text-muted-foreground text-sm">Nenhuma comanda aberta.</p>
        <Button variant="ghost" size="sm" onClick={() => navigate("/restaurante")}>
          ← Voltar ao salão
        </Button>
      </div>
    );
  }

  const isOpen = ["Open", "InPreparation", "Ready"].includes(order.status);

  const handleAddItem = (req: AddOrderItemRequest) => {
    addItemMut.mutate(req, { onSuccess: () => setAddDrawerOpen(false) });
  };

  const handleCloseOrder = async () => {
    await closeOrderMut.mutateAsync(order.id);
    setPayDrawerOpen(true);
  };

  const handleCancel = () => {
    if (!window.confirm("Cancelar esta comanda? Esta ação não pode ser desfeita.")) return;
    cancelMut.mutate(order.id, { onSuccess: () => navigate("/restaurante") });
  };

  return (
    <div className="flex flex-col h-screen overflow-hidden">
      {/* Header */}
      <div className="px-4 pt-5 pb-3 border-b border-border flex items-center gap-3">
        <button onClick={() => navigate("/restaurante")} className="p-1">
          <ArrowLeft className="h-5 w-5" />
        </button>
        <div className="flex-1">
          <h1 className="font-semibold">
            {order.tableNumber ? `Mesa ${order.tableNumber}` : order.orderType}
            <span className="text-muted-foreground font-normal ml-2 text-sm">
              #{order.orderNumber}
            </span>
          </h1>
          <p className="text-xs text-muted-foreground">
            {order.partySize ? `${order.partySize} pessoa(s) · ` : ""}
            {order.status}
          </p>
        </div>
        {isOpen && (
          <button
            onClick={() => setAddDrawerOpen(true)}
            className="rounded-full bg-primary p-2 text-primary-foreground"
          >
            <Plus className="h-5 w-5" />
          </button>
        )}
      </div>

      {/* Items */}
      <div className="flex-1 overflow-y-auto px-4 py-2">
        {order.items.length === 0 ? (
          <p className="text-center text-muted-foreground text-sm mt-8">
            Nenhum item adicionado.
          </p>
        ) : (
          order.items.map((item) => <OrderItemRow key={item.id} item={item} />)
        )}
      </div>

      {/* Totals + action bar */}
      {isOpen && (
        <div className="border-t border-border px-4 pt-3 pb-5">
          <div className="space-y-1 mb-4 text-sm">
            <div className="flex justify-between">
              <span className="text-muted-foreground">Subtotal</span>
              <span>R$ {order.itemsSubtotal.toFixed(2)}</span>
            </div>
            {order.couvertAmount > 0 && (
              <div className="flex justify-between">
                <span className="text-muted-foreground">
                  Couvert ({order.partySize ?? "?"} pessoas)
                </span>
                <span>R$ {order.couvertAmount.toFixed(2)}</span>
              </div>
            )}
          </div>
          <div className="flex gap-2">
            <Button
              variant="outline"
              size="sm"
              className="text-destructive border-destructive/50"
              onClick={handleCancel}
              disabled={cancelMut.isPending}
            >
              <AlertTriangle className="h-4 w-4" />
            </Button>
            <Button
              className="flex-1 h-12"
              onClick={handleCloseOrder}
              disabled={order.items.filter((i) => i.status !== "Cancelled").length === 0 || closeOrderMut.isPending}
            >
              Fechar conta
            </Button>
          </div>
        </div>
      )}

      <AddItemDrawer
        open={addDrawerOpen}
        onClose={() => setAddDrawerOpen(false)}
        onAdd={handleAddItem}
        isLoading={addItemMut.isPending}
      />

      <PaymentDrawer
        open={payDrawerOpen}
        order={order}
        onClose={() => setPayDrawerOpen(false)}
        storeId={storeId}
      />
    </div>
  );
}
```

- [ ] **Step 3: TypeScript check and commit**
```bash
npx tsc --noEmit
git add src/modules/restaurante/components/OrderItemRow.tsx
git add src/modules/restaurante/pages/OrderPage.tsx
git commit -m "feat(restaurante): add OrderPage with item list, add drawer, close action"
```

---

## Task F-10: PaymentDrawer

**Files:**
- Create: `nexo-main/src/modules/restaurante/components/PaymentDrawer.tsx`

- [ ] **Step 1: Create PaymentDrawer**

```tsx
// PaymentDrawer.tsx
import { useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  Sheet, SheetContent, SheetHeader, SheetTitle,
} from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import { usePayOrder } from "../hooks/useOrderMutations";
import { useFoodSettings } from "../hooks/useFoodSettings";
import type { OrderDto, PaymentInputDto } from "../types";

type PayMethod = "cash" | "pix" | "card";

const METHOD_LABELS: Record<PayMethod, string> = {
  cash: "Dinheiro",
  pix: "PIX",
  card: "Cartão",
};

function toBackendPayment(method: PayMethod, amount: number): PaymentInputDto {
  const map: Record<PayMethod, { method: string; type: string }> = {
    cash: { method: "Cash", type: "Cash" },
    pix:  { method: "Pix",  type: "Cash" },
    card: { method: "Debit", type: "Cash" },
  };
  return { ...map[method], amount };
}

interface PaymentDrawerProps {
  open: boolean;
  order: OrderDto;
  storeId: string;
  onClose: () => void;
}

export function PaymentDrawer({ open, order, storeId, onClose }: PaymentDrawerProps) {
  const navigate    = useNavigate();
  const payMut      = usePayOrder(storeId);
  const { data: settings } = useFoodSettings(storeId);

  // If service fee not yet calculated (order just closed), use settings to estimate
  const serviceFeePercent = settings?.serviceFeeEnabled ? (settings.serviceFeePercent ?? 0) : 0;
  const estimatedServiceFee = order.serviceFeeAmount > 0
    ? order.serviceFeeAmount
    : Math.round(order.itemsSubtotal * (serviceFeePercent / 100) * 100) / 100;

  const displayTotal = order.itemsSubtotal + order.couvertAmount + estimatedServiceFee;

  const [method, setMethod]     = useState<PayMethod>("cash");
  const [amount, setAmount]     = useState(displayTotal.toFixed(2));
  const [partySize, setPartySize] = useState(order.partySize?.toString() ?? "");

  const paid   = parseFloat(amount) || 0;
  const change = Math.max(0, paid - displayTotal);

  const splitSuggestion =
    order.partySize && order.partySize > 1
      ? (displayTotal / order.partySize).toFixed(2)
      : null;

  const handlePay = () => {
    payMut.mutate(
      {
        orderId: order.id,
        req: {
          payments: [toBackendPayment(method, paid)],
          partySize: partySize ? parseInt(partySize) : undefined,
        },
      },
      {
        onSuccess: () => {
          navigate("/restaurante");
        },
      }
    );
  };

  return (
    <Sheet open={open} onOpenChange={(v) => !v && onClose()}>
      <SheetContent side="bottom" className="rounded-t-2xl pb-8 max-h-[92vh] overflow-y-auto">
        <SheetHeader className="mb-4">
          <SheetTitle>Fechar conta — #{order.orderNumber}</SheetTitle>
        </SheetHeader>

        {/* Breakdown */}
        <div className="space-y-2 mb-5 text-sm">
          <Row label="Subtotal dos itens" value={order.itemsSubtotal} />
          {(order.couvertAmount > 0) && (
            <Row
              label={`Couvert (${order.partySize ?? "?"} pessoas)`}
              value={order.couvertAmount}
            />
          )}
          {serviceFeePercent > 0 && (
            <Row
              label={`Taxa de serviço ${serviceFeePercent}%`}
              value={estimatedServiceFee}
            />
          )}
          <div className="flex justify-between font-semibold border-t border-border pt-2">
            <span>Total</span>
            <span className="text-base">R$ {displayTotal.toFixed(2)}</span>
          </div>
          {splitSuggestion && (
            <p className="text-xs text-muted-foreground">
              Sugestão: R$ {splitSuggestion} por pessoa
            </p>
          )}
        </div>

        {/* Manual party size (for manual couvert) */}
        {settings?.couvertEnabled && !settings.couvertAutomatic && !order.partySize && (
          <div className="mb-4">
            <label className="text-sm text-muted-foreground mb-1 block">
              Número de pessoas (para couvert)
            </label>
            <Input
              type="number" min={1} value={partySize}
              onChange={(e) => setPartySize(e.target.value)}
            />
          </div>
        )}

        {/* Payment method */}
        <div className="flex gap-2 mb-4">
          {(Object.keys(METHOD_LABELS) as PayMethod[]).map((m) => (
            <button
              key={m}
              onClick={() => setMethod(m)}
              className={cn(
                "flex-1 py-2 rounded-lg text-sm font-medium border transition-colors",
                method === m
                  ? "border-primary bg-primary/10 text-primary"
                  : "border-border text-muted-foreground"
              )}
            >
              {METHOD_LABELS[m]}
            </button>
          ))}
        </div>

        {/* Amount */}
        <div className="mb-2">
          <label className="text-sm text-muted-foreground mb-1 block">Valor recebido</label>
          <Input
            type="number" min={displayTotal}
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            className="text-lg font-semibold"
          />
        </div>

        {method === "cash" && change > 0 && (
          <p className="text-sm text-green-600 font-medium mb-4">
            Troco: R$ {change.toFixed(2)}
          </p>
        )}

        <Button
          className="w-full h-12 text-base mt-2"
          onClick={handlePay}
          disabled={paid < displayTotal || payMut.isPending}
        >
          {payMut.isPending ? "Processando..." : "Confirmar pagamento"}
        </Button>
      </SheetContent>
    </Sheet>
  );
}

function Row({ label, value }: { label: string; value: number }) {
  return (
    <div className="flex justify-between text-muted-foreground">
      <span>{label}</span>
      <span className="text-foreground">R$ {value.toFixed(2)}</span>
    </div>
  );
}
```

- [ ] **Step 2: TypeScript check and commit**
```bash
npx tsc --noEmit
git add src/modules/restaurante/components/PaymentDrawer.tsx
git commit -m "feat(restaurante): add PaymentDrawer with full couvert/service fee breakdown"
```

---

## Task F-11: KitchenPage + KitchenBoard + KitchenCard

**Files:**
- Create: `nexo-main/src/modules/restaurante/components/KitchenCard.tsx`
- Create: `nexo-main/src/modules/restaurante/components/KitchenConnectionBadge.tsx`
- Create: `nexo-main/src/modules/restaurante/components/KitchenBoard.tsx`
- Create: `nexo-main/src/modules/restaurante/pages/KitchenPage.tsx`

- [ ] **Step 1: Create KitchenConnectionBadge**

```tsx
// KitchenConnectionBadge.tsx
import { cn } from "@/lib/utils";
import type { ConnectionMode } from "../types";

export function KitchenConnectionBadge({ mode }: { mode: ConnectionMode }) {
  return (
    <div className="flex items-center gap-1.5">
      <span
        className={cn(
          "h-2 w-2 rounded-full",
          mode === "realtime" ? "bg-green-500 animate-pulse" : "bg-amber-400"
        )}
      />
      <span className="text-xs text-gray-400">
        {mode === "realtime" ? "Tempo real" : "Atualizando (10s)"}
      </span>
    </div>
  );
}
```

- [ ] **Step 2: Create KitchenCard**

```tsx
// KitchenCard.tsx
import { cn } from "@/lib/utils";
import type { KitchenItem } from "../types";
import { useUpdateItemStatus } from "../hooks/useOrderMutations";

const STATUS_SEQUENCE: Record<string, string> = {
  Pending:   "Preparing",
  Preparing: "Ready",
  Ready:     "Delivered",
};

const STATUS_ACTION: Record<string, string> = {
  Pending:   "Iniciar",
  Preparing: "Pronto",
  Ready:     "Entregue",
};

function elapsed(since: string | null): { label: string; color: string } {
  if (!since) return { label: "", color: "text-gray-400" };
  const mins = Math.floor((Date.now() - new Date(since).getTime()) / 60_000);
  const label = mins < 1 ? "<1 min" : `${mins} min`;
  const color = mins < 5 ? "text-green-400" : mins < 10 ? "text-amber-400" : "text-red-400";
  return { label, color };
}

export function KitchenCard({
  item, storeId,
}: {
  item: KitchenItem;
  storeId: string;
}) {
  const updateMut  = useUpdateItemStatus(storeId);
  const { label, color } = elapsed(item.sentToKitchenAt);
  const nextStatus = STATUS_SEQUENCE[item.status];

  return (
    <div className="bg-gray-900 rounded-xl p-4 border border-gray-700 flex flex-col gap-2">
      <div className="flex justify-between items-start">
        <span className="text-xs text-gray-400">
          {item.tableNumber ? `Mesa ${item.tableNumber}` : item.orderType} · #{item.orderNumber}
        </span>
        {label && <span className={cn("text-xs font-medium", color)}>{label}</span>}
      </div>

      <p className="text-lg font-bold leading-tight">
        {item.quantity}× {item.productName}
      </p>

      {item.modifiers.length > 0 && (
        <p className="text-sm text-gray-300">
          {item.modifiers.map((m) => m.labelSnapshot).join(", ")}
        </p>
      )}

      {item.notes && (
        <p className="text-sm text-amber-300 italic">"{item.notes}"</p>
      )}

      {nextStatus && (
        <button
          onClick={() =>
            updateMut.mutate({ orderId: item.orderId, itemId: item.itemId, status: nextStatus })
          }
          disabled={updateMut.isPending}
          className="mt-1 w-full rounded-lg bg-gray-700 hover:bg-gray-600 py-2 text-sm font-medium transition-colors"
        >
          {STATUS_ACTION[item.status]}
        </button>
      )}
    </div>
  );
}
```

- [ ] **Step 3: Create KitchenBoard**

```tsx
// KitchenBoard.tsx
import { KitchenCard } from "./KitchenCard";
import type { KitchenItem } from "../types";

const COLUMNS: { status: KitchenItem["status"]; label: string }[] = [
  { status: "Pending",   label: "Pendente" },
  { status: "Preparing", label: "Preparando" },
  { status: "Ready",     label: "Pronto" },
];

export function KitchenBoard({
  items, storeId,
}: {
  items: KitchenItem[];
  storeId: string;
}) {
  return (
    <div className="grid grid-cols-3 gap-4 h-full overflow-hidden">
      {COLUMNS.map(({ status, label }) => {
        const col = items.filter((i) => i.status === status);
        return (
          <div key={status} className="flex flex-col gap-2 overflow-y-auto">
            <div className="flex items-center justify-between px-1 sticky top-0 bg-gray-950 pb-2">
              <h2 className="text-sm font-semibold text-gray-300 uppercase tracking-wider">
                {label}
              </h2>
              <span className="text-xs bg-gray-800 rounded-full px-2 py-0.5 text-gray-400">
                {col.length}
              </span>
            </div>
            {col.length === 0 ? (
              <p className="text-xs text-gray-600 text-center mt-4">
                {status === "Pending" ? "Tudo em ordem." : "Nenhum item aqui."}
              </p>
            ) : (
              col.map((item) => (
                <KitchenCard key={item.itemId} item={item} storeId={storeId} />
              ))
            )}
          </div>
        );
      })}
    </div>
  );
}
```

- [ ] **Step 4: Create KitchenPage**

```tsx
// KitchenPage.tsx
import { useEffect } from "react";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { KitchenBoard } from "../components/KitchenBoard";
import { KitchenConnectionBadge } from "../components/KitchenConnectionBadge";
import { useKitchenSocket } from "../hooks/useKitchenSocket";
import { useKitchenItems } from "../hooks/useKitchenItems";

export default function KitchenPage() {
  const { session }     = useAuth();
  const storeId         = session?.storeId ?? "";
  const token           = session ? localStorage.getItem("nexo:access_token") ?? undefined : undefined;

  const { connectionMode } = useKitchenSocket(storeId, token);

  // Polling is only active when SignalR has failed; interval driven by hook
  const { data: items = [] } = useKitchenItems(
    storeId,
    connectionMode === "polling" ? 10_000 : undefined
  );

  // Auto-fullscreen on first visit
  useEffect(() => {
    const asked = sessionStorage.getItem("kitchen:fullscreen-asked");
    if (!asked && document.fullscreenEnabled) {
      sessionStorage.setItem("kitchen:fullscreen-asked", "1");
      document.documentElement.requestFullscreen().catch(() => {});
    }
  }, []);

  return (
    <div className="flex flex-col h-screen p-4">
      {/* Header */}
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-xl font-bold text-white">Cozinha</h1>
        <KitchenConnectionBadge mode={connectionMode} />
      </div>

      {/* Board */}
      <div className="flex-1 overflow-hidden">
        <KitchenBoard items={items} storeId={storeId} />
      </div>
    </div>
  );
}
```

- [ ] **Step 5: TypeScript check and commit**
```bash
npx tsc --noEmit
git add src/modules/restaurante/components/KitchenCard.tsx
git add src/modules/restaurante/components/KitchenConnectionBadge.tsx
git add src/modules/restaurante/components/KitchenBoard.tsx
git add src/modules/restaurante/pages/KitchenPage.tsx
git commit -m "feat(restaurante): add KitchenPage with SignalR board and connection indicator"
```

---

## Task F-12: Dashboard Blocks

**Files:**
- Create: `nexo-main/src/modules/dashboard/components/RestauranteBlocks.tsx`
- Modify: `nexo-main/src/modules/dashboard/pages/DashboardPage.tsx`

- [ ] **Step 1: Create RestauranteBlocks**

```tsx
// RestauranteBlocks.tsx
import { useAuth } from "@/modules/auth/context/AuthContext";
import { useRestauranteTables } from "@/modules/restaurante/hooks/useRestauranteTables";
import { useKitchenItems } from "@/modules/restaurante/hooks/useKitchenItems";

/**
 * Only rendered when session.modules.includes("restaurante").
 * Shows two cards: open tables count and kitchen items count.
 * Empty state = meaningful message, NOT "0 mesas" as a KPI.
 * No fake/static numbers — blocks are not rendered until real queries resolve.
 */
export function RestauranteBlocks() {
  const { session } = useAuth();
  const storeId = session?.storeId ?? "";

  const { data: tables, isLoading: tablesLoading } =
    useRestauranteTables(storeId);
  const { data: kitchenItems, isLoading: kitchenLoading } =
    useKitchenItems(storeId);

  const openTables  = tables?.filter((t) => t.status === "Occupied").length ?? 0;
  const pendingItems = kitchenItems?.filter((i) => i.status !== "Delivered").length ?? 0;

  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
      {/* Open Tables */}
      <div className="rounded-xl border border-border bg-card p-5">
        <p className="text-xs font-medium text-muted-foreground uppercase tracking-wide mb-1">
          Mesas abertas
        </p>
        {tablesLoading ? (
          <div className="h-8 w-16 bg-muted animate-pulse rounded mt-1" />
        ) : openTables === 0 ? (
          <p className="text-sm text-muted-foreground mt-1">
            Nenhuma mesa aberta agora.
          </p>
        ) : (
          <p className="text-3xl font-bold">{openTables}</p>
        )}
      </div>

      {/* Kitchen status */}
      <div className="rounded-xl border border-border bg-card p-5">
        <p className="text-xs font-medium text-muted-foreground uppercase tracking-wide mb-1">
          Pedidos na cozinha
        </p>
        {kitchenLoading ? (
          <div className="h-8 w-16 bg-muted animate-pulse rounded mt-1" />
        ) : pendingItems === 0 ? (
          <p className="text-sm text-muted-foreground mt-1">
            Tudo em ordem na cozinha.
          </p>
        ) : (
          <p className="text-3xl font-bold">{pendingItems}</p>
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Add RestauranteBlocks to DashboardPage**

In `DashboardPage.tsx`, add the import:
```tsx
import { RestauranteBlocks } from "@/modules/dashboard/components/RestauranteBlocks";
```

Inside the JSX, after `<KpiCards />` and before `<SalesChart />`, add:
```tsx
{session?.modules.includes("restaurante") && <RestauranteBlocks />}
```

- [ ] **Step 3: TypeScript check and verify build**
```bash
npx tsc --noEmit
npm run build
```
Expected: 0 errors, build succeeds.

- [ ] **Step 4: Commit**
```bash
git add src/modules/dashboard/components/RestauranteBlocks.tsx
git add src/modules/dashboard/pages/DashboardPage.tsx
git commit -m "feat(restaurante): add dashboard blocks for open tables and kitchen status"
```

---

**Phase 2 complete.** Proceed to `2026-04-13-food-service-phase3-intelligence.md`.
