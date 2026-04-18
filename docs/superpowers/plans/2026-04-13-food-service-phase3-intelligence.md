# Orken Food Service — Phase 3: Intelligence + Delivery + Events

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add recipe card management (RecipesPage), CMV dashboard blocks, FoodServiceSettings admin UI, manual couvert flow, manual delivery order intake, and bar event tracking.

**Architecture:** RecipesPage and CMV ship first because they close the stock/cost loop opened by recipe card infrastructure in Phase 1. FoodServiceSettings UI enables admin to configure couvert + service fee without touching the DB. Delivery is manual intake (no external API in v1) — channel tracking only. Events are basic bar/pub performance tracking.

**Tech Stack:** Same as Phase 2. No new packages required.

**Prerequisite:** Phases 1 and 2 complete.

---

## File Map

### New Files — Backend
| File | Purpose |
|------|---------|
| `nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestDeliveryOrder.cs` | Delivery order entity |
| `nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestEvent.cs` | Bar/pub event entity |
| `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestDeliveryOrderConfiguration.cs` | EF config |
| `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestEventConfiguration.cs` | EF config |
| `nexo-backend/src/Nexo.Application/Modules/Restaurante/Interfaces/IDeliveryOrderRepository.cs` | Repository interface |
| `nexo-backend/src/Nexo.Application/Modules/Restaurante/Interfaces/IRestEventRepository.cs` | Repository interface |
| `nexo-backend/src/Nexo.Application/Modules/Restaurante/DeliveryOrderService.cs` | Delivery CRUD |
| `nexo-backend/src/Nexo.Application/Modules/Restaurante/RestEventService.cs` | Event CRUD |
| `nexo-backend/src/Nexo.Infrastructure/Repositories/Modules/Restaurante/DeliveryOrderRepository.cs` | Repository impl |
| `nexo-backend/src/Nexo.Infrastructure/Repositories/Modules/Restaurante/RestEventRepository.cs` | Repository impl |
| `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/DeliveryOrdersController.cs` | HTTP endpoints |
| `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/RestEventsController.cs` | HTTP endpoints |

### New Files — Frontend
| File | Purpose |
|------|---------|
| `src/modules/restaurante/pages/RecipesPage.tsx` | Recipe card management |
| `src/modules/restaurante/pages/FoodSettingsPage.tsx` | Admin config for couvert/service fee |
| `src/modules/restaurante/pages/DeliveryPage.tsx` | Manual delivery intake |
| `src/modules/restaurante/pages/EventsPage.tsx` | Bar/pub event tracking |
| `src/modules/dashboard/components/CmvBlocks.tsx` | Top CMV dishes + threshold alert |
| `src/modules/restaurante/hooks/useRecipeCards.ts` | Recipe queries + mutations |
| `src/modules/restaurante/hooks/useDeliveryOrders.ts` | Delivery queries + mutations |
| `src/modules/restaurante/hooks/useRestEvents.ts` | Event queries + mutations |
| `src/modules/restaurante/api/recipes.api.ts` | Recipe API calls |
| `src/modules/restaurante/api/delivery.api.ts` | Delivery API calls |
| `src/modules/restaurante/api/events.api.ts` | Events API calls |

### Modified Files
| File | Change |
|------|--------|
| `src/app/router/AppRouter.tsx` | Add Phase 3 page routes inside restaurante module block |
| `src/modules/dashboard/pages/DashboardPage.tsx` | Add CmvBlocks when restaurante active |

---

## Task V2-01: RecipesPage — Recipe Card Management

**Files:**
- Create: `nexo-main/src/modules/restaurante/api/recipes.api.ts`
- Create: `nexo-main/src/modules/restaurante/hooks/useRecipeCards.ts`
- Create: `nexo-main/src/modules/restaurante/pages/RecipesPage.tsx`
- Modify: `nexo-main/src/app/router/AppRouter.tsx`

> Uses the existing backend endpoints already in `RecipeCardsController`. No new backend code needed for this task.

- [ ] **Step 1: Create recipes.api.ts**

```typescript
// nexo-main/src/modules/restaurante/api/recipes.api.ts
import { apiClient } from "@/services/api-client";

export interface RecipeIngredientDto {
  id: string;
  ingredientProductId: string;
  ingredientName: string;
  ingredientCode: string;
  quantity: number;
  unit: string;
  currentCostPrice: number;
  lineCost: number;
}

export interface RecipeCardDto {
  id: string;
  productId: string;
  productName: string;
  productCode: string;
  yield: number;
  yieldUnit: string;
  isActive: boolean;
  notes: string | null;
  calculatedCost: number;
  cmvPercent: number;
  ingredients: RecipeIngredientDto[];
  createdAt: string;
}

export interface CreateRecipeCardRequest {
  productId: string;
  yield: number;
  yieldUnit: string;
  notes?: string | null;
}

export interface AddIngredientRequest {
  ingredientProductId: string;
  quantity: number;
  unit: string;
}

export const listRecipeCards = (): Promise<RecipeCardDto[]> =>
  apiClient.get("/restaurante/recipe-cards");

export const getRecipeCard = (id: string): Promise<RecipeCardDto> =>
  apiClient.get(`/restaurante/recipe-cards/${id}`);

export const createRecipeCard = (req: CreateRecipeCardRequest): Promise<RecipeCardDto> =>
  apiClient.post("/restaurante/recipe-cards", req);

export const addIngredient = (
  recipeId: string,
  req: AddIngredientRequest
): Promise<RecipeCardDto> =>
  apiClient.post(`/restaurante/recipe-cards/${recipeId}/ingredients`, req);

export const removeIngredient = (recipeId: string, ingredientId: string): Promise<void> =>
  apiClient.delete(`/restaurante/recipe-cards/${recipeId}/ingredients/${ingredientId}`);

export const activateRecipeCard = (id: string): Promise<RecipeCardDto> =>
  apiClient.post(`/restaurante/recipe-cards/${id}/activate`, {});

export const deactivateRecipeCard = (id: string): Promise<RecipeCardDto> =>
  apiClient.post(`/restaurante/recipe-cards/${id}/deactivate`, {});
```

- [ ] **Step 2: Create useRecipeCards.ts**

```typescript
// nexo-main/src/modules/restaurante/hooks/useRecipeCards.ts
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  listRecipeCards, createRecipeCard, addIngredient, removeIngredient,
  activateRecipeCard, deactivateRecipeCard,
} from "../api/recipes.api";
import type { CreateRecipeCardRequest, AddIngredientRequest } from "../api/recipes.api";

const RECIPES_KEY = ["recipe-cards"] as const;

export function useRecipeCards() {
  return useQuery({ queryKey: RECIPES_KEY, queryFn: listRecipeCards });
}

export function useCreateRecipeCard() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateRecipeCardRequest) => createRecipeCard(req),
    onSuccess: () => qc.invalidateQueries({ queryKey: RECIPES_KEY }),
  });
}

export function useAddIngredient(recipeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: AddIngredientRequest) => addIngredient(recipeId, req),
    onSuccess: () => qc.invalidateQueries({ queryKey: RECIPES_KEY }),
  });
}

export function useRemoveIngredient(recipeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (ingredientId: string) => removeIngredient(recipeId, ingredientId),
    onSuccess: () => qc.invalidateQueries({ queryKey: RECIPES_KEY }),
  });
}

export function useToggleRecipeCard() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, active }: { id: string; active: boolean }) =>
      active ? activateRecipeCard(id) : deactivateRecipeCard(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: RECIPES_KEY }),
  });
}
```

