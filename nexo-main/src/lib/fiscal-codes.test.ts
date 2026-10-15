import { describe, expect, it } from "vitest";
import { formatDocument, isCnpj, isCpf, isGtin, isIbgeCity, normalizeDocument } from "./fiscal-codes";

describe("fiscal codes", () => {
  it("checks CPF and CNPJ digits like the server", () => {
    expect(isCpf("529.982.247-25")).toBe(true);
    expect(isCpf("529.982.247-24")).toBe(false);
    expect(isCpf("111.111.111-11")).toBe(false);
    expect(isCnpj("11.222.333/0001-81")).toBe(true);
    expect(isCnpj("11.222.333/0001-80")).toBe(false);
  });

  it("accepts the alphanumeric CNPJ issued since July 2026", () => {
    expect(isCnpj("12.ABC.345/01DE-35")).toBe(true);
    expect(isCnpj("12abc34501de35")).toBe(true);
    expect(isCnpj("12.ABC.345/01DE-36")).toBe(false);
    expect(isCpf("12ABC345678")).toBe(false);
  });

  it("checks GTIN check digits and leaves in-store codes out", () => {
    expect(isGtin("4006381333931")).toBe(true);
    expect(isGtin("96385074")).toBe(true);
    expect(isGtin("4006381333932")).toBe(false);
    expect(isGtin("00000000")).toBe(false);
    expect(isGtin("2000000000008")).toBe(false);
    expect(isGtin("123")).toBe(false);
  });

  it("checks the IBGE city code against the UF", () => {
    expect(isIbgeCity("3550308")).toBe(true);
    expect(isIbgeCity("3550308", "SP")).toBe(true);
    expect(isIbgeCity("3550308", "RJ")).toBe(false);
    expect(isIbgeCity("0000000")).toBe(false);
  });

  it("masks a document while typing, letters included", () => {
    expect(formatDocument("52998224725")).toBe("529.982.247-25");
    expect(formatDocument("5299822")).toBe("529.982.2");
    expect(formatDocument("11222333000181")).toBe("11.222.333/0001-81");
    expect(formatDocument("12abc34501de35")).toBe("12.ABC.345/01DE-35");
    expect(normalizeDocument("12.ABC.345/01DE-35")).toBe("12ABC34501DE35");
  });
});
