import { IBGE_STATE_CODES, isCnpj, isIbgeCity, onlyDigits } from "@/lib/fiscal-codes";
import type { CustomerFiscalInput, FiscalProfileInput, ProductFiscalInput } from "../api/fiscal.api";

export const STATES = Object.keys(IBGE_STATE_CODES).sort((a, b) => a.localeCompare(b));

const MAX_NOTE_NUMBER = 999_999_999;

/** IE: digits (masks allowed) or ISENTO — letters are refused, as on the server. */
export function isStateRegistration(value: string | null): boolean {
  const v = (value ?? "").trim();
  if (v.toUpperCase() === "ISENTO") return true;
  if (/[^\d./\- ]/.test(v)) return false;
  const n = onlyDigits(v).length;
  return n >= 2 && n <= 14;
}

/** Why the product fiscal fields would be refused by the server — same rules, shown before saving. */
export function fiscalProblems(f: ProductFiscalInput): string[] {
  const problems: string[] = [];
  const d = (v: string | null) => onlyDigits(v);
  if (f.ncm && d(f.ncm).length !== 8) problems.push("NCM: 8 números.");
  if (f.cest && d(f.cest).length !== 7) problems.push("CEST: 7 números.");
  if (f.cfop && !/^[567]\d{3}$/.test(d(f.cfop))) problems.push("CFOP de venda: 4 números começando por 5, 6 ou 7.");
  if (f.taxSituation && !/^\d{2,3}$/.test(f.taxSituation.trim())) problems.push("CST (2 números) ou CSOSN (3 números).");
  if (f.taxUnit && !/^[0-9A-Za-z]{1,6}$/.test(f.taxUnit.trim())) problems.push("Unidade tributável: até 6 letras ou números.");
  return problems;
}

const inRange = (v: number | null, min: number, max: number) => v === null || (Number.isInteger(v) && v >= min && v <= max);

/** Why the issuer profile would be refused by the server — same rules, shown before saving. */
export function profileProblems(p: FiscalProfileInput): string[] {
  const problems: string[] = [];
  if (!p.legalName.trim()) problems.push("Informe a razão social.");
  if (!isCnpj(p.cnpj)) problems.push("CNPJ inválido.");
  if (!isStateRegistration(p.stateRegistration)) problems.push("Inscrição estadual: números ou ISENTO.");
  if (!p.street.trim() || !p.number.trim() || !p.district.trim() || !p.cityName.trim()) problems.push("Complete o endereço.");
  if (!STATES.includes(p.state)) problems.push("Escolha a UF.");
  if (!isIbgeCity(p.cityIbgeCode)) problems.push("Código IBGE do município: 7 números.");
  else if (STATES.includes(p.state) && !isIbgeCity(p.cityIbgeCode, p.state)) problems.push("O código IBGE não é de um município dessa UF.");
  if (onlyDigits(p.zipCode).length !== 8) problems.push("CEP: 8 números.");
  if ((p.operationNature ?? "").length > 60) problems.push("Natureza da operação: até 60 caracteres.");
  if (!inRange(p.nfceSeries, 0, 889) || !inRange(p.nfeSeries, 0, 889)) problems.push("Série: de 0 a 889.");
  if (!inRange(p.nfceNextNumber, 1, MAX_NOTE_NUMBER) || !inRange(p.nfeNextNumber, 1, MAX_NOTE_NUMBER)) problems.push("Próximo número: de 1 a 999.999.999.");
  return problems;
}

/** Why the customer fiscal fields would be refused by the server. */
export function customerFiscalProblems(c: CustomerFiscalInput): string[] {
  const problems: string[] = [];
  if (c.stateRegistration && !isStateRegistration(c.stateRegistration)) problems.push("Inscrição estadual: números ou ISENTO.");
  if (c.taxpayerIndicator === "ContribuinteIcms" && (!c.stateRegistration || c.stateRegistration.trim().toUpperCase() === "ISENTO"))
    problems.push("Contribuinte do ICMS precisa da inscrição estadual (números).");
  if (c.cityIbgeCode && !isIbgeCity(c.cityIbgeCode)) problems.push("Código IBGE do município: 7 números.");
  return problems;
}