- [ ] **Step 3: Create RecipesPage**

```tsx
// nexo-main/src/modules/restaurante/pages/RecipesPage.tsx
import { useState } from "react";
import { ChevronDown, ChevronRight, Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { useProducts } from "@/modules/products/hooks/useProducts";
import {
  useRecipeCards, useCreateRecipeCard,
  useAddIngredient, useRemoveIngredient, useToggleRecipeCard,
} from "../hooks/useRecipeCards";
import type { RecipeCardDto } from "../api/recipes.api";
import { cn } from "@/lib/utils";

function CmvBadge({ pct }: { pct: number }) {
  const color =
    pct <= 30 ? "text-green-600 bg-green-50" :
    pct <= 45 ? "text-amber-600 bg-amber-50" :
    "text-red-600 bg-red-50";
  return (
    <span className={cn("text-xs font-semibold px-2 py-0.5 rounded", color)}>
      CMV {pct.toFixed(1)}%
    </span>
  );
}

function RecipeRow({ recipe }: { recipe: RecipeCardDto }) {
  const [expanded, setExpanded]         = useState(false);
  const [ingProductId, setIngProductId] = useState("");
  const [ingQty, setIngQty]             = useState("");
  const [ingUnit, setIngUnit]           = useState("g");

  const { data: products = [] }         = useProducts(false);
  const addIngMut                       = useAddIngredient(recipe.id);
  const removeIngMut                    = useRemoveIngredient(recipe.id);
  const toggleMut                       = useToggleRecipeCard();

  const handleAddIngredient = () => {
    if (!ingProductId || !ingQty) return;
    addIngMut.mutate(
      { ingredientProductId: ingProductId, quantity: parseFloat(ingQty), unit: ingUnit },
      { onSuccess: () => { setIngProductId(""); setIngQty(""); } }
    );
  };

  return (
    <div className="border border-border rounded-xl overflow-hidden">
      <button
        onClick={() => setExpanded(!expanded)}
        className="w-full flex items-center gap-3 px-4 py-3 text-left hover:bg-muted/50 transition-colors"
      >
        {expanded ? <ChevronDown className="h-4 w-4 shrink-0" /> : <ChevronRight className="h-4 w-4 shrink-0" />}
        <div className="flex-1">
          <p className="font-medium text-sm">{recipe.productName}</p>
          <p className="text-xs text-muted-foreground">
            Rendimento: {recipe.yield} {recipe.yieldUnit} · Custo unitário: R$ {recipe.calculatedCost.toFixed(2)}
          </p>
        </div>
        <CmvBadge pct={recipe.cmvPercent} />
      </button>

      {expanded && (
        <div className="px-4 pb-4 border-t border-border space-y-3">
          {/* Ingredient list */}
          <table className="w-full text-sm mt-3">
            <thead>
              <tr className="text-muted-foreground text-xs">
                <th className="text-left pb-1">Ingrediente</th>
                <th className="text-right pb-1">Qtd</th>
                <th className="text-right pb-1">Un</th>
                <th className="text-right pb-1">Custo</th>
                <th className="pb-1" />
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {recipe.ingredients.map((ing) => (
                <tr key={ing.id}>
                  <td className="py-1.5">{ing.ingredientName}</td>
                  <td className="text-right">{ing.quantity}</td>
                  <td className="text-right">{ing.unit}</td>
                  <td className="text-right">R$ {ing.lineCost.toFixed(2)}</td>
                  <td className="text-right">
                    <button
                      onClick={() => removeIngMut.mutate(ing.id)}
                      className="text-destructive hover:text-destructive/80"
                    >
                      <Trash2 className="h-3.5 w-3.5" />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {/* Add ingredient */}
          <div className="flex gap-2 items-end mt-3">
            <div className="flex-1">
              <Select value={ingProductId} onValueChange={setIngProductId}>
                <SelectTrigger className="h-9 text-sm">
                  <SelectValue placeholder="Ingrediente" />
                </SelectTrigger>
                <SelectContent>
                  {products.map((p) => (
                    <SelectItem key={p.id} value={p.id}>{p.name}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <Input
              placeholder="Qtd"
              type="number" min={0}
              value={ingQty}
              onChange={(e) => setIngQty(e.target.value)}
              className="w-20 h-9"
            />
            <Select value={ingUnit} onValueChange={setIngUnit}>
              <SelectTrigger className="w-20 h-9 text-sm">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {["g", "kg", "ml", "L", "un", "tbsp", "tsp"].map((u) => (
                  <SelectItem key={u} value={u}>{u}</SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Button size="sm" onClick={handleAddIngredient} disabled={addIngMut.isPending}>
              <Plus className="h-4 w-4" />
            </Button>
          </div>

          {/* Toggle active */}
          <div className="flex justify-end mt-2">
            <Button
              variant="outline"
              size="sm"
              onClick={() => toggleMut.mutate({ id: recipe.id, active: !recipe.isActive })}
            >
              {recipe.isActive ? "Desativar" : "Ativar"} ficha
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}

export default function RecipesPage() {
  const { data: recipes = [], isLoading } = useRecipeCards();
  const { data: products = [] }           = useProducts(false);
  const createMut                         = useCreateRecipeCard();

  const [newProductId, setNewProductId]   = useState("");
  const [newYield, setNewYield]           = useState("");
  const [newYieldUnit, setNewYieldUnit]   = useState("porções");
  const [showCreate, setShowCreate]       = useState(false);

  const existingProductIds = new Set(recipes.map((r) => r.productId));
  const availableProducts  = products.filter((p) => p.isActive && !existingProductIds.has(p.id));

  const handleCreate = () => {
    if (!newProductId || !newYield) return;
    createMut.mutate(
      { productId: newProductId, yield: parseFloat(newYield), yieldUnit: newYieldUnit },
      { onSuccess: () => { setNewProductId(""); setNewYield(""); setShowCreate(false); } }
    );
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold">Fichas Técnicas</h1>
          <p className="text-sm text-muted-foreground">
            Gerencie ingredientes e acompanhe o CMV por prato.
          </p>
        </div>
        <Button size="sm" onClick={() => setShowCreate(!showCreate)}>
          <Plus className="h-4 w-4 mr-1" /> Nova ficha
        </Button>
      </div>

      {showCreate && (
        <div className="border border-border rounded-xl p-4 bg-muted/30 flex gap-3 flex-wrap items-end">
          <div className="flex-1 min-w-[160px]">
            <label className="text-xs text-muted-foreground mb-1 block">Produto</label>
            <Select value={newProductId} onValueChange={setNewProductId}>
              <SelectTrigger><SelectValue placeholder="Selecionar produto" /></SelectTrigger>
              <SelectContent>
                {availableProducts.map((p) => (
                  <SelectItem key={p.id} value={p.id}>{p.name}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div>
            <label className="text-xs text-muted-foreground mb-1 block">Rendimento</label>
            <Input type="number" min={0} value={newYield}
              onChange={(e) => setNewYield(e.target.value)} className="w-24" />
          </div>
          <div>
            <label className="text-xs text-muted-foreground mb-1 block">Unidade</label>
            <Select value={newYieldUnit} onValueChange={setNewYieldUnit}>
              <SelectTrigger className="w-28"><SelectValue /></SelectTrigger>
              <SelectContent>
                {["porções", "kg", "L", "un"].map((u) => (
                  <SelectItem key={u} value={u}>{u}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <Button onClick={handleCreate} disabled={createMut.isPending}>
            Criar ficha
          </Button>
        </div>
      )}

      {isLoading ? (
        <div className="space-y-3">
          {[1, 2, 3].map((i) => (
            <div key={i} className="h-16 rounded-xl bg-muted animate-pulse" />
          ))}
        </div>
      ) : recipes.length === 0 ? (
        <p className="text-center text-muted-foreground py-12 text-sm">
          Nenhuma ficha técnica cadastrada.
        </p>
      ) : (
        <div className="space-y-3">
          {recipes.map((r) => <RecipeRow key={r.id} recipe={r} />)}
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Add route to AppRouter.tsx**

Add imports:
```tsx
import RecipesPage from "@/modules/restaurante/pages/RecipesPage";
```

Inside the `<WaiterLayout />` route block (restaurante module), add:
```tsx
<Route path="/restaurante/fichas" element={<RecipesPage />} />
```

Since RecipesPage uses the main app layout (not WaiterLayout), it should go under `<MainAppLayout />` inside the restaurante module route:
```tsx
{/* Restaurante pages that use MainAppLayout (admin/management) */}
<Route element={<ProtectedRoute />}>
  <Route element={<ModuleRoute moduleKey="restaurante" />}>
    <Route element={<MainAppLayout />}>
      <Route path="/restaurante/fichas"       element={<RecipesPage />} />
      <Route path="/restaurante/configuracoes" element={<FoodSettingsPage />} />
      <Route path="/restaurante/delivery"     element={<DeliveryPage />} />
      <Route path="/restaurante/eventos"      element={<EventsPage />} />
    </Route>
  </Route>
