import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { ArrowLeft, MoreHorizontal, Plus } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { ErrorState } from "@/components/shared/ErrorState";
import { StatusBadge } from "@/components/shared/StatusBadge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { formatCurrency, formatDate, formatDateTime } from "@/lib/formatters";
import { ApiError } from "@/services/api-client";
import { useCustomers } from "@/modules/customers/hooks/use-customers";
import { useCustomerPackage, useCancelCustomerPackage } from "../hooks/useCustomerPackages";
import { usePackages } from "../hooks/usePackages";
import { useCustomerPackagePaymentSummary, usePayments, useVoidPayment } from "../hooks/usePayments";
import {
  CUSTOMER_PACKAGE_STATUS_LABELS,
  CUSTOMER_PACKAGE_STATUS_VARIANTS,
  isCustomerPackageActive,
} from "../lib/customer-package-status";
import { PAYMENT_METHOD_LABELS, PAYMENT_STATUS_LABELS, PAYMENT_STATUS_VARIANTS } from "../lib/payment";
import { ConsumePackageDialog } from "../components/ConsumePackageDialog";
import { PaymentDialog } from "../components/PaymentDialog";

export default function CustomerPackageDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const { data: cp, isLoading, isError, refetch } = useCustomerPackage(id);
  const { data: customers } = useCustomers(false);
  const { data: packages } = usePackages(undefined);
  const summaryQ = useCustomerPackagePaymentSummary(id);
  const paymentsQ = usePayments(id ? { customerPackageId: id } : {});
  const cancel = useCancelCustomerPackage();
  const voidPayment = useVoidPayment();

  const [consumeOpen, setConsumeOpen] = useState(false);
  const [payOpen, setPayOpen] = useState(false);

  if (isLoading) {
    return <div className="space-y-4"><Skeleton className="h-9 w-64" /><Skeleton className="h-72 w-full" /></div>;
  }
  if (isError || !cp) {
    return (
      <div className="space-y-6">
        <Button variant="ghost" size="sm" onClick={() => navigate("/service/customer-packages")}>
          <ArrowLeft className="mr-2 h-4 w-4" /> Voltar
        </Button>
        <ErrorState type="notfound" onRetry={refetch} />
      </div>
    );
  }

  const customerName = customers?.find((c) => c.id === cp.customerId)?.name ?? "—";
  const packageName = packages?.find((p) => p.id === cp.packageId)?.name ?? "Pacote";
  const itemNameFor = (catalogItemId: string) =>
    cp.items.find((it) => it.catalogItemId === catalogItemId)?.nameSnapshot ?? "Serviço";
  const active = isCustomerPackageActive(cp.status);

  const summary = summaryQ.data;
  const remaining = summary?.remainingAmount ?? 0;
  const payments = paymentsQ.data ?? [];

  const handleCancel = () =>
    cancel.mutate(cp.id, {
      onSuccess: () => toast.success("Pacote cancelado."),
      onError: (e) => toast.error(e instanceof ApiError ? e.message : "Não foi possível cancelar."),
    });

  const handleVoid = (paymentId: string) =>
    voidPayment.mutate({ id: paymentId, reason: null }, {
      onSuccess: () => toast.success("Pagamento estornado."),
      onError: (e) => toast.error(e instanceof ApiError ? e.message : "Não foi possível estornar."),
    });

  return (
    <div className="space-y-6">
      <Button variant="ghost" size="sm" className="-ml-2" onClick={() => navigate("/service/customer-packages")}>
        <ArrowLeft className="mr-2 h-4 w-4" /> Pacotes de clientes
      </Button>

      <PageHeader
        eyebrow={`${packageName} · ${cp.code}`}
        title={customerName}
        description={cp.expiresAt ? `Expira em ${formatDate(cp.expiresAt)}` : "Sem validade"}
        actions={
          <div className="flex items-center gap-2">
            <StatusBadge variant={CUSTOMER_PACKAGE_STATUS_VARIANTS[cp.status]} label={CUSTOMER_PACKAGE_STATUS_LABELS[cp.status]} dot size="md" />
            {active && (
              <>
                <Button size="sm" onClick={() => setConsumeOpen(true)}>Consumir</Button>
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <Button variant="ghost" size="icon" className="h-8 w-8"><MoreHorizontal className="h-4 w-4" /></Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end">
                    <DropdownMenuItem onClick={handleCancel}>Cancelar pacote</DropdownMenuItem>
                  </DropdownMenuContent>
                </DropdownMenu>
              </>
            )}
          </div>
        }
      />

      {/* Balances */}
      <SectionCard title="Saldos">
        <div className="space-y-1.5">
          {cp.items.map((it) => (
            <div key={it.id} className="flex items-center justify-between rounded-md border border-border px-3 py-2">
              <span className="text-[13px] text-foreground">{it.nameSnapshot}</span>
              <span className="text-[12.5px] text-muted-foreground">
                <span className="font-semibold text-foreground">{it.remainingQuantity}</span> / {it.totalQuantity}
              </span>
            </div>
          ))}
        </div>
      </SectionCard>

      {/* Usage history */}
      <SectionCard title="Histórico de uso">
        {cp.usages.length === 0 ? (
          <p className="py-4 text-center text-[12.5px] text-muted-foreground">Nenhum consumo registrado.</p>
        ) : (
          <div className="space-y-1.5">
            {[...cp.usages].sort((a, b) => b.createdAt.localeCompare(a.createdAt)).map((u) => (
              <div key={u.id} className="flex items-center justify-between rounded-md border border-border px-3 py-2">
                <div>
                  <p className="text-[13px] text-foreground">{itemNameFor(u.catalogItemId)}</p>
                  {u.notes && <p className="text-[11.5px] text-muted-foreground">{u.notes}</p>}
                </div>
                <div className="text-right">
                  <p className="text-[13px] font-medium text-foreground">−{u.quantity}</p>
                  <p className="text-[11px] text-muted-foreground">{formatDateTime(u.createdAt)}</p>
                </div>
              </div>
            ))}
          </div>
        )}
      </SectionCard>

      {/* Payments */}
      <SectionCard
        title="Pagamentos"
        actions={
          <Button size="sm" onClick={() => setPayOpen(true)} disabled={!active || remaining <= 0}>
            <Plus className="mr-1.5 h-4 w-4" /> Registrar pagamento
          </Button>
        }
      >
        {summary && (
          <div className="mb-4 grid grid-cols-3 gap-3">
            <div className="rounded-md border border-border p-3">
              <p className="text-[11px] text-muted-foreground">Total</p>
              <p className="text-[15px] font-semibold text-foreground">{formatCurrency(summary.totalAmount)}</p>
            </div>
            <div className="rounded-md border border-border p-3">
              <p className="text-[11px] text-muted-foreground">Pago</p>
              <p className="text-[15px] font-semibold text-success">{formatCurrency(summary.paidAmount)}</p>
            </div>
            <div className="rounded-md border border-border p-3">
              <p className="text-[11px] text-muted-foreground">Em aberto</p>
              <p className="text-[15px] font-semibold text-foreground">{formatCurrency(summary.remainingAmount)}</p>
            </div>
          </div>
        )}

        {payments.length === 0 ? (
          <p className="py-4 text-center text-[12.5px] text-muted-foreground">Nenhum pagamento registrado.</p>
        ) : (
          <div className="space-y-1.5">
            {payments.map((p) => (
              <div key={p.id} className="flex items-center gap-3 rounded-md border border-border px-3 py-2">
                <div className="min-w-0 flex-1">
                  <p className="text-[13px] font-medium text-foreground">{formatCurrency(p.amount)}</p>
                  <p className="text-[11.5px] text-muted-foreground">{PAYMENT_METHOD_LABELS[p.method]} · {formatDate(p.paidAt)}</p>
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
      </SectionCard>

      <ConsumePackageDialog open={consumeOpen} customerPackage={cp} onClose={() => setConsumeOpen(false)} />
      <PaymentDialog open={payOpen} onClose={() => setPayOpen(false)} target={{ kind: "customer-package", id: cp.id }} suggestedAmount={remaining} />
    </div>
  );
}
