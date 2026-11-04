import { useState } from "react";
import { Check } from "lucide-react";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import type { BuildModelVersionDto } from "../../api/build-twin.api";
import { BUILD_3D_DISABLED_MESSAGE, FORMAT_MATRIX } from "../../lib/twin";
import { ModelUploadForm } from "./ModelUploadForm";
import { VersionProcessing } from "./VersionProcessing";

const STEPS = ["Fontes", "Envio", "Processamento", "Etapas"] as const;

const TONE: Record<(typeof FORMAT_MATRIX)[number]["tone"], string> = {
  best: "bg-emerald-100 text-emerald-800 dark:bg-emerald-900/30 dark:text-emerald-300",
  ok: "bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300",
  export: "bg-amber-100 text-amber-800 dark:bg-amber-900/30 dark:text-amber-300",
  assisted: "bg-purple-100 text-purple-800 dark:bg-purple-900/30 dark:text-purple-300",
  none: "bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300",
};

interface Props {
  projectId: string;
  build3DEnabled: boolean;
  /** "Sem projeto" → the manual zones (simplified representation). */
  onNoProject: () => void;
  /** Step 4: bind stages (opens the bindings or, for a 2D sheet, the zones). */
  onBind: (version: BuildModelVersionDto) => void;
  onCancel?: () => void;
  initialStep?: 1 | 2;
}

/** The 3D onboarding: 1 sources and formats, 2 upload, 3 processing, 4 bind the stages. */
export function TwinOnboarding({ projectId, build3DEnabled, onNoProject, onBind, onCancel, initialStep = 1 }: Props) {
  const [step, setStep] = useState<1 | 2 | 3 | 4>(initialStep);
  const [version, setVersion] = useState<BuildModelVersionDto | null>(null);

  return (
    <div className="space-y-4 rounded-md border border-border bg-card p-3 sm:p-4" data-testid="twin-onboarding">
      <ol className="flex flex-wrap gap-2 text-xs" aria-label="Passos">
        {STEPS.map((label, i) => {
          const n = i + 1;
          const done = n < step;
          return (
            <li key={label} aria-current={n === step ? "step" : undefined}
              className={cn("flex items-center gap-1.5 rounded-full border px-2.5 py-1",
                n === step ? "border-primary text-primary" : done ? "border-border text-foreground" : "border-border text-muted-foreground")}>
              <span className={cn("grid h-4 w-4 place-items-center rounded-full text-[10px]", done ? "bg-primary text-primary-foreground" : "bg-muted")}>
                {done ? <Check className="h-3 w-3" /> : n}
              </span>
              {label}
            </li>
          );
        })}
      </ol>

      {step === 1 && (
        <div className="space-y-3">
          <div>
            <h3 className="text-base font-semibold">Conecte o projeto arquitetônico para visualizar digitalmente a evolução da obra.</h3>
            <p className="mt-1 text-sm text-muted-foreground">
              O Orken lê o projeto, liga cada parte às etapas e pinta o andamento registrado. A geometria vem só do seu projeto: nada é desenhado ou completado.
            </p>
          </div>
          <div className="max-w-full overflow-x-auto">
            <table className="w-full text-sm" data-testid="twin-formats">
              <thead className="text-left text-xs text-muted-foreground"><tr><th className="p-1.5">Você tem</th><th className="p-1.5">O que acontece</th></tr></thead>
              <tbody>
                {FORMAT_MATRIX.map((row) => (
                  <tr key={row.formats} className="border-t border-border align-top">
                    <td className="p-1.5"><span className={cn("inline-block whitespace-nowrap rounded px-1.5 py-0.5 text-xs font-medium", TONE[row.tone])}>{row.formats}</span></td>
                    <td className="p-1.5 text-xs">{row.result}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {!build3DEnabled && (
            <p role="alert" className="rounded-md border border-amber-300 bg-amber-50 p-2 text-sm text-amber-900 dark:border-amber-800 dark:bg-amber-900/20 dark:text-amber-200">
              {BUILD_3D_DISABLED_MESSAGE}
            </p>
          )}
          <div className="flex flex-wrap gap-2">
            <Button type="button" onClick={() => setStep(2)} disabled={!build3DEnabled}>Conectar projeto</Button>
            <Button type="button" variant="outline" onClick={onNoProject}>Não tenho projeto</Button>
            {onCancel && <Button type="button" variant="ghost" onClick={onCancel}>Cancelar</Button>}
          </div>
        </div>
      )}

      {step === 2 && (
        <ModelUploadForm projectId={projectId} onCancel={() => (initialStep === 2 && onCancel ? onCancel() : setStep(1))}
          onUploaded={(r) => { setVersion(r.version); setStep(3); }} />
      )}

      {step === 3 && version && (
        <VersionProcessing projectId={projectId} versionId={version.id} build3DEnabled={build3DEnabled}
          onContinue={() => setStep(4)} />
      )}

      {step === 4 && version && (
        <div className="space-y-2">
          <p className="text-sm">
            Agora ligue as partes do modelo às etapas da obra (por categoria, zona ou elemento). Sem vínculo, o elemento fica neutro:
            não conta como avanço.
          </p>
          <Button type="button" onClick={() => onBind(version)}>
            {version.geometrySource === "AssistedMapping" ? "Desenhar zonas na planta" : "Vincular etapas"}
          </Button>
        </div>
      )}
    </div>
  );
}
