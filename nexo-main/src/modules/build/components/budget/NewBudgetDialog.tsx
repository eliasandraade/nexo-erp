import { useEffect, useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { useCreateBudget } from "../../hooks/use-build";
import { useBudgetTemplates, useCreateBudgetFromTemplate } from "../../hooks/use-build-commercial";
import { parseDecimal } from "../../lib/format";
import { errorMessage } from "../commercial/errors";

interface Props {
  projectId: string;
  /** Default area (the obra's m²). */
  defaultArea?: number | null;
  /** "blank" or "template" (asks for the model and the area). */
  mode: "blank" | "template" | null;
  onClose: () => void;
  onCreated: (id: string) => void;
}

/** New budget for the obra — blank, or from a model ("Novo orçamento a partir de modelo", asks the area m²). */
export function NewBudgetDialog({ projectId, defaultArea, mode, onClose, onCreated }: Props) {
  const createMut = useCreateBudget();
  const fromTplMut = useCreateBudgetFromTemplate();
  const { data: templates } = useBudgetTemplates(mode === "template");
  const [name, setName] = useState("");
  const [margin, setMargin] = useState("");
  const [area, setArea] = useState("");
  const [validUntil, setValidUntil] = useState("");
  const [templateId, setTemplateId] = useState("");

  useEffect(() => {
    if (!mode) return;
    setName(""); setMargin(mode === "blank" ? "15" : ""); setValidUntil(""); setTemplateId("");
    setArea(defaultArea != null ? String(defaultArea) : "");
  }, [mode, defaultArea]);

  const template = templates?.find((t) => t.id === templateId);
  const needsArea = !!template?.items.some((i) => i.quantityPerM2 != null);

  const submit = () => {
    const a = parseDecimal(area);
    const m = parseDecimal(margin);
    if (a != null && (Number.isNaN(a) || a <= 0)) { toast.error("Área inválida."); return; }
    if (m != null && (Number.isNaN(m) || m < 0)) { toast.error("Margem inválida."); return; }
    const done = {
      onSuccess: (dto: { id: string }) => { toast.success("Orçamento criado!"); onCreated(dto.id); onClose(); },
      onError:   (e: unknown) => toast.error(errorMessage(e, "Erro ao criar orçamento.")),
    };
    if (mode === "template") {
      if (!templateId) return;
      if (needsArea && a == null) { toast.error("Este modelo tem quantidades por m²: informe a área."); return; }
      fromTplMut.mutate({
        templateId,
        req: {
          name: name.trim(), projectId, areaM2: a ?? undefined, marginPercent: m ?? undefined,
          validUntil: validUntil || undefined,
        },
      }, done);
    } else {
      createMut.mutate({
        name: name.trim(), projectId, marginPercent: m ?? 15, areaM2: a ?? undefined, validUntil: validUntil || undefined,
      }, done);
    }
  };

  const pending = createMut.isPending || fromTplMut.isPending;

  return (
    <Dialog open={!!mode} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{mode === "template" ? "Novo orçamento a partir de modelo" : "Novo orçamento"}</DialogTitle>
          <DialogDescription>
            {mode === "template"
              ? "Os itens do modelo são copiados; quantidades por m² são multiplicadas pela área."
              : "Comece em branco e adicione os itens."}
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          {mode === "template" && (
            <div>
              <Label htmlFor="nb-template" className="text-xs">Modelo</Label>
              <select id="nb-template" value={templateId} onChange={(e) => setTemplateId(e.target.value)}
                className="mt-0.5 h-9 w-full rounded-md border border-input bg-background px-2 text-sm">
                <option value="">Selecione…</option>
                {templates?.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
              </select>
              {templates?.length === 0 && (
                <p className="mt-1 text-[11px] text-muted-foreground">Nenhum modelo. Crie em “Modelos”.</p>
              )}
            </div>
          )}
          <div>
            <Label htmlFor="nb-name" className="text-xs">Nome do orçamento</Label>
            <Input id="nb-name" value={name} onChange={(e) => setName(e.target.value)} placeholder="Ex: Orçamento V1" className="mt-0.5" />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <Label htmlFor="nb-area" className="text-xs">Área (m²){needsArea ? " *" : ""}</Label>
              <Input id="nb-area" value={area} onChange={(e) => setArea(e.target.value)} inputMode="decimal" className="mt-0.5" />
            </div>
            <div>
              <Label htmlFor="nb-margin" className="text-xs">Margem (%)</Label>
              <Input id="nb-margin" value={margin} onChange={(e) => setMargin(e.target.value)} inputMode="decimal"
                placeholder={template ? `${template.defaultMarginPercent} (do modelo)` : ""} className="mt-0.5" />
            </div>
          </div>
          <div>
            <Label htmlFor="nb-valid" className="text-xs">Válido até (opcional)</Label>
            <Input id="nb-valid" type="date" value={validUntil} onChange={(e) => setValidUntil(e.target.value)} className="mt-0.5" />
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Cancelar</Button>
          <Button onClick={submit} disabled={!name.trim() || (mode === "template" && !templateId) || pending}>
            {pending ? "Criando…" : "Criar"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
