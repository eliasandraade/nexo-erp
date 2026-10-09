import { describe, it, expect } from "vitest";
import {
  commissionSourceLabel,
  currentMonthPeriod,
  entryStatus,
  isOpenEntry,
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
    expect(entryStatus({ payoutId: null, kind: "Earning", reversedAt: null }).label).toBe("Em aberto");
    expect(entryStatus({ payoutId: "p1", kind: "Earning", reversedAt: null }).label).toBe("Fechada");
  });

  it("labels voided earnings and the reversals that discount them", () => {
    expect(entryStatus({ payoutId: null, kind: "Earning", reversedAt: "2026-10-09T10:00:00Z" }).label).toBe("Estornada");
    expect(entryStatus({ payoutId: null, kind: "Reversal", reversedAt: null }).label).toBe("A descontar");
    expect(entryStatus({ payoutId: "p2", kind: "Reversal", reversedAt: null }).label).toBe("Descontada");
    expect(isOpenEntry({ payoutId: null, reversedAt: null })).toBe(true);
    expect(isOpenEntry({ payoutId: null, reversedAt: "2026-10-09T10:00:00Z" })).toBe(false);
    expect(isOpenEntry({ payoutId: "p1", reversedAt: null })).toBe(false);
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
