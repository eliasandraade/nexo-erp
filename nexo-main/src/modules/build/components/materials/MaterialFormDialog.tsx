import { useEffect, useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { useStages } from "../../hooks/use-build";
import { useSaveMaterial } from "../../hooks/use-build-commercial";
import type { BuildMaterialRequestDto } from "../../api/build-commercial.api";
import { parseDecimal } from "../../lib/format";
import { errorMessage } from "../commercial/errors";

interface Props {
  projectId: string;
  /** null = new; undefined = closed. */
  material: BuildMaterialRequestDto | null | undefined;
  onClose: () => void;
}

/** Material request of the obra (editable while Solicitado / Cotado). */
export function MaterialFormDialog({ projectId, material, onClose }: Props) {
  const open = material !== undefined;
  const { data: stages = [] } = useStages(projectId);
  const mut = useSaveMaterial(projectId);
  const [name, setName] = useState("");
  const [unit, setUnit] = useState("un");
  const [qty, setQty] = useState("");
  const [stageId, setStageId] = useState("");
  const [neededBy, setNeededBy] = useState("");
  const [notes, setNotes] = useState("");

  useEffect(() => {
    if (!open) return;
    setName(material?.materialName ?? "");
    setUnit(material?.unit ?? "un");
    setQty(material ? String(material.quantity) : "");
    setStageId(material?.stageId ?? "");
    setNeededBy(material?.neededBy ?? "");
    setNotes(material?.notes ?? "");
  }, [open, material]);

  const save = () => {
    const q = parseDecimal(qty);
    if (!name.trim()) { toast.error("Informe o material."); return; }
    if (q == null || Number.isNaN(q) || q <= 0) { toast.error("Quantidade inválida."); return; }
    mut.mutate({
      id: material?.id,
      req: {
        materialName: name.trim(), unit: unit.trim() || "un", quantity: q, stageId: stageId || null,
        neededBy: neededBy || null, notes: notes.trim() || null,
      },
    }, {
      onSuccess: () => { toast.success(material ? "Material atualizado." : "Material solicitado."); onClose(); },
      onError:   (e) => toast.error(errorMessage(e, "Erro ao salvar o material.")),
    });
  };

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{material ? "Editar material" : "Solicitar material"}</DialogTitle>
          <DialogDescription>Acompanhe da solicitação à entrega na obra.</DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label htmlFor="mt-name" className="text-xs">Material</Label>
            <Input id="mt-name" value={name} onChange={(e) => setName(e.target.value)} placeholder="Ex: Cimento CP II 50kg" className="mt-0.5" />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <Label htmlFor="mt-qty" className="text-xs">Quantidade</Label>
              <Input id="mt-qty" value={qty} onChange={(e) => setQty(e.target.value)} inputMode="decimal" className="mt-0.5" />
            </div>
            <div>
              <Label htmlFor="mt-unit" className="text-xs">Unidade</Label>
              <Input id="mt-unit" value={unit} onChange={(e) => setUnit(e.target.value)} className="mt-0.5" />
            </div>
            <div>
              <Label htmlFor="mt-stage" className="text-xs">Etapa</Label>
              <select id="mt-stage" value={stageId} onChange={(e) => setStageId(e.target.value)}
                className="mt-0.5 h-9 w-full rounded-md border border-input bg-background px-2 text-sm">
                <option value="">Nenhuma</option>
                {stages.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
              </select>
            </div>
            <div>
              <Label htmlFor="mt-needed" className="text-xs">Precisa até</Label>
              <Input id="mt-needed" type="date" value={neededBy} onChange={(e) => setNeededBy(e.target.value)} className="mt-0.5" />
            </div>
          </div>
          <div>
            <Label htmlFor="mt-notes" className="text-xs">Observações</Label>
            <Textarea id="mt-notes" value={notes} onChange={(e) => setNotes(e.target.value)} rows={2} className="mt-0.5" />
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Cancelar</Button>
          <Button onClick={save} disabled={mut.isPending}>Salvar</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
