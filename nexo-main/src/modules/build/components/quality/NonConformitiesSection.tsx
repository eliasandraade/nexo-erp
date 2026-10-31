import { useState } from "react";
import { AlertTriangle, Camera, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useNonConformities } from "../../hooks/use-build-quality";
import type {
  BuildNcSeverity, BuildNcStatus, BuildNonConformityDto, NonConformityFilters,
} from "../../api/build-quality.api";
import { NC_SEVERITIES, NC_STATUS, severityInfo } from "../../lib/quality";
import { fmtDate } from "../../lib/format";
import { StatusPill } from "../commercial/StatusPill";
import { NonConformityFormDialog } from "./NonConformityFormDialog";
import { NonConformityDetailDialog } from "./NonConformityDetailDialog";

const selectCls = "h-8 rounded-md border border-input bg-background px-2 text-sm";

/** Non-conformities of the obra: filters, list, create and the detail with the treatment flow. */
export function NonConformitiesSection({ projectId }: { projectId: string }) {
  const [filters, setFilters] = useState<NonConformityFilters>({});
  const { data, isLoading, isError } = useNonConformities(projectId, filters);
  const list = Array.isArray(data) ? data : [];
  const [editing, setEditing] = useState<BuildNonConformityDto | null | undefined>(undefined);
  const [detail, setDetail] = useState<BuildNonConformityDto | null>(null);

  return (
    <div className="space-y-3">
      <div className="flex items-start gap-2 rounded-md border border-red-200 bg-red-50/60 p-3 text-xs text-red-800 dark:border-red-900 dark:bg-red-900/10 dark:text-red-300">
        <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
        <p>
          Não conformidade crítica aberta impede concluir a etapa (100%) — no avanço manual e na aprovação de medição — e
          impede a entrega do imóvel. Resolvida ou cancelada, libera.
        </p>
      </div>

      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <select aria-label="Filtrar por status" value={filters.status ?? ""} className={selectCls}
            onChange={(e) => setFilters((f) => ({ ...f, status: (e.target.value || undefined) as BuildNcStatus | undefined }))}>
            <option value="">Todos os status</option>
            {(Object.keys(NC_STATUS) as BuildNcStatus[]).map((s) => <option key={s} value={s}>{NC_STATUS[s].label}</option>)}
          </select>
          <select aria-label="Filtrar por gravidade" value={filters.severity ?? ""} className={selectCls}
            onChange={(e) => setFilters((f) => ({ ...f, severity: (e.target.value || undefined) as BuildNcSeverity | undefined }))}>
            <option value="">Todas as gravidades</option>
            {NC_SEVERITIES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
          </select>
          <label className="flex items-center gap-1.5 text-xs">
            <input type="checkbox" checked={!!filters.overdue}
              onChange={(e) => setFilters((f) => ({ ...f, overdue: e.target.checked || undefined }))} />
            <span>Só vencidas</span>
          </label>
        </div>
        <Button size="sm" onClick={() => setEditing(null)}><Plus className="mr-1 h-3.5 w-3.5" /> Nova NC</Button>
      </div>

      {isLoading ? (
        <div className="h-24 animate-pulse rounded-md bg-muted" />
      ) : isError ? (
        <p className="rounded-md border border-destructive/20 bg-destructive/5 p-6 text-center text-sm text-destructive">
          Erro ao carregar as não conformidades.
        </p>
      ) : list.length === 0 ? (
        <p className="rounded-md border border-dashed border-border p-8 text-center text-sm text-muted-foreground">
          Nenhuma não conformidade {filters.status || filters.severity || filters.overdue ? "com esses filtros" : "nesta obra"}.
        </p>
      ) : (
        <ul className="space-y-2">
          {list.map((nc) => {
            const st = NC_STATUS[nc.status] ?? { label: nc.status, tone: "gray" as const };
            const sev = severityInfo(nc.severity);
            return (
              <li key={nc.id}>
                <button type="button" onClick={() => setDetail(nc)}
                  className="w-full rounded-md border border-border bg-card p-3 text-left hover:bg-muted/40">
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <p className="min-w-0 text-sm font-semibold">
                      <span className="mr-1 text-muted-foreground">#{nc.number}</span>{nc.title}
                    </p>
                    <div className="flex flex-wrap gap-1">
                      <StatusPill label={sev.label} tone={sev.tone} />
                      <StatusPill label={st.label} tone={st.tone} />
                      {nc.isOverdue && <StatusPill label="Vencida" tone="red" />}
                    </div>
                  </div>
                  <p className="mt-1 flex flex-wrap items-center gap-x-2 text-xs text-muted-foreground">
                    <span>{nc.stageName ?? "Sem etapa"}</span>
                    {nc.responsibleName && <span>· {nc.responsibleName}</span>}
                    {nc.dueDate && <span>· prazo {fmtDate(nc.dueDate)}</span>}
                    {nc.photoKeys?.length > 0 && <span className="inline-flex items-center gap-0.5">· <Camera className="h-3 w-3" /> {nc.photoKeys.length}</span>}
                  </p>
                </button>
              </li>
            );
          })}
        </ul>
      )}

      <NonConformityFormDialog projectId={projectId} nc={editing} onClose={() => setEditing(undefined)}
        onSaved={(saved) => { if (detail?.id === saved.id) setDetail(saved); }} />
      <NonConformityDetailDialog projectId={projectId} nc={editing === undefined ? detail : null}
        onChange={setDetail} onEdit={(nc) => setEditing(nc)} onClose={() => setDetail(null)} />
    </div>
  );
}
