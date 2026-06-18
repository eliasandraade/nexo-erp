import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { PackageCheck, Plus } from "lucide-react";
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
import { formatDate } from "@/lib/formatters";
import { useCustomers } from "@/modules/customers/hooks/use-customers";
import type { SvcCustomerPackageStatus } from "../api/service.api";
import { useCustomerPackages } from "../hooks/useCustomerPackages";
import { usePackages } from "../hooks/usePackages";
import {
  CUSTOMER_PACKAGE_STATUS_LABELS,
  CUSTOMER_PACKAGE_STATUS_VARIANTS,
} from "../lib/customer-package-status";
import { AssignPackageDialog } from "../components/AssignPackageDialog";

const STATUSES: SvcCustomerPackageStatus[] = ["Active", "Consumed", "Expired", "Cancelled"];

export default function CustomerPackagesPage() {
  const navigate = useNavigate();
  const [statusFilter, setStatusFilter] = useState<"all" | SvcCustomerPackageStatus>("all");
  const [assignOpen, setAssignOpen] = useState(false);

  const { data, isLoading, isError, refetch } = useCustomerPackages(
    statusFilter === "all" ? {} : { status: statusFilter }
  );
  const { data: customers } = useCustomers(false);
  const { data: packages } = usePackages(undefined);

  const customerName = (id: string) => customers?.find((c) => c.id === id)?.name ?? "—";
  const packageName = (id: string) => packages?.find((p) => p.id === id)?.name ?? "—";
  const items = data ?? [];

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Operação"
        title="Pacotes de clientes"
        description="Pacotes vendidos e saldos por cliente."
        actions={
          <Button onClick={() => setAssignOpen(true)}>
            <Plus className="mr-2 h-4 w-4" /> Vender pacote
          </Button>
        }
      />

      <SectionCard noPadding>
        <div className="flex items-center gap-3 px-5 py-3">
          <Select value={statusFilter} onValueChange={(v) => setStatusFilter(v as "all" | SvcCustomerPackageStatus)}>
            <SelectTrigger className="h-9 w-48"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="all">Todos os status</SelectItem>
              {STATUSES.map((s) => <SelectItem key={s} value={s}>{CUSTOMER_PACKAGE_STATUS_LABELS[s]}</SelectItem>)}
            </SelectContent>
          </Select>
          <span className="ml-auto text-[12px] text-muted-foreground">{items.length} pacote(s)</span>
        </div>

        {isLoading && (
          <div className="space-y-2 p-5 pt-0">{[1, 2, 3].map((i) => <Skeleton key={i} className="h-12 w-full" />)}</div>
        )}

        {isError && <ErrorState onRetry={refetch} />}

        {!isLoading && !isError && items.length === 0 && (
          <EmptyState
            icon={PackageCheck}
            title="Nenhum pacote vendido"
            description="Atribua um pacote a um cliente para começar."
            action={
              <Button variant="outline" onClick={() => setAssignOpen(true)}>
                <Plus className="mr-2 h-4 w-4" /> Vender pacote
              </Button>
            }
          />
        )}

        {!isLoading && !isError && items.length > 0 && (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Código</TableHead>
                <TableHead>Cliente</TableHead>
                <TableHead>Pacote</TableHead>
                <TableHead>Expira</TableHead>
                <TableHead>Status</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {items.map((cp) => (
                <TableRow key={cp.id} className="cursor-pointer" onClick={() => navigate(`/service/customer-packages/${cp.id}`)}>
                  <TableCell className="font-medium text-foreground">{cp.code}</TableCell>
                  <TableCell className="text-muted-foreground">{customerName(cp.customerId)}</TableCell>
                  <TableCell className="text-muted-foreground">{packageName(cp.packageId)}</TableCell>
                  <TableCell className="text-muted-foreground">{cp.expiresAt ? formatDate(cp.expiresAt) : "—"}</TableCell>
                  <TableCell>
                    <StatusBadge variant={CUSTOMER_PACKAGE_STATUS_VARIANTS[cp.status]} label={CUSTOMER_PACKAGE_STATUS_LABELS[cp.status]} dot />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </SectionCard>

      <AssignPackageDialog open={assignOpen} onClose={() => setAssignOpen(false)} />
    </div>
  );
}
