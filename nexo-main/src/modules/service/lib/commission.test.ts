import { describe, it, expect } from "vitest";
import {
  commissionSourceLabel,
  currentMonthPeriod,
  entryStatus,
  periodToUtcRange,
  PAYOUT_STATUS_LABELS,
  sumCommission,
  toDateInputValue,
} from "./commission";

describe("commission", () => {
  it("labels each source in the preset's own words", () => {
    const labels = { customer: "Cliente", professional: "Barbeiro", catalogItem: "Serviço",
      appointment: "Agendamento", order: "Comanda", subject: "—" };
    expect(commissionSourceLabel("OrderItem", labels)).toBe("Comanda");
    expect(commissionSourceLabel("Appointment", labels)).toBe("Agendamento");
    expect(commissionSourceLabel("PackageUsage", labels)).toBe("Pacote");
    expect(commissionSourceLabel("OrderItem", null)).toBe("Ordem de serviço");
  });

  it("labels every payout status", () => {
    expect(PAYOUT_STATUS_LABELS.Pending).toBe("A pagar");
    expect(PAYOUT_STATUS_LABELS.Paid).toBe("Pago");
  });

  it("marks an entry as closed once it belongs to a payout", () => {
    expect(entryStatus({ payoutId: null }).label).toBe("Em aberto");
    expect(entryStatus({ payoutId: "p1" }).label).toBe("Fechada");
  });

  it("defaults the period to the current month up to today", () => {
    expect(currentMonthPeriod(new Date(2026, 9, 9, 15, 30))).toEqual({ from: "2026-10-01", to: "2026-10-09" });
    expect(toDateInputValue(new Date(2026, 0, 5))).toBe("2026-01-05");
  });

  it("turns a local inclusive period into UTC instants covering both whole days", () => {
    const range = periodToUtcRange("2026-10-01", "2026-10-09")!;
    expect(new Date(range.start).getTime()).toBe(new Date(2026, 9, 1, 0, 0, 0, 0).getTime());
    expect(new Date(range.end).getTime()).toBe(new Date(2026, 9, 9, 23, 59, 59, 999).getTime());
  });

  it("rejects missing, invalid or inverted periods", () => {
    expect(periodToUtcRange("", "2026-10-09")).toBeNull();
    expect(periodToUtcRange("2026-10-10", "2026-10-09")).toBeNull();
    expect(periodToUtcRange("not-a-date", "2026-10-09")).toBeNull();
  });

  it("sums commission amounts without floating-point drift", () => {
    expect(sumCommission([{ commissionAmount: 0.1 }, { commissionAmount: 0.2 }])).toBe(0.3);
    expect(sumCommission([])).toBe(0);
  });
});
