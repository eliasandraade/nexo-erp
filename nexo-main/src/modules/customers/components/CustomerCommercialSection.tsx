import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import type { CustomerFormInput } from "../types";

interface Props {
  data: CustomerFormInput;
  onChange: (field: keyof CustomerFormInput, value: unknown) => void;
}

export function CustomerCommercialSection({ data, onChange }: Props) {
  return (
    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5">
      <div className="space-y-1.5">
        <Label>Limite de crédito (R$)</Label>
        <Input
          type="number"
          step="0.01"
          min={0}
          value={data.creditLimit}
          onChange={(e) => onChange("creditLimit", e.target.value)}
          placeholder="Opcional"
        />
      </div>
      <div className="space-y-1.5 md:col-span-2 lg:col-span-3">
        <Label>Observações</Label>
        <Textarea
          value={data.notes}
          onChange={(e) => onChange("notes", e.target.value)}
          rows={3}
          placeholder="Informações sobre condições, prazos, histórico..."
        />
      </div>
    </div>
  );
}
