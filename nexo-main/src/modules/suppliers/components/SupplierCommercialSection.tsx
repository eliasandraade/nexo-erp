import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import type { SupplierFormInput } from "../types";

interface Props {
  data: SupplierFormInput;
  onChange: (field: keyof SupplierFormInput, value: unknown) => void;
}

export function SupplierCommercialSection({ data, onChange }: Props) {
  return (
    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5">
      <div className="space-y-1.5">
        <Label>Prazo de pagamento padrão (dias)</Label>
        <div className="flex items-center gap-2">
          <Input
            type="number"
            min={0}
            max={365}
            value={data.paymentTermsDays}
            onChange={(e) => onChange("paymentTermsDays", e.target.value)}
            placeholder="Ex: 30"
            className="w-28"
          />
          <span className="text-sm text-muted-foreground">dias</span>
        </div>
      </div>

      <div className="space-y-1.5 md:col-span-2 lg:col-span-3">
        <Label>Observações</Label>
        <Textarea
          value={data.notes}
          onChange={(e) => onChange("notes", e.target.value)}
          rows={3}
          placeholder="Condições de entrega, pedido mínimo, política de devolução..."
        />
      </div>

      <div className="lg:col-span-3 border-t border-border pt-4 mt-1">
        <p className="text-sm font-medium text-foreground mb-4">Dados bancários</p>
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
          <div className="space-y-1.5">
            <Label>Banco</Label>
            <Input
              value={data.bankName}
              onChange={(e) => onChange("bankName", e.target.value)}
              placeholder="Ex: Bradesco"
            />
          </div>
          <div className="space-y-1.5">
            <Label>Agência</Label>
            <Input
              value={data.bankAgency}
              onChange={(e) => onChange("bankAgency", e.target.value)}
              placeholder="0000"
            />
          </div>
          <div className="space-y-1.5">
            <Label>Conta</Label>
            <Input
              value={data.bankAccount}
              onChange={(e) => onChange("bankAccount", e.target.value)}
              placeholder="00000-0"
            />
          </div>
          <div className="space-y-1.5">
            <Label>Chave Pix</Label>
            <Input
              value={data.pixKey}
              onChange={(e) => onChange("pixKey", e.target.value)}
              placeholder="CPF, CNPJ, e-mail ou telefone"
            />
          </div>
        </div>
      </div>
    </div>
  );
}
