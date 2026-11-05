import { describe, it, expect, vi, afterEach } from "vitest";
import { screen, cleanup, fireEvent } from "@testing-library/react";
import { useLocation } from "react-router-dom";
import { renderTax } from "@/modules/tax/test/render";
import PlatformTaxPage from "./PlatformTaxPage";

// Each section has its own tests; here only the tab routing matters.
vi.mock("./tax/TaxModuleSection", () => ({ TaxModuleSection: () => <p>seção estado</p> }));
vi.mock("./tax/TaxPilotSection", () => ({ TaxPilotSection: () => <p>seção piloto</p> }));
vi.mock("./tax/TaxRulesSection", () => ({ TaxRulesSection: () => <p>seção regras</p> }));
vi.mock("./tax/TaxReferenceSections", () => ({
  TaxSourcesSection: () => <p>seção fontes</p>,
  TaxClassificationsSection: () => <p>seção classificação</p>,
  TaxObligationsSection: () => <p>seção obrigações</p>,
  TaxRetentionSection: () => <p>seção retenção</p>,
  TaxDocumentsSection: () => <p>seção documentos</p>,
  TaxAuditSection: () => <p>seção auditoria</p>,
}));

function Search() {
  return <output data-testid="search">{useLocation().search}</output>;
}

function renderPage(route: string) {
  return renderTax(<><PlatformTaxPage /><Search /></>, { path: "/plataforma/tributario", route });
}

describe("PlatformTaxPage", () => {
  afterEach(cleanup);

  it("opens on the module state when no tab is asked for, and lists every tab", () => {
    renderPage("/plataforma/tributario");

    expect(screen.getByRole("heading", { name: "Inteligência tributária" })).toBeInTheDocument();
    expect(screen.getByText("seção estado")).toBeInTheDocument();
    expect(screen.getAllByRole("tab").map((t) => t.textContent)).toEqual([
      "Estado e ativação", "Piloto", "Regras e revisões", "Fontes", "Classificação", "Obrigações", "Retenção", "Documentos", "Recusas e auditoria",
    ]);
    expect(screen.getByRole("tab", { name: "Estado e ativação" })).toHaveAttribute("aria-selected", "true");
  });

  it("opens the tab named in ?aba=", () => {
    renderPage("/plataforma/tributario?aba=regras");

    expect(screen.getByText("seção regras")).toBeInTheDocument();
    expect(screen.queryByText("seção estado")).not.toBeInTheDocument();
  });

  it("falls back to the module state for an unknown ?aba=", () => {
    renderPage("/plataforma/tributario?aba=inexistente");

    expect(screen.getByText("seção estado")).toBeInTheDocument();
  });

  it("switches tab and records it in the search params", () => {
    renderPage("/plataforma/tributario");

    fireEvent.mouseDown(screen.getByRole("tab", { name: "Recusas e auditoria" }));

    expect(screen.getByText("seção auditoria")).toBeInTheDocument();
    expect(screen.queryByText("seção estado")).not.toBeInTheDocument();
    expect(screen.getByTestId("search")).toHaveTextContent("?aba=auditoria");
  });
});
