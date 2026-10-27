import { describe, expect, it } from "vitest";
import { ApiError } from "@/services/api-client";
import {
  complianceProblems, creditProblems, emptyCompliance, emptyCredit, movementProblems, toComplianceRequest, toCreditRequest, toMovementRequest,
  emptyHypothesis, emptyOffer, emptyScenario, offersProblems, scenarioProblems, simulationProblems, toCompareRequest, toScenarioRequest,
  emptyLine, emptyOperation, formatIsoDate, isProfileMissing, lineProblems, linesProblems, operationProblems, parseDecimal,
  profileDraftProblems, splitList, toLineRequest, toOperationRequest, toProfileVersionRequest, type LineDraft, type ProfileDraft,
} from "./forms";

const line = (patch: Partial<LineDraft> = {}): LineDraft => ({ ...emptyLine(), description: "Camiseta", grossAmount: "100,00", ...patch });

describe("parseDecimal", () => {
  it("reads Brazilian and plain numbers", () => {
    expect(parseDecimal("1.234,56")).toBe(1234.56);
    expect(parseDecimal("R$ 10,5")).toBe(10.5);
    expect(parseDecimal("1234.56")).toBe(1234.56);
    expect(parseDecimal("  ")).toBeNull();
    expect(parseDecimal("abc")).toBeNaN();
  });
});

describe("operation lines", () => {
  it("sends null when the taxes in the price are not known (\"não sei\")", () => {
    expect(toLineRequest(line()).excludedTaxes).toBeNull();
    expect(toLineRequest(line({ excludedTaxesUnknown: false, excludedTaxes: "18,00" })).excludedTaxes).toBe(18);
    expect(toLineRequest(line({ excludedTaxesUnknown: false, excludedTaxes: "0" })).excludedTaxes).toBe(0);
  });

  it("maps the draft to the request the server expects", () => {
    expect(toLineRequest(line({ ncm: "6109.10.00", unconditionalDiscount: "5", conditionalDiscount: "", surcharges: "2,50", quantity: "3", specialTreatment: " " })))
      .toEqual({
        itemKind: "Good", description: "Camiseta", ncm: "61091000", quantity: 3, grossAmount: 100,
        unconditionalDiscount: 5, conditionalDiscount: 0, surcharges: 2.5, excludedTaxes: null, specialTreatment: null, nbs: null, cClassTrib: null,
      });
    expect(toLineRequest(line({ cClassTrib: " 000001 ", nbs: "1.0101.10.00" }))).toMatchObject({ cClassTrib: "000001", nbs: "1.0101.10.00" });
  });

  it("validates a line like the server does", () => {
    expect(lineProblems(line(), 1)).toEqual([]);
    expect(lineProblems(line({ description: "", ncm: "123", quantity: "0", grossAmount: "" }), 2)).toEqual([
      "Linha 2: descreva o item.", "Linha 2: NCM tem 8 números.", "Linha 2: quantidade maior que zero.", "Linha 2: informe o valor da linha.",
    ]);
    expect(lineProblems(line({ unconditionalDiscount: "60", conditionalDiscount: "50" }), 1)).toEqual(["Linha 1: os descontos passam do valor da linha."]);
    expect(lineProblems(line({ grossAmount: "10,001" }), 1)).toEqual(["Linha 1: valor em centavos (2 casas)."]);
    expect(lineProblems(line({ excludedTaxesUnknown: false }), 1)).toEqual(['Linha 1: informe o ICMS/ISS/PIS/Cofins no preço ou marque "não sei".']);
    expect(lineProblems(line({ surcharges: "-1" }), 1)).toEqual(["Linha 1: acréscimos em reais, sem valor negativo."]);
    expect(linesProblems([])).toEqual(["Inclua ao menos uma linha."]);
  });

  it("maps a whole operation, empty optional fields becoming null", () => {
    const op = { ...emptyOperation("2026-10-26"), lines: [line()], counterpartyState: "sp" };
    expect(operationProblems(op)).toEqual([]);
    const req = toOperationRequest(op);
    expect(req).toMatchObject({ operationDate: "2026-10-26", direction: "Outbound", counterpartyRegime: null, counterpartyState: "SP", description: null });
    expect(req.lines).toHaveLength(1);
    expect(operationProblems({ ...op, operationDate: "", counterpartyState: "S" })).toEqual(["Informe a data da operação.", "UF da outra parte: 2 letras."]);
  });
});

