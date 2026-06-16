import { useState } from "react";
import { Search, RefreshCw, ChevronLeft, ChevronRight, Filter } from "lucide-react";
import { cn } from "@/lib/utils";
import { useAuditLog } from "../hooks/usePlatformSystem";
import { usePlatformTenants } from "../hooks/usePlatformTenants";

// ─── Constants ────────────────────────────────────────────────────────────────

// Real backend values (snake_case, lowercase) — must match AuditSeverity / AuditActions.
// The backend filters case-lowered, so these values map 1:1 to stored records.
const SEVERITIES: { value: string; label: string }[] = [
  { value: "info",     label: "Info" },
  { value: "warning",  label: "Aviso" },
  { value: "critical", label: "Crítico" },
];

const ACTION_TYPES: { value: string; label: string }[] = [
  { value: "platform_impersonation", label: "Impersonation" },
  { value: "user_password_changed",  label: "Senha redefinida" },
  { value: "user_session_revoked",   label: "Sessões revogadas" },
  { value: "tenant_created",         label: "Cliente criado" },
  { value: "tenant_updated",         label: "Cliente atualizado" },
  { value: "tenant_status_changed",  label: "Status do cliente alterado" },
  { value: "module_activated",       label: "Módulo ativado" },
  { value: "module_deactivated",     label: "Módulo revogado" },
  { value: "user_logged_in",         label: "Login" },
  { value: "user_logged_out",        label: "Logout" },
  { value: "user_created",           label: "Usuário criado" },
  { value: "user_updated",           label: "Usuário atualizado" },
  { value: "sale_completed",         label: "Venda concluída" },
  { value: "sale_cancelled",         label: "Venda cancelada" },
  { value: "stock_adjustment",       label: "Ajuste de estoque" },
  { value: "cash_open",              label: "Caixa aberto" },
  { value: "cash_close",             label: "Caixa fechado" },
];

const PAGE_SIZE = 25;

const SEVERITY_STYLES: Record<string, string> = {
  info:     "bg-blue-500/10 text-blue-600",
  warning:  "bg-amber-500/10 text-amber-600",
  critical: "bg-red-700/10 text-red-700",
};

const SEVERITY_LABELS: Record<string, string> = {
  info: "Info", warning: "Aviso", critical: "Crítico",
};

const ACTION_LABELS: Record<string, string> = Object.fromEntries(
  ACTION_TYPES.map(a => [a.value, a.label])
);

// ─── Component ────────────────────────────────────────────────────────────────

