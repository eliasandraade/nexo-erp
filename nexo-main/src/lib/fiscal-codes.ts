/** Brazilian fiscal identifiers — same rules as the server (FiscalCodes.cs). Masks are ignored. */

export const onlyDigits = (value: string | null | undefined) => (value ?? "").replace(/\D/g, "");

/** Letters and digits only, upper case — how a CPF/CNPJ is stored (the CNPJ issued since July 2026 has letters). */
export const normalizeDocument = (value: string | null | undefined) => (value ?? "").toUpperCase().replace(/[^0-9A-Z]/g, "");

/** IBGE state code (first two digits of a city code) by UF. */
export const IBGE_STATE_CODES: Record<string, number> = {
  RO: 11, AC: 12, AM: 13, RR: 14, PA: 15, AP: 16, TO: 17,
  MA: 21, PI: 22, CE: 23, RN: 24, PB: 25, PE: 26, AL: 27, SE: 28, BA: 29,
  MG: 31, ES: 32, RJ: 33, SP: 35,
  PR: 41, SC: 42, RS: 43,
  MS: 50, MT: 51, GO: 52, DF: 53,
};

const allSame = (d: string) => d.split("").every((c) => c === d[0]);
const isDigit = (c: string) => c >= "0" && c <= "9";

export function isCpf(value: string | null | undefined): boolean {
  const d = normalizeDocument(value);
  if (d.length !== 11 || !d.split("").every(isDigit) || allSame(d)) return false;
  const check = (count: number) => {
    let sum = 0;
    for (let i = 0; i < count; i++) sum += Number(d[i]) * (count + 1 - i);
    const r = (sum * 10) % 11;
    return r === 10 ? 0 : r;
  };
  return check(9) === Number(d[9]) && check(10) === Number(d[10]);
}

/** CNPJ, numeric or alphanumeric: each character counts as its char code minus 48 (Receita rule). */
export function isCnpj(value: string | null | undefined): boolean {
  const d = normalizeDocument(value);
  if (d.length !== 14 || !isDigit(d[12]) || !isDigit(d[13]) || allSame(d)) return false;
  const check = (count: number) => {
    let sum = 0;
    let weight = count - 7;
    for (let i = 0; i < count; i++) {
      sum += (d.charCodeAt(i) - 48) * weight;
      weight = weight === 2 ? 9 : weight - 1;
    }
    const r = sum % 11;
    return r < 2 ? 0 : 11 - r;
  };
  return check(12) === Number(d[12]) && check(13) === Number(d[13]);
}

/** GTIN-8/12/13/14 with a valid check digit; in-store codes (13 digits starting with 2) and zeros are not GTINs. */
export function isGtin(value: string | null | undefined): boolean {
  const raw = (value ?? "").trim();
  if (![8, 12, 13, 14].includes(raw.length) || !raw.split("").every(isDigit)) return false;
  if (allSame(raw) && raw[0] === "0") return false;
  if (raw.length === 13 && raw[0] === "2") return false;
  let sum = 0;
  for (let i = raw.length - 2; i >= 0; i--) sum += Number(raw[i]) * ((raw.length - 2 - i) % 2 === 0 ? 3 : 1);
  return (10 - (sum % 10)) % 10 === Number(raw[raw.length - 1]);
}

/** 7 digits starting with a real state code; when a UF is given, the code must be from that UF. */
export function isIbgeCity(code: string | null | undefined, state?: string): boolean {
  const c = (code ?? "").trim();
  if (c.length !== 7 || !c.split("").every(isDigit)) return false;
  const prefix = Number(c.slice(0, 2));
  if (!Object.values(IBGE_STATE_CODES).includes(prefix)) return false;
  return state === undefined || IBGE_STATE_CODES[state] === prefix;
}

/** "529.982.247-25" / "12.ABC.345/01DE-35" while typing. */
export function formatDocument(value: string): string {
  const d = normalizeDocument(value).slice(0, 14);
  const cpfLike = d.length <= 11 && d.split("").every(isDigit);
  if (cpfLike) {
    let out = d.slice(0, 3);
    if (d.length > 3) out += "." + d.slice(3, 6);
    if (d.length > 6) out += "." + d.slice(6, 9);
    if (d.length > 9) out += "-" + d.slice(9, 11);
    return out;
  }
  let out = d.slice(0, 2);
  if (d.length > 2) out += "." + d.slice(2, 5);
  if (d.length > 5) out += "." + d.slice(5, 8);
  if (d.length > 8) out += "/" + d.slice(8, 12);
  if (d.length > 12) out += "-" + d.slice(12, 14);
  return out;
}
