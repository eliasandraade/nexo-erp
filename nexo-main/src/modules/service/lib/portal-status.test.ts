import { describe, it, expect } from "vitest";
import {
  buildPortalChecklist, pendingItems, isPortalPublishable, type PortalStatusInput,
} from "./portal-status";

const READY: PortalStatusInput = {
  isConfigured: true,
  presetDisplayName: "Salões de Beleza",
  hasSlug: true,
  bookingEnabled: true,
  activeProfessionals: 2,
  professionalsWithHours: 2,
  activeServices: 3,
};

function statusOf(input: PortalStatusInput, key: string) {
  return buildPortalChecklist(input).find((i) => i.key === key)!.status;
}

describe("buildPortalChecklist", () => {
  it("all OK when everything is configured", () => {
    expect(buildPortalChecklist(READY).every((i) => i.status === "ok")).toBe(true);
  });

  it("slug pending when there is no slug", () => {
    expect(statusOf({ ...READY, hasSlug: false }, "slug")).toBe("pending");
  });

  it("booking pending when disabled", () => {
    expect(statusOf({ ...READY, bookingEnabled: false }, "booking")).toBe("pending");
  });

  it("hours: warn when there are pros but none with hours, pending when no pros", () => {
    expect(statusOf({ ...READY, professionalsWithHours: 0 }, "hours")).toBe("warn");
    expect(statusOf({ ...READY, professionalsWithHours: 0, activeProfessionals: 0 }, "hours")).toBe("pending");
  });

  it("services pending when none active", () => {
    expect(statusOf({ ...READY, activeServices: 0 }, "services")).toBe("pending");
  });

  it("preset pending when not configured", () => {
    expect(statusOf({ ...READY, isConfigured: false }, "preset")).toBe("pending");
  });
});

describe("pendingItems / isPortalPublishable", () => {
  it("ready portal has no pending items and is publishable", () => {
    expect(pendingItems(buildPortalChecklist(READY))).toHaveLength(0);
    expect(isPortalPublishable(READY)).toBe(true);
  });

  it("missing slug blocks publishing and surfaces a pending item", () => {
    const input = { ...READY, hasSlug: false };
    expect(isPortalPublishable(input)).toBe(false);
    expect(pendingItems(buildPortalChecklist(input)).some((i) => i.key === "slug")).toBe(true);
  });

  it("no professional with hours blocks publishing", () => {
    expect(isPortalPublishable({ ...READY, professionalsWithHours: 0 })).toBe(false);
  });

  it("no active services blocks publishing", () => {
    expect(isPortalPublishable({ ...READY, activeServices: 0 })).toBe(false);
  });
});