describe("profile version", () => {
  const draft: ProfileDraft = {
    validFrom: "2026-01-01", regime: "LucroPresumido", state: "SP", cityIbgeCode: "3550308", primaryCnae: "4781-4/00",
    secondaryCnaes: "4782-2/01; 4789-0/99", specialRegimes: "", benefits: "", reason: "Abertura",
  };

  it("validates and maps", () => {
    expect(profileDraftProblems(draft)).toEqual([]);
    expect(toProfileVersionRequest(draft)).toEqual({
      validFrom: "2026-01-01", regime: "LucroPresumido", state: "SP", cityIbgeCode: "3550308", primaryCnae: "4781400",
      secondaryCnaes: ["4782201", "4789099"], specialRegimes: [], benefits: [], reason: "Abertura",
    });
    expect(profileDraftProblems({ ...draft, cityIbgeCode: "355", primaryCnae: "47", secondaryCnaes: "1", reason: " " })).toEqual([
      "Código IBGE do município: 7 números.", "CNAE principal: 7 números.", "Cada CNAE secundário tem 7 números.", "Diga o motivo da nova versão.",
    ]);
  });

});

describe("ancillary-obligation assessment", () => {
  const base = { ...emptyCompliance(), periodFrom: "2026-01-01", periodTo: "2026-06-30", scope: "Estabelecimento — NF-e", assessedBy: "Contabilidade" };

  it("asks for period and scope, and for what was checked before it counts as verified", () => {
    expect(complianceProblems({ ...base, status: "Declared" })).toEqual([]);
    expect(complianceProblems({ ...base, status: "Verified" })).toEqual(["Para verificar, diga quais obrigações foram conferidas e com qual evidência."]);
    expect(complianceProblems({ ...base, status: "Declared", scope: " ", periodTo: "2025-12-31" })).toEqual([
      "O fim do período é anterior ao início.", "Informe o escopo (o que a avaliação cobre).",
    ]);
    expect(complianceProblems({ ...base, status: "UnderRemediation" })).toEqual(["Descreva a inconsistência ou o que está sendo regularizado."]);
  });

  it("maps the checked obligations and keeps PNCT as a recorded fact", () => {
    const req = toComplianceRequest({ ...base, status: "Verified", obligationsChecked: "NF-e com IBS/CBS\nNFC-e", evidence: " relatório ", pnctParticipation: true });
    expect(req).toMatchObject({ status: "Verified", obligationsChecked: ["NF-e com IBS/CBS", "NFC-e"], evidence: "relatório", pnctParticipation: true, notes: null });
  });
});

describe("helpers", () => {
  it("formats dates without timezone shifts and splits lists", () => {
    expect(formatIsoDate("2026-01-01")).toBe("01/01/2026");
    expect(formatIsoDate(null)).toBe("—");
    expect(splitList("a, b;\nc,,")).toEqual(["a", "b", "c"]);
  });

  it("recognizes the missing-profile refusal", () => {
    expect(isProfileMissing(new ApiError(422, "Sem perfil", "tax_profile_missing"))).toBe(true);
    expect(isProfileMissing(new ApiError(422, "Outro", "tax_opportunity_state"))).toBe(false);
    expect(isProfileMissing(new Error("x"))).toBe(false);
  });
});

