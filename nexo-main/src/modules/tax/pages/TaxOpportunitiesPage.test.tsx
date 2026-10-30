import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { renderTax } from "../test/render";
import TaxOpportunitiesPage from "./TaxOpportunitiesPage";
import * as api from "../api/tax.api";
import { ApiError } from "@/services/api-client";

vi.mock("../api/tax.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tax.api")>()),
  listTaxOpportunities: vi.fn(),
}));

globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;
Element.prototype.scrollIntoView ??= () => {};
Element.prototype.hasPointerCapture ??= () => false;
Element.prototype.releasePointerCapture ??= () => {};

const summary = (over: Partial<api.TaxOpportunitySummaryDto>): api.TaxOpportunitySummaryDto => ({
  id: "op-1", type: "CalculatedTaxReduction", impactNature: "CalculatedTaxReduction", status: "InReview", title: "Conceder o desconto como incondicional",
  estimatedImpact: 0.5, risk: "Low", confidence: 90, createdAt: "2026-10-27T11:00:00Z", ...over,
});
const items = [
  summary({ assignedToName: "Gerente Exemplo", requirementsTotal: 3, requirementsMet: 1 }),
  summary({ id: "op-2", title: "Adiar o recolhimento", type: "TaxDeferral", impactNature: "Deferral", estimatedImpact: 900, risk: "Medium", requirementsTotal: 2 }),
  summary({ id: "op-3", title: "Estrutura bloqueada", status: "Blocked", estimatedImpact: null, risk: "High" }),
];
const page = (list: api.TaxOpportunitySummaryDto[], total = list.length): api.Page<api.TaxOpportunitySummaryDto> => ({ items: list, total, skip: 0, take: 25 });
const lastFilter = () => vi.mocked(api.listTaxOpportunities).mock.lastCall?.[0];

async function pick(name: string, option: string) {
  fireEvent.click(screen.getByRole("combobox", { name }));
  fireEvent.click(await screen.findByRole("option", { name: option }));
}

describe("TaxOpportunitiesPage", { timeout: 30_000 }, () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.listTaxOpportunities).mockResolvedValue(page(items));
  });
  afterEach(cleanup);

  it("lists each opportunity with its nature, impact, risk, requirements and responsible — a reduction is never a saving", async () => {
    renderTax(<TaxOpportunitiesPage />);

    const link = await screen.findByRole("link", { name: "Conceder o desconto como incondicional" });
    expect(link).toHaveAttribute("href", "/tributos/oportunidades/op-1");
    const row = link.closest("tr")!;
    expect(row).toHaveTextContent("responsável: Gerente Exemplo");
    expect(row).toHaveTextContent("1/3 atendidos");
    expect(row).toHaveTextContent("90%");
    expect(within(row).getByText("Redução do IBS/CBS calculado — não é economia")).toBeInTheDocument();
    expect(screen.getByText("Adiar o recolhimento").closest("tr")).toHaveTextContent("0/2 atendidos");
    const blocked = screen.getByText("Estrutura bloqueada").closest("tr")!;
    expect(blocked).toHaveTextContent("—");
    expect(blocked).not.toHaveTextContent("responsável");
    expect(screen.queryByText("Economia")).not.toBeInTheDocument();
    expect(lastFilter()).toEqual({ status: "", risk: "", type: "", search: "", skip: 0, take: 25 });
  });

  it("totals the impact by nature on the page, never across natures nor with blocked ones", async () => {
    renderTax(<TaxOpportunitiesPage />);

    const intro = await screen.findByText(/Impacto estimado por natureza, nesta página/);
    const totals = intro.parentElement!.querySelector("ul")!;
    expect(within(totals).getAllByRole("listitem")).toHaveLength(2);
    expect(within(totals).getByText("R$ 0,50")).toBeInTheDocument();
    expect(within(totals).getByText("R$ 900,00")).toBeInTheDocument();
    expect(screen.queryByText("R$ 900,50")).not.toBeInTheDocument();
  });

  it("filters by search, status, risk, type, open and own, going back to the first page", async () => {
    vi.mocked(api.listTaxOpportunities).mockResolvedValue(page(items, 60));
    renderTax(<TaxOpportunitiesPage />);

    await screen.findByText("Adiar o recolhimento");
    fireEvent.click(screen.getByText("2"));
    await waitFor(() => expect(lastFilter()).toMatchObject({ skip: 25 }));

    fireEvent.change(screen.getByLabelText("Buscar"), { target: { value: "desconto" } });
    await waitFor(() => expect(lastFilter()).toMatchObject({ search: "desconto", skip: 0 }));
    await pick("Situação", "Em análise");
    await waitFor(() => expect(lastFilter()).toMatchObject({ status: "InReview" }));
    await pick("Risco", "Risco alto");
    await waitFor(() => expect(lastFilter()).toMatchObject({ risk: "High" }));
    await pick("Tipo", "Adiamento de imposto");
    await waitFor(() => expect(lastFilter()).toMatchObject({ type: "TaxDeferral" }));
    fireEvent.click(screen.getByRole("checkbox", { name: "Só em aberto" }));
    await waitFor(() => expect(lastFilter()).toMatchObject({ open: true }));
    fireEvent.click(screen.getByRole("checkbox", { name: "Sob minha responsabilidade" }));
    await waitFor(() => expect(lastFilter()).toMatchObject({ mine: true }));

    fireEvent.click(screen.getByRole("checkbox", { name: "Só em aberto" }));
    await waitFor(() => expect(lastFilter()?.open).toBeUndefined());
    await pick("Situação", "Todas");
    await pick("Risco", "Todos");
    await pick("Tipo", "Todos");
    await waitFor(() => expect(lastFilter()).toEqual({ status: "", risk: "", type: "", search: "desconto", skip: 0, take: 25, open: undefined, mine: true }));
  });

  it("tells an empty list apart from an empty filter result", async () => {
    vi.mocked(api.listTaxOpportunities).mockResolvedValue(page([]));
    renderTax(<TaxOpportunitiesPage />);
    expect(await screen.findByText("Nenhuma oportunidade")).toBeInTheDocument();
    expect(screen.getByText("Elas aparecem quando um cálculo encontra uma alternativa permitida.")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("checkbox", { name: "Sob minha responsabilidade" }));
    expect(await screen.findByText("Nenhuma oportunidade com estes filtros")).toBeInTheDocument();
    expect(screen.getByText("Ajuste os filtros para ver outras.")).toBeInTheDocument();
  });

  it("says why the opportunities cannot be loaded", async () => {
    vi.mocked(api.listTaxOpportunities).mockRejectedValue(new ApiError(403, "proibido"));
    renderTax(<TaxOpportunitiesPage />);
    expect(await screen.findByText("Seu perfil não pode ver as oportunidades. Peça acesso à diretoria.")).toBeInTheDocument();
  });
});
