import { HardHat } from "lucide-react";
import { toast } from "sonner";
import { Switch } from "@/components/ui/switch";
import { useAdminActionsAllowed } from "../hooks/usePlatformSecurity";
import { useTenantBuildSettings, useUpdateTenantBuildSettings } from "../hooks/usePlatformBuildSettings";
import type { PlatformBuildSettings, UpdatePlatformBuildSettings } from "../services/platformApi";
import { platformErrorMessage } from "../services/platformErrors";

type FlagKey = keyof UpdatePlatformBuildSettings;

const FLAGS: Array<{ key: FlagKey; label: string; hint: string }> = [
  { key: "build3DEnabled",      label: "3D (gêmeo digital)", hint: "Envio e processamento de modelos (IFC, GLB, plantas). Padrão: ligado, sem custo externo." },
  { key: "buildAiEnabled",      label: "IA",                 hint: "Sugestões com IA (vínculos, textos). Padrão: desligado." },
  { key: "buildVisionEnabled",  label: "Visão",              hint: "Leitura de fotos e capturas por visão computacional. Padrão: desligado." },
  { key: "buildPredictEnabled", label: "Previsões",          hint: "Previsões de entrega e de custo. Padrão: ligado." },
];

/** SuperAdmin: the company's costly Orken Build features. Changing them is an administrative act (two-step sign-in). */
export function BuildSettingsCard({ tenantId }: { tenantId: string }) {
  const { data, isLoading, isError } = useTenantBuildSettings(tenantId);
  const update = useUpdateTenantBuildSettings(tenantId);
  const allowed = useAdminActionsAllowed();
  const locked = allowed === false;

  const toggle = (key: FlagKey, on: boolean) => update.mutate({ [key]: on }, {
    onSuccess: () => toast.success("Recursos do Orken Build atualizados."),
    onError: (e) => toast.error(platformErrorMessage(e)),
  });

  return (
    <section className="bg-card border border-border rounded-lg p-4 space-y-3" data-testid="platform-build-settings">
      <div className="flex items-center gap-2">
        <HardHat className="h-4 w-4 text-primary" />
        <h2 className="text-sm font-medium text-foreground">Orken Build</h2>
        {data && !data.customized && <span className="ml-auto text-[10px] text-muted-foreground">padrões</span>}
      </div>
      {isLoading && <p className="text-xs text-muted-foreground">Carregando...</p>}
      {isError && <p role="alert" className="text-xs text-destructive">Não foi possível consultar os recursos do Orken Build.</p>}
      {data && (
        <ul className="space-y-2.5">
          {FLAGS.map((f) => {
            const id = `build-flag-${f.key}`;
            return (
              <li key={f.key} className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <label htmlFor={id} className="text-sm text-foreground">{f.label}</label>
                  <p className="text-[11px] text-muted-foreground">{f.hint}</p>
                </div>
                <Switch id={id} checked={(data as PlatformBuildSettings)[f.key]} disabled={locked || update.isPending}
                  onCheckedChange={(on) => toggle(f.key, on)} />
              </li>
            );
          })}
        </ul>
      )}
      {locked && <p className="text-[11px] text-muted-foreground">Alterar exige login com verificação em duas etapas.</p>}
      {data?.updatedAt && (
        <p className="text-[11px] text-muted-foreground">
          Alterado por {data.updatedByName ?? "—"} em {new Date(data.updatedAt).toLocaleString("pt-BR")}.
        </p>
      )}
    </section>
  );
}
