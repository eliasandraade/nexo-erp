import { useState } from "react";
import {
  Sheet, SheetContent, SheetHeader, SheetTitle,
} from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
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
