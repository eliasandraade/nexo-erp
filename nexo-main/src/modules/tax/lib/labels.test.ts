import { describe, expect, it } from "vitest";
import {
  IMPACT_NATURES, OPPORTUNITY_STATUS, PROHIBITED_STRUCTURES, RISK_LEVELS, labelOf, regimeLabel, toneOf,
} from "./labels";

describe("tax labels", () => {
  it("names every impact nature in words and gives each its own tone", () => {
    expect(IMPACT_NATURES.DefinitiveSaving.label).toBe("Economia");
    expect(IMPACT_NATURES.CalculatedTaxReduction.label).toBe("Redução do IBS/CBS calculado — não é economia");
    expect(IMPACT_NATURES.CalculatedTaxReduction.tone).not.toBe(IMPACT_NATURES.DefinitiveSaving.tone);
    expect(IMPACT_NATURES.Deferral.label).toBe("Diferimento — não é economia");
    expect(IMPACT_NATURES.CashFlow.label).toBe("Fluxo de caixa");
    expect(IMPACT_NATURES.Credit.label).toBe("Crédito");
    expect(IMPACT_NATURES.RiskAvoided.label).toBe("Risco evitado");
    const tones = Object.values(IMPACT_NATURES).map((n) => n.tone);
    expect(new Set(tones).size).toBe(tones.length);
  });

  it("labels opportunity status and risk", () => {
    expect(labelOf(OPPORTUNITY_STATUS, "Blocked")).toBe("Bloqueada");
    expect(toneOf(OPPORTUNITY_STATUS, "Blocked")).toBe("danger");
    expect(labelOf(RISK_LEVELS, "High")).toBe("Risco alto");
    expect(toneOf(RISK_LEVELS, "Low")).toBe("success");
  });

  it("explains every prohibited structure", () => {
    expect(Object.keys(PROHIBITED_STRUCTURES)).toHaveLength(8);
    expect(labelOf(PROHIBITED_STRUCTURES, "FalseSupplier")).toBe("Fornecedor de fachada");
  });

  it("does not crash on values it does not know", () => {
    expect(labelOf(OPPORTUNITY_STATUS, "Archived")).toBe("Archived");
    expect(toneOf(RISK_LEVELS, "Extreme")).toBe("neutral");
    expect(labelOf(RISK_LEVELS, null)).toBe("—");
    expect(regimeLabel("LucroReal")).toBe("Lucro Real");
    expect(regimeLabel(null)).toBe("Não informado");
  });
});
