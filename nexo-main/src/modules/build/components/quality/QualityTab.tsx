import { useState } from "react";
import { ChevronRight, ClipboardCheck, Plus } from "lucide-react";
import { toast } from "sonner";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { useCreateInspection, useInspections, useQualitySummary } from "../../hooks/use-build-quality";
import type { BuildInspectionStatus, BuildNcSeverity, BuildQualitySummaryDto } from "../../api/build-quality.api";
import { INSPECTION_STATUS, NC_SEVERITIES } from "../../lib/quality";
import { fmtDate, fmtPercent } from "../../lib/format";
import { StatusPill } from "../commercial/StatusPill";
import { errorMessage } from "../commercial/errors";
import { InspectionFormDialog } from "./InspectionFormDialog";
import { InspectionRunner } from "./InspectionRunner";
import { NonConformitiesSection } from "./NonConformitiesSection";
import { ChecklistTemplatesSection } from "./ChecklistTemplatesSection";

type Section = "vistorias" | "ncs" | "checklists";

const SECTIONS: Array<{ key: Section; label: string }> = [
  { key: "vistorias",  label: "Vistorias" },
  { key: "ncs",        label: "Não conformidades" },
  { key: "checklists", label: "Checklists" },
];

/** "Qualidade": summary, inspections (run on site), non-conformities and the checklist templates. */
export function QualityTab({ projectId }: { projectId: string }) {
  const [section, setSection] = useState<Section>("vistorias");
  const [running, setRunning] = useState<string | null>(null);

  if (running) return <InspectionRunner projectId={projectId} inspectionId={running} onBack={() => setRunning(null)} />;

  return (
    <div className="space-y-4">
      <QualitySummary projectId={projectId} />
      <div className="flex gap-1 overflow-x-auto rounded-md bg-muted p-1" role="tablist" aria-label="Seções de qualidade">
        {SECTIONS.map((s) => (
          <button key={s.key} role="tab" aria-selected={section === s.key} onClick={() => setSection(s.key)}
            className={cn("whitespace-nowrap rounded px-3 py-1.5 text-sm font-medium",
              section === s.key ? "bg-background shadow-sm" : "text-muted-foreground hover:text-foreground")}>
            {s.label}
          </button>
        ))}
      </div>
      {section === "vistorias" && <InspectionsSection projectId={projectId} onRun={setRunning} />}
      {section === "ncs" && <NonConformitiesSection projectId={projectId} />}
      {section === "checklists" && <ChecklistTemplatesSection />}
    </div>
  );
}

function isSummary(v: unknown): v is BuildQualitySummaryDto {
  return !!v && typeof v === "object" && !Array.isArray(v) && "openNonConformities" in v;
}

function QualitySummary({ projectId }: { projectId: string }) {
  const { data } = useQualitySummary(projectId);
  if (!isSummary(data)) return null;
  const byStatus = data.inspectionsByStatus ?? {};
  const bySeverity = data.openNonConformitiesBySeverity ?? {};
  const statuses = (Object.keys(INSPECTION_STATUS) as BuildInspectionStatus[]).filter((s) => (byStatus[s] ?? 0) > 0);
  return (
    <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4" data-testid="quality-summary">
      <div className="rounded-md border border-border bg-card p-3">
        <p className="text-xs text-muted-foreground">Vistorias</p>
        {statuses.length === 0
          ? <p className="mt-1 text-sm text-muted-foreground">Nenhuma ainda</p>
          : (
            <ul className="mt-1 space-y-0.5 text-xs">
              {statuses.map((s) => (
                <li key={s} className="flex justify-between gap-2"><span>{INSPECTION_STATUS[s].label}</span><span className="font-semibold tabular-nums">{byStatus[s]}</span></li>
              ))}
            </ul>
          )}
      </div>
      <div className="rounded-md border border-border bg-card p-3">
        <p className="text-xs text-muted-foreground">Taxa de aprovação</p>
        <p className="mt-1 text-xl font-bold tabular-nums">{data.passRate == null ? "—" : fmtPercent(data.passRate)}</p>
        <p className="text-[11px] text-muted-foreground">{data.completedInspections} vistoria(s) concluída(s)</p>
      </div>
      <div className="rounded-md border border-border bg-card p-3">
        <p className="text-xs text-muted-foreground">NCs abertas</p>
        <p className="mt-1 text-xl font-bold tabular-nums">{data.openNonConformities}</p>
        <div className="mt-1 flex flex-wrap gap-1">
          {NC_SEVERITIES.filter((s) => (bySeverity[s.value as BuildNcSeverity] ?? 0) > 0).map((s) => (
            <StatusPill key={s.value} label={`${s.label}: ${bySeverity[s.value]}`} tone={s.tone} />
          ))}
        </div>
      </div>
      <div className={cn("rounded-md border bg-card p-3", data.criticalOpen > 0 ? "border-red-300 dark:border-red-800" : "border-border")}>
        <p className="text-xs text-muted-foreground">Atenção</p>
        <ul className="mt-1 space-y-0.5 text-xs">
          <li className="flex justify-between"><span>Vencidas</span><span className="font-semibold tabular-nums">{data.overdueNonConformities}</span></li>
          <li className="flex justify-between"><span>Aguardando verificação</span><span className="font-semibold tabular-nums">{data.awaitingVerification}</span></li>
          <li className={cn("flex justify-between", data.criticalOpen > 0 && "font-semibold text-destructive")}>
            <span>Críticas abertas</span><span className="tabular-nums">{data.criticalOpen}</span>
          </li>
        </ul>
      </div>
    </div>
  );
}

