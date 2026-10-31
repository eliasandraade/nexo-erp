import { useEffect, useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { useMaterialAction } from "../../hooks/use-build-commercial";
import type { BuildMaterialRequestDto } from "../../api/build-commercial.api";
import { localToday, parseDecimal } from "../../lib/format";
import { errorMessage } from "../commercial/errors";

export type MaterialStep = "quote" | "order" | "deliver";

interface Props {
  projectId: string;
  material: BuildMaterialRequestDto | null;
  step: MaterialStep | null;
  suppliers: Array<{ id: string; name: string }>;
  onClose: () => void;
}

const TITLES: Record<MaterialStep, string> = {
  quote:   "Registrar cotação",
  order:   "Registrar pedido",
  deliver: "Registrar entrega",
};

/** Quote (estimated cost), order (supplier, unit cost) and delivery (partial or total) of a material. */
export function MaterialStepDialog({ projectId, material, step, suppliers, onClose }: Props) {
  const mut = useMaterialAction(projectId);
  const [supplierId, setSupplierId] = useState("");
  const [cost, setCost] = useState("");
  const [qty, setQty] = useState("");
  const [date, setDate] = useState(localToday);
  const [note, setNote] = useState("");
  const [receivedBy, setReceivedBy] = useState("");

  useEffect(() => {
    if (!material || !step) return;
    setSupplierId(material.supplierId ?? "");
    setCost(material.estimatedUnitCost != null ? String(material.estimatedUnitCost) : "");
    setQty(String(material.remainingQuantity));
    setDate(localToday());
    setNote(""); setReceivedBy("");
  }, [material, step]);

  if (!material || !step) return null;

  const q = parseDecimal(qty);
  const deliverProblem = step === "deliver"
    ? (q == null || Number.isNaN(q) || q <= 0
      ? "Informe a quantidade recebida."
      : q > material.remainingQuantity
        ? `Passa do que falta entregar (${material.remainingQuantity} ${material.unit}).`
        : null)
    : null;
  const partial = step === "deliver" && !deliverProblem && q != null && q < material.remainingQuantity;

  const submit = () => {
    const c = parseDecimal(cost);
    if (step !== "deliver" && c != null && (Number.isNaN(c) || c < 0)) { toast.error("Custo inválido."); return; }
    if (deliverProblem) { toast.error(deliverProblem); return; }
    const action = step === "quote"
      ? { kind: "quote" as const, supplierId: supplierId || null, estimatedUnitCost: c }
      : step === "order"
        ? { kind: "order" as const, supplierId: supplierId || null, unitCost: c }
        : {
            kind: "deliver" as const, quantity: q as number, date: date || undefined,
            note: note.trim() || undefined, receivedBy: receivedBy.trim() || undefined,
          };
    mut.mutate({ id: material.id, action }, {
      onSuccess: (dto) => {
        toast.success(step === "deliver"
          ? dto.status === "Delivered" ? "Entrega concluída." : `Entrega parcial registrada. Faltam ${dto.remainingQuantity} ${dto.unit}.`
          : step === "order" ? "Pedido registrado." : "Cotação registrada.");
        onClose();
      },
      onError: (e) => toast.error(errorMessage(e, "Não foi possível registrar.")),
    });
  };

  return (
    <Dialog open onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{TITLES[step]}</DialogTitle>
          <DialogDescription>{material.materialName} — {material.quantity} {material.unit}</DialogDescription>
        </DialogHeader>
        {step === "deliver" ? (
          <div className="space-y-3">
            <p className="text-xs text-muted-foreground">
              Entregue até agora: {material.deliveredQuantity} {material.unit} · falta {material.remainingQuantity} {material.unit}
            </p>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <Label htmlFor="md-qty" className="text-xs">Quantidade recebida</Label>
                <Input id="md-qty" value={qty} onChange={(e) => setQty(e.target.value)} inputMode="decimal" className="mt-0.5" />
              </div>
              <div>
                <Label htmlFor="md-date" className="text-xs">Data</Label>
                <Input id="md-date" type="date" value={date} onChange={(e) => setDate(e.target.value)} className="mt-0.5" />
              </div>
            </div>
            <div>
              <Label htmlFor="md-by" className="text-xs">Recebido por</Label>
              <Input id="md-by" value={receivedBy} onChange={(e) => setReceivedBy(e.target.value)} className="mt-0.5" />
            </div>
            <div>
              <Label htmlFor="md-note" className="text-xs">Observação</Label>
              <Input id="md-note" value={note} onChange={(e) => setNote(e.target.value)} className="mt-0.5" />
            </div>
            {deliverProblem ? (
              <p role="alert" className="text-xs text-destructive">{deliverProblem}</p>
            ) : (
              <p className="text-xs text-muted-foreground">{partial ? "Entrega parcial: o pedido continua aberto." : "Entrega total: o material fica como entregue."}</p>
            )}
          </div>
        ) : (
          <div className="space-y-3">
            <div>
              <Label htmlFor="ms-supplier" className="text-xs">Fornecedor{step === "quote" ? " (opcional)" : ""}</Label>
              <select id="ms-supplier" value={supplierId} onChange={(e) => setSupplierId(e.target.value)}
                className="mt-0.5 h-9 w-full rounded-md border border-input bg-background px-2 text-sm">
                <option value="">Sem fornecedor</option>
                {suppliers.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
              </select>
            </div>
            <div>
              <Label htmlFor="ms-cost" className="text-xs">
                {step === "quote" ? "Custo unitário estimado (R$)" : "Custo unitário do pedido (R$)"}
              </Label>
              <Input id="ms-cost" value={cost} onChange={(e) => setCost(e.target.value)} inputMode="decimal" className="mt-0.5" />
            </div>
          </div>
        )}
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Cancelar</Button>
          <Button onClick={submit} disabled={mut.isPending || !!deliverProblem}>Registrar</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
