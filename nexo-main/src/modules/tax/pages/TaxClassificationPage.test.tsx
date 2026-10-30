import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { toast } from "sonner";
import { renderTax } from "../test/render";
import TaxClassificationPage from "./TaxClassificationPage";
import * as api from "../api/tax.api";
import { ApiError } from "@/services/api-client";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("../api/tax.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tax.api")>()),
  listItemClassifications: vi.fn(),
  listClassificationCodes: vi.fn(),
  setItemClassification: vi.fn(),
}));

globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;
Element.prototype.scrollIntoView ??= () => {};
Element.prototype.hasPointerCapture ??= () => false;
Element.prototype.releasePointerCapture ??= () => {};

const item = (over: Partial<api.TaxItemClassificationDto>): api.TaxItemClassificationDto => ({
  id: null, productId: "p1", productName: "Produto Exemplo A", productNcm: "22030000", itemKind: null, ncm: null, nbs: null, cClassTrib: null,
  catalogStatus: null, notes: null, setByName: null, updatedAt: null, ...over,
});
const unclassified = item({});
const outOfCatalogue = item({ id: "c2", productId: "p2", productName: "Produto Exemplo B", itemKind: "Good", ncm: "21069090", cClassTrib: "999999" });
const inactive = item({ id: "c3", productId: "p3", productName: "Serviço Exemplo C", productNcm: null, itemKind: "Service", nbs: "1.0101", cClassTrib: "200030",
  catalogStatus: "Inactive", setByName: "Gerente Exemplo", updatedAt: "2026-10-20T12:00:00Z", notes: "conferir" });
const active = item({ id: "c4", productId: "p4", productName: "Produto Exemplo D", itemKind: "Good", ncm: "22021000", cClassTrib: "000001", catalogStatus: "Active", setByName: "Gerente Exemplo" });

const code = (over: Partial<api.TaxClassificationCodeDto>): api.TaxClassificationCodeDto => ({
  id: "k1", kind: "CClassTrib", code: "000001", description: "Tributação integral", source: "Tabela oficial cClassTrib", sourceVersion: "2026-09",
  validFrom: "2026-01-01", validTo: null, status: "Active", professionalReview: "Pending", professionalReviewNotes: null, notes: null, importBatchId: null,
  sourceDocumentId: null, createdByName: "Operador", createdAt: "2026-09-01T12:00:00Z", statusChangedByName: null, statusChangedAt: null, ...over,
});
const codes = [code({}), code({ id: "k2", code: "200030", description: "Redução de 30%", sourceVersion: null, professionalReview: "Approved" })];

const page = <T,>(items: T[], total = items.length): api.Page<T> => ({ items, total, skip: 0, take: 50 });

