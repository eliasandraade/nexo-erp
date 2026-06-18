import { describe, it, expect } from "vitest";
import type { SvcOrderStatus } from "../api/service.api";
import {
  allowedOrderTransitions,
  isOrderMutable,
  ORDER_STATUS_LABELS,
} from "./order-status";

describe("order-status", () => {
  it("mirrors the backend order transition machine", () => {
    expect(allowedOrderTransitions("Draft").sort()).toEqual(["Cancelled", "Open"]);
    expect(allowedOrderTransitions("Open").sort()).toEqual(["Cancelled", "InProgress"]);
    expect(allowedOrderTransitions("InProgress").sort()).toEqual(["Cancelled", "Completed"]);
    expect(allowedOrderTransitions("Completed")).toEqual([]);
    expect(allowedOrderTransitions("Cancelled")).toEqual([]);
  });

  it("only allows item edits while non-terminal", () => {
    expect(isOrderMutable("Draft")).toBe(true);
    expect(isOrderMutable("Open")).toBe(true);
    expect(isOrderMutable("InProgress")).toBe(true);
    expect(isOrderMutable("Completed")).toBe(false);
    expect(isOrderMutable("Cancelled")).toBe(false);
  });

  it("labels every status", () => {
    const all: SvcOrderStatus[] = ["Draft", "Open", "InProgress", "Completed", "Cancelled"];
    for (const s of all) expect(ORDER_STATUS_LABELS[s]).toBeTruthy();
  });
});