describe("simulation scenarios and supplier offers", () => {
  it("keeps baseline facts when a scenario leaves them empty", () => {
    const s = { ...emptyScenario("Lucro Presumido"), regime: "LucroPresumido" as const, assumeCreditable: true };
    expect(scenarioProblems(s, 1)).toEqual([]);
    expect(toScenarioRequest(s)).toEqual({
      name: "Lucro Presumido", regime: "LucroPresumido", referenceDate: null, direction: null, counterpartyRegime: null,
      lines: null, convertConditionalDiscounts: false, rateHypotheses: null, assumeCreditable: true,
    });
  });

  it("maps and validates rate hypotheses", () => {
    const s = { ...emptyScenario("2027"), hypotheses: [{ ...emptyHypothesis(), ratePercent: "8,8", label: "Alíquota de referência estimada" }] };
    expect(toScenarioRequest(s).rateHypotheses).toEqual([{ tax: "Cbs", ratePercent: 8.8, label: "Alíquota de referência estimada" }]);
    expect(scenarioProblems({ ...s, name: "", hypotheses: [{ ...emptyHypothesis(), ratePercent: "120" }] }, 2)).toEqual([
      "Cenário 2: dê um nome.", "Cenário 2: alíquota hipotética entre 0 e 100%.", "Cenário 2: diga de onde vem a alíquota hipotética.",
    ]);
    expect(simulationProblems("", { ...emptyOperation("2026-10-26"), lines: [line()] }, [])).toEqual(["Dê um nome à simulação.", "De 1 a 10 cenários."]);
  });

  it("needs 2 to 10 named offers and maps them", () => {
    const a = { ...emptyOffer(), supplier: "A", lines: [line()] };
    const b = { ...emptyOffer(), supplier: "", regime: "SimplesNacional" as const, lines: [line({ grossAmount: "" })] };
    expect(offersProblems([a])).toEqual(["Compare de 2 a 10 fornecedores."]);
    expect(offersProblems([a, b])).toEqual(["Fornecedor 2: informe o nome.", "Fornecedor 2 — Linha 1: informe o valor da linha."]);
    const req = toCompareRequest("2026-10-26", [a, { ...b, supplier: "B", lines: [line()] }], false);
    expect(req.offers.map((o) => [o.supplier, o.supplierRegime])).toEqual([["A", null], ["B", "SimplesNacional"]]);
    expect(req.rateHypotheses).toBeNull();
  });
});

describe("credits", () => {
  it("validates and maps a credit registration", () => {
    const d = { ...emptyCredit("2026-10-01"), originalAmount: "10,00", potentialAmount: "9,50", potentialBasis: "Insumo da atividade", supplierDocument: "11.222.333/0001-81" };
    expect(creditProblems(d)).toEqual([]);
    expect(toCreditRequest(d)).toMatchObject({ tax: "Cbs", sourceDocumentKey: null, supplierDocument: "11222333000181", originalAmount: 10, potentialAmount: 9.5, expiresOn: null });
    expect(creditProblems({ ...d, sourceDocumentKey: "123", potentialAmount: "11", expiresOn: "2026-09-01" })).toEqual([
      "A chave de acesso tem 44 números.", "O vencimento é anterior à aquisição.", "O crédito potencial não passa do imposto destacado.",
    ]);
  });

  it("asks only what each movement needs", () => {
    const base = { movement: "Use" as const, amount: "5", reason: "", obligationTax: "Cbs" as const, obligationReference: "", onDate: "" };
    expect(movementProblems(base)).toEqual(["Informe a obrigação (ex.: apuração 10/2026)."]);
    expect(movementProblems({ ...base, movement: "Block", amount: "" })).toEqual(["Informe o valor (maior que zero).", "Diga o motivo."]);
    expect(movementProblems({ ...base, movement: "Expire", amount: "" })).toEqual([]);
    expect(toMovementRequest({ ...base, obligationReference: "10/2026" })).toEqual({ movement: "Use", amount: 5, reason: null, obligationTax: "Cbs", obligationReference: "10/2026", onDate: null });
    expect(toMovementRequest({ ...base, movement: "Expire", amount: "" })).toMatchObject({ amount: null, obligationTax: null, obligationReference: null });
    expect(movementProblems({ ...base, movement: "StartRequirementsReview", amount: "" })).toEqual([]);
    expect(movementProblems({ ...base, movement: "ConfirmToBeAppropriated", amount: "5" })).toEqual(["Diga a base da confirmação dos requisitos."]);
    expect(movementProblems({ ...base, movement: "Discard", amount: "" })).toEqual(["Diga o motivo."]);
  });
});
