import { toast } from "sonner";
import { Label } from "@/components/ui/label";
import { useProjectWeather, useSetWeatherSensitivity } from "../../hooks/use-build-forecast";
import { choiceToSensitivity, sensitivityToChoice, type SensitivityChoice } from "../../lib/forecast";
import { errorMessage } from "../commercial/errors";

const selectClass =
  "h-9 w-full rounded-md border border-input bg-background px-2 text-sm focus:outline-none focus:ring-2 focus:ring-ring disabled:opacity-50";

/**
 * "Sensível ao clima" of a stage: Automático (keyword rule on the name) / Sim / Não → PUT stages/{id}/weather-sensitivity
 * with null / true / false. The current setting comes from the weather route's stage list.
 */
export function StageWeatherSensitivity({ projectId, stageId }: { projectId: string; stageId: string }) {
  const { data } = useProjectWeather(projectId);
  const set = useSetWeatherSensitivity(projectId);
  const stage = Array.isArray(data?.stages) ? data.stages.find((s) => s.stageId === stageId) : undefined;
  const value = sensitivityToChoice(stage?.explicitSetting);
  const id = `stage-weather-${stageId}`;

  let hint = "Automático: o Orken decide pelo nome da etapa (concretagem, cobertura, fachada…).";
  if (stage?.basis === "keyword" && stage.keyword) hint = `Automático: sensível — o nome contém “${stage.keyword}”.`;
  else if (stage?.basis === "none" && value === "auto") hint = "Automático: não sensível pelo nome.";

  return (
    <div className="space-y-1">
      <Label htmlFor={id} className="text-xs">Sensível ao clima</Label>
      <select id={id} className={selectClass} value={value} disabled={set.isPending}
        onChange={(e) => set.mutate({ stageId, value: choiceToSensitivity(e.target.value as SensitivityChoice) }, {
          onSuccess: () => toast.success("Sensibilidade ao clima atualizada."),
          onError: (err) => toast.error(errorMessage(err, "Não foi possível atualizar.")),
        })}>
        <option value="auto">Automático</option>
        <option value="yes">Sim</option>
        <option value="no">Não</option>
      </select>
      <p className="text-[11px] text-muted-foreground">{hint}</p>
    </div>
  );
}
