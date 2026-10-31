import { describe, it, expect } from "vitest";
import type { BuildDashboardProjectDto } from "../api/build.api";
import type { BuildForecastHistoryItemDto } from "../api/build-forecast.api";
import {
  bySeverity, choiceToSensitivity, confidenceLevel, deliverySparkline, filterPortfolio, fmtConfidence, fmtDelay, fmtRate, fmtWeekday,
  isRiskOnlyFactor, methodLabel, riskScore, riskTab, sensitivityToChoice, severityMeta, sortPortfolio, totalNc, weatherIcon,
  weatherRiskLabel,
} from "./forecast";

const p = (over: Partial<BuildDashboardProjectDto> = {}): BuildDashboardProjectDto => ({
  projectId: "p", name: "Obra", clientName: "C", status: "InProgress", actualPercent: 10, plannedPercent: 20, expectedEnd: null,
  forecastDelivery: null, daysLate: null, forecastMethodology: "productivity/1", costRealized: 0, contractTotal: null, cpiLike: null,
  openNonConformities: {}, openAssistance: 0, pendingChangeOrders: 0, lateMaterials: 0, portalPublished: false,
  lastDailyLogDate: null, pendingAiSuggestions: 0, ...over,
});

const h = (date: string, delivery: string | null): BuildForecastHistoryItemDto => ({
  id: date, date, generatedAt: `${date}T00:00:00Z`, source: "Scheduled", methodology: "productivity/1", version: 1,
  forecastDelivery: delivery, plannedDelivery: null, delayDays: null, confidence: 0.5, createdByName: null,
});

describe("forecast formatting", () => {
  it("methods, factors, severity", () => {
    expect(methodLabel("completed")).toBe("Concluída");
    expect(methodLabel("window", 14)).toBe("Ritmo dos últimos 14 dias");
    expect(methodLabel("overall")).toBe("Ritmo desde o início");
    expect(methodLabel("planned")).toBe("Ritmo planejado (sem dados)");
    expect(methodLabel("none")).toBe("Sem previsão");
    expect(isRiskOnlyFactor("weather_risk")).toBe(true);
    expect(isRiskOnlyFactor("pending_change_order")).toBe(true);
    expect(isRiskOnlyFactor("low_productivity")).toBe(false);
    expect(severityMeta("high").label).toBe("Alto");
    expect(severityMeta("weird").label).toBe("Sem risco");
    expect(severityMeta(null).label).toBe("Sem risco");
    expect([{ severity: "low" }, { severity: "x" }, { severity: "high" }].sort(bySeverity).map((x) => x.severity)).toEqual(["high", "low", "x"]);
  });

  it("confidence, delays and rates", () => {
    expect(fmtConfidence(0.734)).toBe("73%");
    expect(fmtConfidence(1.4)).toBe("100%");
    expect(fmtConfidence(null)).toBe("—");
    expect(confidenceLevel(0.8)).toBe("alta");
    expect(confidenceLevel(0.5)).toBe("média");
    expect(confidenceLevel(0.2)).toBe("baixa");
    expect(fmtDelay(1)).toBe("+1 dia");
    expect(fmtDelay(12)).toBe("+12 dias");
    expect(fmtDelay(0)).toBe("no prazo");
    expect(fmtDelay(-1)).toBe("1 dia adiantada");
    expect(fmtDelay(-4)).toBe("4 dias adiantada");
    expect(fmtDelay(undefined)).toBe("—");
    expect(fmtRate(2.5)).toBe("2,5%/dia útil");
    expect(fmtRate(null)).toBe("—");
  });

  it("sparkline: oldest → newest, later delivery higher; null with fewer than 2 points", () => {
    expect(deliverySparkline([h("2026-10-30", "2027-01-10")], 100, 10)).toBeNull();
    expect(deliverySparkline([h("2026-10-30", null), h("2026-10-29", "2027-01-01")], 100, 10)).toBeNull();
    // newest first in the API: 30/10 → 10/01, 29/10 → 01/01 → drawn 01/01 (low) then 10/01 (high)
    expect(deliverySparkline([h("2026-10-30", "2027-01-10"), h("2026-10-29", "2027-01-01")], 100, 10)).toBe("0.0,10.0 100.0,0.0");
    // a flat line does not divide by zero
    expect(deliverySparkline([h("2026-10-30", "2027-01-10"), h("2026-10-29", "2027-01-10")], 100, 10)).toBe("0.0,10.0 100.0,10.0");
  });
});

