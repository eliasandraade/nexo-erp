import { useQuery } from "@tanstack/react-query";
import { FlaskConical, PowerOff, ShieldAlert } from "lucide-react";
import { cn } from "@/lib/utils";
import { TAX_KEYS, getTaxGovernance } from "../api/tax.api";
import { INTERNAL_APPROVAL_NOTICE, TONE_CLASSES } from "../lib/labels";

/**
 * Where this company stands: module off (kill switch), controlled pilot (shadow mode — nothing fiscal is
 * changed automatically) or new work not admitted. Says nothing when the module simply runs.
 */
export function TaxGovernanceBanner() {
  const status = useQuery({ queryKey: TAX_KEYS.governance, queryFn: getTaxGovernance, staleTime: 60_000 });
  const s = status.data;
  if (!s?.mode) return null;

  if (!s.enabled) {
    return (
      <Banner tone="warning" icon={PowerOff} title="Inteligência tributária desligada nesta instalação">
        Nenhum cálculo, simulação ou decisão novo é executado. O que já foi registrado continua disponível para consulta e reprodução.
      </Banner>
    );
  }
  if (s.mode === "ControlledPilot") {
    return (
      <Banner tone="pilot" icon={FlaskConical} title="Piloto controlado">
        <span className="block">{s.effectsNotice}</span>
        <span className="block">Revisão profissional pendente. {INTERNAL_APPROVAL_NOTICE}</span>
        {!s.admitted && s.refusalMessage && <span className="block font-medium">{s.refusalMessage}</span>}
      </Banner>
    );
  }
  if (!s.admitted && s.refusalMessage) {
    return <Banner tone="warning" icon={ShieldAlert} title="Execução bloqueada">{s.refusalMessage}</Banner>;
  }
  return null;
}

function Banner({ tone, icon: Icon, title, children }: Readonly<{
  tone: "warning" | "pilot"; icon: typeof FlaskConical; title: string; children: React.ReactNode;
}>) {
  return (
    <div role="status" className={cn("flex items-start gap-2 rounded-md border p-3 text-sm", TONE_CLASSES[tone])}>
      <Icon className="mt-0.5 h-4 w-4 shrink-0" />
      <div className="space-y-1">
        <p className="font-semibold">{title}</p>
        <p className="space-y-1">{children}</p>
      </div>
    </div>
  );
}
