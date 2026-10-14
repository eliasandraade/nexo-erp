import { formatCurrency } from "@/lib/formatters";
import type { CashPaymentTotalDto } from "../types";
import { PAYMENT_METHOD_LABEL } from "../types";

interface CashReceiptsByMethodProps {
  receipts: CashPaymentTotalDto[];
}

/**
 * What the session received, by payment method. Only "Dinheiro" is in the drawer — Pix and cards
 * are listed so the operator sees the whole shift, but they never change the expected balance.
 */
export function CashReceiptsByMethod({ receipts }: CashReceiptsByMethodProps) {
  if (receipts.length === 0) {
    return <p className="text-sm text-muted-foreground">Nenhuma venda registrada neste caixa ainda.</p>;
  }

  return (
    <ul className="divide-y divide-border text-sm">
      {receipts.map((r) => (
        <li key={`${r.method}-${r.type}`} className="flex items-center justify-between gap-4 py-2">
          <span>
            <span className="font-medium">{PAYMENT_METHOD_LABEL[r.method] ?? r.method}</span>
            {r.type === "Credit" && <span className="text-muted-foreground"> · a prazo</span>}
            {r.method === "Cash" && r.type === "Cash" && (
              <span className="text-muted-foreground"> · na gaveta</span>
            )}
            <span className="ml-2 text-xs text-muted-foreground">
              {r.count} {r.count === 1 ? "pagamento" : "pagamentos"}
            </span>
          </span>
          <span className="font-semibold tabular-nums">{formatCurrency(r.amount)}</span>
        </li>
      ))}
    </ul>
  );
}
