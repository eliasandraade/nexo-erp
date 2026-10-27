import { describe, it, expect, vi, beforeEach } from "vitest";
import { apiClient } from "@/services/api-client";
import * as api from "./tax.api";

vi.mock("@/services/api-client", () => ({ apiClient: { get: vi.fn(), post: vi.fn() } }));

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);

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
    api.listTaxCalculations(20);
    api.listTaxSimulations();
    api.listTaxCredits();
    api.getTaxCreditTotals();
    api.getSplitProvider();
    api.listSplitExpectations("c 1");
    api.listSplitExpectations();

    expect(get.mock.calls.map((c) => c[0])).toEqual([
      "/tax/overview", "/tax/profile", "/tax/rules", "/tax/legal-sources", "/tax/calculations?take=20", "/tax/simulations",
      "/tax/credits", "/tax/credits/totals", "/tax/split/provider",
      expect.stringMatching(/^\/tax\/split\/expectations\?calculationId=c(%20|\+)1/), expect.stringMatching(/^\/tax\/split\/expectations/),
    ]);
  });

  it("filters opportunities by status only when one is chosen", () => {
    api.listTaxOpportunities("Blocked", 200);
    api.listTaxOpportunities();
    expect(get.mock.calls[0][0]).toMatch(/^\/tax\/opportunities\?.*status=Blocked/);
    expect(get.mock.calls[0][0]).toContain("take=200");
    expect(get.mock.calls[1][0]).not.toContain("status=");
  });

  it("sends the workflow actions with the bodies the server validates", () => {
    api.startOpportunityReview("o1");
    api.answerRequirement("o1", "r1", false, "depende de pagamento");
    api.addOpportunityEvidence("o1", "Contrato novo", null);
    api.blockOpportunity("o1", "DocumentMismatch", "motivo");
    api.rejectOpportunity("o1", "não vale");
    api.approveOpportunity("o1", true);

    expect(post.mock.calls).toEqual([
      ["/tax/opportunities/o1/review", {}],
      ["/tax/opportunities/o1/requirements/r1", { met: false, note: "depende de pagamento" }],
      ["/tax/opportunities/o1/evidence", { description: "Contrato novo", documentReference: null }],
      ["/tax/opportunities/o1/block", { structure: "DocumentMismatch", reason: "motivo" }],
      ["/tax/opportunities/o1/reject", { reason: "não vale" }],
      ["/tax/opportunities/o1/approve", { acknowledgeHighRisk: true }],
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
});
