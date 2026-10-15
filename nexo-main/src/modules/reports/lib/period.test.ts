import { describe, expect, it } from "vitest";
import { fmtPeriod, methodLabel, periodProblem, presetPeriod } from "./period";

describe("report periods", () => {
  const now = new Date(2026, 2, 15, 22, 30); // 15/03/2026 22:30 local

  it("builds the presets from the local calendar day", () => {
    expect(presetPeriod("today", now)).toEqual({ from: "2026-03-15", to: "2026-03-15" });
    expect(presetPeriod("7d", now)).toEqual({ from: "2026-03-09", to: "2026-03-15" });
    expect(presetPeriod("month", now)).toEqual({ from: "2026-03-01", to: "2026-03-15" });
    expect(presetPeriod("lastMonth", now)).toEqual({ from: "2026-02-01", to: "2026-02-28" });
    expect(presetPeriod("lastMonth", new Date(2026, 0, 10))).toEqual({ from: "2025-12-01", to: "2025-12-31" });
  });

  it("explains a period the server would refuse", () => {
    expect(periodProblem({ from: "", to: "2026-03-01" })).toMatch(/duas datas/);
    expect(periodProblem({ from: "2026-03-10", to: "2026-03-01" })).toMatch(/antes da inicial/);
    expect(periodProblem({ from: "2025-01-01", to: "2026-03-01" })).toMatch(/366/);
    expect(periodProblem({ from: "2025-03-01", to: "2026-03-01" })).toBeNull();
  });

  it("formats periods and payment methods in Portuguese", () => {
    expect(fmtPeriod({ from: "2026-03-01", to: "2026-03-15" })).toBe("01/03/2026 a 15/03/2026");
    expect(fmtPeriod({ from: "2026-03-15", to: "2026-03-15" })).toBe("15/03/2026");
    expect(methodLabel("Pix")).toBe("Pix");
    expect(methodLabel("Credit")).toBe("Cartão de crédito");
  });
});
