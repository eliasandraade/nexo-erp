import { useEffect, useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { useSaveBudgetAsTemplate } from "../../hooks/use-build-commercial";
import type { BuildBudgetDto } from "../../api/build.api";
import { errorMessage } from "../commercial/errors";

interface Props {
  budget: BuildBudgetDto;
  open: boolean;
  onClose: () => void;
}

/** Saves the budget's lines as a reusable model — fixed quantities or "per m²" (needs the area). */
export function SaveAsTemplateDialog({ budget, open, onClose }: Props) {
  const mut = useSaveBudgetAsTemplate();
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [perM2, setPerM2] = useState(false);
  const hasArea = budget.areaM2 != null && budget.areaM2 > 0;

  useEffect(() => {
    if (!open) return;
    setName(budget.name); setDescription(""); setPerM2(false);
  }, [open, budget.name]);

  const save = () => {
    mut.mutate({
      id: budget.id,
      req: { name: name.trim(), description: description.trim() || undefined, perSquareMeter: perM2 },
    }, {
      onSuccess: () => { toast.success("Modelo salvo. Use-o em “Novo a partir de modelo”."); onClose(); },
      onError:   (e) => toast.error(errorMessage(e, "Erro ao salvar o modelo.")),
    });
  };

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Salvar como modelo</DialogTitle>
          <DialogDescription>Os itens deste orçamento viram um modelo para novos orçamentos.</DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label htmlFor="sat-name" className="text-xs">Nome do modelo</Label>
            <Input id="sat-name" value={name} onChange={(e) => setName(e.target.value)} className="mt-0.5" />
          </div>
          <div>
            <Label htmlFor="sat-desc" className="text-xs">Descrição (opcional)</Label>
            <Input id="sat-desc" value={description} onChange={(e) => setDescription(e.target.value)} className="mt-0.5" />
          </div>
          <div className="flex items-start justify-between gap-3">
            <div>
              <Label htmlFor="sat-perm2" className="text-sm">Quantidades por m²</Label>
              <p className="text-[11px] text-muted-foreground">
                {hasArea
                  ? `Cada quantidade é dividida pela área (${budget.areaM2} m²) e, no uso, multiplicada pela área do novo orçamento.`
                  : "Informe a área do orçamento para salvar por m²."}
              </p>
            </div>
            <Switch id="sat-perm2" checked={perM2} disabled={!hasArea} onCheckedChange={setPerM2} />
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Cancelar</Button>
          <Button onClick={save} disabled={!name.trim() || mut.isPending}>Salvar modelo</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
