import { useEffect, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { AlertCircle, Loader2 } from "lucide-react";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { updateProfessional, type SvcProfessionalDto } from "../api/service.api";
import { serviceKeys } from "../hooks/useServicePreset";
import {
  type DayHours, parseWorkingHours, buildWorkingHoursJson, weekHasErrors, emptyWeek,
} from "../lib/working-hours";
import { WeeklyHoursEditor } from "./WeeklyHoursEditor";

interface ProfessionalHoursDialogProps {
  professional: SvcProfessionalDto | null;
  professionalLabel: string;
  onClose: () => void;
}

export function ProfessionalHoursDialog({
  professional, professionalLabel, onClose,
}: ProfessionalHoursDialogProps) {
  const qc = useQueryClient();
  const [days, setDays] = useState<DayHours[]>(emptyWeek);

  useEffect(() => {
    if (professional) setDays(parseWorkingHours(professional.workingHoursJson));
  }, [professional]);

  const saveMut = useMutation({
    mutationFn: (json: string | null) =>
      updateProfessional(professional!.id, {
        name:                     professional!.name,
        role:                     professional!.role,
        specialty:                professional!.specialty,
        color:                    professional!.color,
        phone:                    professional!.phone,
        email:                    professional!.email,
        defaultCommissionPercent: professional!.defaultCommissionPercent,
        workingHoursJson:         json,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: serviceKeys.professionals() });
      onClose();
    },
  });

  const hasErrors = weekHasErrors(days);

  return (
    <Dialog open={professional !== null} onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-h-[90vh] max-w-lg overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Horários — {professional?.name}</DialogTitle>
        </DialogHeader>

        <p className="text-xs text-muted-foreground">
          Defina os dias e horários em que {professional?.name} atende. Esses horários geram a
          disponibilidade do {professionalLabel.toLowerCase()} no portal público.
        </p>

        <div className="py-2">
          <WeeklyHoursEditor value={days} onChange={setDays} />
        </div>

        {saveMut.isError && (
          <div className="flex items-center gap-2 text-xs text-destructive">
            <AlertCircle className="h-3.5 w-3.5 shrink-0" />
            {(saveMut.error as Error)?.message ?? "Erro ao salvar horários."}
          </div>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={saveMut.isPending}>
            Cancelar
          </Button>
          <Button
            onClick={() => saveMut.mutate(buildWorkingHoursJson(days))}
            disabled={saveMut.isPending || hasErrors}
          >
            {saveMut.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
            Salvar horários
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
