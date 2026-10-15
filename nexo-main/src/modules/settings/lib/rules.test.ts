import { describe, expect, it } from "vitest";
import { checkDiscount, discountPercent, settingsProblems } from "./rules";

const rules = { allowValueDiscount: true, allowPercentDiscount: true, requireManagerAuth: true, maxDiscountPercent: 10 };

describe("PDV discount rule", () => {
  it("measures the discount on the gross value", () => {
    expect(discountPercent(200, 25)).toBe(12.5);
    expect(discountPercent(0, 5)).toBe(0);
  });

  it("lets managers through and asks a manager otherwise", () => {
    expect(checkDiscount(rules, "vendedor", 100, 10)).toBe("ok");
    expect(checkDiscount(rules, "vendedor", 100, 10.01)).toBe("needs-manager");
    expect(checkDiscount(rules, "gerente", 100, 50)).toBe("ok");
    expect(checkDiscount({ ...rules, requireManagerAuth: false }, "vendedor", 100, 30)).toBe("over-limit");
  });

  it("refuses settings the server would refuse", () => {
    expect(settingsProblems({ inventory: { enableLowStockAlerts: true, enableZeroStockAlerts: true }, pos: rules })).toEqual([]);
    expect(settingsProblems({ inventory: { enableLowStockAlerts: true, enableZeroStockAlerts: true }, pos: { ...rules, allowValueDiscount: false, allowPercentDiscount: false } })).toHaveLength(1);
  });
});
