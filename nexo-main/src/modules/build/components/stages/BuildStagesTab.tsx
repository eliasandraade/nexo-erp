import { useState } from "react";
import {
  ArrowDown, ArrowUp, Check, Pencil, Plus, SlidersHorizontal, Trash2, X,
} from "lucide-react";
import { toast } from "sonner";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  useCreateStage, useDeleteStage, useReorderStages, useStages, useUpdateStage,
} from "../../hooks/use-build";
import type { BuildStageDto } from "../../api/build.api";
import { parseDecimal } from "../../lib/format";
import { StageProgressDialog } from "./StageProgressDialog";

const fmtDay = (iso: string) => new Date(`${iso}T12:00:00`).toLocaleDateString("pt-BR", { day: "2-digit", month: "2-digit" });
const fmtWeight = (w: number | undefined) => (w ?? 1).toLocaleString("pt-BR", { maximumFractionDigits: 2 });

/** Weight text → number (> 0) or an error message; empty means "use the default/keep". */
function parseWeight(raw: string): { value: number | undefined } | { error: string } {
  const n = parseDecimal(raw);
  if (n === null) return { value: undefined };
  if (Number.isNaN(n) || n <= 0) return { error: "O peso da etapa precisa ser maior que zero." };
  return { value: n };
}

interface Details { id: string; name: string; description: string; start: string; end: string; weight: string }

