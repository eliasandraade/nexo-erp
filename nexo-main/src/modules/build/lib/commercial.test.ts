import { describe, it, expect } from "vitest";
import { budgetTotals, fmtDays, fmtSignedBRL, measuredPercentProblem } from "./commercial";
import { budgetPdfFileName } from "../api/build-commercial.api";
import type { BuildBudgetDto } from "../api/build.api";

describe("commercial helpers", () => {
  it("names the budget PDF orcamento-<name>-v<N>.pdf without accents or symbols", () => {
    expect(budgetPdfFileName("Reforma Cozinha — João", 3)).toBe("orcamento-reforma-cozinha-joao-v3.pdf");
    expect(budgetPdfFileName("!!!", undefined)).toBe("orcamento-orcamento-v1.pdf");
  });

  it("validates a measured % against the current one and 100", () => {
    expect(measuredPercentProblem(30, 40)).toMatch(/menor que o atual \(40%\)/);
    expect(measuredPercentProblem(101, 40)).toMatch(/100%/);
    expect(measuredPercentProblem(40, 40)).toBeNull();
    expect(measuredPercentProblem(100, 40)).toBeNull();
    expect(measuredPercentProblem(50.5, 40)).toMatch(/inteiro/);
  });

  it("computes totals per kind from the items when the server does not send them", () => {
    const b = {
      totalCost: 300, finalPrice: 360, marginPercent: 20, areaM2: 3,
      items: [{ kind: "Material", totalCost: 100 }, { kind: "Labor", totalCost: 150 }, { totalCost: 50 }],
    } as unknown as BuildBudgetDto;
    const t = budgetTotals(b);
    expect([t.material, t.labor, t.other, t.subtotal, t.marginValue, t.total]).toEqual([100, 150, 50, 300, 60, 360]);
    expect(t.totalPerM2).toBe(120);
  });

  it("formats signed money and schedule impacts", () => {
    const plain = (v: number) => fmtSignedBRL(v).replace(/\s/g, " ");   // pt-BR currency uses a no-break space
    expect(plain(1500)).toBe("+R$ 1.500,00");
    expect(plain(-20)).toBe("−R$ 20,00");
    expect(fmtDays(0)).toBe("sem impacto no prazo");
    expect(fmtDays(-1)).toBe("−1 dia");
  });
});
