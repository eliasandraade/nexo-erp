import { describe, it, expect } from "vitest";
import { buildIcs, toIcsDate, escapeIcsText } from "./ics";

describe("toIcsDate", () => {
  it("formats an ISO UTC instant as a basic iCal UTC stamp", () => {
    expect(toIcsDate("2026-06-20T14:05:00Z")).toBe("20260620T140500Z");
  });
});

describe("escapeIcsText", () => {
  it("escapes commas, semicolons, backslashes and newlines", () => {
    expect(escapeIcsText("Banho, tosa; gato\\cão\nobs")).toBe("Banho\\, tosa\\; gato\\\\cão\\nobs");
  });
});

describe("buildIcs", () => {
  const ics = buildIcs({
    uid: "abc@orken",
    startUtc: "2026-06-20T14:00:00Z",
    endUtc: "2026-06-20T15:00:00Z",
    title: "Consulta — Clínica Exemplo",
    description: "Profissional: Dra. Ana",
  });

  it("produces a valid VEVENT with the key fields", () => {
    expect(ics).toContain("BEGIN:VCALENDAR");
    expect(ics).toContain("BEGIN:VEVENT");
    expect(ics).toContain("UID:abc@orken");
    expect(ics).toContain("DTSTART:20260620T140000Z");
    expect(ics).toContain("DTEND:20260620T150000Z");
    expect(ics).toContain("SUMMARY:Consulta — Clínica Exemplo");
    expect(ics).toContain("DESCRIPTION:Profissional: Dra. Ana");
    expect(ics).toContain("END:VCALENDAR");
  });

  it("omits optional fields when absent", () => {
    const minimal = buildIcs({ uid: "u", startUtc: "2026-06-20T14:00:00Z", endUtc: "2026-06-20T15:00:00Z", title: "X" });
    expect(minimal).not.toContain("LOCATION:");
    expect(minimal).not.toContain("DESCRIPTION:");
  });
});
