import { useState } from "react";
import { Minus, Plus } from "lucide-react";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import type { PublicMenuProductDto, CartItem, CartModifier } from "../types";
import { ModifierPicker } from "./ModifierPicker";

interface ProductSheetProps {
  product:  PublicMenuProductDto | null;
  onClose:  () => void;
  onAdd:    (item: CartItem) => void;
}

export function ProductSheet({ product, onClose, onAdd }: ProductSheetProps) {
  const [qty,       setQty]       = useState(1);
  const [notes,     setNotes]     = useState("");
  const [modifiers, setModifiers] = useState<CartModifier[]>([]);

  const reset = () => { setQty(1); setNotes(""); setModifiers([]); };
  const handleClose = () => { reset(); onClose(); };

  // Validate required groups
  const missingRequired = product?.modifierGroups.filter((g) => {
    if (!g.isRequired) return false;
    const count = modifiers.filter((m) =>
      g.options.some((o) => o.id === m.modifierId)
    ).length;
    return count < g.minSelections;
  }) ?? [];

  const modTotal = modifiers.reduce((s, m) => s + m.price, 0);
  const unitTotal = (product?.price ?? 0) + modTotal;

  const handleAdd = () => {
    if (!product || missingRequired.length > 0) return;
    onAdd({
      productId:   product.id,
      productName: product.name,
      price:       product.price,
      quantity:    qty,
      notes:       notes.trim(),
      modifiers,
    });
    handleClose();
  };

  return (
    <Sheet open={!!product} onOpenChange={(v) => !v && handleClose()}>
      <SheetContent side="bottom" className="rounded-t-2xl flex flex-col max-h-[90vh] pb-safe-bottom pb-6">
        {product && (
          <>
            {/* Cover image */}
            {product.imageUrl && (
              <div className="w-full h-40 -mx-4 -mt-4 mb-4 overflow-hidden rounded-t-2xl shrink-0">
                <img src={product.imageUrl} alt={product.name} className="w-full h-full object-cover" />
              </div>
            )}

            <SheetHeader className="mb-3 shrink-0">
              <SheetTitle>{product.name}</SheetTitle>
              {product.description && (
                <p className="text-sm text-muted-foreground">{product.description}</p>
              )}
              <p className="text-base font-bold tabular-nums">R$ {product.price.toFixed(2)}</p>
            </SheetHeader>

            <div className="flex-1 overflow-y-auto min-h-0 flex flex-col gap-4">
              {/* Modifiers */}
              <ModifierPicker
                groups={product.modifierGroups}
                selected={modifiers}
                onChange={setModifiers}
              />

              {/* Notes */}
              <Input
                placeholder="Alguma observação? (opcional)"
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
              />
            </div>

            {/* Quantity + Add */}
            <div className="flex items-center gap-3 mt-4 shrink-0">
              <div className="flex items-center gap-2">
                <button
                  onClick={() => setQty((q) => Math.max(1, q - 1))}
                  disabled={qty <= 1}
                  className="h-10 w-10 rounded-full border border-border flex items-center justify-center disabled:opacity-30"
                >
                  <Minus className="h-4 w-4" />
                </button>
                <span className="w-8 text-center font-semibold tabular-nums">{qty}</span>
                <button
                  onClick={() => setQty((q) => q + 1)}
                  className="h-10 w-10 rounded-full border border-border flex items-center justify-center"
                >
                  <Plus className="h-4 w-4" />
                </button>
              </div>

              <Button
                className="flex-1 h-12 text-base"
                onClick={handleAdd}
                disabled={missingRequired.length > 0}
              >
                Adicionar · R$ {(unitTotal * qty).toFixed(2)}
              </Button>
            </div>

            {missingRequired.length > 0 && (
              <p className="text-xs text-amber-400 text-center shrink-0">
                Escolha: {missingRequired.map((g) => g.name).join(", ")}
              </p>
            )}
          </>
        )}
      </SheetContent>
    </Sheet>
  );
}