export default function PlatformActivityPage() {
  const [tenantId, setTenantId] = useState("");
  const [search,   setSearch]   = useState("");
  const [severity, setSeverity] = useState("");
  const [actionType, setActionType] = useState("");
  const [page, setPage] = useState(1);

  // debounced search: run query with current text
  const { data, isLoading, isFetching, refetch } = useAuditLog({
    tenantId:   tenantId   || undefined,
    search:     search     || undefined,
    severity:   severity   || undefined,
    actionType: actionType || undefined,
    page,
    pageSize: PAGE_SIZE,
  });

  const { data: tenantsData } = usePlatformTenants();

  const totalPages = data ? Math.max(1, Math.ceil(data.total / PAGE_SIZE)) : 1;

  const resetFilters = () => {
    setTenantId("");
    setSearch("");
    setSeverity("");
    setActionType("");
    setPage(1);
  };

  const handleFilterChange = (fn: () => void) => {
    fn();
    setPage(1);
  };

  return (
    <div className="p-6 space-y-5 max-w-6xl">

      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="font-display text-[20px] font-bold text-foreground tracking-tight">Log de Atividade</h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            Auditoria global de todas as ações do sistema
          </p>
        </div>
        <button
          onClick={() => refetch()}
          disabled={isFetching}
          className="flex items-center gap-1.5 h-8 px-3 rounded-xl border border-border text-sm text-muted-foreground hover:text-foreground hover:bg-muted transition-colors disabled:opacity-50"
        >
          <RefreshCw className={cn("h-3.5 w-3.5", isFetching && "animate-spin")} />
          Atualizar
        </button>
      </div>

      {/* Filters */}
      <div className="bg-card border border-border rounded-lg p-4 space-y-3">
        <div className="flex items-center gap-1.5 text-xs font-medium text-muted-foreground mb-1">
          <Filter className="h-3.5 w-3.5" /> Filtros
        </div>
        <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
          {/* Search */}
          <div className="relative col-span-2 md:col-span-1">
            <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-muted-foreground pointer-events-none" />
            <input
              placeholder="Buscar descrição..."
              value={search}
              onChange={e => handleFilterChange(() => setSearch(e.target.value))}
              className="w-full h-8 pl-8 pr-3 rounded-md border border-border bg-background text-sm focus:outline-none focus:ring-2 focus:ring-primary/20"
            />
          </div>

          {/* Tenant */}
          <select
            value={tenantId}
            onChange={e => handleFilterChange(() => setTenantId(e.target.value))}
            className="h-8 px-2.5 rounded-md border border-border bg-background text-sm focus:outline-none focus:ring-2 focus:ring-primary/20"
          >
            <option value="">Todos os clientes</option>
            {tenantsData?.map(t => (
              <option key={t.id} value={t.id}>
                {t.tradeName ?? t.companyName}
              </option>
            ))}
          </select>

          {/* Severity */}
          <select
            value={severity}
            onChange={e => handleFilterChange(() => setSeverity(e.target.value))}
            className="h-8 px-2.5 rounded-md border border-border bg-background text-sm focus:outline-none focus:ring-2 focus:ring-primary/20"
          >
            <option value="">Todas as severidades</option>
            {SEVERITIES.map(s => (
              <option key={s.value} value={s.value}>{s.label}</option>
            ))}
          </select>

          {/* Action type */}
          <select
            value={actionType}
            onChange={e => handleFilterChange(() => setActionType(e.target.value))}
            className="h-8 px-2.5 rounded-md border border-border bg-background text-sm focus:outline-none focus:ring-2 focus:ring-primary/20"
          >
            <option value="">Todos os tipos</option>
            {ACTION_TYPES.map(a => (
              <option key={a.value} value={a.value}>{a.label}</option>
            ))}
          </select>
        </div>

        {/* Active filter chips + clear */}
        {(tenantId || search || severity || actionType) && (
          <div className="flex items-center gap-2 pt-1">
            <span className="text-xs text-muted-foreground">Filtros ativos:</span>
            {search     && <Chip label={`"${search}"`}     onRemove={() => handleFilterChange(() => setSearch(""))} />}
            {tenantId   && <Chip label="Cliente específico" onRemove={() => handleFilterChange(() => setTenantId(""))} />}
            {severity   && <Chip label={SEVERITY_LABELS[severity] ?? severity}     onRemove={() => handleFilterChange(() => setSeverity(""))} />}
            {actionType && <Chip label={ACTION_LABELS[actionType] ?? actionType}   onRemove={() => handleFilterChange(() => setActionType(""))} />}
            <button
              onClick={resetFilters}
              className="text-xs text-muted-foreground hover:text-foreground underline-offset-2 hover:underline ml-1"
            >
              Limpar tudo
            </button>
          </div>
        )}
      </div>

      {/* Table */}
      <div className="bg-card border border-border rounded-lg overflow-hidden">
        {/* Summary */}
        <div className="px-4 py-3 border-b border-border flex items-center justify-between">
          <span className="text-sm text-muted-foreground">
            {isLoading ? "Carregando..." : `${data?.total ?? 0} registro${data?.total !== 1 ? "s" : ""} encontrado${data?.total !== 1 ? "s" : ""}`}
          </span>
          {isFetching && !isLoading && (
            <span className="text-xs text-muted-foreground flex items-center gap-1">
              <RefreshCw className="h-3 w-3 animate-spin" /> Atualizando...
            </span>
          )}
        </div>

        {isLoading ? (
          <div className="p-10 text-center text-sm text-muted-foreground">Carregando registros...</div>
        ) : !data?.records.length ? (
          <div className="p-10 text-center text-sm text-muted-foreground">
            Nenhum registro encontrado para os filtros selecionados.
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border bg-muted/30">
                  <th className="text-left px-4 py-2.5 text-xs font-medium text-muted-foreground whitespace-nowrap">Data / Hora</th>
                  <th className="text-left px-4 py-2.5 text-xs font-medium text-muted-foreground">Tipo</th>
                  <th className="text-left px-4 py-2.5 text-xs font-medium text-muted-foreground">Severidade</th>
                  <th className="text-left px-4 py-2.5 text-xs font-medium text-muted-foreground">Ator</th>
                  <th className="text-left px-4 py-2.5 text-xs font-medium text-muted-foreground">Entidade</th>
                  <th className="text-left px-4 py-2.5 text-xs font-medium text-muted-foreground">Descrição</th>
                  <th className="text-left px-4 py-2.5 text-xs font-medium text-muted-foreground whitespace-nowrap">IP</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {data.records.map(r => (
                  <tr key={r.id} className="hover:bg-muted/20 transition-colors">
                    <td className="px-4 py-3 text-xs text-muted-foreground whitespace-nowrap font-mono">
                      {new Date(r.createdAt).toLocaleString("pt-BR")}
                    </td>
                    <td className="px-4 py-3">
                      <span title={r.actionType} className="text-xs text-foreground">
                        {ACTION_LABELS[r.actionType] ?? r.actionType}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <span className={cn(
                        "px-1.5 py-0.5 rounded text-[10px] font-medium",
                        SEVERITY_STYLES[r.severity] ?? "bg-muted text-muted-foreground"
                      )}>
                        {SEVERITY_LABELS[r.severity] ?? r.severity}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      {r.actorName ? (
                        <div>
                          <p className="text-xs font-medium text-foreground">{r.actorName}</p>
                          <p className="text-[10px] text-muted-foreground">{r.actorType}</p>
                        </div>
                      ) : (
                        <span className="text-xs text-muted-foreground">—</span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <p className="text-xs text-muted-foreground">{r.entityType}</p>
                      <p className="font-mono text-[10px] text-muted-foreground truncate max-w-[120px]">{r.entityId}</p>
                    </td>
                    <td className="px-4 py-3 text-xs text-foreground max-w-xs truncate" title={r.description}>
                      {r.description}
                    </td>
                    <td className="px-4 py-3 font-mono text-[10px] text-muted-foreground whitespace-nowrap">
                      {r.ipAddress ?? "—"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Pagination */}
        {!isLoading && (data?.total ?? 0) > PAGE_SIZE && (
          <div className="px-4 py-3 border-t border-border flex items-center justify-between">
            <span className="text-xs text-muted-foreground">
              Página {page} de {totalPages}
            </span>
            <div className="flex items-center gap-1">
              <button
                onClick={() => setPage(p => Math.max(1, p - 1))}
                disabled={page <= 1}
                className="p-1.5 rounded-md border border-border hover:bg-muted disabled:opacity-40 transition-colors"
              >
                <ChevronLeft className="h-3.5 w-3.5" />
              </button>
              {Array.from({ length: Math.min(5, totalPages) }, (_, i) => {
                const start = Math.max(1, Math.min(page - 2, totalPages - 4));
                const pg = start + i;
                return (
                  <button
                    key={pg}
                    onClick={() => setPage(pg)}
                    className={cn(
                      "w-7 h-7 rounded-md text-xs border transition-colors",
                      pg === page
                        ? "bg-primary text-primary-foreground border-primary"
                        : "border-border hover:bg-muted text-muted-foreground"
                    )}
                  >
                    {pg}
                  </button>
                );
              })}
              <button
                onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                disabled={page >= totalPages}
                className="p-1.5 rounded-md border border-border hover:bg-muted disabled:opacity-40 transition-colors"
              >
                <ChevronRight className="h-3.5 w-3.5" />
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

// ─── Helpers ─────────────────────────────────────────────────────────────────

function Chip({ label, onRemove }: { label: string; onRemove: () => void }) {
  return (
    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded bg-primary/10 text-primary text-xs">
      {label}
      <button onClick={onRemove} className="ml-0.5 hover:text-destructive transition-colors">×</button>
    </span>
  );
}
