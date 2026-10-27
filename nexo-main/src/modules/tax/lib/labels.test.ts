import { describe, expect, it } from "vitest";
import {
  COMPLIANCE_STATUS, COMPLIANCE_VERIFIED_COPY, IMPACT_NATURES, OPPORTUNITY_STATUS, POTENTIAL_CREDIT_COPY, PROFESSIONAL_REVIEW, PROHIBITED_STRUCTURES, RISK_LEVELS,
  SCENARIO_APPROVED_COPY, SPLIT_EXPECTATION_COPY, SYSTEM_CALCULATION_COPY, labelOf, regimeLabel, toneOf,
} from "./labels";

describe("tax labels", () => {
  it("names every impact nature in words and gives each its own tone", () => {
    expect(IMPACT_NATURES.DefinitiveSaving.label).toBe("Economia");
    expect(IMPACT_NATURES.CalculatedTaxReduction.label).toBe("Redução simulada do IBS/CBS calculado");
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

describe("review copy", () => {
  it("states what each record is, and is not", () => {
    expect(POTENTIAL_CREDIT_COPY).toBe("Crédito potencial estimado. Não representa validação de elegibilidade, apropriação ou disponibilidade para utilização.");
    expect(SPLIT_EXPECTATION_COPY).toBe("Expectativa gerencial de segregação. Não comprova retenção ou recolhimento.");
    expect(SCENARIO_APPROVED_COPY).toBe("Cenário aprovado no escopo indicado. Não executa alteração comercial ou fiscal.");
    expect(SYSTEM_CALCULATION_COPY).toBe("Cálculo do sistema com as regras e os dados indicados.");
    expect(COMPLIANCE_VERIFIED_COPY).toBe("Obrigações verificadas no período e no escopo indicados.");
    expect(IMPACT_NATURES.CalculatedTaxReduction.label).toBe("Redução simulada do IBS/CBS calculado");
    expect(PROFESSIONAL_REVIEW.ReviewReceived.label).toContain("não é aprovação");
    expect(COMPLIANCE_STATUS.RemediationVerified.label).not.toBe(COMPLIANCE_STATUS.Verified.label);
    expect(Object.values(COMPLIANCE_STATUS).map((s) => s.label).join(" ")).not.toMatch(/conforme/i);
  });
});
