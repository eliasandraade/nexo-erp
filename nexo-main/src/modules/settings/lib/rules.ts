import type { AppSettings, PosSettings } from "../types";

/** What the server would refuse on save, said before saving. */
export function settingsProblems(s: AppSettings): string[] {
  const problems: string[] = [];
  if (s.pos.maxDiscountPercent < 0 || s.pos.maxDiscountPercent > 100) problems.push("O desconto máximo vai de 0% a 100%.");
  if (!s.pos.allowValueDiscount && !s.pos.allowPercentDiscount)
    problems.push("Permita ao menos um tipo de desconto (R$ ou %).");
  return problems;
}

const MANAGER_ROLES = ["gerente", "diretoria"];

export const isManagerRole = (role: string | undefined) => MANAGER_ROLES.includes((role ?? "").toLowerCase());

/** Discount as % of the gross value, rounded to 2 places like the server. */
export function discountPercent(gross: number, discount: number): number {
  if (gross <= 0 || discount <= 0) return 0;
  return Math.round((discount / gross) * 10000) / 100;
}

export type DiscountCheck = "ok" | "needs-manager" | "over-limit";

/**
 * Same rule as the server: above the limit a manager closes the sale, or — when the company allows
 * it — a manager authorizes at the PDV with login and password.
 */
export function checkDiscount(rules: PosSettings, role: string | undefined, gross: number, discount: number): DiscountCheck {
  if (discountPercent(gross, discount) <= rules.maxDiscountPercent || isManagerRole(role)) return "ok";
  return rules.requireManagerAuth ? "needs-manager" : "over-limit";
}
