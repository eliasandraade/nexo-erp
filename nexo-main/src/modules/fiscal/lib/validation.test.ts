import { describe, expect, it } from "vitest";
import type { FiscalProfileInput } from "../api/fiscal.api";
import { fiscalProblems, profileProblems } from "./validation";

const profile: FiscalProfileInput = {
  legalName: "Loja Ltda", tradeName: null, cnpj: "11.222.333/0001-81", stateRegistration: "ISENTO", municipalRegistration: null,
  taxRegime: "SimplesNacional", street: "Rua A", number: "1", complement: null, district: "Centro", cityName: "São Paulo",
  cityIbgeCode: "3550308", state: "SP", zipCode: "01001-000",
};

describe("fiscal validation", () => {
  it("accepts a complete issuer profile and explains what is wrong otherwise", () => {
    expect(profileProblems(profile)).toEqual([]);
    expect(profileProblems({ ...profile, cnpj: "11.222.333/0001-80", cityIbgeCode: "355", state: "", zipCode: "123" }))
      .toEqual(["CNPJ inválido.", "Código IBGE do município: 7 números.", "Escolha a UF.", "CEP: 8 números."]);
    expect(profileProblems({ ...profile, stateRegistration: "123.456.789.110" })).toEqual([]);
  });

  it("checks product fiscal fields like the server", () => {
    expect(fiscalProblems({ ncm: "6109.10.00", cest: null, origin: 0, cfop: "5102", taxSituation: "102" })).toEqual([]);
    expect(fiscalProblems({ ncm: "123", cest: "1", origin: null, cfop: "1102", taxSituation: "1" })).toHaveLength(4);
  });
});
