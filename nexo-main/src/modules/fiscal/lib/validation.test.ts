import { describe, expect, it } from "vitest";
import type { FiscalProfileInput } from "../api/fiscal.api";
import { customerFiscalProblems, fiscalProblems, isStateRegistration, profileProblems } from "./validation";

const profile: FiscalProfileInput = {
  legalName: "Loja Ltda", tradeName: null, cnpj: "11.222.333/0001-81", stateRegistration: "ISENTO", municipalRegistration: null,
  taxRegime: "SimplesNacional", street: "Rua A", number: "1", complement: null, district: "Centro", cityName: "São Paulo",
  cityIbgeCode: "3550308", state: "SP", zipCode: "01001-000",
  operationNature: "Venda de mercadoria", nfceSeries: 1, nfceNextNumber: 1, nfeSeries: null, nfeNextNumber: null,
};

describe("fiscal validation", () => {
  it("accepts a complete issuer profile and explains what is wrong otherwise", () => {
    expect(profileProblems(profile)).toEqual([]);
    expect(profileProblems({ ...profile, cnpj: "12.ABC.345/01DE-35" })).toEqual([]);
    expect(profileProblems({ ...profile, cnpj: "11.222.333/0001-80", cityIbgeCode: "355", state: "", zipCode: "123" }))
      .toEqual(["CNPJ inválido.", "Escolha a UF.", "Código IBGE do município: 7 números.", "CEP: 8 números."]);
    expect(profileProblems({ ...profile, state: "RJ" })).toEqual(["O código IBGE não é de um município dessa UF."]);
    expect(profileProblems({ ...profile, nfceSeries: 900, nfeNextNumber: 0 })).toEqual(["Série: de 0 a 889.", "Próximo número: de 1 a 999.999.999."]);
  });

  it("takes an IE as digits or ISENTO, never with letters", () => {
    expect(isStateRegistration("123.456.789.110")).toBe(true);
    expect(isStateRegistration("isento")).toBe(true);
    expect(isStateRegistration("P-01100424.3/002")).toBe(false);
    expect(isStateRegistration("abc12")).toBe(false);
  });

  it("checks product fiscal fields like the server", () => {
    expect(fiscalProblems({ ncm: "6109.10.00", cest: null, origin: 0, cfop: "5102", taxSituation: "102", taxUnit: "UN" })).toEqual([]);
    expect(fiscalProblems({ ncm: "123", cest: "1", origin: null, cfop: "1102", taxSituation: "1", taxUnit: "CAIXA-12" })).toHaveLength(5);
  });

  it("asks an ICMS taxpayer customer for the IE", () => {
    expect(customerFiscalProblems({ stateRegistration: null, taxpayerIndicator: "ContribuinteIcms", cityIbgeCode: null }))
      .toEqual(["Contribuinte do ICMS precisa da inscrição estadual (números)."]);
    expect(customerFiscalProblems({ stateRegistration: "110.042.490.114", taxpayerIndicator: "ContribuinteIcms", cityIbgeCode: "3550308" })).toEqual([]);
  });
});
