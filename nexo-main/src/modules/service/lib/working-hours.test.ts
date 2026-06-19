import { describe, it, expect } from "vitest";
import {
  parseWorkingHours, buildWorkingHoursJson, dayError, weekHasErrors,
  professionalHasHours, emptyWeek, type DayHours,
} from "./working-hours";

function day(weekday: number, patch: Partial<DayHours> = {}): DayHours {
  return {
    weekday, enabled: true, start: "09:00", end: "18:00",
    hasBreak: false, breakStart: "12:00", breakEnd: "13:00", ...patch,
  };
}

describe("parseWorkingHours", () => {
  it.each([null, undefined, "", "   ", "not json", "{}", "[]"])(
    "blank/malformed %s ⇒ all-disabled week", (json) => {
      const week = parseWorkingHours(json as string | null);
      expect(week).toHaveLength(7);
      expect(week.every((d) => !d.enabled)).toBe(true);
    });

  it("single window ⇒ enabled day, no break", () => {
    const week = parseWorkingHours('[{"weekday":1,"windows":[{"start":"09:00","end":"18:00"}]}]');
    const mon = week.find((d) => d.weekday === 1)!;
    expect(mon.enabled).toBe(true);
    expect(mon.start).toBe("09:00");
    expect(mon.end).toBe("18:00");
    expect(mon.hasBreak).toBe(false);
  });

  it("two windows ⇒ break between them", () => {
    const week = parseWorkingHours(
      '[{"weekday":3,"windows":[{"start":"09:00","end":"12:00"},{"start":"14:00","end":"18:00"}]}]');
    const wed = week.find((d) => d.weekday === 3)!;
    expect(wed.enabled).toBe(true);
    expect(wed.hasBreak).toBe(true);
    expect(wed.start).toBe("09:00");
    expect(wed.breakStart).toBe("12:00");
    expect(wed.breakEnd).toBe("14:00");
    expect(wed.end).toBe("18:00");
  });
});

describe("buildWorkingHoursJson", () => {
  it("empty/disabled week ⇒ null", () => {
    expect(buildWorkingHoursJson(emptyWeek())).toBeNull();
  });

  it("single window day ⇒ one window", () => {
    const json = buildWorkingHoursJson([day(1, { end: "17:00" })]);
    expect(JSON.parse(json!)).toEqual([{ weekday: 1, windows: [{ start: "09:00", end: "17:00" }] }]);
  });

  it("day with break ⇒ two windows", () => {
    const json = buildWorkingHoursJson([
      day(2, { hasBreak: true, breakStart: "12:00", breakEnd: "13:30", end: "18:00" }),
    ]);
    expect(JSON.parse(json!)).toEqual([
      { weekday: 2, windows: [{ start: "09:00", end: "12:00" }, { start: "13:30", end: "18:00" }] },
    ]);
  });

  it("round-trips parse → build", () => {
    const original = '[{"weekday":5,"windows":[{"start":"08:00","end":"12:00"},{"start":"13:00","end":"17:00"}]}]';
    const rebuilt = buildWorkingHoursJson(parseWorkingHours(original));
    expect(JSON.parse(rebuilt!)).toEqual(JSON.parse(original));
  });

  it("drops disabled days", () => {
    const json = buildWorkingHoursJson([day(1), day(2, { enabled: false })]);
    expect(JSON.parse(json!)).toHaveLength(1);
  });
});

describe("dayError / weekHasErrors", () => {
  it("disabled day is always valid", () => {
    expect(dayError(day(1, { enabled: false, start: "20:00", end: "08:00" }))).toBeNull();
  });
  it("start after end is invalid", () => {
    expect(dayError(day(1, { start: "18:00", end: "09:00" }))).not.toBeNull();
  });
  it("break outside the shift is invalid", () => {
    expect(dayError(day(1, { hasBreak: true, breakStart: "08:00", breakEnd: "08:30" }))).not.toBeNull();
  });
  it("valid day with break passes", () => {
    expect(dayError(day(1, { hasBreak: true, breakStart: "12:00", breakEnd: "13:00" }))).toBeNull();
  });
  it("weekHasErrors detects a bad day", () => {
    expect(weekHasErrors([day(1), day(2, { start: "18:00", end: "09:00" })])).toBe(true);
    expect(weekHasErrors([day(1), day(2, { enabled: false })])).toBe(false);
  });
});

describe("professionalHasHours", () => {
  it.each([null, "", "[]", "garbage"])("no real hours for %s", (json) => {
    expect(professionalHasHours(json as string | null)).toBe(false);
  });
  it("true when at least one window exists", () => {
    expect(professionalHasHours('[{"weekday":1,"windows":[{"start":"09:00","end":"18:00"}]}]')).toBe(true);
  });
});
