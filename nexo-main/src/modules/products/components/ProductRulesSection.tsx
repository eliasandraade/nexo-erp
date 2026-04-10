import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { SectionCard } from "@/components/shared/SectionCard";
// Rules configuration — not yet supported by the backend.
// Kept for future use; not rendered in the current ProductForm.
interface ProductRuleSettings {
  alertNoSaleDays: number;
  alertNoSaleEnabled: boolean;
  alertLowStock: boolean;
  alertHighStockLowTurnover: boolean;
  highStockQuantity: number;
  analysisWindowDays: number;
  alertRecurrentRupture: boolean;
  maxDiscountPercent: number;
  maxDiscountEnabled: boolean;
}

interface Props {
  rules: ProductRuleSettings;
  onChange: (field: keyof ProductRuleSettings, value: unknown) => void;
}

function RuleRow({ label, description, enabled, onToggle, children }: {
  label: string;
  description: string;
  enabled: boolean;
  onToggle: (v: boolean) => void;
  children?: React.ReactNode;
}) {
  return (
    <div className="flex flex-col gap-3 py-4 border-b border-border last:border-b-0">
      <div className="flex items-start justify-between gap-4">
        <div className="flex-1">
          <p className="text-sm font-medium text-foreground">{label}</p>
          <p className="text-xs text-muted-foreground mt-0.5">{description}</p>
        </div>
        <Switch checked={enabled} onCheckedChange={onToggle} />
      </div>
      {enabled && children && (
        <div className="flex flex-wrap gap-4 pl-0">{children}</div>
      )}
    </div>
  );
}

export function ProductRulesSection({ rules, onChange }: Props) {
  return (
    <SectionCard title="Configurações de regras e alertas" description="Parâmetros operacionais do produto para geração de alertas automáticos.">
      <RuleRow
        label="Alertar se ficar sem venda"
        description="Gera alerta quando o produto não registra vendas no período configurado."
        enabled={rules.alertNoSaleEnabled}
        onToggle={(v) => onChange("alertNoSaleEnabled", v)}
      >
        <div className="space-y-1.5">
          <Label className="text-xs">Dias sem venda para alerta</Label>
          <Input type="number" className="w-32" value={rules.alertNoSaleDays} onChange={(e) => onChange("alertNoSaleDays", parseInt(e.target.value) || 0)} />
        </div>
      </RuleRow>

      <RuleRow
        label="Alertar estoque abaixo do mínimo"
        description="Gera alerta quando a quantidade em estoque cai abaixo do estoque mínimo configurado."
        enabled={rules.alertLowStock}
        onToggle={(v) => onChange("alertLowStock", v)}
      />

      <RuleRow
        label="Alertar estoque alto com baixo giro"
        description="Identifica quando o produto possui estoque elevado e poucas vendas no período."
        enabled={rules.alertHighStockLowTurnover}
        onToggle={(v) => onChange("alertHighStockLowTurnover", v)}
      >
        <div className="space-y-1.5">
          <Label className="text-xs">Quantidade alta em estoque</Label>
          <Input type="number" className="w-32" value={rules.highStockQuantity} onChange={(e) => onChange("highStockQuantity", parseInt(e.target.value) || 0)} />
        </div>
        <div className="space-y-1.5">
          <Label className="text-xs">Janela de análise (dias)</Label>
          <Input type="number" className="w-32" value={rules.analysisWindowDays} onChange={(e) => onChange("analysisWindowDays", parseInt(e.target.value) || 0)} />
        </div>
      </RuleRow>

      <RuleRow
        label="Alertar ruptura recorrente"
        description="Identifica quando o produto entra em ruptura de estoque com frequência."
        enabled={rules.alertRecurrentRupture}
        onToggle={(v) => onChange("alertRecurrentRupture", v)}
      />

      <RuleRow
        label="Limite de desconto recomendado"
        description="Define o percentual máximo de desconto sugerido para este produto."
        enabled={rules.maxDiscountEnabled}
        onToggle={(v) => onChange("maxDiscountEnabled", v)}
      >
        <div className="space-y-1.5">
          <Label className="text-xs">Percentual máximo de desconto (%)</Label>
          <Input type="number" className="w-32" value={rules.maxDiscountPercent} onChange={(e) => onChange("maxDiscountPercent", parseInt(e.target.value) || 0)} />
        </div>
      </RuleRow>
    </SectionCard>
  );
}