function InspectionsSection({ projectId, onRun }: { projectId: string; onRun: (id: string) => void }) {
  const [status, setStatus] = useState<BuildInspectionStatus | "">("");
  const { data, isLoading, isError } = useInspections(projectId, { kind: "Quality", status: status || undefined });
  const list = Array.isArray(data) ? data : [];
  const create = useCreateInspection(projectId);
  const [creating, setCreating] = useState(false);

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <select aria-label="Filtrar vistorias por status" value={status}
          onChange={(e) => setStatus(e.target.value as BuildInspectionStatus | "")}
          className="h-8 rounded-md border border-input bg-background px-2 text-sm">
          <option value="">Todos os status</option>
          {(Object.keys(INSPECTION_STATUS) as BuildInspectionStatus[]).map((s) => <option key={s} value={s}>{INSPECTION_STATUS[s].label}</option>)}
        </select>
        <Button size="sm" onClick={() => setCreating(true)}><Plus className="mr-1 h-3.5 w-3.5" /> Nova vistoria</Button>
      </div>

      {isLoading ? (
        <div className="h-24 animate-pulse rounded-md bg-muted" />
      ) : isError ? (
        <p className="rounded-md border border-destructive/20 bg-destructive/5 p-6 text-center text-sm text-destructive">
          Erro ao carregar as vistorias.
        </p>
      ) : list.length === 0 ? (
        <p className="rounded-md border border-dashed border-border p-8 text-center text-sm text-muted-foreground">
          Nenhuma vistoria de qualidade {status ? "com esse status" : "nesta obra"}.
        </p>
      ) : (
        <ul className="space-y-2">
          {list.map((i) => {
            const st = INSPECTION_STATUS[i.status] ?? { label: i.status, tone: "gray" as const };
            return (
              <li key={i.id}>
                <button type="button" onClick={() => onRun(i.id)}
                  className="flex w-full items-center gap-3 rounded-md border border-border bg-card p-3 text-left hover:bg-muted/40">
                  <ClipboardCheck className="h-5 w-5 shrink-0 text-muted-foreground" />
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-semibold">{i.title}</p>
                    <p className="text-xs text-muted-foreground">
                      {i.stageName ?? "Sem etapa"}
                      {i.scheduledFor ? ` · ${fmtDate(i.scheduledFor)}` : ""}
                      {i.inspectorName ? ` · ${i.inspectorName}` : ""}
                    </p>
                    <p className="text-xs text-muted-foreground">
                      {i.okItems} ok · {i.notOkItems} não ok · {i.pendingItems} pendente(s)
                    </p>
                  </div>
                  <StatusPill label={st.label} tone={st.tone} />
                  <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground" />
                </button>
              </li>
            );
          })}
        </ul>
      )}

      <InspectionFormDialog open={creating} projectId={projectId} pending={create.isPending} onClose={() => setCreating(false)}
        onSubmit={(req) => create.mutate(req, {
          onSuccess: (insp) => { toast.success("Vistoria criada."); setCreating(false); onRun(insp.id); },
          onError:   (e) => toast.error(errorMessage(e, "Erro ao criar a vistoria.")),
        })} />
    </div>
  );
}
