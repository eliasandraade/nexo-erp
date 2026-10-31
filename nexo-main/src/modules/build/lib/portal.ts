import { PORTAL_PUBLIC_ORIGIN, type BuildPortalVisibility } from "../api/build-portal.api";

const SLUG_CHARS = "abcdefghijklmnopqrstuvwxyz0123456789-";
const HEX = "0123456789abcdefABCDEF";

/** Shapes typed text into a slug as the user types: lowercase, no accents, spaces → hyphen, no doubled hyphen. */
export function slugify(raw: string): string {
  const base = raw.normalize("NFD").toLowerCase();
  let out = "";
  for (const ch of base) {
    const code = ch.codePointAt(0) ?? 0;
    if (code >= 0x300 && code <= 0x36f) continue;   // combining accent marks (no regex: Sonar hotspot)
    const c = ch === " " || ch === "_" ? "-" : ch;
    if (!SLUG_CHARS.includes(c)) continue;
    if (c === "-" && (out === "" || out.endsWith("-"))) continue;
    out += c;
  }
  return out.slice(0, 60);
}

/** Client-side check mirroring the server (reserved names are left to the server). Null when valid. */
export function slugProblem(slug: string): string | null {
  if (!slug) return "Informe o endereço (slug).";
  if (slug.length < 3 || slug.length > 60) return "O endereço precisa ter entre 3 e 60 caracteres.";
  if (slug.startsWith("-") || slug.endsWith("-")) return "O endereço não pode começar nem terminar com hífen.";
  return null;
}

/** #RRGGBB, checked without a regex. */
export function isHexColor(s: string): boolean {
  return s.length === 7 && s[0] === "#" && [...s.slice(1)].every((c) => HEX.includes(c));
}

/** Public address: https://www.orken.com.br/{companySlug}/{projectSlug}. */
export function portalPublicUrl(companySlug: string | null | undefined, projectSlug: string | null | undefined): string {
  return `${PORTAL_PUBLIC_ORIGIN}/${companySlug || "{empresa}"}/${projectSlug || "{obra}"}`;
}

/** Drops leading/trailing hyphens left while typing ("minha-obra-" → "minha-obra"). */
export function trimHyphens(slug: string): string {
  let start = 0;
  let end = slug.length;
  while (start < end && slug[start] === "-") start++;
  while (end > start && slug[end - 1] === "-") end--;
  return slug.slice(start, end);
}

const FINANCIAL_FLAGS: Array<keyof BuildPortalVisibility> = ["financialSummary", "financialCategories", "financialTransactions", "receipts"];

/** True when any financial item is shown to the client (the UI warns about it). */
export function hasFinancialVisible(v: BuildPortalVisibility): boolean {
  return FINANCIAL_FLAGS.some((f) => v[f]);
}
