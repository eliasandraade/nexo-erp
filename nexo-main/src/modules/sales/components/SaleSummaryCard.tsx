import { Calendar, User, Hash } from "lucide-react";
import { SectionCard } from "@/components/shared/SectionCard";
import { Separator } from "@/components/ui/separator";
import { SaleStatusBadge } from "./SaleStatusBadge";
import type { CompletedSale } from "../types";
import { formatCurrency } from "@/lib/formatters";

interface SaleSummaryCardProps {
  sale: CompletedSale;
}

// Includes seconds for the sale detail view
function formatDateTime(iso: string) {
  return new Date(iso).toLocaleString("pt-BR", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}

function Row({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between text-sm py-1.5">
      <span className="text-muted-foreground">{label}</span>
      <span className="font-medium">{value}</span>
    </div>
  );
}

export function SaleSummaryCard({ sale }: SaleSummaryCardProps) {
  return (
    <SectionCard title="Resumo da venda">
      <div className="space-y-0.5">
        <Row
          label={<span className="flex items-center gap-1.5"><Hash className="h-3.5 w-3.5" /> Identificador</span>}
          value={<span className="font-mono text-xs">{sale.id}</span>}
        />
        <Row
          label={<span className="flex items-center gap-1.5"><Calendar className="h-3.5 w-3.5" /> Data / Hora</span>}
          value={<span className="tabular-nums">{formatDateTime(sale.timestamp)}</span>}
        />
        <Row
          label={<span className="flex items-center gap-1.5"><User className="h-3.5 w-3.5" /> Operador</span>}
          value={sale.operator}
        />
        <Row label="Status" value={<SaleStatusBadge status={sale.status} />} />

        <Separator className="my-3" />

        <Row label="Subtotal" value={<span className="tabular-nums">{formatCurrency(sale.subtotal)}</span>} />
        {sale.discountTotal > 0 && (
          <Row
            label="Desconto"
            value={<span className="tabular-nums text-red-600 dark:text-red-400">- {formatCurrency(sale.discountTotal)}</span>}
          />
        )}
        <div className="flex items-center justify-between text-sm pt-1.5">
          <span className="font-semibold">Total</span>
          <span className="text-lg font-bold tabular-nums">{formatCurrency(sale.total)}</span>
        </div>
        {sale.change > 0 && (
          <Row
            label="Troco"
            value={<span className="tabular-nums text-green-600 dark:text-green-400">{formatCurrency(sale.change)}</span>}
          />
        )}
      </div>
    </SectionCard>
  );
}
