import { describe, it, expect } from "vitest";
import type { SvcAppointmentDto, SvcOrderDto, SvcPaymentDto } from "../api/service.api";
import { appointmentStats, openOrdersStats, paidTotal } from "./dashboard";

const appt = (status: SvcAppointmentDto["status"]): SvcAppointmentDto => ({
  id: status, storeId: "s", customerId: "c", professionalId: "p", catalogItemId: "k",
  subjectId: null, startsAt: "2026-06-18T12:00:00Z", endsAt: "2026-06-18T12:30:00Z",
  status, notes: null, cancellationReason: null, priceSnapshot: 0,
  createdAt: "", updatedAt: "",
});

const order = (status: SvcOrderDto["status"], total: number): SvcOrderDto => ({
  id: status + total, storeId: "s", code: "OS", customerId: "c", subjectId: null,
  professionalId: null, appointmentId: null, status, notes: null, cancellationReason: null,
  totalAmount: total, items: [], createdAt: "", updatedAt: "",
});

const payment = (status: SvcPaymentDto["status"], amount: number): SvcPaymentDto => ({
  id: status + amount, storeId: "s", customerId: "c", orderId: "o", customerPackageId: null,
  amount, method: "Pix", status, paidAt: "", externalReference: null, notes: null,
  voidReason: null, voidedAt: null, createdAt: "", updatedAt: "",
});

describe("dashboard aggregators", () => {
  it("appointmentStats splits done vs remaining; cancelled/no-show excluded from remaining", () => {
    const stats = appointmentStats([
      appt("Completed"), appt("Completed"),
      appt("Scheduled"), appt("Confirmed"), appt("InProgress"),
      appt("Cancelled"), appt("NoShow"),
    ]);
    expect(stats.total).toBe(7);
    expect(stats.done).toBe(2);
    expect(stats.remaining).toBe(3);
  });

  it("openOrdersStats counts + sums only Open/InProgress", () => {
    const stats = openOrdersStats([
      order("Open", 100), order("InProgress", 50),
      order("Draft", 999), order("Completed", 999), order("Cancelled", 999),
    ]);
    expect(stats.count).toBe(2);
    expect(stats.total).toBe(150);
  });

  it("paidTotal sums only Paid amounts, ignoring Voided", () => {
    expect(paidTotal([payment("Paid", 80), payment("Paid", 20), payment("Voided", 1000)])).toBe(100);
  });

  it("handles empty inputs", () => {
    expect(appointmentStats([])).toEqual({ total: 0, done: 0, remaining: 0 });
    expect(openOrdersStats([])).toEqual({ count: 0, total: 0 });
    expect(paidTotal([])).toBe(0);
  });
});
