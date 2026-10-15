import { describe, expect, it } from "vitest";
import { formatDocument, isCnpj, isCpf, isGtin, onlyDigits } from "./fiscal-codes";

describe("fiscal codes", () => {
  it("checks CPF and CNPJ digits like the server", () => {
    expect(isCpf("529.982.247-25")).toBe(true);
    expect(isCpf("529.982.247-24")).toBe(false);
    expect(isCpf("111.111.111-11")).toBe(false);
    expect(isCnpj("11.222.333/0001-81")).toBe(true);
    expect(isCnpj("11.222.333/0001-80")).toBe(false);
  });

  it("checks GTIN check digits", () => {
    expect(isGtin("4006381333931")).toBe(true);
    expect(isGtin("96385074")).toBe(true);
    expect(isGtin("4006381333932")).toBe(false);
    expect(isGtin("123")).toBe(false);
  });

  it("masks a document while typing", () => {
    expect(formatDocument("52998224725")).toBe("529.982.247-25");
    expect(formatDocument("5299822")).toBe("529.982.2");
    expect(formatDocument("11222333000181")).toBe("11.222.333/0001-81");
    expect(onlyDigits("11.222.333/0001-81")).toBe("11222333000181");
  });
});
