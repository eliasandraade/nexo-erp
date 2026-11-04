import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { HardHat } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter,
} from "@/components/ui/dialog";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { useCreateProject } from "../../hooks/use-build";
import type { BuildProjectType, BuildTrackingMode } from "../../api/build.api";
import { TrackingModeChoice } from "./TrackingModeChoice";
import { PROJECT_TYPES } from "../../lib/project-types";
import { ProjectSiteFields } from "./ProjectSiteFields";
import { EMPTY_SITE, parseSite, type SiteFieldsValue } from "../../lib/site";

interface Props {
  open: boolean;
  onClose: () => void;
}

export function NewProjectDialog({ open, onClose }: Props) {
  const navigate  = useNavigate();
  const createMut = useCreateProject();

  const [name,      setName]      = useState("");
  const [client,    setClient]    = useState("");
  const [location,  setLocation]  = useState("");
  const [typeVal,   setTypeVal]   = useState<BuildProjectType>("House");
  const [budget,    setBudget]    = useState("");
  const [startDate, setStartDate] = useState("");
  const [tracking,  setTracking]  = useState<BuildTrackingMode>("Standard");
  const [site,      setSite]      = useState<SiteFieldsValue>(EMPTY_SITE);

  const reset = () => {
    setName(""); setClient(""); setLocation(""); setTypeVal("House");
    setBudget(""); setStartDate(""); setTracking("Standard"); setSite(EMPTY_SITE);
  };

  const handleClose = () => { reset(); onClose(); };

  const parsedSite = parseSite(site);
  const siteOk = "value" in parsedSite;

  const handleSubmit = () => {
    if (!name.trim() || !client.trim()) return;
    if (!("value" in parsedSite)) { toast.error(parsedSite.error); return; }
    const { latitude, longitude, areaM2 } = parsedSite.value;
    createMut.mutate({
      name:            name.trim(),
      clientName:      client.trim(),
      location:        location.trim() || undefined,
      type:            typeVal,
      budgetEstimated: budget ? Number.parseFloat(budget) : undefined,
      startDate:       startDate || undefined,
      trackingMode:    tracking,
      latitude:        latitude ?? undefined,
      longitude:       longitude ?? undefined,
      areaM2:          areaM2 ?? undefined,
    }, {
      onSuccess: (dto) => {
        toast.success("Obra criada com sucesso!");
        handleClose();
        navigate(`/build/projetos/${dto.id}`);
      },
      onError: (err) => {
        toast.error(err instanceof Error ? err.message : "Erro ao criar obra.");
      },
    });
  };

  return (
    <Dialog open={open} onOpenChange={(v) => !v && handleClose()}>
      <DialogContent className="sm:max-w-lg max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <HardHat className="h-5 w-5 text-primary" />
            Nova obra
          </DialogTitle>
        </DialogHeader>

        <div className="space-y-3 py-1">
          <div className="space-y-1">
            <Label htmlFor="new-project-name" className="text-xs">Nome da obra *</Label>
            <Input id="new-project-name" value={name} onChange={(e) => setName(e.target.value)}
              placeholder="Ex: Residencial Parque Sul" autoFocus />
          </div>

          <div className="space-y-1">
            <Label htmlFor="new-project-client" className="text-xs">Cliente *</Label>
            <Input id="new-project-client" value={client} onChange={(e) => setClient(e.target.value)}
              placeholder="Nome do cliente" />
          </div>

          <TrackingModeChoice value={tracking} onChange={setTracking} />

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
              <Label htmlFor="new-project-budget" className="text-xs">Orçamento previsto</Label>
              <div className="relative">
                <span className="absolute left-2.5 top-1/2 -translate-y-1/2 text-xs text-muted-foreground">R$</span>
                <Input id="new-project-budget" value={budget} onChange={(e) => setBudget(e.target.value)}
                  type="number" min={0} className="pl-7 text-sm" placeholder="0,00" />
              </div>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1">
              <Label htmlFor="new-project-start" className="text-xs">Data de início</Label>
              <Input id="new-project-start" value={startDate} onChange={(e) => setStartDate(e.target.value)}
                type="date" className="text-sm" />
            </div>
            <div className="space-y-1">
              <Label htmlFor="new-project-location" className="text-xs">Localização</Label>
              <Input id="new-project-location" value={location} onChange={(e) => setLocation(e.target.value)}
                placeholder="Endereço / cidade" className="text-sm" />
            </div>
          </div>

          <ProjectSiteFields idPrefix="new-project" value={site} onChange={setSite} />
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={handleClose}>Cancelar</Button>
          <Button onClick={handleSubmit}
            disabled={!name.trim() || !client.trim() || !siteOk || createMut.isPending}>
            {createMut.isPending ? "Criando…" : "Criar obra"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
