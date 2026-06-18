import { useState } from "react";
import { MoreHorizontal, Plus } from "lucide-react";
import { toast } from "sonner";
import { SectionCard } from "@/components/shared/SectionCard";
import { StatusBadge } from "@/components/shared/StatusBadge";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { formatCurrency, formatDate } from "@/lib/formatters";
import { ApiError } from "@/services/api-client";
import {
  useCustomerPackagePaymentSummary,
  useOrderPaymentSummary,
  usePayments,
  useVoidPayment,
} from "../hooks/usePayments";
import { PAYMENT_METHOD_LABELS, PAYMENT_STATUS_LABELS, PAYMENT_STATUS_VARIANTS } from "../lib/payment";
import { PaymentDialog } from "./PaymentDialog";

interface PaymentsPanelProps {
  /** The payment target — its summary, list and record dialog all derive from this. */
  target: { kind: "order" | "customer-package"; id: string };
  /** Whether recording a new payment is currently allowed (e.g. order not cancelled). */
  canRecord: boolean;
}

/**
 * Shared payments section for the order and customer-package detail screens: summary
 * (total/paid/remaining + Quitado), record (PaymentDialog) and the payment list with void.
 * Single source so the two detail pages don't duplicate it.
 */
export function PaymentsPanel({ target, canRecord }: PaymentsPanelProps) {
  const orderSummary = useOrderPaymentSummary(target.kind === "order" ? target.id : undefined);
  const cpSummary = useCustomerPackagePaymentSummary(
    target.kind === "customer-package" ? target.id : undefined
  );
  const payments = usePayments(
    target.kind === "order" ? { orderId: target.id } : { customerPackageId: target.id }
  );
  const voidPayment = useVoidPayment();

  const [payOpen, setPayOpen] = useState(false);

  const summary = target.kind === "order" ? orderSummary.data : cpSummary.data;
  const remaining = summary?.remainingAmount ?? 0;
  const list = payments.data ?? [];

  const handleVoid = (id: string) =>
    voidPayment.mutate(
      { id, reason: null },
      {
        onSuccess: () => toast.success("Pagamento estornado."),
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "Não foi possível estornar."),
      }
    );

  return (
    <SectionCard
      title="Pagamentos"
      actions={
        <Button size="sm" onClick={() => setPayOpen(true)} disabled={!canRecord || remaining <= 0}>
          <Plus className="mr-1.5 h-4 w-4" /> Registrar pagamento
        </Button>
      }
    >
      {summary && (
        <div className="mb-4 grid grid-cols-3 gap-3">
          <SummaryStat label="Total" value={formatCurrency(summary.totalAmount)} />
          <SummaryStat label="Pago" value={formatCurrency(summary.paidAmount)} accent />
          <SummaryStat label="Em aberto" value={formatCurrency(summary.remainingAmount)} />
        </div>
      )}
      {summary?.isFullyPaid && (
        <div className="mb-3"><StatusBadge variant="success" label="Quitado" dot /></div>
      )}

      {list.length === 0 ? (
        <p className="py-4 text-center text-[12.5px] text-muted-foreground">Nenhum pagamento registrado.</p>
      ) : (
        <div className="space-y-1.5">
          {list.map((p) => (
            <div key={p.id} className="flex items-center gap-3 rounded-md border border-border px-3 py-2">
              <div className="min-w-0 flex-1">
                <p className="text-[13px] font-medium text-foreground">{formatCurrency(p.amount)}</p>
                <p className="text-[11.5px] text-muted-foreground">
                  {PAYMENT_METHOD_LABELS[p.method]} · {formatDate(p.paidAt)}
                </p>
              </div>
              <StatusBadge variant={PAYMENT_STATUS_VARIANTS[p.status]} label={PAYMENT_STATUS_LABELS[p.status]} />
              {p.status === "Paid" && (
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <Button variant="ghost" size="icon" className="h-7 w-7"><MoreHorizontal className="h-4 w-4" /></Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end">
                    <DropdownMenuItem onClick={() => handleVoid(p.id)}>Estornar</DropdownMenuItem>
                  </DropdownMenuContent>
                </DropdownMenu>
              )}
            </div>
          ))}
        </div>
      )}

      <PaymentDialog open={payOpen} onClose={() => setPayOpen(false)} target={target} suggestedAmount={remaining} />
    </SectionCard>
  );
}

function SummaryStat({ label, value, accent }: { label: string; value: string; accent?: boolean }) {
  return (
    <div className="rounded-md border border-border p-3">
      <p className="text-[11px] text-muted-foreground">{label}</p>
      <p className={`text-[15px] font-semibold ${accent ? "text-success" : "text-foreground"}`}>{value}</p>
    </div>
  );
}
