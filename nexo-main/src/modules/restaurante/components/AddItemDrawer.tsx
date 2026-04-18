import { useState } from "react";
import {
  Sheet, SheetContent, SheetHeader, SheetTitle,
} from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Minus, Plus } from "lucide-react";
import { useProducts } from "@/modules/products/hooks/use-products";
import { useStockItems } from "@/modules/inventory/hooks/use-stock";
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

  // search is kept alive across the product→modifier→product flow
  const [search, setSearch]                   = useState("");
  const [selectedProduct, setSelectedProduct] = useState<string | null>(null);
  const [quantity, setQuantity]               = useState(1);
  const [notes, setNotes]                     = useState("");
  const [showNotes, setShowNotes]             = useState(false);
  const [selected, setSelected]               = useState<Record<string, string[]>>({});
  const [errors, setErrors]                   = useState<Record<string, string>>({});

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
      quantity,
      notes:     notes || null,
      modifiers: modifiers.length > 0 ? modifiers : undefined,
    });
    // Reset — preserve search so the waiter can quickly add another item
    setSelectedProduct(null);
    setQuantity(1);
    setNotes("");
    setShowNotes(false);
    setSelected({});
    setErrors({});
  };

  // Back arrow: return to product list keeping the search term
  const handleBack = () => {
    setSelectedProduct(null);
    setQuantity(1);
    setNotes("");
    setShowNotes(false);
    setSelected({});
    setErrors({});
  };

  const selectedProductName = products.find((p) => p.id === selectedProduct)?.name;

  return (
    <Sheet open={open} onOpenChange={(v) => !v && onClose()}>
      <SheetContent side="bottom" className="rounded-t-2xl flex flex-col max-h-[90vh] pb-8">
        <SheetHeader className="mb-4 shrink-0">
          <SheetTitle>Adicionar item</SheetTitle>
        </SheetHeader>

        {!selectedProduct ? (
          /* ── Product search ────────────────────────────────────────────── */
          <>
            <Input
              placeholder="Buscar por nome ou código..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="mb-3 shrink-0"
              autoFocus
            />
            {/* List fills remaining sheet height */}
            <div className="flex-1 overflow-y-auto min-h-0">
              {filtered.map((p) => {
                const stock   = stockMap.get(p.id);
                const lowStock = stock !== undefined && stock <= 0;
                return (
                  <button
                    key={p.id}
                    onClick={() => setSelectedProduct(p.id)}
                    className="w-full flex items-center justify-between rounded-lg px-3 py-3 text-left hover:bg-muted transition-colors"
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
          /* ── Product configuration ─────────────────────────────────────── */
          <div className="flex-1 overflow-y-auto min-h-0 flex flex-col gap-4">
            {/* Back link — keeps search intact */}
            <button
              onClick={handleBack}
              className="text-sm text-muted-foreground hover:text-foreground text-left shrink-0"
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

            {/* Quantity stepper */}
            <div className="shrink-0">
              <label className="text-sm text-muted-foreground mb-2 block">Quantidade</label>
              <div className="flex items-center gap-3">
                <button
                  type="button"
                  onClick={() => setQuantity((q) => Math.max(1, q - 1))}
                  disabled={quantity <= 1}
                  className="h-11 w-11 rounded-lg border border-border flex items-center justify-center text-foreground disabled:opacity-30 hover:bg-muted transition-colors"
                >
                  <Minus className="h-4 w-4" />
                </button>
                <span className="text-xl font-semibold w-8 text-center tabular-nums">
                  {quantity}
                </span>
                <button
                  type="button"
                  onClick={() => setQuantity((q) => q + 1)}
                  className="h-11 w-11 rounded-lg border border-border flex items-center justify-center text-foreground hover:bg-muted transition-colors"
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
                  onChange={(e) => setNotes(e.target.value)}
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
              {isLoading ? "Adicionando..." : "Adicionar à comanda"}
            </Button>
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
