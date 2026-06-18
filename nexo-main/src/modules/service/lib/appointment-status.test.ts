import { describe, it, expect } from "vitest";
import type { SvcAppointmentStatus } from "../api/service.api";
import {
  allowedTransitions,
  isTerminalStatus,
  transitionNeedsReason,
  APPOINTMENT_STATUS_LABELS,
} from "./appointment-status";

describe("appointment-status", () => {
  it("mirrors the backend transition machine", () => {
    expect(allowedTransitions("Scheduled").sort()).toEqual(["Cancelled", "Confirmed", "NoShow"]);
    expect(allowedTransitions("Confirmed").sort()).toEqual(["Cancelled", "InProgress", "NoShow"]);
    expect(allowedTransitions("InProgress").sort()).toEqual(["Cancelled", "Completed"]);
  });

  it("treats Completed / NoShow / Cancelled as terminal", () => {
    for (const s of ["Completed", "NoShow", "Cancelled"] as SvcAppointmentStatus[]) {
      expect(isTerminalStatus(s)).toBe(true);
      expect(allowedTransitions(s)).toEqual([]);
    }
  });

  it("does not allow skipping straight to Completed from Scheduled", () => {
    expect(allowedTransitions("Scheduled")).not.toContain("Completed");
    expect(allowedTransitions("Scheduled")).not.toContain("InProgress");
  });

  it("flags cancellation/no-show as needing a reason", () => {
    expect(transitionNeedsReason("Cancelled")).toBe(true);
    expect(transitionNeedsReason("NoShow")).toBe(true);
    expect(transitionNeedsReason("Confirmed")).toBe(false);
    expect(transitionNeedsReason("Completed")).toBe(false);
  });

  it("labels every status", () => {
    const all: SvcAppointmentStatus[] = [
      "Scheduled", "Confirmed", "InProgress", "Completed", "NoShow", "Cancelled",
    ];
    for (const s of all) expect(APPOINTMENT_STATUS_LABELS[s]).toBeTruthy();
  });
});
