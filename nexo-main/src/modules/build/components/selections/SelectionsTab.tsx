import { useState } from "react";
import { Ban, CheckCircle2, EyeOff, Info, Pencil, Plus, Star, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { useCancelSelection, useRemoveSelectionOption, useSelections } from "../../hooks/use-build-commercial";
import type { BuildSelectionDto, BuildSelectionOptionDto } from "../../api/build-commercial.api";
import { SELECTION_STATUS, fmtSignedBRL } from "../../lib/commercial";
import { fmtDate, fmtDateTime } from "../../lib/format";
import { StatusPill } from "../commercial/StatusPill";
import { errorMessage } from "../commercial/errors";
import { SelectionFormDialog } from "./SelectionFormDialog";
import { SelectionOptionDialog } from "./SelectionOptionDialog";
import { SelectionConfirmDialog } from "./SelectionConfirmDialog";

interface Props {
  projectId: string;
  onOpenChangeOrders: () => void;
}

/** "Escolhas do cliente": options the client picks on the portal (or that the company records), confirmed here. */
export function SelectionsTab({ projectId, onOpenChangeOrders }: Props) {
  const { data: list, isLoading, isError } = useSelections(projectId);
  const cancelMut = useCancelSelection(projectId);
  const removeOptMut = useRemoveSelectionOption(projectId);
  const [editing, setEditing] = useState<BuildSelectionDto | null | undefined>(undefined);
  const [optionFor, setOptionFor] = useState<{ selectionId: string; option: BuildSelectionOptionDto | null } | null>(null);
  const [confirming, setConfirming] = useState<BuildSelectionDto | null>(null);

  const cancel = (s: BuildSelectionDto) => {
    if (!confirm(`Cancelar a escolha “${s.title}”? Ela sai do portal do cliente.`)) return;
    cancelMut.mutate(s.id, {
      onSuccess: () => toast.success("Escolha cancelada."),
      onError:   (e) => toast.error(errorMessage(e, "Não foi possível cancelar.")),
    });
  };

  const removeOption = (o: BuildSelectionOptionDto) => {
    if (!confirm(`Remover a opção “${o.name}”?`)) return;
    removeOptMut.mutate(o.id, {
      onSuccess: () => toast.success("Opção removida."),
      onError:   (e) => toast.error(errorMessage(e, "Não foi possível remover.")),
    });
  };

  return (
    <div className="space-y-4">
      <div className="flex items-start gap-2 rounded-md border border-border bg-muted/30 p-3 text-xs text-muted-foreground">
        <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />
        <p>
          O cliente escolhe no portal do cliente (só em portal protegido por senha). Você confirma a escolha aqui;
          se a opção muda o preço ou o prazo, a confirmação gera um aditivo em rascunho.
        </p>
      </div>

      <div className="flex justify-end">
        <Button size="sm" onClick={() => setEditing(null)}><Plus className="mr-1 h-3.5 w-3.5" /> Nova escolha</Button>
      </div>

      {isLoading ? (
        <div className="h-24 animate-pulse rounded-md bg-muted" />
      ) : isError ? (
        <p className="rounded-md border border-destructive/20 bg-destructive/5 p-6 text-center text-sm text-destructive">
          Erro ao carregar as escolhas.
        </p>
      ) : !list?.length ? (
        <p className="rounded-md border border-dashed border-border p-8 text-center text-sm text-muted-foreground">
          Nenhuma escolha do cliente nesta obra.
        </p>
      ) : (
        <ul className="space-y-3">
          {list.map((s) => {
            const st = SELECTION_STATUS[s.status] ?? { label: s.status, tone: "gray" as const };
            const isOpen = s.status === "Open";
            const chosen = s.options.find((o) => o.id === s.chosenOptionId);
            return (
              <li key={s.id} className="space-y-3 rounded-md border border-border bg-card p-3">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div className="min-w-0">
                    <p className="flex items-center gap-1.5 text-sm font-semibold">
                      {s.title}
                      {!s.clientVisible && <EyeOff className="h-3.5 w-3.5 text-muted-foreground" aria-label="Oculta no portal" />}
                    </p>
                    <p className="text-xs text-muted-foreground">
                      {s.stageName ? `${s.stageName} · ` : ""}
                      Prazo: {s.deadline ? fmtDate(s.deadline) : "sem prazo"}
                      {s.deadlinePassed && isOpen && <span className="ml-1 font-semibold text-amber-600">(vencido)</span>}
                    </p>
                  </div>
                  <StatusPill label={st.label} tone={st.tone} />
                </div>
                {s.description && <p className="whitespace-pre-wrap text-xs text-muted-foreground">{s.description}</p>}

                <ul className="grid gap-2 sm:grid-cols-2">
                  {s.options.map((o) => (
                    <li key={o.id}
                      className={`flex gap-2 rounded-md border p-2 ${o.id === s.chosenOptionId ? "border-primary bg-primary/5" : "border-border"}`}>
                      {o.imageUrl && <img src={o.imageUrl} alt={o.name} className="h-12 w-12 shrink-0 rounded object-cover" />}
                      <div className="min-w-0 flex-1 text-xs">
                        <p className="flex items-center gap-1 text-sm font-medium">
                          {o.name}
                          {o.isStandard && <Star className="h-3 w-3 fill-current text-amber-500" aria-label="Padrão" />}
                          {o.id === s.chosenOptionId && <CheckCircle2 className="h-3.5 w-3.5 text-primary" aria-label="Escolhida" />}
                        </p>
                        <p className="text-muted-foreground">
                          {o.isStandard ? "Padrão" : fmtSignedBRL(o.priceDelta)}
                          {o.leadTimeDays ? ` · +${o.leadTimeDays} dias` : ""}
                        </p>
                        {o.description && <p className="truncate text-muted-foreground">{o.description}</p>}
                      </div>
                      {isOpen && (
                        <div className="flex shrink-0 flex-col gap-1">
                          <button aria-label={`Editar opção ${o.name}`} onClick={() => setOptionFor({ selectionId: s.id, option: o })}
                            className="p-0.5 text-muted-foreground hover:text-foreground"><Pencil className="h-3 w-3" /></button>
                          <button aria-label={`Remover opção ${o.name}`} onClick={() => removeOption(o)}
                            className="p-0.5 text-muted-foreground hover:text-destructive"><Trash2 className="h-3 w-3" /></button>
                        </div>
                      )}
                    </li>
                  ))}
                </ul>

                {chosen && (
                  <div className="rounded-md bg-muted/40 p-2 text-xs">
                    <p>
                      <span className="font-medium">Escolha do cliente:</span> {chosen.name}
                      {s.chosenVia ? ` (${s.chosenVia === "Portal" ? "pelo portal" : "registrada pela empresa"})` : ""}
                      {s.decidedAt ? ` em ${fmtDateTime(s.decidedAt)}` : ""}
                    </p>
                    {s.clientComment && <p className="mt-1"><span className="font-medium">Comentário:</span> {s.clientComment}</p>}
                    {s.confirmedAt && (
                      <p className="mt-1 text-muted-foreground">
                        Confirmada{s.confirmedByName ? ` por ${s.confirmedByName}` : ""} em {fmtDateTime(s.confirmedAt)}
                      </p>
                    )}
                  </div>
                )}
                {s.changeOrderId && (
                  <button onClick={onOpenChangeOrders} className="text-xs text-primary hover:underline">
                    Esta escolha gerou um aditivo — ver na aba Aditivos
                  </button>
                )}

                <div className="flex flex-wrap gap-2">
                  {isOpen && (
                    <>
                      <Button size="sm" variant="outline" onClick={() => setOptionFor({ selectionId: s.id, option: null })}>
                        <Plus className="mr-1 h-3.5 w-3.5" /> Opção
                      </Button>
                      <Button size="sm" variant="outline" onClick={() => setEditing(s)}>
                        <Pencil className="mr-1 h-3.5 w-3.5" /> Editar
                      </Button>
                    </>
                  )}
                  {(isOpen || s.status === "Submitted") && s.options.length > 0 && (
                    <Button size="sm" onClick={() => setConfirming(s)}>
                      <CheckCircle2 className="mr-1 h-3.5 w-3.5" /> Confirmar
                    </Button>
                  )}
                  {(isOpen || s.status === "Submitted") && (
                    <Button size="sm" variant="ghost" onClick={() => cancel(s)} disabled={cancelMut.isPending}>
                      <Ban className="mr-1 h-3.5 w-3.5" /> Cancelar
                    </Button>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      )}

      <SelectionFormDialog projectId={projectId} selection={editing} onClose={() => setEditing(undefined)} />
      <SelectionOptionDialog projectId={projectId} selectionId={optionFor?.selectionId ?? null}
        option={optionFor ? optionFor.option : undefined} onClose={() => setOptionFor(null)} />
      <SelectionConfirmDialog projectId={projectId} selection={confirming}
        onClose={() => setConfirming(null)} onOpenChangeOrders={onOpenChangeOrders} />
    </div>
  );
}
