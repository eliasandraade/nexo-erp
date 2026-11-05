import { describe, it, expect, vi, beforeEach } from "vitest";
import { apiClient } from "@/services/api-client";
import * as api from "./tax.api";

vi.mock("@/services/api-client", () => ({ apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), postForm: vi.fn() } }));

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);
const put = vi.mocked(apiClient.put);
const postForm = vi.mocked(apiClient.postForm);

/**
 * The client must address the routes the backend exposes (TaxIntelligence controllers). A route that
 * drifts — as happened when split expectations moved controller — would only fail at runtime.
 */
describe("tax API client routes", () => {
  beforeEach(() => vi.clearAllMocks());

  it("reads the overview, profile, catalogue and lists", () => {
    api.getTaxOverview();
    api.getTaxProfile();
    api.listTaxRules();
    api.listLegalSources();
    api.listTaxCalculations({ take: 20 });
    api.listTaxSimulations();
    api.listTaxCredits();
    api.getTaxCreditTotals();
    api.getSplitProvider();
    api.listSplitExpectations({ calculationId: "c 1" });
    api.listSplitExpectations();

    expect(get.mock.calls.map((c) => c[0])).toEqual([
      "/tax/overview", "/tax/profile", "/tax/rules", "/tax/legal-sources", "/tax/calculations?take=20", "/tax/simulations?skip=0&take=50",
      "/tax/credits", "/tax/credits/totals", "/tax/split/provider",
      expect.stringMatching(/^\/tax\/split\/expectations\?calculationId=c(%20|\+)1/), expect.stringMatching(/^\/tax\/split\/expectations/),
    ]);
  });

  it("filters opportunities by status only when one is chosen", () => {
    api.listTaxOpportunities({ status: "Blocked", take: 200 });
    api.listTaxOpportunities();
    expect(get.mock.calls[0][0]).toMatch(/^\/tax\/opportunities\?.*status=Blocked/);
    expect(get.mock.calls[0][0]).toContain("take=200");
    expect(get.mock.calls[1][0]).not.toContain("status=");
  });

  it("sends the workflow actions with the bodies the server validates", () => {
    api.startOpportunityReview("o1");
    api.answerRequirement("o1", "r1", false, "depende de pagamento");
    api.addOpportunityEvidence("o1", { description: "Contrato novo", documentReference: null, type: "CommercialInstrument", requirementId: "r1", notes: null });
    api.reviewOpportunityEvidence("o1", "e1", false, "sem assinatura");
    api.verifyOpportunityExecution("o1", { outcome: "Verified", fiscalDocumentReference: "chave", authorizationProtocol: null, notes: null });
    api.blockOpportunity("o1", "DocumentMismatch", "motivo");
    api.rejectOpportunity("o1", "não vale");
    api.approveOpportunity("o1", true);
    api.approveOpportunity("o1", false, "  Diretoria de uma pessoa ");

    expect(post.mock.calls).toEqual([
      ["/tax/opportunities/o1/review", {}],
      ["/tax/opportunities/o1/requirements/r1", { met: false, note: "depende de pagamento" }],
      ["/tax/opportunities/o1/evidence", { description: "Contrato novo", documentReference: null, type: "CommercialInstrument", requirementId: "r1", notes: null }],
      ["/tax/opportunities/o1/evidence/e1/review", { accepted: false, notes: "sem assinatura" }],
      ["/tax/opportunities/o1/execution", { outcome: "Verified", fiscalDocumentReference: "chave", authorizationProtocol: null, notes: null }],
      ["/tax/opportunities/o1/block", { structure: "DocumentMismatch", reason: "motivo" }],
      ["/tax/opportunities/o1/reject", { reason: "não vale" }],
      ["/tax/opportunities/o1/approve", { acknowledgeHighRisk: true, selfApprovalJustification: null }],
      ["/tax/opportunities/o1/approve", { acknowledgeHighRisk: false, selfApprovalJustification: "Diretoria de uma pessoa" }],
    ]);
  });

  it("addresses calculations, credits and split payment", () => {
    api.calculateSale("s/1");
    api.verifyTaxCalculation("c1");
    api.createSplitExpectations("c1", "Simplified");
    api.moveTaxCredit("k1", { movement: "Use", amount: 10, reason: null, obligationTax: "Cbs", obligationReference: "obr", onDate: null });
    api.recordSplitSettlement("e1", 7.56, "psp-1", "2026-10-27T12:00:00Z");
    api.reverseSplitSettlement("s1", "duplicado");

    expect(post.mock.calls.map((c) => c[0])).toEqual([
      "/tax/calculations/from-sale/s%2F1",
      "/tax/calculations/c1/verify",
      "/tax/calculations/c1/split-expectations",
      "/tax/credits/k1/movements",
      "/tax/split/expectations/e1/settlements",
      "/tax/split/settlements/s1/reverse",
    ]);
    expect(post.mock.calls[2][1]).toEqual({ procedure: "Simplified" });
    expect(post.mock.calls[5][1]).toEqual({ reason: "duplicado" });
  });

  it("pages the lists and leaves out empty filters", () => {
    api.listTaxOpportunities({ status: "", risk: "High", open: true, mine: false, search: "", skip: 25, take: 25 });
    api.listTaxCredits({ tax: "Cbs", stage: "", expiring: true });
    api.listSplitExpectations({ status: "Insufficient", minAgeDays: 30 });
    const [opps, credits, split] = get.mock.calls.map((c) => c[0] as string);
    expect(opps).toBe("/tax/opportunities?risk=High&open=true&mine=false&skip=25&take=25");
    expect(credits).toBe("/tax/credits?tax=Cbs&expiring=true");
    expect(split).toBe("/tax/split/expectations?status=Insufficient&minAgeDays=30");
    expect(api.splitCsvUrl({ calculationId: "c1", status: "Excess", skip: 50 })).toBe("/tax/split/expectations/export?status=Excess");
    expect(api.decisionExportUrl("d1", true)).toBe("/tax/decisions/d1/export?includeFiles=true");
    expect(api.evidenceFileUrl("f1")).toBe("/tax/evidence-files/f1/content");
    expect(api.simulationCsvUrl("s1")).toBe("/tax/simulations/s1/export");
  });

  it("sends evidence files as multipart and never a client-chosen name for the assignee", () => {
    const file = new File(["%PDF-1.7"], "proposta.pdf", { type: "application/pdf" });
    api.uploadOpportunityEvidence("o1", file, { description: "Proposta", documentReference: "P-1", type: "CommercialInstrument", requirementId: "r1", notes: null });
    api.replaceOpportunityEvidence("o1", "e1", "Proposta v2", null, file);
    api.replaceOpportunityEvidence("o1", "e1", "Só a descrição", "REF");
    api.assignOpportunity("o1", "u1");
    api.assignOpportunity("o1", null);
    api.revokeOpportunityEvidence("o1", "e1", "documento errado");

    const [upload, replace, replaceNoFile] = postForm.mock.calls;
    expect(upload[0]).toBe("/tax/opportunities/o1/evidence/files");
    const form = upload[1] as FormData;
    expect(form.get("file")).toBe(file);
    expect(form.get("requirementId")).toBe("r1");
    expect(form.get("documentReference")).toBe("P-1");
    expect(form.has("notes")).toBe(false);
    expect(replace[0]).toBe("/tax/opportunities/o1/evidence/e1/replace");
    expect((replace[1] as FormData).get("file")).toBe(file);
    expect((replaceNoFile[1] as FormData).has("file")).toBe(false);
    expect((replaceNoFile[1] as FormData).get("documentReference")).toBe("REF");
    expect(post.mock.calls).toEqual([
      ["/tax/opportunities/o1/assign", { userId: "u1" }],
      ["/tax/opportunities/o1/assign", { userId: null }],
      ["/tax/opportunities/o1/evidence/e1/revoke", { reason: "documento errado" }],
    ]);
  });

  it("addresses policies, classification and obligations", () => {
    api.getMateriality();
    api.listRetentionPolicies();
    api.getRetentionPreview();
    api.listItemClassifications("café", true, 0, 50);
    api.listClassificationCodes("01");
    api.listObligations();
    api.getOpportunityTimeline("o1");
    api.configureMateriality({ threshold: 10000, currency: "BRL", effectiveFrom: "2026-11-01", effectiveTo: null, rationale: "ata", decisionReference: "ATA-1" });
    api.setItemClassification("p1", { itemKind: "Good", ncm: "22030000", nbs: null, cClassTrib: "000001", notes: null });

    expect(get.mock.calls.map((c) => c[0])).toEqual([
      "/tax/materiality", "/tax/retention-policies", "/tax/retention-policies/preview",
      "/tax/classifications/items?search=caf%C3%A9&onlyMissing=true&skip=0&take=50", "/tax/classifications/codes?search=01&take=50",
      "/tax/obligations", "/tax/opportunities/o1/timeline",
    ]);
    expect(put.mock.calls.map((c) => c[0])).toEqual(["/tax/materiality", "/tax/classifications/items/p1"]);
  });
});
