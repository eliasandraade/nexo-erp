import { Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type { CartItem } from "../types";
import { formatCurrency } from "@/lib/formatters";

interface PosCartTableProps {
  items: CartItem[];
  onUpdateQuantity: (productId: string, quantity: number) => void;
  onRemove: (productId: string) => void;
}

export function PosCartTable({ items, onUpdateQuantity, onRemove }: PosCartTableProps) {
  if (items.length === 0) {
    return (
      <div className="flex flex-1 items-center justify-center text-center py-12">
        <div>
          <p className="text-sm font-medium text-muted-foreground">Carrinho vazio</p>
          <p className="text-xs text-muted-foreground mt-1">
            Busque um produto acima para adicionar.
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="overflow-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-border text-xs text-muted-foreground">
            <th className="text-left py-2 pr-2 font-medium">Produto</th>
            <th className="text-center py-2 px-2 font-medium w-20">Qtd</th>
            <th className="text-right py-2 px-2 font-medium w-24">Unitário</th>
            <th className="text-right py-2 pl-2 pr-8 font-medium w-24">Total</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {items.map((item) => (
            <tr key={item.productId} className="group">
              <td className="py-2 pr-2">
                <div className="flex items-center gap-1.5">
                  <button
                    type="button"
                    onClick={() => onRemove(item.productId)}
                    className="opacity-0 group-hover:opacity-100 transition-opacity text-muted-foreground hover:text-destructive"
                    title="Remover item"
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </button>
                  <div>
                    <p className="font-medium leading-tight">{item.description}</p>
                    <p className="text-xs text-muted-foreground">{item.code}</p>
                  </div>
                </div>
              </td>
              <td className="py-2 px-2">
                <Input
                  type="number"
                  min={1}
                  value={item.quantity}
                  onChange={(e) =>
                    onUpdateQuantity(item.productId, parseInt(e.target.value) || 1)
                  }
                  className="h-7 text-center w-16 text-sm"
                />
              </td>
              <td className="py-2 px-2 text-right tabular-nums text-muted-foreground">
                {formatCurrency(item.unitPrice)}
              </td>
              <td className="py-2 pl-2 text-right tabular-nums font-medium pr-2">
                <div className="flex items-center justify-end gap-1">
                  {formatCurrency(item.totalPrice)}
                  <Button
                    variant="ghost"
                    size="sm"
                    className="h-6 w-6 p-0 opacity-0 group-hover:opacity-100 text-muted-foreground hover:text-destructive"
                    onClick={() => onRemove(item.productId)}
                    tabIndex={-1}
                  >
                    <Trash2 className="h-3 w-3" />
                  </Button>
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
