/** Brazilian fiscal identifiers — same rules as the server (FiscalCodes.cs). Masks are ignored. */

export const onlyDigits = (value: string | null | undefined) => (value ?? "").replace(/\D/g, "");

const allSame = (d: string) => d.split("").every((c) => c === d[0]);

export function isCpf(value: string | null | undefined): boolean {
  const d = onlyDigits(value);
  if (d.length !== 11 || allSame(d)) return false;
  const check = (count: number) => {
    let sum = 0;
    for (let i = 0; i < count; i++) sum += Number(d[i]) * (count + 1 - i);
    const r = (sum * 10) % 11;
    return r === 10 ? 0 : r;
  };
  return check(9) === Number(d[9]) && check(10) === Number(d[10]);
}

export function isCnpj(value: string | null | undefined): boolean {
  const d = onlyDigits(value);
  if (d.length !== 14 || allSame(d)) return false;
  const check = (count: number) => {
    let sum = 0;
    let weight = count - 7;
    for (let i = 0; i < count; i++) {
      sum += Number(d[i]) * weight;
      weight = weight === 2 ? 9 : weight - 1;
    }
    const r = sum % 11;
    return r < 2 ? 0 : 11 - r;
  };
  return check(12) === Number(d[12]) && check(13) === Number(d[13]);
}

/** GTIN-8/12/13/14 with a valid check digit. */
export function isGtin(value: string | null | undefined): boolean {
  const raw = (value ?? "").trim();
  if (![8, 12, 13, 14].includes(raw.length) || !/^\d+$/.test(raw)) return false;
  let sum = 0;
  for (let i = raw.length - 2; i >= 0; i--) sum += Number(raw[i]) * ((raw.length - 2 - i) % 2 === 0 ? 3 : 1);
  return (10 - (sum % 10)) % 10 === Number(raw[raw.length - 1]);
}

/** "529.982.247-25" / "11.222.333/0001-81" while typing; anything else stays digits. */
export function formatDocument(value: string): string {
  const d = onlyDigits(value).slice(0, 14);
  if (d.length <= 11) return d.replace(/(\d{3})(\d)/, "$1.$2").replace(/(\d{3})(\d)/, "$1.$2").replace(/(\d{3})(\d{1,2})$/, "$1-$2");
  return d.replace(/^(\d{2})(\d)/, "$1.$2").replace(/^(\d{2})\.(\d{3})(\d)/, "$1.$2.$3").replace(/\.(\d{3})(\d)/, ".$1/$2").replace(/(\d{4})(\d)/, "$1-$2");
}
