import type { ReactElement } from "react";
import { render } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { TaxOpportunityDto } from "../api/tax.api";
import { ENGINE_SCENARIOS } from "./engineFixture";

/** Renders inside the providers the tax screens rely on (router links, React Query). */
export function renderTax(ui: ReactElement, { path = "/", route = "/" }: { path?: string; route?: string } = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[route]}>
        <Routes><Route path={path} element={ui} /></Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

/** An opportunity as the API returns it, built on the real engine scenarios. */
export function opportunity(over: Partial<TaxOpportunityDto> = {}): TaxOpportunityDto {
  return {
    id: "op-1", type: "CalculatedTaxReduction", impactNature: "CalculatedTaxReduction", status: "InReview",
    title: "Conceder o desconto como incondicional",
    summary: "R$ 50.00 de desconto concedido sob condição integram a base do IBS e da CBS.",
    detectorKey: "unconditional-discount@2", engineVersion: "orken-tax-engine/1.0.0", sourceCalculationId: "calc-1", estimatedImpact: 0.5, risk: "Low", confidence: 90,
    legalSourceCodes: ["LC214-2025-ART12-P2-III-P3"], caveats: "Muda a condição comercial.",
    blockedAs: null, blockReason: null, rejectionReason: null, reviewStartedByName: "Gerente", reviewedByName: "Gerente", reviewedAt: "2026-10-27T12:00:00Z",
    createdAt: "2026-10-27T11:00:00Z", professionalReviewPending: true,
    requirements: [
      { id: "r1", code: "on-document", text: "O desconto consta do documento fiscal da operação.", legalSourceCode: "LC214-2025-ART12-P2-III-P3",
        met: true, note: null, answeredByName: "Gerente", answeredAt: "2026-10-27T12:00:00Z" },
      { id: "r2", code: "no-later-event", text: "O desconto não depende de evento posterior.", legalSourceCode: "LC214-2025-ART12-P2-III-P3",
        met: null, note: null, answeredByName: null, answeredAt: null },
    ],
    evidence: [],
    current: ENGINE_SCENARIOS[0], proposed: ENGINE_SCENARIOS[1], decision: null,
    ...over,
  } as TaxOpportunityDto;
}