describe("weather", () => {
  it("icon from the description, else from the rain", () => {
    expect(weatherIcon({ description: "Trovoada com granizo forte", precipitationMm: 0 })).toBe("storm");
    expect(weatherIcon({ description: "Neve fraca", precipitationMm: 0 })).toBe("snow");
    expect(weatherIcon({ description: "Pancadas de chuva fortes", precipitationMm: 0 })).toBe("rain");
    expect(weatherIcon({ description: "Garoa fraca", precipitationMm: 0 })).toBe("drizzle");
    expect(weatherIcon({ description: "Neblina", precipitationMm: 0 })).toBe("fog");
    expect(weatherIcon({ description: "Parcialmente nublado", precipitationMm: 0 })).toBe("cloud-sun");
    expect(weatherIcon({ description: "Nublado", precipitationMm: 0 })).toBe("cloud");
    expect(weatherIcon({ description: "Céu limpo", precipitationMm: 0 })).toBe("sun");
    expect(weatherIcon({ description: null, precipitationMm: 8 })).toBe("rain");
    expect(weatherIcon({ description: null, precipitationMm: null })).toBe("cloud-sun");
    expect(weatherRiskLabel("heavy_rain")).toBe("Chuva forte");
    expect(weatherRiskLabel("hail")).toBe("hail");
    expect(fmtWeekday("2026-11-02")).toMatch(/02\/11/);
  });

  it("sensitivity choice ↔ API value", () => {
    expect(sensitivityToChoice(null)).toBe("auto");
    expect(sensitivityToChoice(undefined)).toBe("auto");
    expect(sensitivityToChoice(true)).toBe("yes");
    expect(sensitivityToChoice(false)).toBe("no");
    expect(choiceToSensitivity("auto")).toBeNull();
    expect(choiceToSensitivity("yes")).toBe(true);
    expect(choiceToSensitivity("no")).toBe(false);
  });
});

describe("portfolio", () => {
  const list = [
    p({ projectId: "a", name: "Alfa", daysLate: 10, cpiLike: 0.9, openNonConformities: { Low: 2 } }),
    p({ projectId: "b", name: "Beta", daysLate: -2, openNonConformities: { Critical: 1, High: 1 } }),
    p({ projectId: "c", name: "Gama" }),
  ];

  it("filters and counts", () => {
    expect(filterPortfolio(list, "all")).toHaveLength(3);
    expect(filterPortfolio(list, "late").map((x) => x.name)).toEqual(["Alfa"]);
    expect(filterPortfolio(list, "critical").map((x) => x.name)).toEqual(["Beta"]);
    expect(totalNc(list[1])).toBe(2);
    expect(totalNc(p({ openNonConformities: undefined as unknown as Record<string, number> }))).toBe(0);
  });

  it("risk score and sorts", () => {
    expect(riskScore(list[0])).toBeCloseTo(20);   // 10 days + (1 − 0.9) × 100
    expect(riskScore(list[1])).toBe(30);
    expect(sortPortfolio(list, "risk").map((x) => x.name)).toEqual(["Beta", "Alfa", "Gama"]);
    expect(sortPortfolio(list, "name").map((x) => x.name)).toEqual(["Alfa", "Beta", "Gama"]);
    expect(sortPortfolio(list, "daysLate").map((x) => x.name)).toEqual(["Alfa", "Beta", "Gama"]);
    expect(sortPortfolio(list, "cpi").map((x) => x.name)).toEqual(["Alfa", "Beta", "Gama"]);
    expect(list.map((x) => x.name)).toEqual(["Alfa", "Beta", "Gama"]);   // not mutated
  });

  it("risk → tab", () => {
    expect(riskTab("critical_nonconformity")).toBe("qualidade");
    expect(riskTab("late_material")).toBe("materiais");
    expect(riskTab("forecast_late")).toBe("riscos");
    expect(riskTab("cost_performance")).toBe("riscos");
  });
});
