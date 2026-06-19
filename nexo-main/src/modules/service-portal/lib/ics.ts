/** Minimal RFC-5545 calendar event builder for the booking confirmation (frontend-only, no backend). */

export interface IcsEvent {
  uid:          string;
  startUtc:     string; // ISO UTC (…Z)
  endUtc:       string; // ISO UTC (…Z)
  title:        string;
  description?: string | null;
  location?:    string | null;
}

/** ISO UTC → iCal basic UTC stamp (YYYYMMDDTHHMMSSZ). */
export function toIcsDate(iso: string): string {
  const d = new Date(iso);
  const p = (n: number, w = 2) => String(n).padStart(w, "0");
  return (
    `${d.getUTCFullYear()}${p(d.getUTCMonth() + 1)}${p(d.getUTCDate())}` +
    `T${p(d.getUTCHours())}${p(d.getUTCMinutes())}${p(d.getUTCSeconds())}Z`
  );
}

/** Escapes text per RFC 5545 (commas, semicolons, backslashes, newlines). */
export function escapeIcsText(value: string): string {
  return value
    .replace(/\\/g, "\\\\")
    .replace(/;/g, "\\;")
    .replace(/,/g, "\\,")
    .replace(/\r?\n/g, "\\n");
}

export function buildIcs(e: IcsEvent): string {
  const lines = [
    "BEGIN:VCALENDAR",
    "VERSION:2.0",
    "PRODID:-//Orken//Service Portal//PT-BR",
    "CALSCALE:GREGORIAN",
    "BEGIN:VEVENT",
    `UID:${e.uid}`,
    `DTSTAMP:${toIcsDate(new Date().toISOString())}`,
    `DTSTART:${toIcsDate(e.startUtc)}`,
    `DTEND:${toIcsDate(e.endUtc)}`,
    `SUMMARY:${escapeIcsText(e.title)}`,
  ];
  if (e.description) lines.push(`DESCRIPTION:${escapeIcsText(e.description)}`);
  if (e.location) lines.push(`LOCATION:${escapeIcsText(e.location)}`);
  lines.push("END:VEVENT", "END:VCALENDAR");
  return lines.join("\r\n");
}

/** Triggers a download of the event as a .ics file (browser side). */
export function downloadIcs(filename: string, ics: string): void {
  const blob = new Blob([ics], { type: "text/calendar;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename.endsWith(".ics") ? filename : `${filename}.ics`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}
