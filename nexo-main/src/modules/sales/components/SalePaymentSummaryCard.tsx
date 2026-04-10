import { Banknote, CreditCard, QrCode } from "lucide-react";
import { SectionCard } from "@/components/shared/SectionCard";
import { Separator } from "@/components/ui/separator";
import { paymentMethodLabels } from "../types";
import type { CompletedSale, PaymentMethod } from "../types";
import { formatCurrency } from "@/lib/formatters";

interface SalePaymentSummaryCardProps {
  sale: CompletedSale;
}

const methodIcons: Record<PaymentMethod, React.ElementType> = {
  cash: Banknote,
  pix: QrCode,
  card: CreditCard,
};

export function SalePaymentSummaryCard({ sale }: SalePaymentSummaryCardProps) {
  const totalPaid = sale.payments.reduce((acc, p) => acc + p.amount, 0);

  return (
    <SectionCard title="Pagamento">
      <div className="space-y-2">
        {sale.payments.map((payment, idx) => {
          const Icon = methodIcons[payment.method];
          return (
            <div key={idx} className="flex items-center justify-between text-sm">
              <span className="flex items-center gap-2 text-muted-foreground">
                <Icon className="h-4 w-4" />
                {paymentMethodLabels[payment.method]}
              </span>
              <span className="font-medium tabular-nums">
                {formatCurrency(payment.amount)}
              </span>
            </div>
          );
        })}

        {sale.payments.length > 1 && (
          <>
            <Separator />
            <div className="flex items-center justify-between text-sm">
              <span className="text-muted-foreground">Total pago</span>
              <span className="font-semibold tabular-nums">{formatCurrency(totalPaid)}</span>
            </div>
          </>
        )}

        {sale.change > 0 && (
          <>
            <Separator />
            <div className="flex items-center justify-between text-sm">
              <span className="text-muted-foreground">Troco devolvido</span>
              <span className="font-semibold tabular-nums text-green-600 dark:text-green-400">
                {formatCurrency(sale.change)}
              </span>
            </div>
          </>
        )}
      </div>
    </SectionCard>
  );
}
