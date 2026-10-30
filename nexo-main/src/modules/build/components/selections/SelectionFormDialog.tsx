import { useEffect, useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { useStages } from "../../hooks/use-build";
import { useSaveSelection } from "../../hooks/use-build-commercial";
import type { BuildSelectionDto } from "../../api/build-commercial.api";
import { errorMessage } from "../commercial/errors";

interface Props {
  projectId: string;
  /** null = new; undefined = closed. */
  selection: BuildSelectionDto | null | undefined;
  onClose: () => void;
}

/** Create / edit a client selection (only while it is open). */
export function SelectionFormDialog({ projectId, selection, onClose }: Props) {
  const open = selection !== undefined;
  const { data: stages = [] } = useStages(projectId);
  const mut = useSaveSelection(projectId);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [stageId, setStageId] = useState("");
  const [deadline, setDeadline] = useState("");
  const [visible, setVisible] = useState(true);

  useEffect(() => {
    if (!open) return;
    setTitle(selection?.title ?? "");
    setDescription(selection?.description ?? "");
    setStageId(selection?.stageId ?? "");
    setDeadline(selection?.deadline ?? "");
    setVisible(selection?.clientVisible ?? true);
  }, [open, selection]);

  const save = () => {
    if (!title.trim()) { toast.error("Informe o título."); return; }
    mut.mutate({
      id: selection?.id,
      req: {
        title: title.trim(), description: description.trim() || null, stageId: stageId || null,
        deadline: deadline || null, clientVisible: visible,
      },
    }, {
      onSuccess: () => { toast.success(selection ? "Escolha atualizada." : "Escolha criada. Adicione as opções."); onClose(); },
      onError:   (e) => toast.error(errorMessage(e, "Erro ao salvar a escolha.")),
    });
  };

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{selection ? "Editar escolha" : "Nova escolha do cliente"}</DialogTitle>
          <DialogDescription>Ex.: piso da sala, metais do banheiro. O cliente escolhe uma das opções até o prazo.</DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label htmlFor="sel-title" className="text-xs">Título</Label>
            <Input id="sel-title" value={title} onChange={(e) => setTitle(e.target.value)} className="mt-0.5" />
          </div>
          <div>
            <Label htmlFor="sel-desc" className="text-xs">Descrição</Label>
            <Textarea id="sel-desc" value={description} onChange={(e) => setDescription(e.target.value)} rows={2} className="mt-0.5" />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <Label htmlFor="sel-stage" className="text-xs">Etapa</Label>
              <select id="sel-stage" value={stageId} onChange={(e) => setStageId(e.target.value)}
                className="mt-0.5 h-9 w-full rounded-md border border-input bg-background px-2 text-sm">
                <option value="">Nenhuma</option>
                {stages.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
              </select>
            </div>
            <div>
              <Label htmlFor="sel-deadline" className="text-xs">Prazo para escolher</Label>
              <Input id="sel-deadline" type="date" value={deadline} onChange={(e) => setDeadline(e.target.value)} className="mt-0.5" />
            </div>
          </div>
          <p className="text-[11px] text-muted-foreground">Sem etapa, um prazo adicional da opção vai só na descrição do aditivo.</p>
          <div className="flex items-center justify-between gap-3">
            <Label htmlFor="sel-visible" className="text-sm">Visível no portal do cliente</Label>
            <Switch id="sel-visible" checked={visible} onCheckedChange={setVisible} />
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
