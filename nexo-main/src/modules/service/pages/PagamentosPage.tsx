import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { CreditCard, MoreHorizontal } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { ErrorState } from "@/components/shared/ErrorState";
import { StatusBadge } from "@/components/shared/StatusBadge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { formatCurrency, formatDate } from "@/lib/formatters";
import { ApiError } from "@/services/api-client";
import { useCustomers } from "@/modules/customers/hooks/use-customers";
import type { SvcPaymentStatus } from "../api/service.api";
import { usePayments, useVoidPayment } from "../hooks/usePayments";
import {
  PAYMENT_METHOD_LABELS,
  PAYMENT_STATUS_LABELS,
  PAYMENT_STATUS_VARIANTS,
} from "../lib/payment";

const STATUSES: SvcPaymentStatus[] = ["Paid", "Voided"];

export default function PagamentosPage() {
  const navigate = useNavigate();
  const [statusFilter, setStatusFilter] = useState<"all" | SvcPaymentStatus>("all");

  const { data, isLoading, isError, refetch } = usePayments(
    statusFilter === "all" ? {} : { status: statusFilter }
  );
  const { data: customers } = useCustomers(false);
  const voidPayment = useVoidPayment();

  const customerName = (id: string) => customers?.find((c) => c.id === id)?.name ?? "—";
  const payments = data ?? [];

  const handleVoid = (id: string) =>
    voidPayment.mutate({ id, reason: null }, {
      onSuccess: () => toast.success("Pagamento estornado."),
      onError: (e) => toast.error(e instanceof ApiError ? e.message : "Não foi possível estornar."),
    });

  const goToTarget = (orderId: string | null, customerPackageId: string | null) => {
    if (orderId) navigate(`/service/ordens/${orderId}`);
    else if (customerPackageId) navigate(`/service/customer-packages/${customerPackageId}`);
  };

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Operação"
        title="Pagamentos"
        description="Pagamentos recebidos em ordens e pacotes. Registro operacional do Service — não movimenta o caixa global."
      />

      <SectionCard noPadding>
        <div className="flex items-center gap-3 px-5 py-3">
          <Select value={statusFilter} onValueChange={(v) => setStatusFilter(v as "all" | SvcPaymentStatus)}>
            <SelectTrigger className="h-9 w-44"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="all">Todos os status</SelectItem>
              {STATUSES.map((s) => <SelectItem key={s} value={s}>{PAYMENT_STATUS_LABELS[s]}</SelectItem>)}
            </SelectContent>
          </Select>
          <span className="ml-auto text-[12px] text-muted-foreground">{payments.length} pagamento(s)</span>
        </div>

        {isLoading && (
          <div className="space-y-2 p-5 pt-0">{[1, 2, 3].map((i) => <Skeleton key={i} className="h-12 w-full" />)}</div>
        )}

        {isError && <ErrorState onRetry={refetch} />}

        {!isLoading && !isError && payments.length === 0 && (
          <EmptyState icon={CreditCard} title="Nenhum pagamento" description="Pagamentos aparecem aqui ao registrá-los em ordens e pacotes." />
        )}

        {!isLoading && !isError && payments.length > 0 && (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Data</TableHead>
                <TableHead>Cliente</TableHead>
                <TableHead>Origem</TableHead>
                <TableHead>Forma</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="text-right">Valor</TableHead>
                <TableHead className="w-10" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {payments.map((p) => (
                <TableRow key={p.id}>
                  <TableCell className="text-muted-foreground">{formatDate(p.paidAt)}</TableCell>
                  <TableCell className="text-foreground">{customerName(p.customerId)}</TableCell>
                  <TableCell>
                    <button
                      onClick={() => goToTarget(p.orderId, p.customerPackageId)}
                      className="text-[12.5px] text-primary hover:underline"
                    >
                      {p.orderId ? "Ordem" : "Pacote"}
                    </button>
                  </TableCell>
                  <TableCell className="text-muted-foreground">{PAYMENT_METHOD_LABELS[p.method]}</TableCell>
                  <TableCell>
                    <StatusBadge variant={PAYMENT_STATUS_VARIANTS[p.status]} label={PAYMENT_STATUS_LABELS[p.status]} dot />
                  </TableCell>
                  <TableCell className="text-right font-medium text-foreground">{formatCurrency(p.amount)}</TableCell>
                  <TableCell>
                    {p.status === "Paid" && (
                      <DropdownMenu>
                        <DropdownMenuTrigger asChild>
                          <Button variant="ghost" size="icon" className="h-8 w-8"><MoreHorizontal className="h-4 w-4" /></Button>
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end">
                          <DropdownMenuItem onClick={() => handleVoid(p.id)}>Estornar</DropdownMenuItem>
                        </DropdownMenuContent>
                      </DropdownMenu>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </SectionCard>
    </div>
  );
}