</Route>
```

- [ ] **Step 5: TypeScript check and commit**
```bash
npx tsc --noEmit
git add src/modules/restaurante/api/recipes.api.ts
git add src/modules/restaurante/hooks/useRecipeCards.ts
git add src/modules/restaurante/pages/RecipesPage.tsx
git add src/app/router/AppRouter.tsx
git commit -m "feat(restaurante): add RecipesPage with CMV calculation and ingredient management"
```

---

## Task V2-02: CMV Dashboard Blocks

**Files:**
- Create: `nexo-main/src/modules/dashboard/components/CmvBlocks.tsx`
- Modify: `nexo-main/src/modules/dashboard/pages/DashboardPage.tsx`

- [ ] **Step 1: Create CmvBlocks**

```tsx
// nexo-main/src/modules/dashboard/components/CmvBlocks.tsx
import { useRecipeCards } from "@/modules/restaurante/hooks/useRecipeCards";
import { TrendingUp, AlertTriangle } from "lucide-react";
import { cn } from "@/lib/utils";

const CMV_THRESHOLD = 45; // above this = alert

export function CmvBlocks() {
  const { data: recipes = [], isLoading } = useRecipeCards();

  const active = recipes.filter((r) => r.isActive && r.cmvPercent > 0);
  const sorted = [...active].sort((a, b) => b.cmvPercent - a.cmvPercent);
  const top5   = sorted.slice(0, 5);
  const alerts = sorted.filter((r) => r.cmvPercent > CMV_THRESHOLD);

  if (isLoading) {
    return <div className="h-40 rounded-xl bg-muted animate-pulse" />;
  }

  if (active.length === 0) {
    return (
      <div className="rounded-xl border border-border bg-card p-5">
        <p className="text-xs font-medium text-muted-foreground uppercase tracking-wide mb-2">
          CMV por prato
        </p>
        <p className="text-sm text-muted-foreground">
          Nenhuma ficha técnica ativa com preço de custo calculado.
        </p>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {/* Alert row */}
      {alerts.length > 0 && (
        <div className="rounded-xl border border-amber-200 bg-amber-50 dark:border-amber-900/40 dark:bg-amber-900/10 p-4 flex items-start gap-3">
          <AlertTriangle className="h-4 w-4 text-amber-500 mt-0.5 shrink-0" />
          <div>
            <p className="text-sm font-medium text-amber-700 dark:text-amber-400">
              {alerts.length} prato(s) com CMV acima de {CMV_THRESHOLD}%
            </p>
            <p className="text-xs text-amber-600 dark:text-amber-500 mt-0.5">
              {alerts.map((r) => r.productName).join(", ")}
            </p>
          </div>
        </div>
      )}

      {/* Top 5 table */}
      <div className="rounded-xl border border-border bg-card p-5">
        <div className="flex items-center gap-2 mb-3">
          <TrendingUp className="h-4 w-4 text-muted-foreground" />
          <p className="text-xs font-medium text-muted-foreground uppercase tracking-wide">
            Top CMV — pratos ativos
          </p>
        </div>
        <div className="space-y-2">
          {top5.map((recipe) => (
            <div key={recipe.id} className="flex items-center gap-3">
              <span className="flex-1 text-sm truncate">{recipe.productName}</span>
              <div className="flex items-center gap-2">
                <div className="w-24 h-1.5 rounded-full bg-muted overflow-hidden">
                  <div
                    className={cn(
                      "h-full rounded-full",
                      recipe.cmvPercent <= 30 ? "bg-green-500" :
                      recipe.cmvPercent <= 45 ? "bg-amber-500" : "bg-red-500"
                    )}
                    style={{ width: `${Math.min(recipe.cmvPercent, 100)}%` }}
                  />
                </div>
                <span className={cn(
                  "text-xs font-semibold w-12 text-right",
                  recipe.cmvPercent <= 30 ? "text-green-600" :
                  recipe.cmvPercent <= 45 ? "text-amber-600" : "text-red-600"
                )}>
                  {recipe.cmvPercent.toFixed(1)}%
                </span>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Add CmvBlocks to DashboardPage**

```tsx
import { CmvBlocks } from "@/modules/dashboard/components/CmvBlocks";

// Inside the JSX, after RestauranteBlocks:
{session?.modules.includes("restaurante") && <CmvBlocks />}
```

- [ ] **Step 3: TypeScript check and commit**
```bash
npx tsc --noEmit
git add src/modules/dashboard/components/CmvBlocks.tsx
git add src/modules/dashboard/pages/DashboardPage.tsx
git commit -m "feat(restaurante): add CMV dashboard blocks with alert threshold"
```

---

## Task V2-03: FoodServiceSettings Admin UI

**Files:**
- Create: `nexo-main/src/modules/restaurante/pages/FoodSettingsPage.tsx`

- [ ] **Step 1: Create FoodSettingsPage**

```tsx
// nexo-main/src/modules/restaurante/pages/FoodSettingsPage.tsx
import { useState, useEffect } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { useFoodSettings } from "../hooks/useFoodSettings";
import { useQueryClient, useMutation } from "@tanstack/react-query";
import { updateFoodSettings } from "../api/restaurante.api";
import { FOOD_SETTINGS_KEY } from "../hooks/useFoodSettings";
import type { UpdateFoodServiceSettingsRequest } from "../types";

export default function FoodSettingsPage() {
  const { session }  = useAuth();
  const storeId      = session?.storeId ?? "";
  const qc           = useQueryClient();

  const { data: settings, isLoading } = useFoodSettings(storeId);

  const [form, setForm] = useState<UpdateFoodServiceSettingsRequest>({
    storeType:            "restaurant",
    couvertEnabled:       false,
    couvertPricePerPerson: null,
    couvertAutomatic:     false,
    serviceFeeEnabled:    false,
    serviceFeePercent:    null,
    orderTypesEnabled:    "DineIn,Counter,Takeaway",
  });

  useEffect(() => {
    if (settings) {
      setForm({
        storeType:            settings.storeType,
        couvertEnabled:       settings.couvertEnabled,
        couvertPricePerPerson: settings.couvertPricePerPerson,
        couvertAutomatic:     settings.couvertAutomatic,
        serviceFeeEnabled:    settings.serviceFeeEnabled,
        serviceFeePercent:    settings.serviceFeePercent,
        orderTypesEnabled:    settings.orderTypesEnabled,
      });
    }
  }, [settings]);

  const saveMut = useMutation({
    mutationFn: () => updateFoodSettings(form),
    onSuccess: () => qc.invalidateQueries({ queryKey: FOOD_SETTINGS_KEY(storeId) }),
  });

  if (isLoading) {
    return <div className="h-40 rounded-xl bg-muted animate-pulse" />;
  }

  return (
    <div className="max-w-lg space-y-8">
      <div>
        <h1 className="text-xl font-semibold">Configurações do Restaurante</h1>
        <p className="text-sm text-muted-foreground mt-1">
          Defina couvert, taxa de serviço e tipo de operação.
        </p>
      </div>

      {/* Store type */}
      <section className="space-y-3">
        <h2 className="text-sm font-semibold">Tipo de estabelecimento</h2>
        <Select
          value={form.storeType}
          onValueChange={(v) => setForm({ ...form, storeType: v })}
        >
          <SelectTrigger>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="restaurant">Restaurante</SelectItem>
            <SelectItem value="bar">Bar</SelectItem>
            <SelectItem value="pub">Pub</SelectItem>
          </SelectContent>
        </Select>
      </section>

      {/* Couvert */}
      <section className="space-y-3 border border-border rounded-xl p-4">
        <div className="flex items-center justify-between">
          <div>
            <p className="font-medium text-sm">Couvert</p>
            <p className="text-xs text-muted-foreground">
              Valor por pessoa, cobrado automaticamente ou no fechamento.
            </p>
          </div>
          <Switch
            checked={form.couvertEnabled}
            onCheckedChange={(v) => setForm({ ...form, couvertEnabled: v })}
          />
        </div>

        {form.couvertEnabled && (
          <>
            <div>
              <label className="text-xs text-muted-foreground mb-1 block">
                Valor por pessoa (R$)
              </label>
              <Input
                type="number" min={0} step={0.50}
                value={form.couvertPricePerPerson ?? ""}
                onChange={(e) =>
                  setForm({ ...form, couvertPricePerPerson: parseFloat(e.target.value) || null })
                }
              />
            </div>
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm">Couvert automático</p>
                <p className="text-xs text-muted-foreground">
                  Aplica ao abrir comanda. Se desativado, aplica no fechamento.
                </p>
              </div>
              <Switch
                checked={form.couvertAutomatic}
                onCheckedChange={(v) => setForm({ ...form, couvertAutomatic: v })}
              />
            </div>
          </>
        )}
      </section>

      {/* Service fee */}
      <section className="space-y-3 border border-border rounded-xl p-4">
        <div className="flex items-center justify-between">
          <div>
            <p className="font-medium text-sm">Taxa de serviço</p>
            <p className="text-xs text-muted-foreground">
              Percentual sobre o subtotal dos itens (nunca sobre couvert).
            </p>
          </div>
          <Switch
            checked={form.serviceFeeEnabled}
            onCheckedChange={(v) => setForm({ ...form, serviceFeeEnabled: v })}
          />
        </div>

        {form.serviceFeeEnabled && (
          <div>
            <label className="text-xs text-muted-foreground mb-1 block">
              Percentual (%)
            </label>
            <Input
              type="number" min={0} max={100} step={0.5}
              value={form.serviceFeePercent ?? ""}
              onChange={(e) =>
                setForm({ ...form, serviceFeePercent: parseFloat(e.target.value) || null })
              }
            />
          </div>
        )}
      </section>

      <Button
        onClick={() => saveMut.mutate()}
        disabled={saveMut.isPending}
        className="w-full"
      >
        {saveMut.isPending ? "Salvando..." : "Salvar configurações"}
      </Button>

      {saveMut.isSuccess && (
        <p className="text-sm text-green-600 text-center">Configurações salvas.</p>
      )}
    </div>
  );
}
```

- [ ] **Step 2: TypeScript check and commit**
```bash
npx tsc --noEmit
git add src/modules/restaurante/pages/FoodSettingsPage.tsx
git commit -m "feat(restaurante): add FoodSettingsPage for couvert and service fee admin config"
```

---

## Task V2-04: Couvert Manual Flow (Backend Update)

**Files:**
- No new files — this is already handled in `B-12` (PayAsync accepts `partySize` and recalculates couvert when `CouvertAutomatic=false`).
- Frontend: PaymentDrawer already prompts for partySize when `!settings.couvertAutomatic && !order.partySize` (implemented in F-10).

- [ ] **Step 1: Verify end-to-end flow**

Run integration test `PayOrder_WithServiceFee_CalculatesCorrectTotal` from B-15, then manually test:
1. Set `CouvertAutomatic = false` in FoodSettingsPage
2. Open a DineIn order without party size
3. Add items
4. Close order
5. In PaymentDrawer: enter party size → verify couvert recalculates before "Confirmar pagamento"

Expected: the breakdown updates in real time as partySize is entered.

- [ ] **Step 2: Commit (if any minor fix needed)**
```bash
git commit -m "test(restaurante): verify manual couvert flow end-to-end"
```

---

## Task V2-05: RestDeliveryOrder — Backend Entity + Service + Controller

**Files:**
- Create: `nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestDeliveryOrder.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestDeliveryOrderConfiguration.cs`
- Create: `nexo-backend/src/Nexo.Application/Modules/Restaurante/Interfaces/IDeliveryOrderRepository.cs`
- Create: `nexo-backend/src/Nexo.Application/Modules/Restaurante/DeliveryOrderService.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Repositories/Modules/Restaurante/DeliveryOrderRepository.cs`
- Create: `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/DeliveryOrdersController.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/NexoDbContext.cs`

- [ ] **Step 1: Create RestDeliveryOrder entity**

```csharp
// nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestDeliveryOrder.cs
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Restaurante;

public enum DeliveryChannel { Own, IFood, WhatsApp, Rappi, Other }
public enum DeliveryStatus  { Pending, Preparing, OutForDelivery, Delivered, Cancelled }

/// <summary>
/// Manual delivery order intake. v1: no external API — operator records order manually.
/// Channel tracking: Own / iFood / WhatsApp / Rappi / Other.
/// </summary>
public class RestDeliveryOrder : StoreEntity
{
    private RestDeliveryOrder() { }
    private RestDeliveryOrder(Guid tenantId) : base(tenantId) { }

    public int             OrderNumber     { get; private set; }
    public DeliveryChannel Channel         { get; private set; }
    public DeliveryStatus  Status          { get; private set; }
    public string          CustomerName    { get; private set; } = string.Empty;
    public string?         CustomerPhone   { get; private set; }
    public string?         DeliveryAddress { get; private set; }
    public decimal         Total           { get; private set; }
    public string?         Notes           { get; private set; }
    public DateTime        ReceivedAt      { get; private set; }
    public DateTime?       DeliveredAt     { get; private set; }
    public DateTime?       CancelledAt     { get; private set; }

    public static RestDeliveryOrder Create(
        Guid tenantId, int orderNumber, DeliveryChannel channel,
        string customerName, decimal total,
        string? customerPhone = null, string? address = null, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(customerName))
            throw new DomainException("Customer name is required.");
        if (total < 0)
            throw new DomainException("Total cannot be negative.");

        return new RestDeliveryOrder(tenantId)
        {
            OrderNumber     = orderNumber,
            Channel         = channel,
            Status          = DeliveryStatus.Pending,
            CustomerName    = customerName.Trim(),
            CustomerPhone   = customerPhone?.Trim(),
            DeliveryAddress = address?.Trim(),
            Total           = total,
            Notes           = notes?.Trim(),
            ReceivedAt      = DateTime.UtcNow,
        };
    }

    public void SetPreparing()   { Status = DeliveryStatus.Preparing;       SetUpdatedAt(); }
    public void SetOutForDelivery() { Status = DeliveryStatus.OutForDelivery; SetUpdatedAt(); }
    public void SetDelivered()   { Status = DeliveryStatus.Delivered; DeliveredAt = DateTime.UtcNow; SetUpdatedAt(); }
    public void Cancel()
    {
        if (Status == DeliveryStatus.Delivered)
            throw new DomainException("Cannot cancel a delivered order.");
        Status      = DeliveryStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        SetUpdatedAt();
    }
}
```

- [ ] **Step 2: Create EF configuration**

```csharp
// RestDeliveryOrderConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class RestDeliveryOrderConfiguration : IEntityTypeConfiguration<RestDeliveryOrder>
{
    public void Configure(EntityTypeBuilder<RestDeliveryOrder> builder)
    {
        builder.ToTable("rest_delivery_orders", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();
        builder.Property(x => x.OrderNumber).HasColumnName("order_number").IsRequired();
        builder.Property(x => x.Channel).HasColumnName("channel").HasMaxLength(20).HasConversion<string>().IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).HasConversion<string>().IsRequired();
        builder.Property(x => x.CustomerName).HasColumnName("customer_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.CustomerPhone).HasColumnName("customer_phone").HasMaxLength(30);
        builder.Property(x => x.DeliveryAddress).HasColumnName("delivery_address").HasMaxLength(500);
        builder.Property(x => x.Total).HasColumnName("total").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(500);
        builder.Property(x => x.ReceivedAt).HasColumnName("received_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.DeliveredAt).HasColumnName("delivered_at").HasColumnType("timestamptz");
        builder.Property(x => x.CancelledAt).HasColumnName("cancelled_at").HasColumnType("timestamptz");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Nexo.Domain.Entities.Store>()
            .WithMany().HasForeignKey(x => x.StoreId)
            .HasConstraintName("fk_rest_delivery_orders_stores").OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.Status })
            .HasDatabaseName("ix_rest_delivery_orders_tenant_store_status");
        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.ReceivedAt })
            .HasDatabaseName("ix_rest_delivery_orders_tenant_store_received");
    }
}
```

- [ ] **Step 3: Add DTOs to RestauranteDtos.cs**

```csharp
// ═══════════════════════════════════════════════════════════
// DELIVERY
// ═══════════════════════════════════════════════════════════

public record CreateDeliveryOrderRequest(
    string  Channel,          // "Own"|"IFood"|"WhatsApp"|"Rappi"|"Other"
    string  CustomerName,
    decimal Total,
    string? CustomerPhone   = null,
    string? DeliveryAddress = null,
    string? Notes           = null);

public record UpdateDeliveryStatusRequest(string Status);

public record DeliveryOrderDto(
    Guid    Id, int OrderNumber, string Channel, string Status,
    string  CustomerName, string? CustomerPhone, string? DeliveryAddress,
    decimal Total, string? Notes,
    DateTime ReceivedAt, DateTime? DeliveredAt, DateTime? CancelledAt);
```

- [ ] **Step 4: Create DeliveryOrderService, Repository, Controller**

```csharp
// IDeliveryOrderRepository.cs
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante.Interfaces;

public interface IDeliveryOrderRepository
{
    Task<IReadOnlyList<RestDeliveryOrder>> GetAllAsync(CancellationToken ct = default);
    Task<RestDeliveryOrder?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<int> GetNextNumberAsync(CancellationToken ct = default);
    Task AddAsync(RestDeliveryOrder order, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

```csharp
// DeliveryOrderService.cs
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante;

public class DeliveryOrderService
{
    private readonly IDeliveryOrderRepository _repo;
    private readonly ICurrentTenant           _currentTenant;

    public DeliveryOrderService(IDeliveryOrderRepository repo, ICurrentTenant currentTenant)
    {
        _repo = repo; _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<DeliveryOrderDto>> GetAllAsync(CancellationToken ct = default)
        => (await _repo.GetAllAsync(ct)).Select(Map).ToList();

    public async Task<DeliveryOrderDto> CreateAsync(CreateDeliveryOrderRequest req, CancellationToken ct = default)
    {
        var channel = Enum.Parse<DeliveryChannel>(req.Channel, ignoreCase: true);
        var number  = await _repo.GetNextNumberAsync(ct);
        var order   = RestDeliveryOrder.Create(
            _currentTenant.Id, number, channel,
            req.CustomerName, req.Total,
            req.CustomerPhone, req.DeliveryAddress, req.Notes);
        await _repo.AddAsync(order, ct);
        await _repo.SaveChangesAsync(ct);
        return Map(order);
    }

    public async Task<DeliveryOrderDto> UpdateStatusAsync(
        Guid id, UpdateDeliveryStatusRequest req, CancellationToken ct = default)
    {
        var order  = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("DeliveryOrder", id);
        var status = Enum.Parse<DeliveryStatus>(req.Status, ignoreCase: true);
        switch (status)
        {
            case DeliveryStatus.Preparing:       order.SetPreparing();       break;
            case DeliveryStatus.OutForDelivery:  order.SetOutForDelivery();  break;
            case DeliveryStatus.Delivered:       order.SetDelivered();       break;
            case DeliveryStatus.Cancelled:       order.Cancel();             break;
            default: throw new DomainException($"Cannot set status {req.Status} via this endpoint.");
        }
        await _repo.SaveChangesAsync(ct);
        return Map(order);
    }

    private static DeliveryOrderDto Map(RestDeliveryOrder o) => new(
        o.Id, o.OrderNumber, o.Channel.ToString(), o.Status.ToString(),
        o.CustomerName, o.CustomerPhone, o.DeliveryAddress,
        o.Total, o.Notes, o.ReceivedAt, o.DeliveredAt, o.CancelledAt);
}
```

```csharp
// DeliveryOrderRepository.cs
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Restaurante;

public class DeliveryOrderRepository : IDeliveryOrderRepository
{
    private readonly NexoDbContext _context;
    public DeliveryOrderRepository(NexoDbContext ctx) => _context = ctx;

    public async Task<IReadOnlyList<RestDeliveryOrder>> GetAllAsync(CancellationToken ct = default)
        => await _context.RestDeliveryOrders
            .OrderByDescending(x => x.ReceivedAt).ToListAsync(ct);

    public async Task<RestDeliveryOrder?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.RestDeliveryOrders.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<int> GetNextNumberAsync(CancellationToken ct = default)
    {
        var max = await _context.RestDeliveryOrders.MaxAsync(x => (int?)x.OrderNumber, ct);
        return (max ?? 0) + 1;
    }

    public async Task AddAsync(RestDeliveryOrder order, CancellationToken ct = default)
        => await _context.RestDeliveryOrders.AddAsync(order, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
```

```csharp
// DeliveryOrdersController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Restaurante;

namespace Nexo.Api.Controllers.Modules.Restaurante;

[ApiController]
[Authorize]
[RequireModule("restaurante")]
[Route("api/restaurante/delivery-orders")]
public class DeliveryOrdersController : ControllerBase
{
    private readonly DeliveryOrderService _service;
    public DeliveryOrdersController(DeliveryOrderService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _service.GetAllAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDeliveryOrderRequest req, CancellationToken ct)
        => Ok(await _service.CreateAsync(req, ct));

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid id, [FromBody] UpdateDeliveryStatusRequest req, CancellationToken ct)
        => Ok(await _service.UpdateStatusAsync(id, req, ct));
}
```

- [ ] **Step 5: Register in DI, add DbSet, generate migration**

`Application/DependencyInjection.cs`:
```csharp
services.AddScoped<DeliveryOrderService>();
services.AddScoped<RestEventService>();
```

`Infrastructure/DependencyInjection.cs`:
```csharp
services.AddScoped<IDeliveryOrderRepository, DeliveryOrderRepository>();
services.AddScoped<IRestEventRepository, RestEventRepository>();
```

`NexoDbContext.cs`:
```csharp
public DbSet<RestDeliveryOrder> RestDeliveryOrders => Set<RestDeliveryOrder>();
```

```bash
dotnet ef migrations add AddDeliveryOrders \
  --project src/Nexo.Infrastructure --startup-project src/Nexo.Api
dotnet ef database update --project src/Nexo.Infrastructure --startup-project src/Nexo.Api
dotnet build src/Nexo.Api/Nexo.Api.csproj
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**
```bash
git add nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestDeliveryOrder.cs
git add nexo-backend/src/Nexo.Infrastructure/
git add nexo-backend/src/Nexo.Application/Modules/Restaurante/
git add nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/DeliveryOrdersController.cs
git commit -m "feat(restaurante): add RestDeliveryOrder entity, service, and controller"
```

---

## Task V2-06: DeliveryPage Frontend

**Files:**
- Create: `nexo-main/src/modules/restaurante/api/delivery.api.ts`
- Create: `nexo-main/src/modules/restaurante/hooks/useDeliveryOrders.ts`
- Create: `nexo-main/src/modules/restaurante/pages/DeliveryPage.tsx`

- [ ] **Step 1: Create delivery.api.ts**

```typescript
// delivery.api.ts
import { apiClient } from "@/services/api-client";

export interface DeliveryOrderDto {
  id: string;
  orderNumber: number;
  channel: string;
  status: string;
  customerName: string;
  customerPhone: string | null;
  deliveryAddress: string | null;
  total: number;
  notes: string | null;
  receivedAt: string;
  deliveredAt: string | null;
  cancelledAt: string | null;
}

export interface CreateDeliveryOrderRequest {
  channel: string;
  customerName: string;
  total: number;
  customerPhone?: string | null;
  deliveryAddress?: string | null;
  notes?: string | null;
}

export const listDeliveryOrders = (): Promise<DeliveryOrderDto[]> =>
  apiClient.get("/restaurante/delivery-orders");

export const createDeliveryOrder = (req: CreateDeliveryOrderRequest): Promise<DeliveryOrderDto> =>
  apiClient.post("/restaurante/delivery-orders", req);

export const updateDeliveryStatus = (id: string, status: string): Promise<DeliveryOrderDto> =>
  apiClient.patch(`/restaurante/delivery-orders/${id}/status`, { status });
```

- [ ] **Step 2: Create useDeliveryOrders.ts**

```typescript
// useDeliveryOrders.ts
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  listDeliveryOrders, createDeliveryOrder, updateDeliveryStatus,
} from "../api/delivery.api";
import type { CreateDeliveryOrderRequest } from "../api/delivery.api";

const DELIVERY_KEY = ["delivery-orders"] as const;

export function useDeliveryOrders() {
  return useQuery({ queryKey: DELIVERY_KEY, queryFn: listDeliveryOrders, refetchInterval: 30_000 });
}

export function useCreateDeliveryOrder() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateDeliveryOrderRequest) => createDeliveryOrder(req),
    onSuccess: () => qc.invalidateQueries({ queryKey: DELIVERY_KEY }),
  });
}

export function useUpdateDeliveryStatus() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, status }: { id: string; status: string }) =>
      updateDeliveryStatus(id, status),
    onSuccess: () => qc.invalidateQueries({ queryKey: DELIVERY_KEY }),
  });
}
```

- [ ] **Step 3: Create DeliveryPage**

```tsx
// DeliveryPage.tsx
import { useState } from "react";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import {
  Sheet, SheetContent, SheetHeader, SheetTitle,
} from "@/components/ui/sheet";
import { cn } from "@/lib/utils";
import {
  useDeliveryOrders, useCreateDeliveryOrder, useUpdateDeliveryStatus,
} from "../hooks/useDeliveryOrders";
import type { DeliveryOrderDto } from "../api/delivery.api";

const CHANNELS = ["Own", "IFood", "WhatsApp", "Rappi", "Other"] as const;
const CHANNEL_LABELS: Record<string, string> = {
  Own: "Próprio", IFood: "iFood", WhatsApp: "WhatsApp", Rappi: "Rappi", Other: "Outro",
};

const STATUS_NEXT: Record<string, string | null> = {
  Pending: "Preparing", Preparing: "OutForDelivery",
  OutForDelivery: "Delivered", Delivered: null, Cancelled: null,
};
const STATUS_ACTION: Record<string, string> = {
  Pending: "Iniciar preparo", Preparing: "Saiu para entrega",
  OutForDelivery: "Entregue", Delivered: "Entregue",
};
const STATUS_COLOR: Record<string, string> = {
  Pending:       "bg-muted text-muted-foreground",
  Preparing:     "bg-amber-100 text-amber-700",
  OutForDelivery:"bg-blue-100 text-blue-700",
  Delivered:     "bg-green-100 text-green-700",
  Cancelled:     "bg-red-100 text-red-700",
};
const STATUS_LABEL: Record<string, string> = {
  Pending: "Pendente", Preparing: "Preparando",
  OutForDelivery: "Saiu", Delivered: "Entregue", Cancelled: "Cancelado",
};

function DeliveryCard({ order }: { order: DeliveryOrderDto }) {
  const updateMut = useUpdateDeliveryStatus();
  const next      = STATUS_NEXT[order.status];

  return (
    <div className="rounded-xl border border-border p-4 space-y-2">
      <div className="flex items-start justify-between gap-2">
        <div>
          <p className="font-medium text-sm">#{order.orderNumber} · {order.customerName}</p>
          <p className="text-xs text-muted-foreground">
            {CHANNEL_LABELS[order.channel] ?? order.channel}
            {order.customerPhone && ` · ${order.customerPhone}`}
          </p>
          {order.deliveryAddress && (
            <p className="text-xs text-muted-foreground">{order.deliveryAddress}</p>
          )}
        </div>
        <div className="text-right">
          <p className="font-semibold text-sm">R$ {order.total.toFixed(2)}</p>
          <span className={cn("text-[10px] font-medium px-1.5 py-0.5 rounded mt-1 inline-block", STATUS_COLOR[order.status])}>
            {STATUS_LABEL[order.status]}
          </span>
        </div>
      </div>
      {next && (
        <Button
          variant="outline" size="sm" className="w-full"
          onClick={() => updateMut.mutate({ id: order.id, status: next })}
          disabled={updateMut.isPending}
        >
          {STATUS_ACTION[order.status]}
        </Button>
      )}
    </div>
  );
}

export default function DeliveryPage() {
  const { data: orders = [], isLoading } = useDeliveryOrders();
  const createMut = useCreateDeliveryOrder();

  const [sheetOpen, setSheetOpen] = useState(false);
  const [form, setForm] = useState({
    channel: "Own", customerName: "", total: "",
    customerPhone: "", deliveryAddress: "", notes: "",
  });

  const active    = orders.filter((o) => !["Delivered", "Cancelled"].includes(o.status));
  const completed = orders.filter((o) => ["Delivered", "Cancelled"].includes(o.status)).slice(0, 10);

  const handleCreate = () => {
    if (!form.customerName || !form.total) return;
    createMut.mutate(
      {
        channel:         form.channel,
        customerName:    form.customerName,
        total:           parseFloat(form.total),
        customerPhone:   form.customerPhone || null,
        deliveryAddress: form.deliveryAddress || null,
        notes:           form.notes || null,
      },
      {
        onSuccess: () => {
          setSheetOpen(false);
          setForm({ channel: "Own", customerName: "", total: "", customerPhone: "", deliveryAddress: "", notes: "" });
        },
      }
    );
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold">Delivery</h1>
          <p className="text-sm text-muted-foreground">
            Pedidos em andamento: {active.length}
          </p>
        </div>
        <Button size="sm" onClick={() => setSheetOpen(true)}>
          <Plus className="h-4 w-4 mr-1" /> Novo pedido
        </Button>
      </div>

      {isLoading ? (
        <div className="space-y-3">
          {[1, 2].map((i) => <div key={i} className="h-24 rounded-xl bg-muted animate-pulse" />)}
        </div>
      ) : (
        <>
          {active.length === 0 && (
            <p className="text-center text-muted-foreground py-8 text-sm">
              Nenhum pedido em andamento.
            </p>
          )}
          <div className="space-y-3">
            {active.map((o) => <DeliveryCard key={o.id} order={o} />)}
          </div>
          {completed.length > 0 && (
            <>
              <h2 className="text-sm font-medium text-muted-foreground mt-4">Recentes</h2>
              <div className="space-y-3 opacity-60">
                {completed.map((o) => <DeliveryCard key={o.id} order={o} />)}
              </div>
            </>
          )}
        </>
      )}

      <Sheet open={sheetOpen} onOpenChange={(v) => !v && setSheetOpen(false)}>
        <SheetContent side="bottom" className="rounded-t-2xl pb-8 overflow-y-auto max-h-[90vh]">
          <SheetHeader className="mb-4">
            <SheetTitle>Novo pedido de delivery</SheetTitle>
          </SheetHeader>
          <div className="space-y-3">
            <Select value={form.channel} onValueChange={(v) => setForm({ ...form, channel: v })}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                {CHANNELS.map((c) => (
                  <SelectItem key={c} value={c}>{CHANNEL_LABELS[c]}</SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Input placeholder="Nome do cliente *" value={form.customerName}
              onChange={(e) => setForm({ ...form, customerName: e.target.value })} />
            <Input placeholder="Total (R$) *" type="number" min={0} value={form.total}
              onChange={(e) => setForm({ ...form, total: e.target.value })} />
            <Input placeholder="Telefone" value={form.customerPhone}
              onChange={(e) => setForm({ ...form, customerPhone: e.target.value })} />
            <Input placeholder="Endereço de entrega" value={form.deliveryAddress}
              onChange={(e) => setForm({ ...form, deliveryAddress: e.target.value })} />
            <Input placeholder="Observação" value={form.notes}
              onChange={(e) => setForm({ ...form, notes: e.target.value })} />
            <Button className="w-full" onClick={handleCreate} disabled={createMut.isPending}>
              {createMut.isPending ? "Salvando..." : "Registrar pedido"}
            </Button>
          </div>
        </SheetContent>
      </Sheet>
    </div>
  );
}
```

- [ ] **Step 4: TypeScript check and commit**
```bash
npx tsc --noEmit
git add src/modules/restaurante/api/delivery.api.ts
git add src/modules/restaurante/hooks/useDeliveryOrders.ts
git add src/modules/restaurante/pages/DeliveryPage.tsx
git commit -m "feat(restaurante): add DeliveryPage for manual delivery order intake"
```

---

## Task V2-07: RestEvent — Backend Entity + EventsPage Frontend

**Files:**
- Create: `nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestEvent.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestEventConfiguration.cs`
- Create: `nexo-backend/src/Nexo.Application/Modules/Restaurante/Interfaces/IRestEventRepository.cs`
- Create: `nexo-backend/src/Nexo.Application/Modules/Restaurante/RestEventService.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Repositories/Modules/Restaurante/RestEventRepository.cs`
- Create: `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/RestEventsController.cs`
- Create: `nexo-main/src/modules/restaurante/api/events.api.ts`
- Create: `nexo-main/src/modules/restaurante/hooks/useRestEvents.ts`
- Create: `nexo-main/src/modules/restaurante/pages/EventsPage.tsx`

- [ ] **Step 1: Create RestEvent entity**

```csharp
// nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestEvent.cs
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Bar/pub event: show, live music, themed night.
/// Tracks attraction cost vs. revenue generated during the event.
/// </summary>
public class RestEvent : StoreEntity
{
    private RestEvent() { }
    private RestEvent(Guid tenantId) : base(tenantId) { }

    public string   Name            { get; private set; } = string.Empty;
    public string?  Description     { get; private set; }
    public DateTime EventDate       { get; private set; }
    public decimal  AttractionCost  { get; private set; }  // cost of performer/show
    public decimal  RevenueAmount   { get; private set; }  // manually recorded revenue
    public string?  Notes           { get; private set; }

    // Computed: margin = Revenue - AttractionCost
    public decimal Margin => RevenueAmount - AttractionCost;

    public static RestEvent Create(
        Guid tenantId, string name, DateTime eventDate,
        decimal attractionCost, string? description = null, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Event name is required.");
        if (attractionCost < 0)
            throw new DomainException("Attraction cost cannot be negative.");

        return new RestEvent(tenantId)
        {
            Name           = name.Trim(),
            Description    = description?.Trim(),
            EventDate      = eventDate,
            AttractionCost = attractionCost,
            RevenueAmount  = 0,
            Notes          = notes?.Trim(),
        };
    }

    public void UpdateRevenue(decimal revenue)
    {
        if (revenue < 0) throw new DomainException("Revenue cannot be negative.");
        RevenueAmount = revenue;
        SetUpdatedAt();
    }

    public void Update(string name, DateTime eventDate, decimal attractionCost, string? description, string? notes)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Event name is required.");
        Name           = name.Trim();
        EventDate      = eventDate;
        AttractionCost = attractionCost;
        Description    = description?.Trim();
        Notes          = notes?.Trim();
        SetUpdatedAt();
    }
}
```

- [ ] **Step 2: Create EF configuration**

```csharp
// RestEventConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class RestEventConfiguration : IEntityTypeConfiguration<RestEvent>
{
    public void Configure(EntityTypeBuilder<RestEvent> builder)
    {
        builder.ToTable("rest_events", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(1000);
        builder.Property(x => x.EventDate).HasColumnName("event_date").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.AttractionCost).HasColumnName("attraction_cost")
            .HasColumnType("numeric(18,2)").HasDefaultValue(0m).IsRequired();
        builder.Property(x => x.RevenueAmount).HasColumnName("revenue_amount")
            .HasColumnType("numeric(18,2)").HasDefaultValue(0m).IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(500);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.Ignore(x => x.Margin);

        builder.HasOne<Nexo.Domain.Entities.Store>()
            .WithMany().HasForeignKey(x => x.StoreId)
            .HasConstraintName("fk_rest_events_stores").OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.EventDate })
            .HasDatabaseName("ix_rest_events_tenant_store_date");
    }
}
```

- [ ] **Step 3: Create service, repository, controller**

Follow the same pattern as DeliveryOrderService. Key methods:
- `GetAllAsync()` — returns all events ordered by EventDate desc
- `CreateAsync(CreateRestEventRequest)` — fields: Name, EventDate, AttractionCost, Description, Notes
- `UpdateRevenueAsync(Guid id, decimal revenue)` — records actual revenue after the event
- `UpdateAsync(Guid id, UpdateRestEventRequest)` — edit event details

Add DTOs to `RestauranteDtos.cs`:
```csharp
public record CreateRestEventRequest(
    string   Name, DateTime EventDate, decimal AttractionCost,
    string?  Description = null, string? Notes = null);

public record UpdateRevenueRequest(decimal Revenue);

public record RestEventDto(
    Guid Id, string Name, string? Description,
    DateTime EventDate, decimal AttractionCost, decimal RevenueAmount, decimal Margin,
    string? Notes, DateTime CreatedAt);
```

Add to `NexoDbContext.cs`:
```csharp
public DbSet<RestEvent> RestEvents => Set<RestEvent>();
```

Generate migration:
```bash
dotnet ef migrations add AddRestEventsAndDelivery \
  --project src/Nexo.Infrastructure --startup-project src/Nexo.Api
dotnet ef database update --project src/Nexo.Infrastructure --startup-project src/Nexo.Api
dotnet build src/Nexo.Api/Nexo.Api.csproj
```

- [ ] **Step 4: Create events.api.ts, useRestEvents.ts, EventsPage.tsx**

Follow the same pattern as `delivery.api.ts` + `useDeliveryOrders.ts` + `DeliveryPage.tsx`.

`EventsPage` shows:
- List of events sorted by EventDate desc
- Each card: name, date, attraction cost, revenue, margin (green if positive, red if negative)
- "Registrar faturamento" button → small input to record post-event revenue
- "Novo evento" button → sheet with: name, date, attraction cost, description, notes

```typescript
// events.api.ts
import { apiClient } from "@/services/api-client";

export interface RestEventDto {
  id: string; name: string; description: string | null;
  eventDate: string; attractionCost: number; revenueAmount: number; margin: number;
  notes: string | null; createdAt: string;
}

export interface CreateRestEventRequest {
  name: string; eventDate: string; attractionCost: number;
  description?: string | null; notes?: string | null;
}

export const listRestEvents = (): Promise<RestEventDto[]> =>
  apiClient.get("/restaurante/events");

export const createRestEvent = (req: CreateRestEventRequest): Promise<RestEventDto> =>
  apiClient.post("/restaurante/events", req);

export const updateEventRevenue = (id: string, revenue: number): Promise<RestEventDto> =>
  apiClient.patch(`/restaurante/events/${id}/revenue`, { revenue });
```

- [ ] **Step 5: TypeScript check and final build**
```bash
npx tsc --noEmit
npm run build
```
Expected: 0 errors, build succeeds.

- [ ] **Step 6: Commit**
```bash
git add nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestEvent.cs
git add nexo-backend/src/Nexo.Infrastructure/
git add nexo-backend/src/Nexo.Application/Modules/Restaurante/
git add nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/RestEventsController.cs
git add nexo-main/src/modules/restaurante/api/events.api.ts
git add nexo-main/src/modules/restaurante/hooks/useRestEvents.ts
git add nexo-main/src/modules/restaurante/pages/EventsPage.tsx
git commit -m "feat(restaurante): add RestEvent entity and EventsPage for bar/pub event tracking"
```

---

## Phase 3 Complete

All 3 phases are now planned. Summary of plan files:

| File | Coverage |
|------|---------|
| `2026-04-13-food-service-phase1-backend.md` | B-01 – B-15 (backend foundation, SignalR) |
| `2026-04-13-food-service-phase2-frontend.md` | F-01 – F-12 (UI, hooks, SignalR client) |
| `2026-04-13-food-service-phase3-intelligence.md` | V2-01 – V2-07 (recipes, CMV, delivery, events) |

**Execution options:**
1. **Subagent-Driven (recommended)** — `superpowers:subagent-driven-development` — fresh subagent per task
2. **Inline** — `superpowers:executing-plans` — sequential with checkpoints

Checkpoints to validate manually before moving on:
- **B-14** — Test SignalR hub via Postman WebSocket before starting Phase 2
- **F-05** — Verify `useKitchenSocket` connects in browser DevTools before building KitchenPage
