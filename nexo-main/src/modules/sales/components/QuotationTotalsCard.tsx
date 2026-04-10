import { SectionCard } from "@/components/shared/SectionCard";
import { formatCurrency } from "@/lib/formatters";
import type { QuotationItem } from "../types/quotation";

interface QuotationTotalsCardProps {
  items: QuotationItem[];
}

export function QuotationTotalsCard({ items }: QuotationTotalsCardProps) {
  const subtotal = items.reduce((sum, i) => sum + i.unitPrice * i.quantity, 0);
  const discountTotal = items.reduce((sum, i) => sum + i.discount, 0);
  const total = subtotal - discountTotal;
  const totalItems = items.reduce((sum, i) => sum + i.quantity, 0);

  return (
    <SectionCard title="Resumo">
      <div className="space-y-3 text-sm">
        <div className="flex justify-between text-muted-foreground">
          <span>Itens</span>
          <span>{totalItems}</span>
        </div>
        <div className="flex justify-between text-muted-foreground">
          <span>Subtotal</span>
          <span className="tabular-nums">{formatCurrency(subtotal)}</span>
        </div>
        {discountTotal > 0 && (
          <div className="flex justify-between text-destructive">
            <span>Descontos</span>
            <span className="tabular-nums">- {formatCurrency(discountTotal)}</span>
          </div>
        )}
        <div className="border-t border-border pt-3 flex justify-between font-semibold text-base">
          <span>Total</span>
          <span className="tabular-nums">{formatCurrency(total)}</span>
        </div>
      </div>
    </SectionCard>
  );
}