export function BuildStagesTab({ projectId }: { projectId: string }) {
  const { data: stages = [], isLoading } = useStages(projectId);
  const createMut  = useCreateStage(projectId);
  const deleteMut  = useDeleteStage(projectId);
  const updateMut  = useUpdateStage(projectId);
  const reorderMut = useReorderStages(projectId);

  const [details, setDetails]       = useState<Details | null>(null);
  const [adding, setAdding]         = useState(false);
  const [newName, setNewName]       = useState("");
  const [newWeight, setNewWeight]   = useState("");
  const [progressStage, setProgressStage] = useState<BuildStageDto | null>(null);

  const saveDetails = (stage: BuildStageDto) => {
    if (!details || !details.name.trim()) return;
    if (details.start && details.end && details.end < details.start) { toast.error("O fim previsto precisa ser depois do início."); return; }
    const w = parseWeight(details.weight);
    if ("error" in w) { toast.error(w.error); return; }
    updateMut.mutate({ id: stage.id, req: {
      name: details.name.trim(), description: details.description.trim() || null, order: stage.order,
      plannedStart: details.start || null, plannedEnd: details.end || null,
      weight: w.value,
    } }, {
      onSuccess: () => { setDetails(null); toast.success("Etapa atualizada."); },
      onError:   (e) => toast.error(e instanceof Error ? e.message : "Erro ao atualizar etapa."),
    });
  };

  // swap with the neighbour and renumber everything 0..n-1 (gaps or duplicates heal themselves)
  const move = (index: number, delta: -1 | 1) => {
    const order = [...stages];
    const target = index + delta;
    if (target < 0 || target >= order.length) return;
    [order[index], order[target]] = [order[target], order[index]];
    reorderMut.mutate({ entries: order.map((st, i) => ({ stageId: st.id, order: i })) }, {
      onError: (e) => toast.error(e instanceof Error ? e.message : "Erro ao reordenar."),
    });
  };

  const resetAdd = () => { setAdding(false); setNewName(""); setNewWeight(""); };

  const handleAddStage = () => {
    if (!newName.trim()) return;
    const w = parseWeight(newWeight);
    if ("error" in w) { toast.error(w.error); return; }
    createMut.mutate({ name: newName.trim(), weight: w.value }, {
      onSuccess: () => { resetAdd(); toast.success("Etapa adicionada!"); },
      onError:   (e) => toast.error(e instanceof Error ? e.message : "Erro ao criar etapa."),
    });
  };

  const handleDelete = (id: string) => {
    if (!confirm("Remover esta etapa?")) return;
    deleteMut.mutate(id, {
      onSuccess: () => toast.success("Etapa removida."),
      onError:   (e) => toast.error(e instanceof Error ? e.message : "Erro ao remover."),
    });
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <p className="text-sm text-muted-foreground">
          {stages.length} etapa{stages.length !== 1 ? "s" : ""}
        </p>
        {!adding && (
          <Button size="sm" variant="outline" onClick={() => setAdding(true)}>
            <Plus className="h-3.5 w-3.5 mr-1" /> Adicionar etapa
          </Button>
        )}
      </div>

      {adding && (
        <div className="space-y-1 rounded-md border border-dashed border-primary/40 p-3">
          <div className="flex items-end gap-2">
            <div className="flex-1 space-y-1">
              <Label htmlFor="new-stage-name" className="text-xs">Nome da etapa</Label>
              <Input id="new-stage-name" value={newName} onChange={(e) => setNewName(e.target.value)}
                placeholder="Nome da etapa…" className="text-sm" autoFocus
                onKeyDown={(e) => e.key === "Enter" && handleAddStage()} />
            </div>
            <div className="w-20 space-y-1">
              <Label htmlFor="new-stage-weight" className="text-xs">Peso</Label>
              <Input id="new-stage-weight" inputMode="decimal" value={newWeight} placeholder="1"
                onChange={(e) => setNewWeight(e.target.value)} className="text-sm"
                onKeyDown={(e) => e.key === "Enter" && handleAddStage()} />
            </div>
            <button onClick={handleAddStage} disabled={!newName.trim() || createMut.isPending}
              aria-label="Salvar etapa" className="p-2 text-primary disabled:opacity-40">
              <Check className="h-4 w-4" />
            </button>
            <button onClick={resetAdd} aria-label="Cancelar" className="p-2 text-muted-foreground">
              <X className="h-4 w-4" />
            </button>
          </div>
          <p className="text-[11px] text-muted-foreground">
            O peso define quanto a etapa conta no progresso geral (padrão 1). Ex.: estrutura 3, pintura 1.
          </p>
        </div>
      )}

      {isLoading ? (
        <div className="space-y-2">
          {[1, 2, 3].map((i) => (
            <div key={i} className="h-16 rounded-md bg-muted animate-pulse" />
          ))}
        </div>
      ) : stages.length === 0 ? (
        <div className="rounded-md border border-dashed border-border p-8 text-center">
          <p className="text-sm text-muted-foreground">Nenhuma etapa cadastrada.</p>
          <Button variant="outline" size="sm" className="mt-3" onClick={() => setAdding(true)}>
            <Plus className="h-3.5 w-3.5 mr-1" /> Adicionar primeira etapa
          </Button>
        </div>
      ) : (
        <div className="space-y-2">
          {stages.map((stage, index) => (
            <div key={stage.id} className="rounded-md border border-border bg-card p-4 space-y-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="min-w-0 flex-1 basis-40">
                  <p className="font-medium text-sm truncate">{stage.name}</p>
                  {stage.description && (
                    <p className="text-xs text-muted-foreground truncate">{stage.description}</p>
                  )}
                  <p className="text-[11px] text-muted-foreground">
                    Peso {fmtWeight(stage.weight)}
                    {(stage.plannedStartDate || stage.plannedEndDate) && (
                      <> · Previsto: {stage.plannedStartDate ? fmtDay(stage.plannedStartDate) : "—"} a {stage.plannedEndDate ? fmtDay(stage.plannedEndDate) : "—"}</>
                    )}
                  </p>
                </div>
                <div className="flex items-center gap-1 shrink-0">
                  <span className={cn(
                    "text-xs px-2 py-0.5 rounded-full font-medium",
                    stage.status === "Completed" ? "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400" :
                    stage.status === "InProgress" ? "bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400" :
                    "bg-muted text-muted-foreground",
                  )}>
                    {stage.status === "Completed" ? "Concluída" :
                     stage.status === "InProgress" ? "Em andamento" : "Pendente"}
                  </span>
                  <button
                    onClick={() => move(index, -1)} disabled={index === 0 || reorderMut.isPending}
                    aria-label={`Subir ${stage.name}`}
                    className="text-muted-foreground hover:text-foreground transition-colors p-2 disabled:opacity-30"
                  >
                    <ArrowUp className="h-3.5 w-3.5" />
                  </button>
                  <button
                    onClick={() => move(index, 1)} disabled={index === stages.length - 1 || reorderMut.isPending}
                    aria-label={`Descer ${stage.name}`}
                    className="text-muted-foreground hover:text-foreground transition-colors p-2 disabled:opacity-30"
                  >
                    <ArrowDown className="h-3.5 w-3.5" />
                  </button>
                  <button
                    onClick={() => setProgressStage(stage)}
                    aria-label={`Atualizar progresso de ${stage.name}`}
                    className="text-muted-foreground hover:text-foreground transition-colors p-2"
                  >
                    <SlidersHorizontal className="h-3.5 w-3.5" />
                  </button>
                  <button
                    onClick={() => setDetails({ id: stage.id, name: stage.name, description: stage.description ?? "",
                      start: stage.plannedStartDate ?? "", end: stage.plannedEndDate ?? "",
                      weight: String(stage.weight ?? 1) })}
                    aria-label={`Editar ${stage.name}`}
                    className="text-muted-foreground hover:text-foreground transition-colors p-2"
                  >
                    <Pencil className="h-3.5 w-3.5" />
                  </button>
                  <button
                    onClick={() => handleDelete(stage.id)}
                    aria-label={`Remover ${stage.name}`}
                    className="text-muted-foreground hover:text-destructive transition-colors p-2"
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </button>
                </div>
              </div>

              {details?.id === stage.id && (
                <div className="grid gap-2 rounded-md border border-dashed border-primary/40 p-3 sm:grid-cols-3">
                  <div className="space-y-1 sm:col-span-2">
                    <Label htmlFor={`stage-name-${stage.id}`} className="text-xs">Nome</Label>
                    <Input id={`stage-name-${stage.id}`} value={details.name} maxLength={200}
                      onChange={(e) => setDetails({ ...details, name: e.target.value })} className="text-sm" />
                  </div>
                  <div className="space-y-1">
                    <Label htmlFor={`stage-weight-${stage.id}`} className="text-xs">Peso</Label>
                    <Input id={`stage-weight-${stage.id}`} inputMode="decimal" value={details.weight}
                      onChange={(e) => setDetails({ ...details, weight: e.target.value })} className="text-sm" />
                  </div>
                  <div className="space-y-1 sm:col-span-3">
                    <Label htmlFor={`stage-desc-${stage.id}`} className="text-xs">Descrição</Label>
                    <Input id={`stage-desc-${stage.id}`} value={details.description} maxLength={1000}
                      onChange={(e) => setDetails({ ...details, description: e.target.value })} className="text-sm" />
                  </div>
                  <div className="space-y-1">
                    <Label htmlFor={`stage-start-${stage.id}`} className="text-xs">Início previsto</Label>
                    <Input id={`stage-start-${stage.id}`} type="date" value={details.start}
                      onChange={(e) => setDetails({ ...details, start: e.target.value })} className="text-sm" />
                  </div>
                  <div className="space-y-1">
                    <Label htmlFor={`stage-end-${stage.id}`} className="text-xs">Fim previsto</Label>
                    <Input id={`stage-end-${stage.id}`} type="date" value={details.end}
                      onChange={(e) => setDetails({ ...details, end: e.target.value })} className="text-sm" />
                  </div>
                  <p className="text-[11px] text-muted-foreground sm:col-span-3">
                    O peso define quanto a etapa conta no progresso geral (maior que zero).
                  </p>
                  <div className="flex gap-2 sm:col-span-3">
                    <Button size="sm" onClick={() => saveDetails(stage)} disabled={!details.name.trim() || updateMut.isPending}>Salvar etapa</Button>
                    <Button size="sm" variant="ghost" onClick={() => setDetails(null)}>Cancelar</Button>
                  </div>
                </div>
              )}

              <div className="space-y-1">
                <div className="flex items-center justify-between text-xs text-muted-foreground">
                  <span>Progresso</span>
                  <span className="tabular-nums">{stage.progressPercent}%</span>
                </div>
                <div className="h-2 rounded-full bg-muted overflow-hidden">
                  <div
                    className={cn(
                      "h-full rounded-full transition-all",
                      stage.progressPercent === 100 ? "bg-emerald-500" :
                      stage.progressPercent > 0 ? "bg-primary" : "bg-muted-foreground/20",
                    )}
                    style={{ width: `${stage.progressPercent}%` }}
                  />
                </div>
              </div>
            </div>
          ))}
        </div>
      )}

      <StageProgressDialog projectId={projectId} stage={progressStage} onClose={() => setProgressStage(null)} />
    </div>
  );
}
