import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { Product } from "../types";

interface Props {
  data: Partial<Product>;
  onChange: (field: string, value: unknown) => void;
}

export function ProductInventorySection({ data, onChange }: Props) {
  return (
    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5">
      <div className="space-y-1.5">
        <Label>Estoque mínimo</Label>
        <Input
          type="number"
          min="0"
          value={data.minStockQuantity ?? ""}
          onChange={(e) => onChange("minStockQuantity", parseFloat(e.target.value) || 0)}
        />
      </div>
      <div className="space-y-1.5">
        <Label>Estoque máximo</Label>
        <Input
          type="number"
          min="0"
          value={data.maxStockQuantity ?? ""}
          onChange={(e) => {
            const v = parseFloat(e.target.value);
            onChange("maxStockQuantity", isNaN(v) || v === 0 ? null : v);
          }}
          placeholder="Opcional"
        />
      </div>
    </div>
  );
}
