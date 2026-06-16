import { useState } from "react";
import { HardHat } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter,
} from "@/components/ui/dialog";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { toast } from "sonner";
import { useUpdateProject } from "../hooks/use-build";
import type { BuildProjectDto, BuildProjectType } from "../api/build.api";

const PROJECT_TYPES: Array<{ value: BuildProjectType; label: string }> = [
  { value: "House",      label: "Residencial" },
  { value: "Commercial", label: "Comercial" },
  { value: "Renovation", label: "Reforma" },
  { value: "Building",   label: "Edifício" },
  { value: "Other",      label: "Outro" },
];

interface Props {
  project: BuildProjectDto;
  open: boolean;
  onClose: () => void;
}

/** Edit dialog for an existing project. Only mounted for non-terminal projects. */
export function EditProjectDialog({ project, open, onClose }: Props) {
  const updateMut = useUpdateProject(project.id);

  const [name,      setName]      = useState(project.name);
  const [client,    setClient]    = useState(project.clientName);
  const [typeVal,   setTypeVal]   = useState<BuildProjectType>(project.type);
  const [location,  setLocation]  = useState(project.location ?? "");
  const [budgetEst, setBudgetEst] = useState(project.budgetEstimated != null ? String(project.budgetEstimated) : "");
  const [budgetApr, setBudgetApr] = useState(project.budgetApproved != null ? String(project.budgetApproved) : "");
  const [startDate, setStartDate] = useState(project.startDate ?? "");
  const [endDate,   setEndDate]   = useState(project.expectedEndDate ?? "");

  // Reset local state to the project whenever the dialog is (re)opened.
  const handleOpenChange = (v: boolean) => {
    if (!v) onClose();
  };

  const handleSubmit = () => {
    if (!name.trim() || !client.trim()) return;
    updateMut.mutate({
      name:            name.trim(),
      clientName:      client.trim(),
      type:            typeVal,
      location:        location.trim() || undefined,
      budgetEstimated: budgetEst ? parseFloat(budgetEst) : undefined,
      budgetApproved:  budgetApr ? parseFloat(budgetApr) : undefined,
      startDate:       startDate || undefined,
      expectedEndDate: endDate || undefined,
    }, {
      onSuccess: () => { toast.success("Obra atualizada!"); onClose(); },
      onError:   (e) => toast.error(e instanceof Error ? e.message : "Erro ao atualizar obra."),
    });
  };

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <HardHat className="h-5 w-5 text-primary" />
            Editar obra
          </DialogTitle>
        </DialogHeader>

        <div className="space-y-3 py-1">
          <div className="space-y-1">
            <Label className="text-xs">Nome da obra *</Label>
            <Input value={name} onChange={(e) => setName(e.target.value)} autoFocus />
          </div>

          <div className="space-y-1">
            <Label className="text-xs">Cliente *</Label>
            <Input value={client} onChange={(e) => setClient(e.target.value)} />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1">
              <Label className="text-xs">Tipo</Label>
              <Select value={typeVal} onValueChange={(v) => setTypeVal(v as BuildProjectType)}>
                <SelectTrigger className="text-sm"><SelectValue /></SelectTrigger>
                <SelectContent>
                  {PROJECT_TYPES.map((t) => (
                    <SelectItem key={t.value} value={t.value}>{t.label}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1">
              <Label className="text-xs">Localização</Label>
              <Input value={location} onChange={(e) => setLocation(e.target.value)}
                placeholder="Endereço / cidade" className="text-sm" />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1">
              <Label className="text-xs">Orçamento previsto</Label>
              <div className="relative">
                <span className="absolute left-2.5 top-1/2 -translate-y-1/2 text-xs text-muted-foreground">R$</span>
                <Input value={budgetEst} onChange={(e) => setBudgetEst(e.target.value)}
                  type="number" min={0} className="pl-7 text-sm" placeholder="0,00" />
              </div>
            </div>
            <div className="space-y-1">
              <Label className="text-xs">Orçamento aprovado</Label>
              <div className="relative">
                <span className="absolute left-2.5 top-1/2 -translate-y-1/2 text-xs text-muted-foreground">R$</span>
                <Input value={budgetApr} onChange={(e) => setBudgetApr(e.target.value)}
                  type="number" min={0} className="pl-7 text-sm" placeholder="0,00" />
              </div>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1">
              <Label className="text-xs">Data de início</Label>
              <Input value={startDate} onChange={(e) => setStartDate(e.target.value)}
                type="date" className="text-sm" />
            </div>
            <div className="space-y-1">
              <Label className="text-xs">Previsão de término</Label>
              <Input value={endDate} onChange={(e) => setEndDate(e.target.value)}
                type="date" className="text-sm" />
            </div>
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Cancelar</Button>
          <Button onClick={handleSubmit}
            disabled={!name.trim() || !client.trim() || updateMut.isPending}>
            {updateMut.isPending ? "Salvando…" : "Salvar alterações"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
