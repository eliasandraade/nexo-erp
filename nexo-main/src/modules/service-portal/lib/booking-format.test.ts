import { describe, it, expect } from "vitest";
import {
  formatPrice, formatDuration, formatTime, formatDateLong, formatSlotLabel,
  groupSlotsByDay, isValidPhone,
} from "./booking-format";

// Force UTC so assertions don't depend on the test runner's local timezone.
const UTC = { timeZone: "UTC" };

describe("formatPrice", () => {
  it("formats BRL", () => {
    const out = formatPrice(80);
    expect(out).toContain("R$");
    expect(out).toContain("80,00");
  });
});

describe("formatDuration", () => {
  it.each([
    [30, "30 min"],
    [45, "45 min"],
    [60, "1h"],
    [90, "1h30"],
    [120, "2h"],
    [150, "2h30"],
  ])("formats %i minutes as %s", (min, expected) => {
    expect(formatDuration(min)).toBe(expected);
  });
});

describe("time/date formatting (UTC)", () => {
  const iso = "2026-06-19T14:00:00Z";

  it("formatTime renders 24h clock", () => {
    expect(formatTime(iso, UTC)).toBe("14:00");
  });

  it("formatDateLong includes day and month", () => {
    const out = formatDateLong(iso, UTC).toLowerCase();
    expect(out).toContain("19");
    expect(out).toContain("jun");
  });

  it("formatSlotLabel joins date and time", () => {
    expect(formatSlotLabel(iso, UTC)).toContain("14:00");
  });
});

describe("groupSlotsByDay (UTC)", () => {
  it("groups slots by calendar day preserving order", () => {
    const days = groupSlotsByDay([
      { startsAt: "2026-06-19T09:00:00Z", endsAt: "2026-06-19T10:00:00Z" },
      { startsAt: "2026-06-19T10:00:00Z", endsAt: "2026-06-19T11:00:00Z" },
      { startsAt: "2026-06-20T08:00:00Z", endsAt: "2026-06-20T09:00:00Z" },
    ], UTC);

    expect(days).toHaveLength(2);
    expect(days[0].dayKey).toBe("2026-06-19");
    expect(days[0].slots.map((s) => s.timeLabel)).toEqual(["09:00", "10:00"]);
    expect(days[1].dayKey).toBe("2026-06-20");
    expect(days[1].slots).toHaveLength(1);
    expect(days[1].slots[0].startsAt).toBe("2026-06-20T08:00:00Z");
  });

  it("returns empty for no slots", () => {
    expect(groupSlotsByDay([], UTC)).toEqual([]);
  });
});

describe("isValidPhone", () => {
  it.each([
    ["11999998888", true],
    ["(11) 9 9999-8888", true],
    ["8533334444", true],
    ["1199", false],
    ["", false],
    ["abc", false],
  ])("validates %s → %s", (phone, expected) => {
    expect(isValidPhone(phone)).toBe(expected);
  });
});