describe("TaxClassificationPage", { timeout: 30_000 }, () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.listItemClassifications).mockResolvedValue(page([unclassified, outOfCatalogue, inactive, active]));
    vi.mocked(api.listClassificationCodes).mockResolvedValue(page(codes));
  });
  afterEach(cleanup);

  it("tells an unclassified product apart from a code outside the catalogue and from the catalogue status", async () => {
    renderTax(<TaxClassificationPage />);

    const rowOf = async (name: string) => (await screen.findByText(name)).closest("tr")!;
    const a = await rowOf("Produto Exemplo A");
    expect(within(a).getByText("Sem classificação")).toBeInTheDocument();
    expect(a).toHaveTextContent("22030000");
    expect(within(a).getByRole("button", { name: "Classificar" })).toBeInTheDocument();

    const b = await rowOf("Produto Exemplo B");
    expect(within(b).getByText("Fora do catálogo")).toBeInTheDocument();
    expect(within(b).getByText("Mercadoria")).toBeInTheDocument();
    expect(within(b).getByRole("button", { name: "Alterar" })).toBeInTheDocument();

    const c = await rowOf("Serviço Exemplo C");
    expect(within(c).getByText("Inativo")).toBeInTheDocument();
    expect(within(c).queryByText("Fora do catálogo")).not.toBeInTheDocument();
    expect(c).toHaveTextContent("— / 1.0101");
    expect(c).toHaveTextContent(/Gerente Exemplo · /);

    const d = await rowOf("Produto Exemplo D");
    expect(within(d).getByText("Ativo")).toBeInTheDocument();
    expect(d).toHaveTextContent("Gerente Exemplo");
    expect(listItems()).toEqual(["", false, 0, 50]);
  });

  it("searches, filters the unclassified and pages through the server", async () => {
    vi.mocked(api.listItemClassifications).mockResolvedValue(page([unclassified], 120));
    renderTax(<TaxClassificationPage />);

    await screen.findByText("Produto Exemplo A");
    fireEvent.click(screen.getByText("2"));
    await waitFor(() => expect(listItems()).toEqual(["", false, 50, 50]));

    fireEvent.change(screen.getByLabelText("Buscar produto"), { target: { value: "cerveja" } });
    await waitFor(() => expect(listItems()).toEqual(["cerveja", false, 0, 50]));
    fireEvent.click(screen.getByRole("checkbox", { name: "Só sem classificação" }));
    await waitFor(() => expect(listItems()).toEqual(["cerveja", true, 0, 50]));
  });

  it("says when there is no product, or none with the filters", async () => {
    vi.mocked(api.listItemClassifications).mockResolvedValue(page([]));
    renderTax(<TaxClassificationPage />);
    expect(await screen.findByText("Nenhum produto cadastrado")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("checkbox", { name: "Só sem classificação" }));
    expect(await screen.findByText("Nenhum produto com estes filtros")).toBeInTheDocument();
  });

  it("says why the classifications cannot be loaded", async () => {
    vi.mocked(api.listItemClassifications).mockRejectedValue(new ApiError(403, "proibido"));
    renderTax(<TaxClassificationPage />);
    expect(await screen.findByText("Seu perfil não pode ver as classificações. Peça acesso à diretoria.")).toBeInTheDocument();
  });

  it("classifies a good with a code picked from the catalogue", async () => {
    vi.mocked(api.setItemClassification).mockResolvedValue({ ...unclassified, cClassTrib: "000001", catalogStatus: "Active" });
    renderTax(<TaxClassificationPage />);

    fireEvent.click(within((await screen.findByText("Produto Exemplo A")).closest("tr")!).getByRole("button", { name: "Classificar" }));
    const dialog = await screen.findByRole("dialog");
    expect(dialog).toHaveTextContent("Classificar “Produto Exemplo A”");
    expect(within(dialog).getByLabelText("NCM")).toHaveValue("22030000");
    expect(within(dialog).getByText("Digite ou escolha da lista.")).toBeInTheDocument();

    const list = within(dialog).getByRole("listbox", { name: "Códigos do catálogo" });
    const option = await within(list).findByRole("option", { name: /000001 — Tributação integral/ });
    expect(option).toHaveTextContent("(Revisão profissional pendente)");
    fireEvent.click(option);
    expect(option).toHaveAttribute("aria-selected", "true");
    expect(within(dialog).getByLabelText("cClassTrib escolhido")).toHaveValue("000001");
    expect(within(dialog).getByText("Tabela oficial cClassTrib · 2026-09")).toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText("Observação (opcional)"), { target: { value: " conferido com o contador " } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar classificação" }));

    await waitFor(() => expect(api.setItemClassification).toHaveBeenCalledWith("p1", {
      itemKind: "Good", ncm: "22030000", nbs: null, cClassTrib: "000001", notes: "conferido com o contador",
    }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Classificação registrada."));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("asks a service for its NBS and warns when the code saved is not active in the catalogue", async () => {
    vi.mocked(api.setItemClassification).mockResolvedValue({ ...inactive, cClassTrib: "200030", catalogStatus: "Inactive" });
    renderTax(<TaxClassificationPage />);

    fireEvent.click(within((await screen.findByText("Produto Exemplo A")).closest("tr")!).getByRole("button", { name: "Classificar" }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar classificação" }));
    expect(within(dialog).getByText("Escolha o cClassTrib.")).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole("combobox", { name: "Tipo" }));
    fireEvent.click(await screen.findByRole("option", { name: "Serviço" }));
    expect(within(dialog).queryByLabelText("NCM")).not.toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText("cClassTrib escolhido"), { target: { value: "200030" } });
    expect(within(dialog).getByText("Tabela oficial cClassTrib")).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar classificação" }));
    expect(within(dialog).getByText("Serviço: informe a NBS.")).toBeInTheDocument();
    expect(api.setItemClassification).not.toHaveBeenCalled();

    fireEvent.change(within(dialog).getByLabelText("NBS"), { target: { value: "1.0101" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar classificação" }));
    await waitFor(() => expect(api.setItemClassification).toHaveBeenCalledWith("p1", {
      // The NCM typed for the good is hidden now and never sent with the service.
      itemKind: "Service", ncm: null, nbs: "1.0101", cClassTrib: "200030", notes: null,
    }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith(
      "Classificação registrada — mas o código não está ativo no catálogo: a operação continua fora do recorte.",
    ));
  });

  it("drops the NBS when a service becomes a good", async () => {
    vi.mocked(api.setItemClassification).mockResolvedValue({ ...inactive, itemKind: "Good", nbs: null, ncm: "49019900", catalogStatus: "Active" });
    renderTax(<TaxClassificationPage />);

    fireEvent.click(within((await screen.findByText("Serviço Exemplo C")).closest("tr")!).getByRole("button", { name: "Alterar" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByLabelText("NBS")).toHaveValue("1.0101");
    fireEvent.click(within(dialog).getByRole("combobox", { name: "Tipo" }));
    fireEvent.click(await screen.findByRole("option", { name: "Mercadoria" }));
    expect(within(dialog).queryByLabelText("NBS")).not.toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText("NCM"), { target: { value: "4901.99.00" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar classificação" }));

    await waitFor(() => expect(api.setItemClassification).toHaveBeenCalledWith("p3", {
      itemKind: "Good", ncm: "4901.99.00", nbs: null, cClassTrib: "200030", notes: "conferir",
    }));
  });

  it("refuses an NCM without 8 digits, searches the catalogue and explains a refusal", async () => {
    vi.mocked(api.setItemClassification).mockRejectedValue(new ApiError(422, "Código desconhecido."));
    renderTax(<TaxClassificationPage />);

    fireEvent.click(within((await screen.findByText("Produto Exemplo B")).closest("tr")!).getByRole("button", { name: "Alterar" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByLabelText("cClassTrib escolhido")).toHaveValue("999999");
    fireEvent.change(within(dialog).getByLabelText("NCM"), { target: { value: "2106" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar classificação" }));
    expect(within(dialog).getByText("NCM: 8 números.")).toBeInTheDocument();
    expect(api.setItemClassification).not.toHaveBeenCalled();

    fireEvent.change(within(dialog).getByLabelText("Procurar no catálogo"), { target: { value: "redução" } });
    await waitFor(() => expect(api.listClassificationCodes).toHaveBeenLastCalledWith("redução"));

    fireEvent.change(within(dialog).getByLabelText("NCM"), { target: { value: "" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar classificação" }));
    await waitFor(() => expect(api.setItemClassification).toHaveBeenCalledWith("p2", expect.objectContaining({ ncm: null, cClassTrib: "999999" })));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Não foi possível concluir", { description: "Código desconhecido." }));
    expect(screen.getByRole("dialog")).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("says when the catalogue is empty or cannot be loaded", async () => {
    vi.mocked(api.listClassificationCodes).mockResolvedValueOnce(page([]));
    renderTax(<TaxClassificationPage />);

    fireEvent.click(within((await screen.findByText("Serviço Exemplo C")).closest("tr")!).getByRole("button", { name: "Alterar" }));
    let dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByLabelText("NBS")).toHaveValue("1.0101");
    expect(within(dialog).getByLabelText("Observação (opcional)")).toHaveValue("conferir");
    expect(await within(dialog).findByText("Nenhum código ativo encontrado. A plataforma ainda não importou o catálogo?")).toBeInTheDocument();
    fireEvent.keyDown(dialog, { key: "Escape" });
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());

    vi.mocked(api.listClassificationCodes).mockRejectedValueOnce(new Error("rede"));
    fireEvent.click(within(screen.getByText("Produto Exemplo D").closest("tr")!).getByRole("button", { name: "Alterar" }));
    dialog = await screen.findByRole("dialog");
    expect(await within(dialog).findByText("Não foi possível carregar o catálogo.")).toBeInTheDocument();
  });
});

function listItems() {
  return vi.mocked(api.listItemClassifications).mock.lastCall;
}
