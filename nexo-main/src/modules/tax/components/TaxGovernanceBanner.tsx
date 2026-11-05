import { useQuery } from "@tanstack/react-query";
import { FlaskConical, PauseCircle, PowerOff, ShieldAlert } from "lucide-react";
import { cn } from "@/lib/utils";
import { TAX_KEYS, getTaxGovernance } from "../api/tax.api";
import { INTERNAL_APPROVAL_NOTICE, REFUSALS, TONE_CLASSES, refusalTitle } from "../lib/labels";

/**
 * Where this company stands: module off (kill switch), suspended by the platform, controlled pilot (shadow mode —
 * nothing fiscal is changed automatically) or new work not admitted. Says nothing when the module simply runs.
 */
export function TaxGovernanceBanner() {
  const status = useQuery({ queryKey: TAX_KEYS.governance, queryFn: getTaxGovernance, staleTime: 60_000 });
  const s = status.data;
  if (!s?.mode) return null;
  const action = s.refusalCode ? REFUSALS[s.refusalCode]?.action : undefined;

  if (!s.enabled) {
    return (
      <Banner tone="warning" icon={PowerOff} title="Inteligência tributária desligada nesta instalação">
        Nenhum cálculo, simulação ou decisão novo é executado. O que já foi registrado continua disponível para consulta e reprodução.
      </Banner>
    );
  }
  if (s.moduleStatus === "Suspended") {
    return (
      <Banner tone="warning" icon={PauseCircle} title="Módulo suspenso pela plataforma">
        <span className="block">Nenhum cálculo, simulação ou decisão novo é executado até a plataforma reativar. O histórico continua disponível.</span>
        {s.refusalMessage && <span className="block">{s.refusalMessage}</span>}
      </Banner>
    );
  }
  if (s.mode === "ControlledPilot") {
    return (
      <Banner tone="pilot" icon={FlaskConical} title="Piloto controlado">
        <span className="block">{s.effectsNotice}</span>
        <span className="block">As regras usam aprovação interna do piloto, não revisão profissional. {INTERNAL_APPROVAL_NOTICE}</span>
        {!s.admitted && <span className="block font-medium">{refusalTitle(s.refusalCode)}{s.refusalMessage ? `: ${s.refusalMessage}` : ""}</span>}
        {!s.admitted && action && <span className="block text-xs">{action}</span>}
      </Banner>
    );
  }
  if (!s.admitted) {
    return (
      <Banner tone="warning" icon={ShieldAlert} title={refusalTitle(s.refusalCode)}>
        {s.refusalMessage && <span className="block">{s.refusalMessage}</span>}
        {action && <span className="block text-xs">{action}</span>}
      </Banner>
    );
  }
  return null;
}

function Banner({ tone, icon: Icon, title, children }: Readonly<{
  tone: "warning" | "pilot"; icon: typeof FlaskConical; title: string; children: React.ReactNode;
}>) {
  return (
    <section aria-label={title} className={cn("flex items-start gap-2 rounded-md border p-3 text-sm", TONE_CLASSES[tone])}>
      <Icon className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
      <div className="space-y-1">
        <p className="font-semibold">{title}</p>
        <div className="space-y-1">{children}</div>
      </div>
    </section>
  );
}
