import { Box, ClipboardList } from "lucide-react";
import { cn } from "@/lib/utils";
import type { BuildTrackingMode } from "../../api/build.api";

const OPTIONS: Array<{
  value: BuildTrackingMode;
  title: string;
  description: string;
  icon: React.ElementType;
}> = [
  {
    value: "Standard",
    title: "Acompanhamento padrão",
    description:
      "Progresso, cronograma, diário, fotos, vídeos, documentos, financeiro configurável, previsões e portal do cliente.",
    icon: ClipboardList,
  },
  {
    value: "ThreeD",
    title: "Acompanhamento 3D",
    description: "Conecte o projeto arquitetônico para visualizar digitalmente a evolução da obra.",
    icon: Box,
  },
];

interface Props {
  value: BuildTrackingMode;
  onChange: (v: BuildTrackingMode) => void;
}

/** "Como você deseja acompanhar esta obra?" — two clear cards (radio semantics). */
export function TrackingModeChoice({ value, onChange }: Props) {
  return (
    <fieldset className="space-y-2">
      <legend className="text-xs font-medium">Como você deseja acompanhar esta obra?</legend>
      <div role="radiogroup" aria-label="Modo de acompanhamento" className="grid gap-2 sm:grid-cols-2">
        {OPTIONS.map(({ value: v, title, description, icon: Icon }) => {
          const selected = value === v;
          return (
            <button
              key={v}
              type="button"
              role="radio"
              aria-checked={selected}
              onClick={() => onChange(v)}
              className={cn(
                "flex flex-col items-start gap-1.5 rounded-md border p-3 text-left transition-colors",
                "focus:outline-none focus-visible:ring-2 focus-visible:ring-ring",
                selected ? "border-primary bg-primary/5" : "border-border hover:bg-muted/40",
              )}
            >
              <span className="flex items-center gap-2 text-sm font-medium">
                <Icon className={cn("h-4 w-4", selected ? "text-primary" : "text-muted-foreground")} />
                {title}
              </span>
              <span className="text-xs leading-snug text-muted-foreground">{description}</span>
            </button>
          );
        })}
      </div>
      {value === "ThreeD" && (
        <p className="rounded-md border border-dashed border-border bg-muted/30 p-2 text-xs text-muted-foreground">
          O envio do modelo 3D será habilitado em breve. Por enquanto a obra é acompanhada pelas etapas, diário e
          portal do cliente, e o modo 3D fica registrado para quando o recurso chegar.
        </p>
      )}
    </fieldset>
  );
}
