import { describe, it, expect, vi, afterEach } from "vitest";
import { screen, cleanup, fireEvent } from "@testing-library/react";
import { renderTax } from "../test/render";
import TaxRulesPage from "./TaxRulesPage";
import * as api from "../api/tax.api";

vi.mock("../api/tax.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tax.api")>()),
  listTaxRules: vi.fn(),
  listLegalSources: vi.fn(),
}));

const rule: api.TaxRuleDto = {
  id: "r1", key: "CBS.RATE.2026-TEST", name: "CBS de 2026 (alíquota de teste)", description: "CBS de 0,9% em 2026.",
  versions: [{
    id: "v1", version: 1, status: "Active", validFrom: "2026-01-01", validTo: "2026-12-31", priority: 10,
    conditions: [], result: { kind: "rate" }, contentHash: "e2de5c9c", origin: "seed:tax-catalog-v1", createdBy: "Orken (catálogo inicial)",
    notes: null, activatedAt: "2026-10-26T00:00:00Z", legalSourceCodes: ["LC214-2025-ART346"], createdAt: "2026-10-26T00:00:00Z",
    professionalReview: "Pending", professionalReviewedBy: null, professionalReviewedAt: null, professionalReviewNotes: null,
  }],
};

const source: api.TaxLegalSourceDto = {
  id: "s1", code: "LC214-2025-ART346", kind: "ComplementaryLaw", title: "Lei Complementar nº 214, de 16 de janeiro de 2025",
  reference: "art. 346", url: "https://www.planalto.gov.br/ccivil_03/leis/lcp/Lcp214compilado.htm", excerpt: "Art. 346. …",
  publishedOn: "2025-01-16", textVerification: "Verified", verifiedAt: "2026-10-26T00:00:00Z", verifiedBy: "Conferência literal", notes: null,
};

describe("TaxRulesPage", () => {
  afterEach(cleanup);

  it("shows the technical status and the professional review as two separate states", async () => {
    vi.mocked(api.listTaxRules).mockResolvedValue([rule]);
    renderTax(<TaxRulesPage />);

    expect(await screen.findByText("Em vigor")).toBeInTheDocument();
    expect(screen.getByText("Revisão tributária pendente")).toBeInTheDocument();
    expect(screen.queryByText("Revisão tributária aprovada")).not.toBeInTheDocument();
  });

  it("shows who reviewed a rule and what they asked", async () => {
    vi.mocked(api.listTaxRules).mockResolvedValue([{
      ...rule,
      versions: [{ ...rule.versions[0], professionalReview: "ChangesRequired", professionalReviewedBy: "Contadora",
        professionalReviewedAt: "2026-11-01T12:00:00Z", professionalReviewNotes: "Ajustar a vigência." }],
    }]);
    renderTax(<TaxRulesPage />);

    expect(await screen.findByText("Revisão tributária pediu ajustes")).toBeInTheDocument();
    expect(screen.getByText(/Revisão tributária por Contadora .*: Ajustar a vigência\./)).toBeInTheDocument();
  });

  it("says a verified excerpt is only the official text, not a professional approval", async () => {
    vi.mocked(api.listTaxRules).mockResolvedValue([rule]);
    vi.mocked(api.listLegalSources).mockResolvedValue([source]);
    renderTax(<TaxRulesPage />);

    fireEvent.mouseDown(await screen.findByRole("tab", { name: "Fontes jurídicas" }));
    expect((await screen.findByText("Texto oficial conferido")).parentElement).toHaveAttribute("title", expect.stringContaining("não é parecer jurídico"));
    expect(screen.getByText(/a revisão da interpretação por um profissional tributário é um estado à parte/)).toBeInTheDocument();
  });
});
