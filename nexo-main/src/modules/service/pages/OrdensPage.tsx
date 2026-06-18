import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { ClipboardList, Plus } from "lucide-react";
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
import { formatCurrency } from "@/lib/formatters";
import { useCustomers } from "@/modules/customers/hooks/use-customers";
import type { SvcOrderStatus } from "../api/service.api";
import { useOrders } from "../hooks/useOrders";
import { useServicePreset } from "../context/ServicePresetContext";
import { ORDER_STATUS_LABELS, ORDER_STATUS_VARIANTS } from "../lib/order-status";
import { OrderCreateDialog } from "../components/OrderCreateDialog";

const STATUS_FILTERS: SvcOrderStatus[] = ["Draft", "Open", "InProgress", "Completed", "Cancelled"];

export default function OrdensPage() {
  const { labels } = useServicePreset();
  const orderTerm = labels?.order ?? "Ordem";
  const navigate = useNavigate();

  const [statusFilter, setStatusFilter] = useState<"all" | SvcOrderStatus>("all");
  const [createOpen, setCreateOpen] = useState(false);

  const { data, isLoading, isError, refetch } = useOrders(
    statusFilter === "all" ? {} : { status: statusFilter }
  );
  const { data: customers } = useCustomers(false);
  const customerName = (id: string) => customers?.find((c) => c.id === id)?.name ?? "—";

  const orders = data ?? [];

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Operação"
        title={`${orderTerm}s`}
        description="Ordens de serviço com itens, status e total."
        actions={
          <Button onClick={() => setCreateOpen(true)}>
            <Plus className="mr-2 h-4 w-4" /> Nova {orderTerm.toLowerCase()}
          </Button>
        }
      />

      <SectionCard noPadding>
        <div className="flex items-center gap-3 px-5 py-3">
          <Select value={statusFilter} onValueChange={(v) => setStatusFilter(v as "all" | SvcOrderStatus)}>
            <SelectTrigger className="h-9 w-48"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="all">Todos os status</SelectItem>
              {STATUS_FILTERS.map((s) => (
                <SelectItem key={s} value={s}>{ORDER_STATUS_LABELS[s]}</SelectItem>
              ))}
            </SelectContent>
          </Select>
          <span className="ml-auto text-[12px] text-muted-foreground">
            {orders.length} {orders.length === 1 ? "ordem" : "ordens"}
          </span>
        </div>

        {isLoading && (
          <div className="space-y-2 p-5 pt-0">
            {[1, 2, 3].map((i) => <Skeleton key={i} className="h-12 w-full" />)}
          </div>
        )}

        {isError && <ErrorState onRetry={refetch} />}

        {!isLoading && !isError && orders.length === 0 && (
          <EmptyState
            icon={ClipboardList}
            title="Nenhuma ordem encontrada"
            description={`Crie a primeira ${orderTerm.toLowerCase()}.`}
            action={
              <Button variant="outline" onClick={() => setCreateOpen(true)}>
                <Plus className="mr-2 h-4 w-4" /> Nova {orderTerm.toLowerCase()}
              </Button>
            }
          />
        )}

        {!isLoading && !isError && orders.length > 0 && (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Código</TableHead>
                <TableHead>Cliente</TableHead>
                <TableHead className="text-center">Itens</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="text-right">Total</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {orders.map((o) => (
                <TableRow
                  key={o.id}
                  className="cursor-pointer"
                  onClick={() => navigate(`/service/ordens/${o.id}`)}
                >
                  <TableCell className="font-medium text-foreground">{o.code}</TableCell>
                  <TableCell className="text-muted-foreground">{customerName(o.customerId)}</TableCell>
                  <TableCell className="text-center text-muted-foreground">{o.items.length}</TableCell>
                  <TableCell>
                    <StatusBadge variant={ORDER_STATUS_VARIANTS[o.status]} label={ORDER_STATUS_LABELS[o.status]} dot />
                  </TableCell>
                  <TableCell className="text-right font-medium text-foreground">{formatCurrency(o.totalAmount)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </SectionCard>

      <OrderCreateDialog open={createOpen} onClose={() => setCreateOpen(false)} />
    </div>
  );
}
