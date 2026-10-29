import type { ImpactNature, TaxOpportunitySummaryDto, TaxOverviewDto } from "../api/tax.api";
import { IMPACT_NATURES, type Tone } from "./labels";

export interface ImpactTile {
  nature: ImpactNature;
  label: string;
  hint: string;
  tone: Tone;
  amount: number;
}

/**
 * The potential impacts of open opportunities, one tile per nature. They are different things
 * (a lower IBS/CBS on the document is not a saving; a deferral is not a saving; a credit depends
 * on conditions) and are deliberately NOT summed: there is no "total" here on purpose.
 */
export function overviewImpacts(
  o: Pick<TaxOverviewDto, "potentialSavings" | "potentialCalculatedTaxReductions" | "potentialDeferrals" | "potentialCashFlow" | "potentialCredits">,
): ImpactTile[] {
  const pairs: [ImpactNature, number][] = [
    ["DefinitiveSaving", o.potentialSavings],
    ["CalculatedTaxReduction", o.potentialCalculatedTaxReductions],
    ["Deferral", o.potentialDeferrals],
    ["CashFlow", o.potentialCashFlow],
    ["Credit", o.potentialCredits],
  ];
  return pairs.map(([nature, amount]) => ({ nature, amount, ...IMPACT_NATURES[nature] }));
}

/**
 * Groups opportunity impacts by nature (blocked/rejected and unknown impacts are left out).
 * Each nature keeps its own subtotal — callers must never add the groups together.
 */
export function impactsByNature(items: Pick<TaxOpportunitySummaryDto, "impactNature" | "status" | "estimatedImpact">[]): Partial<Record<ImpactNature, number>> {
  const out: Partial<Record<ImpactNature, number>> = {};
  for (const i of items) {
    if (i.status === "Blocked" || i.status === "Rejected" || i.estimatedImpact === null) continue;
    out[i.impactNature] = Math.round(((out[i.impactNature] ?? 0) + i.estimatedImpact) * 100) / 100;
  }
  return out;
}

/** What the establishment should do first, in plain words, from the overview figures. */
export function overviewAttention(o: TaxOverviewDto): string[] {
  const a: string[] = [];
  if (!o.profileConfigured) a.push("Cadastre o perfil tributário: sem ele nenhum cálculo é feito.");
  if (o.openHighRisk > 0) a.push(`${o.openHighRisk} ${o.openHighRisk === 1 ? "oportunidade de risco alto espera" : "oportunidades de risco alto esperam"} decisão da diretoria.`);
  if (o.splitDivergences > 0) a.push(`${o.splitDivergences} ${o.splitDivergences === 1 ? "retenção do split não confere" : "retenções do split não conferem"} com o esperado.`);
  if (o.creditsToBeAppropriated > 0) a.push("Há créditos com requisitos confirmados que ainda não foram apropriados.");
  return a;
}
