import { useState, useMemo } from "react";
import {
  Sheet, SheetContent, SheetHeader, SheetTitle,
} from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Minus, Plus, Search } from "lucide-react";
import { cn } from "@/lib/utils";
import { useProducts, useCategories } from "@/modules/products/hooks/use-products";
import { useStockItems } from "@/modules/inventory/hooks/use-stock";
import { ModifierSelector } from "./ModifierSelector";
import { useModifierGroups } from "../hooks/useModifierGroups";
import type { AddOrderItemRequest } from "../types";

// ─── Constants ────────────────────────────────────────────────────────────────

const ALL = "__all__";

// ─── Main component ───────────────────────────────────────────────────────────

interface AddItemDrawerProps {
  open: boolean;
  onClose: () => void;
  onAdd: (req: AddOrderItemRequest) => void;
  isLoading: boolean;
}

export function AddItemDrawer({ open, onClose, onAdd, isLoading }: AddItemDrawerProps) {
  const { data: products  = [] } = useProducts(false);
  const { data: categories = [] } = useCategories();
  const { data: stockItems = [] } = useStockItems();

  // ── State ──────────────────────────────────────────────────────────────────
  const [search,          setSearch]          = useState("");
  const [activeCat,       setActiveCat]       = useState(ALL);
  const [selectedProduct, setSelectedProduct] = useState<string | null>(null);
  const [quantity,        setQuantity]        = useState(1);
  const [notes,           setNotes]           = useState("");
  const [showNotes,       setShowNotes]       = useState(false);
  const [selected,        setSelected]        = useState<Record<string, string[]>>({});
  const [errors,          setErrors]          = useState<Record<string, string>>({});

  const { data: modifierGroups = [] } = useModifierGroups(selectedProduct);
  const stockMap = useMemo(
    () => new Map(stockItems.map(s => [s.productId, s.currentQuantity])),
    [stockItems]
  );

  // ── Category pills — only categories that have ≥1 active product ──────────
  const usedCategoryIds = useMemo(() => {
    const set = new Set<string>();
    products.forEach(p => { if (p.isActive && p.categoryId) set.add(p.categoryId); });
    return set;
  }, [products]);

  const visibleCategories = useMemo(
    () => categories.filter(c => c.isActive && usedCategoryIds.has(c.id)),
    [categories, usedCategoryIds]
  );

  const hasUncategorized = useMemo(
    () => products.some(p => p.isActive && !p.categoryId),
    [products]
  );

  // ── Filtered products ──────────────────────────────────────────────────────
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return products.filter(p => {
      if (!p.isActive) return false;
      // Category filter (search overrides category — shows all matching)
      if (!q && activeCat !== ALL) {
        if (activeCat === "__uncategorized__") {
          if (p.categoryId) return false;
        } else {
          if (p.categoryId !== activeCat) return false;
        }
      }
      // Text search
      if (q) {
        return (
          p.name.toLowerCase().includes(q) ||
          p.code.toLowerCase().includes(q) ||
          (p.barcode ?? "").includes(q)
        );
      }
      return true;
    });
  }, [products, activeCat, search]);

  // ── Entry mutations ────────────────────────────────────────────────────────
  const handleToggleModifier = (groupId: string, modifierId: string, maxSelections: number) => {
    setSelected(prev => {
      const curr = prev[groupId] ?? [];
      if (curr.includes(modifierId))
        return { ...prev, [groupId]: curr.filter(id => id !== modifierId) };
      if (maxSelections === 1) return { ...prev, [groupId]: [modifierId] };
      if (curr.length >= maxSelections) return prev;
      return { ...prev, [groupId]: [...curr, modifierId] };
    });
    setErrors(e => ({ ...e, [groupId]: "" }));
  };

  const handleAdd = () => {
    if (!selectedProduct) return;
    const newErrors: Record<string, string> = {};
    for (const g of modifierGroups) {
      if (g.isRequired && !(selected[g.id]?.length)) {
        newErrors[g.id] = `Selecione uma opção para "${g.name}"`;
      }
    }
    if (Object.keys(newErrors).length > 0) { setErrors(newErrors); return; }

    const modifiers = Object.values(selected).flat().map(id => ({ modifierId: id }));
    onAdd({
      productId: selectedProduct,
      quantity,
      notes:     notes || null,
      modifiers: modifiers.length > 0 ? modifiers : undefined,
    });
    resetDetail();
  };

  const resetDetail = () => {
    setSelectedProduct(null);
    setQuantity(1);
    setNotes("");
    setShowNotes(false);
    setSelected({});
    setErrors({});
  };

  const selectedProductData = products.find(p => p.id === selectedProduct);

  // ─────────────────────────────────────────────────────────────────────────
  return (
    <Sheet open={open} onOpenChange={v => !v && onClose()}>
      <SheetContent side="bottom" className="rounded-t-2xl flex flex-col max-h-[92vh] pb-safe-bottom pb-6">
        <SheetHeader className="mb-3 shrink-0">
          <SheetTitle>
            {selectedProduct ? selectedProductData?.name ?? "Configurar item" : "Adicionar item"}
          </SheetTitle>
        </SheetHeader>

        {!selectedProduct ? (
          /* ── Product browser ───────────────────────────────────────────── */
          <>
            {/* Search */}
            <div className="relative mb-3 shrink-0">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
              <Input
                placeholder="Buscar por nome ou código..."
                value={search}
                onChange={e => setSearch(e.target.value)}
                className="pl-9"
              />
            </div>

            {/* Category pills — hidden when actively searching */}
            {!search && (
              <div className="flex gap-1.5 overflow-x-auto pb-2 mb-2 shrink-0 scrollbar-none">
                <CategoryPill
                  label="Todos"
                  active={activeCat === ALL}
                  onClick={() => setActiveCat(ALL)}
                />
                {visibleCategories.map(cat => (
                  <CategoryPill
                    key={cat.id}
                    label={cat.name}
                    active={activeCat === cat.id}
                    onClick={() => setActiveCat(cat.id)}
                  />
                ))}
                {hasUncategorized && (
                  <CategoryPill
                    label="Outros"
                    active={activeCat === "__uncategorized__"}
                    onClick={() => setActiveCat("__uncategorized__")}
                  />
                )}
              </div>
            )}

            {/* Product grid */}
            <div className="flex-1 overflow-y-auto min-h-0">
              {filtered.length === 0 ? (
                <p className="text-sm text-muted-foreground text-center py-10">
                  Nenhum produto encontrado.
                </p>
              ) : (
                <div className="grid grid-cols-2 gap-2 pb-2">
                  {filtered.map(p => {
                    const stock   = stockMap.get(p.id);
                    const outOfStock = stock !== undefined && stock <= 0;
                    return (
                      <button
                        key={p.id}
                        onClick={() => setSelectedProduct(p.id)}
                        className={cn(
                          "flex flex-col items-start rounded-xl border border-border bg-card p-3 text-left transition-colors active:scale-[0.97]",
                          "hover:border-primary/40 hover:bg-primary/5",
                          outOfStock && "opacity-50"
                        )}
                      >
                        {/* Category tag */}
                        {activeCat === ALL && p.categoryId && (() => {
                          const catName = categories.find(c => c.id === p.categoryId)?.name;
                          return catName ? (
                            <span className="text-[9px] font-semibold uppercase tracking-wide text-muted-foreground mb-1 truncate max-w-full">
                              {catName}
                            </span>
                          ) : null;
                        })()}

                        {/* Product name */}
                        <p className="text-sm font-medium text-foreground leading-snug line-clamp-2 mb-2">
                          {p.name}
                        </p>

                        {/* Price + stock */}
                        <div className="mt-auto w-full flex items-end justify-between gap-1">
                          <span className="text-base font-bold text-foreground tabular-nums">
                            R$ {p.salePrice.toFixed(2)}
                          </span>
                          {outOfStock && (
                            <span className="text-[9px] text-destructive font-medium shrink-0">
                              Sem estoque
                            </span>
                          )}
                        </div>
                      </button>
                    );
                  })}
                </div>
              )}
            </div>
          </>
        ) : (
          /* ── Product configuration ─────────────────────────────────────── */
          <div className="flex-1 overflow-y-auto min-h-0 flex flex-col gap-4">
            {/* Back link */}
            <button
              onClick={resetDetail}
              className="text-sm text-muted-foreground hover:text-foreground text-left shrink-0"
            >
              ← Voltar ao cardápio
            </button>

            {/* Modifiers */}
            <ModifierSelector
              groups={modifierGroups}
              selected={selected}
              onToggle={handleToggleModifier}
              errors={errors}
            />

            {/* Quantity stepper */}
            <div className="shrink-0">
              <label className="text-sm text-muted-foreground mb-2 block">Quantidade</label>
              <div className="flex items-center gap-3">
                <button
                  type="button"
                  onClick={() => setQuantity(q => Math.max(1, q - 1))}
                  disabled={quantity <= 1}
                  className="h-11 w-11 rounded-lg border border-border flex items-center justify-center disabled:opacity-30 hover:bg-muted transition-colors"
                >
                  <Minus className="h-4 w-4" />
                </button>
                <span className="text-xl font-semibold w-8 text-center tabular-nums">
                  {quantity}
                </span>
                <button
                  type="button"
                  onClick={() => setQuantity(q => q + 1)}
                  className="h-11 w-11 rounded-lg border border-border flex items-center justify-center hover:bg-muted transition-colors"
                >
                  <Plus className="h-4 w-4" />
                </button>
              </div>
            </div>

            {/* Notes — collapsed by default */}
            <div className="shrink-0">
              {showNotes ? (
                <Textarea
                  placeholder="Observação (opcional)"
                  value={notes}
                  onChange={e => setNotes(e.target.value)}
                  rows={2}
                  autoFocus
                />
              ) : (
                <button
                  type="button"
                  onClick={() => setShowNotes(true)}
                  className="text-sm text-primary hover:underline"
                >
                  + Adicionar observação
                </button>
              )}
            </div>

            <Button
              className="w-full h-12 text-base shrink-0"
              onClick={handleAdd}
              disabled={isLoading}
            >
              {isLoading ? "Adicionando..." : `Adicionar${quantity > 1 ? ` (${quantity}×)` : ""}`}
            </Button>
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}

// ─── CategoryPill ─────────────────────────────────────────────────────────────

function CategoryPill({
  label, active, onClick,
}: {
  label: string; active: boolean; onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        "shrink-0 px-3.5 py-1.5 rounded-full text-sm font-medium whitespace-nowrap transition-colors",
        active
          ? "bg-primary text-primary-foreground"
          : "bg-muted text-muted-foreground hover:text-foreground"
      )}
    >
      {label}
    </button>
  );
}
