import { describe, expect, it } from "vitest";
import type { TaxOverviewDto } from "../api/tax.api";
import { impactsByNature, overviewAttention, overviewImpacts } from "./overview";

const overview: TaxOverviewDto = {
  profileConfigured: true, periodFrom: "2026-09-26", periodTo: "2026-10-26", calculations: 3, taxedBase: 1000, estimatedTax: 10,
  effectiveRatePercent: 1, creditsAvailable: 5, creditsBlocked: 0, creditsNotAppropriated: 0, openOpportunities: 2,
  potentialSavings: 100, potentialCalculatedTaxReductions: 0.5, potentialDeferrals: 250, potentialCashFlow: 30, potentialCredits: 12.5, openHighRisk: 0,
  blockedOpportunities: 0, splitDivergences: 0, activeRulesToday: 4, pendingRulesToday: 1,
};

describe("overview impacts", () => {
  it("keeps each nature in its own tile, never a total", () => {
    const tiles = overviewImpacts(overview);
    expect(tiles.map((t) => [t.nature, t.amount])).toEqual([
      ["DefinitiveSaving", 100], ["CalculatedTaxReduction", 0.5], ["Deferral", 250], ["CashFlow", 30], ["Credit", 12.5],
    ]);
    expect(tiles.find((t) => t.nature === "CalculatedTaxReduction")?.label).toBe("Redução do IBS/CBS calculado — não é economia");
    expect(tiles.find((t) => t.nature === "DefinitiveSaving")?.amount).toBe(100);
    expect(tiles.find((t) => t.nature === "Deferral")?.label).toBe("Diferimento — não é economia");
    expect(tiles.some((t) => t.amount === 393 || t.amount === 100.5)).toBe(false);
  });

  it("groups opportunity impacts per nature and leaves out blocked, rejected and unknown ones", () => {
    expect(impactsByNature([
      { impactNature: "DefinitiveSaving", status: "Identified", estimatedImpact: 10.1 },
      { impactNature: "DefinitiveSaving", status: "InReview", estimatedImpact: 0.2 },
      { impactNature: "Deferral", status: "Approved", estimatedImpact: 50 },
      { impactNature: "DefinitiveSaving", status: "Blocked", estimatedImpact: 999 },
      { impactNature: "Credit", status: "Rejected", estimatedImpact: 7 },
      { impactNature: "CashFlow", status: "Identified", estimatedImpact: null },
    ])).toEqual({ DefinitiveSaving: 10.3, Deferral: 50 });
  });

  it("says what needs attention", () => {
    expect(overviewAttention(overview)).toEqual([]);
    expect(overviewAttention({ ...overview, profileConfigured: false, openHighRisk: 2, splitDivergences: 1 })).toEqual([
      "Cadastre o perfil tributário: sem ele nenhum cálculo é feito.",
      "2 oportunidades de risco alto esperam decisão da diretoria.",
      "1 retenção do split não confere com o esperado.",
    ]);
  });
});
