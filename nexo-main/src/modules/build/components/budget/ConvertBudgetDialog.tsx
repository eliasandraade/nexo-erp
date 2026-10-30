import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { useConvertBudget, useProjects } from "../../hooks/use-build";
import type { BuildBudgetDto } from "../../api/build.api";
import { fmtBRL } from "../../lib/format";
import { errorMessage } from "../commercial/errors";

interface Props {
  budget: BuildBudgetDto;
  defaultProjectId?: string;
  open: boolean;
  onClose: () => void;
}

/** Approved budget → obra: links it, sets the approved value and (obra without stages) creates the stages. */
export function ConvertBudgetDialog({ budget, defaultProjectId, open, onClose }: Props) {
  const navigate = useNavigate();
  const convertMut = useConvertBudget();
  const { data: projects } = useProjects(undefined, 100);
  const [projectId, setProjectId] = useState("");

  useEffect(() => {
    if (open) setProjectId(budget.projectId ?? defaultProjectId ?? "");
  }, [open, budget.projectId, defaultProjectId]);

  const options = (projects?.items ?? []).filter((p) => p.status !== "Cancelled" && p.status !== "Completed");
  const stageNames = [...new Set(budget.items.map((i) => i.stageName).filter((n): n is string => !!n))];

  const convert = () => {
    if (!projectId) return;
    convertMut.mutate({ id: budget.id, req: { projectId } }, {
      onSuccess: (dto) => {
        toast.success("Orçamento convertido. A obra está com o valor aprovado.");
        onClose();
        navigate(`/build/projetos/${dto.projectId ?? projectId}`);
      },
      onError: (e) => toast.error(errorMessage(e, "Erro ao converter.")),
    });
  };

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Converter em obra</DialogTitle>
          <DialogDescription>Não pode ser desfeito: o orçamento fica como “Convertido”.</DialogDescription>
        </DialogHeader>
        <div className="space-y-3 text-sm">
          <div>
            <Label htmlFor="cv-project" className="text-xs">Obra</Label>
            <select id="cv-project" value={projectId} onChange={(e) => setProjectId(e.target.value)}
              className="mt-0.5 h-9 w-full rounded-md border border-input bg-background px-2 text-sm">
              <option value="">Selecione a obra…</option>
              {options.map((p) => <option key={p.id} value={p.id}>{p.name} — {p.clientName}</option>)}
            </select>
          </div>
          <ul className="list-disc space-y-1 pl-5 text-muted-foreground">
            <li>O valor aprovado da obra passa a ser {fmtBRL(budget.finalPrice)}.</li>
            <li>
              Se a obra ainda não tem etapas, {stageNames.length > 0
                ? `são criadas ${stageNames.length} etapa(s) a partir dos itens (${stageNames.join(", ")}).`
                : "nenhuma etapa é criada (os itens não têm nome de etapa)."}
              {" "}Obra que já tem etapas fica como está.
            </li>
          </ul>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Cancelar</Button>
          <Button onClick={convert} disabled={!projectId || convertMut.isPending}>Converter e abrir a obra</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
