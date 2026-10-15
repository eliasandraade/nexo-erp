import { isCnpj, onlyDigits } from "@/lib/fiscal-codes";
import type { FiscalProfileInput, ProductFiscalInput } from "../api/fiscal.api";

export const STATES = ["AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"];

/** Why a field would be refused by the server, or null — same rules, shown before saving. */
export function fiscalProblems(f: ProductFiscalInput): string[] {
  const problems: string[] = [];
  const d = (v: string | null) => onlyDigits(v);
  if (f.ncm && d(f.ncm).length !== 8) problems.push("NCM: 8 números.");
  if (f.cest && d(f.cest).length !== 7) problems.push("CEST: 7 números.");
  if (f.cfop && !/^[567]\d{3}$/.test(d(f.cfop))) problems.push("CFOP de venda: 4 números começando por 5, 6 ou 7.");
  if (f.taxSituation && !/^\d{2,3}$/.test(f.taxSituation.trim())) problems.push("CST (2 números) ou CSOSN (3 números).");
  return problems;
}

/** Same rules as the server, shown before saving. */
export function profileProblems(p: FiscalProfileInput): string[] {
  const problems: string[] = [];
  if (!p.legalName.trim()) problems.push("Informe a razão social.");
  if (!isCnpj(p.cnpj)) problems.push("CNPJ inválido.");
  const ie = p.stateRegistration.trim();
  if (!(ie.toUpperCase() === "ISENTO" || (onlyDigits(ie).length >= 2 && onlyDigits(ie).length <= 14))) problems.push("Inscrição estadual: números ou ISENTO.");
  if (!p.street.trim() || !p.number.trim() || !p.district.trim() || !p.cityName.trim()) problems.push("Complete o endereço.");
  if (!/^\d{7}$/.test(p.cityIbgeCode.trim())) problems.push("Código IBGE do município: 7 números.");
  if (!STATES.includes(p.state)) problems.push("Escolha a UF.");
  if (onlyDigits(p.zipCode).length !== 8) problems.push("CEP: 8 números.");
  return problems;
}
