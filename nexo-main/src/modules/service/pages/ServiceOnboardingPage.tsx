import { useState } from "react";
import {
  Stethoscope,
  Dumbbell,
  Apple,
  Wrench,
  Code,
  Car,
  PawPrint,
  Scissors,
  Languages,
  ConciergeBell,
  Loader2,
  type LucideIcon,
} from "lucide-react";
import { toast } from "sonner";
import { SERVICE_PRESET_OPTIONS } from "../lib/service-family";
import { useSetServicePreset } from "../hooks/useServiceSettings";

const PRESET_ICONS: Record<string, LucideIcon> = {
  "clinica-medica": Stethoscope,
  "personal-trainer": Dumbbell,
  "nutricionista": Apple,
  "oficina-mecanica": Wrench,
  "programador-autonomo": Code,
  "autoescola": Car,
  "pet-shop": PawPrint,
  "salao-beleza": Scissors,
  "escola-idiomas": Languages,
};

/**
 * First-run onboarding for the Service area. Shown (full-screen, by ServicePresetProvider) when
 * the store has no preset yet. Choosing a ramo calls PUT /v1/service/settings/preset; on success
 * the provider re-renders configured and the real screens appear. No preset is auto-picked.
 */
export default function ServiceOnboardingPage() {
  const setPreset = useSetServicePreset();
  const [choosing, setChoosing] = useState<string | null>(null);

  const choose = (key: string) => {
    setChoosing(key);
    setPreset.mutate(key, {
      onSuccess: () => toast.success("Ramo configurado."),
      onError: () => {
        toast.error("Não foi possível salvar o ramo. Tente novamente.");
        setChoosing(null);
      },
    });
  };

  return (
    <div className="flex min-h-screen w-full flex-col items-center justify-center bg-background p-6">
      <div className="w-full max-w-3xl">
        <div className="mb-8 text-center">
          <span className="mx-auto mb-4 flex h-11 w-11 items-center justify-center rounded-xl bg-primary/10 text-primary">
            <ConciergeBell className="h-5 w-5" />
          </span>
          <h1 className="font-display text-[22px] font-bold tracking-tight text-foreground">
            Qual é o ramo do seu negócio?
          </h1>
          <p className="mx-auto mt-1.5 max-w-md text-[13px] leading-relaxed text-muted-foreground">
            Escolha para adaptar o Orken Service à sua operação — telas, termos e fluxos mudam
            conforme o ramo.
          </p>
        </div>

        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {SERVICE_PRESET_OPTIONS.map((opt) => {
            const Icon = PRESET_ICONS[opt.key] ?? ConciergeBell;
            const isChoosing = choosing === opt.key;
            return (
              <button
                key={opt.key}
                type="button"
                disabled={setPreset.isPending}
                onClick={() => choose(opt.key)}
                className="group flex items-center gap-3 rounded-lg border border-border bg-card p-4 text-left transition-colors hover:border-primary/50 hover:bg-accent/40 disabled:cursor-not-allowed disabled:opacity-60"
              >
                <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-md bg-primary/10 text-primary">
                  {isChoosing ? <Loader2 className="h-[18px] w-[18px] animate-spin" /> : <Icon className="h-[18px] w-[18px]" />}
                </span>
                <span className="text-[13.5px] font-medium leading-snug text-foreground">{opt.label}</span>
              </button>
            );
          })}
        </div>
      </div>
    </div>
  );
}
