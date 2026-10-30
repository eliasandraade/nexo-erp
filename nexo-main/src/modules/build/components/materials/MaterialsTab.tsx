import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, Ban, ChevronDown, ChevronRight, Pencil, Plus, ShoppingCart, Tag, Truck } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { fetchSuppliers } from "@/modules/suppliers/api/suppliers.api";
import { useStages } from "../../hooks/use-build";
import { useProjectMovements } from "../../hooks/use-interpreter";
import { useMaterialAction, useMaterials } from "../../hooks/use-build-commercial";
import type { BuildMaterialRequestDto, BuildMaterialStatus, MaterialFilters } from "../../api/build-commercial.api";
import { MATERIAL_STATUS } from "../../lib/commercial";
import { fmtBRL, fmtDate } from "../../lib/format";
import { StatusPill } from "../commercial/StatusPill";
import { errorMessage } from "../commercial/errors";
import { MaterialFormDialog } from "./MaterialFormDialog";
import { MaterialStepDialog, type MaterialStep } from "./MaterialStepDialog";

const selectCls = "h-8 rounded-md border border-input bg-background px-2 text-sm";

/** "Materiais": requests of the obra from quote to delivery, late ones flagged, linked to an obra expense. */
export function MaterialsTab({ projectId }: { projectId: string }) {
  const [filters, setFilters] = useState<MaterialFilters>({});
  const { data: list, isLoading, isError } = useMaterials(projectId, filters);
  const { data: stages = [] } = useStages(projectId);
  const { data: suppliers = [] } = useQuery({
    queryKey:  ["suppliers", "list"],
    queryFn:   () => fetchSuppliers(),
    staleTime: 60_000,
  });
  const movementsQ = useProjectMovements(projectId);
  const movements = movementsQ.data?.pages.flatMap((p) => p.items) ?? [];
  const actionMut = useMaterialAction(projectId);

  const [editing, setEditing] = useState<BuildMaterialRequestDto | null | undefined>(undefined);
  const [stepFor, setStepFor] = useState<{ material: BuildMaterialRequestDto; step: MaterialStep } | null>(null);
  const [expanded, setExpanded] = useState<string | null>(null);

  const cancel = (m: BuildMaterialRequestDto) => {
    if (!confirm(`Cancelar o pedido de “${m.materialName}”?`)) return;
    actionMut.mutate({ id: m.id, action: { kind: "cancel" } }, {
      onSuccess: () => toast.success("Material cancelado."),
      onError:   (e) => toast.error(errorMessage(e, "Não foi possível cancelar.")),
    });
  };

  const linkMovement = (m: BuildMaterialRequestDto, movementId: string) => {
    actionMut.mutate({ id: m.id, action: { kind: "movement", movementId: movementId || null } }, {
      onSuccess: () => toast.success(movementId ? "Despesa ligada ao material." : "Despesa desligada."),
      onError:   (e) => toast.error(errorMessage(e, "Não foi possível ligar a despesa.")),
    });
  };

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <select aria-label="Filtrar por status" value={filters.status ?? ""} className={selectCls}
            onChange={(e) => setFilters((f) => ({ ...f, status: (e.target.value || undefined) as BuildMaterialStatus | undefined }))}>
            <option value="">Todos os status</option>
            {Object.entries(MATERIAL_STATUS).map(([k, v]) => <option key={k} value={k}>{v.label}</option>)}
          </select>
          <select aria-label="Filtrar por etapa" value={filters.stageId ?? ""} className={selectCls}
            onChange={(e) => setFilters((f) => ({ ...f, stageId: e.target.value || undefined }))}>
            <option value="">Todas as etapas</option>
            {stages.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
          <label className="flex items-center gap-1.5 text-sm">
            <input type="checkbox" checked={!!filters.late}
              onChange={(e) => setFilters((f) => ({ ...f, late: e.target.checked || undefined }))} />
            Só atrasados
          </label>
        </div>
        <Button size="sm" onClick={() => setEditing(null)}><Plus className="mr-1 h-3.5 w-3.5" /> Solicitar material</Button>
      </div>

      {isLoading ? (
        <div className="h-24 animate-pulse rounded-md bg-muted" />
      ) : isError ? (
        <p className="rounded-md border border-destructive/20 bg-destructive/5 p-6 text-center text-sm text-destructive">
          Erro ao carregar os materiais.
        </p>
      ) : !list?.length ? (
        <p className="rounded-md border border-dashed border-border p-8 text-center text-sm text-muted-foreground">
          Nenhum material {filters.status || filters.stageId || filters.late ? "com esses filtros" : "solicitado nesta obra"}.
        </p>
      ) : (
        <ul className="space-y-2">
          {list.map((m) => {
            const st = MATERIAL_STATUS[m.status] ?? { label: m.status, tone: "gray" as const };
            const editable = m.status === "Requested" || m.status === "Quoted";
            const ordered = m.status === "Ordered" || m.status === "PartiallyDelivered";
            const active = m.status !== "Delivered" && m.status !== "Cancelled";
            const open = expanded === m.id;
            return (
              <li key={m.id} className="space-y-2 rounded-md border border-border bg-card p-3" data-testid={`material-${m.id}`}>
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div className="min-w-0">
                    <p className="text-sm font-semibold">{m.materialName}</p>
                    <p className="text-xs text-muted-foreground">
                      {m.quantity} {m.unit}
                      {m.stageName ? ` · ${m.stageName}` : ""}
                      {m.neededBy ? ` · precisa até ${fmtDate(m.neededBy)}` : ""}
                      {m.supplierName ? ` · ${m.supplierName}` : ""}
                    </p>
                  </div>
                  <div className="flex items-center gap-1.5">
                    {m.isLate && (
                      <span className="inline-flex items-center gap-1 rounded-full bg-red-100 px-2 py-0.5 text-xs font-semibold text-red-700 dark:bg-red-900/30 dark:text-red-400">
                        <AlertTriangle className="h-3 w-3" /> Atrasado
                      </span>
                    )}
                    <StatusPill label={st.label} tone={st.tone} />
                  </div>
                </div>

                <div className="flex flex-wrap gap-x-6 gap-y-1 text-xs text-muted-foreground">
                  {m.estimatedTotal != null && <p>Estimado: <span className="tabular-nums text-foreground">{fmtBRL(m.estimatedTotal)}</span></p>}
                  {(ordered || m.status === "Delivered") && (
                    <p>Entregue: <span className="tabular-nums text-foreground">{m.deliveredQuantity} / {m.quantity} {m.unit}</span></p>
                  )}
                  {m.deliveredAt && <p>Concluído em {fmtDate(m.deliveredAt)}</p>}
                </div>

                <div className="flex flex-wrap items-center gap-2">
                  {editable && (
                    <>
                      <Button size="sm" variant="outline" onClick={() => setEditing(m)}><Pencil className="mr-1 h-3.5 w-3.5" /> Editar</Button>
                      <Button size="sm" variant="outline" onClick={() => setStepFor({ material: m, step: "quote" })}>
                        <Tag className="mr-1 h-3.5 w-3.5" /> Cotar
                      </Button>
                      <Button size="sm" variant="outline" onClick={() => setStepFor({ material: m, step: "order" })}>
                        <ShoppingCart className="mr-1 h-3.5 w-3.5" /> Pedir
                      </Button>
                    </>
                  )}
                  {ordered && (
                    <Button size="sm" variant="outline" onClick={() => setStepFor({ material: m, step: "deliver" })}>
                      <Truck className="mr-1 h-3.5 w-3.5" /> Registrar entrega
                    </Button>
                  )}
                  {active && (
                    <Button size="sm" variant="ghost" onClick={() => cancel(m)} disabled={actionMut.isPending}>
                      <Ban className="mr-1 h-3.5 w-3.5" /> Cancelar
                    </Button>
                  )}
                  <select aria-label={`Despesa da obra de ${m.materialName}`} value={m.movementId ?? ""}
                    onChange={(e) => linkMovement(m, e.target.value)} disabled={actionMut.isPending}
                    className={`${selectCls} max-w-[16rem]`}>
                    <option value="">Sem despesa ligada</option>
                    {m.movementId && !movements.some((mv) => mv.id === m.movementId) && (
                      <option value={m.movementId}>Despesa ligada</option>
                    )}
                    {movements.map((mv) => (
                      <option key={mv.id} value={mv.id}>{fmtDate(mv.date)} · {mv.description} · {fmtBRL(mv.amount)}</option>
                    ))}
                  </select>
                </div>

                {m.deliveries.length > 0 && (
                  <div>
                    <button onClick={() => setExpanded(open ? null : m.id)}
                      className="flex items-center gap-1 text-xs text-primary hover:underline">
                      {open ? <ChevronDown className="h-3 w-3" /> : <ChevronRight className="h-3 w-3" />}
                      Histórico de entregas ({m.deliveries.length})
                    </button>
                    {open && (
                      <ul className="mt-1 space-y-0.5 border-l border-border pl-3 text-xs">
                        {m.deliveries.map((d) => (
                          <li key={d.id}>
                            {fmtDate(d.date)} — {d.quantity} {m.unit}
                            {d.receivedBy ? ` · recebido por ${d.receivedBy}` : ""}
                            {d.note ? ` · ${d.note}` : ""}
                            <span className="text-muted-foreground"> (registrado por {d.recordedByName})</span>
                          </li>
                        ))}
                      </ul>
                    )}
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}

      <MaterialFormDialog projectId={projectId} material={editing} onClose={() => setEditing(undefined)} />
      <MaterialStepDialog projectId={projectId} material={stepFor?.material ?? null} step={stepFor?.step ?? null}
        suppliers={suppliers} onClose={() => setStepFor(null)} />
    </div>
  );
}
