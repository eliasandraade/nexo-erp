import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { PortalSlugSection, publicBookingUrl, normalizeSlug } from "./PortalSlugSection";

vi.mock("@/modules/stores/services/storesApi", () => ({
  checkSlugAvailability: vi.fn().mockResolvedValue({ available: true, normalized: "x" }),
  setPublicSlug: vi.fn().mockResolvedValue({ publicSlug: "x" }),
}));

afterEach(cleanup);

function renderWithClient(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("publicBookingUrl / normalizeSlug", () => {
  it("builds the /agendar/ url", () => {
    expect(publicBookingUrl("minha-clinica")).toBe("app.orken.com.br/agendar/minha-clinica");
  });
  it("normalizes accents and spaces", () => {
    expect(normalizeSlug("Minha Clínica  Nova!")).toBe("minha-clinica-nova");
  });
});

describe("PortalSlugSection", () => {
  it("with a saved slug shows the public link and a copy button", () => {
    renderWithClient(<PortalSlugSection storeId="s1" currentSlug="minha-clinica" />);
    expect(screen.getByText("app.orken.com.br/agendar/minha-clinica")).toBeInTheDocument();
    expect(screen.getByTitle("Copiar link")).toBeInTheDocument();
  });

  it("without a slug shows the CTA and no copy/link (never a broken link)", () => {
    renderWithClient(<PortalSlugSection storeId="s1" currentSlug={null} />);
    expect(screen.getByText(/Defina um endereço/)).toBeInTheDocument();
    expect(screen.queryByTitle("Copiar link")).not.toBeInTheDocument();
  });
});
