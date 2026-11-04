import { useEffect, useState } from "react";
import { Info, Pencil, Plus, ShieldCheck, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { useDefaultWarranty, useDeleteWarranty, useSaveWarranty, useWarranty } from "../../hooks/use-build-quality";
import type { BuildWarrantyTermDto } from "../../api/build-quality.api";
import { WARRANTY_DISCLAIMER, warrantyState } from "../../lib/quality";
import { fmtDate } from "../../lib/format";
import { StatusPill } from "../commercial/StatusPill";
import { errorMessage } from "../commercial/errors";

/** Warranty terms of the obra (per system), with the reference set as a starting point. */
export function WarrantySection({ projectId }: { projectId: string }) {
  const { data, isLoading, isError } = useWarranty(projectId);
  const list = Array.isArray(data) ? data : [];
  const defaults = useDefaultWarranty(projectId);
  const del = useDeleteWarranty(projectId);
  const [editing, setEditing] = useState<BuildWarrantyTermDto | null | undefined>(undefined);

  const createDefaults = () => defaults.mutate(undefined, {
    onSuccess: () => toast.success("Garantias de referência criadas. Ajuste ao contrato."),
    onError:   (e) => toast.error(errorMessage(e, "Não foi possível criar as garantias padrão.")),
  });

  const remove = (t: BuildWarrantyTermDto) => {
    if (!confirm(`Excluir a garantia de “${t.system}”?`)) return;
    del.mutate(t.id, {
      onSuccess: () => toast.success("Garantia excluída."),
      onError:   (e) => toast.error(errorMessage(e, "Não foi possível excluir.")),
    });
  };

  return (
    <div className="space-y-3">
      <div className="flex items-start gap-2 rounded-md border border-border bg-muted/30 p-3 text-xs text-muted-foreground">
        <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />
        <p data-testid="warranty-disclaimer">
          {WARRANTY_DISCLAIMER} O conjunto padrão é uma referência (inspirado na ABNT NBR 15575 e no Código Civil, art. 618).
          As garantias sem data de início começam a contar na entrega do imóvel.
        </p>
      </div>

      <div className="flex flex-wrap justify-end gap-2">
        {list.length === 0 && !isLoading && (
          <Button size="sm" variant="outline" onClick={createDefaults} disabled={defaults.isPending}>
            <ShieldCheck className="mr-1 h-3.5 w-3.5" /> Criar garantias padrão
          </Button>
        )}
        <Button size="sm" onClick={() => setEditing(null)}><Plus className="mr-1 h-3.5 w-3.5" /> Nova garantia</Button>
      </div>

      {isLoading ? (
        <div className="h-24 animate-pulse rounded-md bg-muted" />
      ) : isError ? (
        <p className="rounded-md border border-destructive/20 bg-destructive/5 p-6 text-center text-sm text-destructive">
          Erro ao carregar as garantias.
        </p>
      ) : list.length === 0 ? (
        <p className="rounded-md border border-dashed border-border p-8 text-center text-sm text-muted-foreground">
          Nenhuma garantia cadastrada. Na entrega, sem nenhuma, o conjunto de referência é criado.
        </p>
      ) : (
        <ul className="grid gap-2 sm:grid-cols-2">
          {list.map((t) => {
            const s = warrantyState(t);
            return (
              <li key={t.id} className="flex items-start justify-between gap-2 rounded-md border border-border bg-card p-3">
                <div className="min-w-0 space-y-0.5">
                  <p className="flex flex-wrap items-center gap-1.5 text-sm font-semibold">
                    {t.system}
                    <StatusPill label={s.label} tone={s.tone} />
                    {t.isDefault && <StatusPill label="Referência" tone="gray" />}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {t.months} meses{t.startsAt ? ` · de ${fmtDate(t.startsAt)} até ${fmtDate(t.expiresAt)}` : ""}
                  </p>
                  {t.description && <p className="text-xs text-muted-foreground">{t.description}</p>}
                </div>
                <div className="flex shrink-0 gap-1">
                  <button aria-label={`Editar garantia ${t.system}`} onClick={() => setEditing(t)}
                    className="p-1 text-muted-foreground hover:text-foreground"><Pencil className="h-3.5 w-3.5" /></button>
                  <button aria-label={`Excluir garantia ${t.system}`} onClick={() => remove(t)}
                    className="p-1 text-muted-foreground hover:text-destructive"><Trash2 className="h-3.5 w-3.5" /></button>
                </div>
              </li>
            );
          })}
        </ul>
      )}

      <WarrantyFormDialog projectId={projectId} term={editing} onClose={() => setEditing(undefined)} />
    </div>
  );
}

function WarrantyFormDialog({ projectId, term, onClose }: { projectId: string; term: BuildWarrantyTermDto | null | undefined; onClose: () => void }) {
  const open = term !== undefined;
  const mut = useSaveWarranty(projectId);
  const [system, setSystem] = useState("");
  const [months, setMonths] = useState("");
  const [startsAt, setStartsAt] = useState("");
  const [description, setDescription] = useState("");

  useEffect(() => {
    if (!open) return;
    setSystem(term?.system ?? "");
    setMonths(term ? String(term.months) : "");
    setStartsAt(term?.startsAt ?? "");
    setDescription(term?.description ?? "");
  }, [open, term]);

  const save = () => {
    const m = Number(months);
    if (!system.trim()) { toast.error("Informe o sistema coberto."); return; }
    if (!Number.isInteger(m) || m < 1 || m > 600) { toast.error("O prazo deve ser um número inteiro de meses (1 a 600)."); return; }
    mut.mutate({ id: term?.id, req: { system: system.trim(), months: m, startsAt: startsAt || null, description: description.trim() || null } }, {
      onSuccess: () => { toast.success("Garantia salva."); onClose(); },
      onError:   (e) => toast.error(errorMessage(e, "Erro ao salvar a garantia.")),
    });
  };

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{term ? "Editar garantia" : "Nova garantia"}</DialogTitle>
          <DialogDescription>Sem data de início, a garantia começa a contar na entrega do imóvel.</DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label htmlFor="wt-system" className="text-xs">Sistema</Label>
            <Input id="wt-system" value={system} onChange={(e) => setSystem(e.target.value)} placeholder="Ex: Impermeabilização" className="mt-0.5" />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <Label htmlFor="wt-months" className="text-xs">Prazo (meses)</Label>
              <Input id="wt-months" value={months} onChange={(e) => setMonths(e.target.value)} inputMode="numeric" className="mt-0.5" />
            </div>
            <div>
              <Label htmlFor="wt-start" className="text-xs">Início</Label>
              <Input id="wt-start" type="date" value={startsAt} onChange={(e) => setStartsAt(e.target.value)} className="mt-0.5" />
            </div>
          </div>
          <div>
            <Label htmlFor="wt-desc" className="text-xs">Descrição</Label>
            <Textarea id="wt-desc" value={description} onChange={(e) => setDescription(e.target.value)} rows={2} className="mt-0.5" />
          </div>
          <p className="text-[11px] text-muted-foreground">{WARRANTY_DISCLAIMER}</p>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Cancelar</Button>
          <Button onClick={save} disabled={mut.isPending}>Salvar garantia</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
