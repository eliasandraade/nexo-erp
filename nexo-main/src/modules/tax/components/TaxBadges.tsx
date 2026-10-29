import { cn } from "@/lib/utils";
import type { ImpactNature, OpportunityStatus, RiskLevel } from "../api/tax.api";
import { IMPACT_NATURES, OPPORTUNITY_STATUS, RISK_LEVELS, TONE_CLASSES, labelOf, toneOf, type Tone } from "../lib/labels";

export function ToneBadge({ label, tone, title, className }: Readonly<{ label: string; tone: Tone; title?: string; className?: string }>) {
  return (
    <span title={title} className={cn("inline-flex max-w-full items-center rounded border px-1.5 py-0.5 text-xs font-medium", TONE_CLASSES[tone], className)}>
      <span className="truncate">{label}</span>
    </span>
  );
}

export function ImpactBadge({ nature }: Readonly<{ nature: ImpactNature }>) {
  const n = IMPACT_NATURES[nature];
  if (!n) return <ToneBadge label={nature} tone="neutral" />;
  return <ToneBadge label={n.label} tone={n.tone} title={n.hint} />;
}

export function RiskBadge({ risk }: Readonly<{ risk: RiskLevel }>) {
  return <ToneBadge label={labelOf(RISK_LEVELS, risk)} tone={toneOf(RISK_LEVELS, risk)} />;
}

export function OpportunityStatusBadge({ status }: Readonly<{ status: OpportunityStatus }>) {
  return <ToneBadge label={labelOf(OPPORTUNITY_STATUS, status)} tone={toneOf(OPPORTUNITY_STATUS, status)} />;
}
