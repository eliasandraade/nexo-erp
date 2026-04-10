import { useState } from "react";
import { AlertCircle, Shield } from "lucide-react";
import { useQuery } from "@tanstack/react-query";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { Skeleton } from "@/components/ui/skeleton";
import { StatusBadge } from "@/components/shared/StatusBadge";
import { auditService } from "../services/auditService";
import { AuditFilters } from "../components/AuditFilters";
import { AuditTable } from "../components/AuditTable";
import type { AuditFilters as AuditFiltersType } from "../types";

const defaultFilters: AuditFiltersType = {
  actionType: "all",
  severity: "all",
  actor: "all",
};

export default function AuditoriaPage() {
  const [filters, setFilters] = useState<AuditFiltersType>(defaultFilters);

  const { data: stats, isLoading: loadingStats } = useQuery({
    queryKey: ["audit-stats"],
    queryFn: () => auditService.getStats(),
  });

  const {
    data: records = [],
    isLoading: loadingRecords,
    isError,
  } = useQuery({
    queryKey: ["audit-records", filters],
    queryFn: () => auditService.listAuditRecords(filters),
  });

  return (
    <div className="space-y-6">
      <PageHeader
        title="Auditoria"
        description="Consulte o histórico de ações sensíveis do sistema."
      />

      {/* Stats strip */}
      {!loadingStats && stats && (
        <div className="flex flex-wrap gap-3">
          <div className="bg-card border border-border rounded-lg px-4 py-2.5 flex items-center gap-3">
            <span className="text-xs text-muted-foreground">Total</span>
            <span className="text-sm font-bold tabular-nums">{stats.total}</span>
          </div>
          <div className="bg-card border border-border rounded-lg px-4 py-2.5 flex items-center gap-3">
            <StatusBadge label={`${stats.critical} crítico(s)`} variant="danger" />
          </div>
          <div className="bg-card border border-border rounded-lg px-4 py-2.5 flex items-center gap-3">
            <StatusBadge label={`${stats.warning} atenção`} variant="warning" />
          </div>
          <div className="bg-card border border-border rounded-lg px-4 py-2.5 flex items-center gap-3">
            <StatusBadge label={`${stats.info} informativos`} variant="info" />
          </div>
        </div>
      )}

      <SectionCard title="Log de auditoria">
        <div className="space-y-4">
          <AuditFilters filters={filters} onChange={setFilters} />

          {isError ? (
            <EmptyState
              icon={AlertCircle}
              title="Erro ao carregar registros"
              description="Não foi possível carregar o log de auditoria. Tente novamente."
            />
          ) : loadingRecords ? (
            <div className="space-y-3">
              {Array.from({ length: 8 }).map((_, i) => (
                <Skeleton key={i} className="h-10 w-full" />
              ))}
            </div>
          ) : records.length > 0 ? (
            <>
              <AuditTable records={records} />
              <div className="pt-2 text-xs text-muted-foreground">
                {records.length} registro(s) encontrado(s)
              </div>
            </>
          ) : (
            <EmptyState
              icon={Shield}
              title="Nenhum registro encontrado"
              description="Nenhum registro corresponde aos filtros selecionados."
            />
          )}
        </div>
      </SectionCard>
    </div>
  );
}
