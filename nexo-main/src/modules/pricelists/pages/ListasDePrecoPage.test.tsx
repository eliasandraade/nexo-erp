import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";
import { apiClient } from "@/services/api-client";
import { polyfillRadix } from "@/modules/platform/test/aiFixtures";
import ListasDePrecoPage from "./ListasDePrecoPage";
import type { PriceListDetailDto, PriceListDto } from "../api/pricelists.api";

vi.mock("@/services/api-client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/services/api-client")>()),
  apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn(), patch: vi.fn() },
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

polyfillRadix();

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);
const put = vi.mocked(apiClient.put);
const del = vi.mocked(apiClient.delete);

const BASE = "/varejo/price-lists";
const LIST_URL = `${BASE}?includeInactive=true`;

const priceList = (over: Partial<PriceListDto> & Pick<PriceListDto, "id" | "name">): PriceListDto => ({
  description: null, isDefault: false, isActive: true, itemCount: 0, createdAt: "2026-10-01T12:00:00Z",
  validFrom: null, validUntil: null, priority: 0, ...over,
});
const detailOf = (l: PriceListDto, over: Partial<PriceListDetailDto> = {}): PriceListDetailDto => {
  const { itemCount: _itemCount, ...rest } = l;
  return { ...rest, items: [], customers: [], ...over };
};

/** What the server answers, changed by each test as the server would. */
let lists: PriceListDto[];
let details: Record<string, PriceListDetailDto>;

const products = [
  { id: "cafe", name: "Café 500g", code: "CAFE", salePrice: 18.9 },
  { id: "acucar", name: "Açúcar 1kg", code: "ACU", salePrice: 5.49 },
];
const customers = [
  { id: "cli1", name: "Mercadinho Bom Preço" },
  { id: "cli2", name: "Padaria Pão Quente" },
];

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(<QueryClientProvider client={client}><ListasDePrecoPage /></QueryClientProvider>);
}
const listLoads = () => get.mock.calls.filter(([url]) => url === LIST_URL).length;

function choose(label: string, option: RegExp) {
  fireEvent.keyDown(screen.getByLabelText(label), { key: "Enter" });
  fireEvent.keyDown(screen.getByRole("option", { name: option }), { key: "Enter" });
}

async function openList(name: string) {
  fireEvent.click(await screen.findByRole("button", { name }));
  return screen.findByDisplayValue(name);
}

beforeEach(() => {
  const padrao = priceList({ id: "l1", name: "Tabela padrão", isDefault: true, itemCount: 2 });
  const atacado = priceList({
    id: "l2", name: "Atacado", priority: 5, itemCount: 1,
    validFrom: "2026-11-01T03:00:00Z", validUntil: "2026-12-31T03:00:00Z",
  });
  const verao = priceList({ id: "l3", name: "Promoção de verão", isActive: false });
  lists = [padrao, atacado, verao];
  details = {
    l1: detailOf(padrao, { customers: null }),
    l2: detailOf(atacado, {
      items: [{ id: "i1", productId: "cafe", productName: "Café 500g", productCode: "CAFE", price: 15.9 }],
      customers: [{ customerId: "cli1", name: "Mercadinho Bom Preço" }],
    }),
    l3: detailOf(verao),
  };
  get.mockImplementation(async (url: string) => {
    if (url === LIST_URL) return structuredClone(lists);
    if (url === "/products") return structuredClone(products);
    if (url === "/customers") return structuredClone(customers);
    const id = url.startsWith(`${BASE}/`) ? url.slice(BASE.length + 1) : "";
    if (details[id]) return structuredClone(details[id]);
    throw new Error(`GET inesperado: ${url}`);
  });
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe("ListasDePrecoPage — the lists", () => {
  it("lists every price list with its default, inactive, validity and priority", async () => {
    renderPage();

    const padrao = (await screen.findByRole("button", { name: "Tabela padrão" })).closest("tr")!;
    expect(within(padrao).getByText("Padrão")).toBeInTheDocument();
    expect(within(padrao).getByText("Sem prazo")).toBeInTheDocument();

    const atacado = screen.getByRole("button", { name: "Atacado" }).closest("tr")!;
    expect(within(atacado).getByText("5")).toBeInTheDocument();
    expect(within(atacado).getByText(/a .*2026/)).toBeInTheDocument();

    const verao = screen.getByRole("button", { name: "Promoção de verão" }).closest("tr")!;
    expect(within(verao).getByText("Inativa")).toBeInTheDocument();
  });

  it("with no list it says sales use each product's own price", async () => {
    lists = [];
    renderPage();

    expect(await screen.findByText("Nenhuma lista de preço")).toBeInTheDocument();
    expect(screen.getByText("Sem listas, a venda usa o preço de cada produto.")).toBeInTheDocument();
  });

  it("the first list created becomes the default, and opens for editing", async () => {
    lists = [];
    post.mockImplementation(async (url: string, body: unknown) => {
      if (url !== BASE) throw new Error(`POST inesperado: ${url}`);
      const created = priceList({ id: "l9", name: "Varejo", isDefault: true });
      lists = [created];
      details.l9 = detailOf(created);
      return { ...created, body };
    });
    renderPage();
    await screen.findByText("Nenhuma lista de preço");

    fireEvent.change(screen.getByPlaceholderText("Ex.: Atacado, Promoção de verão"), { target: { value: "  Varejo  " } });
    fireEvent.click(screen.getByRole("button", { name: /Criar lista/ }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Lista criada."));
    expect(post).toHaveBeenCalledWith(BASE, { name: "Varejo", isDefault: true });
    expect(await screen.findByDisplayValue("Varejo")).toBeInTheDocument();
    expect(await screen.findByRole("button", { name: "Varejo" })).toBeInTheDocument();
    expect(listLoads()).toBeGreaterThan(1);
    expect(screen.getByPlaceholderText("Ex.: Atacado, Promoção de verão")).toHaveValue("");
  });

  it("a new list is not default when the company already has one", async () => {
    post.mockResolvedValueOnce(priceList({ id: "l4", name: "Atacado 2" }));
    details.l4 = detailOf(priceList({ id: "l4", name: "Atacado 2" }));
    renderPage();
    await screen.findByRole("button", { name: "Tabela padrão" });

    fireEvent.change(screen.getByPlaceholderText("Ex.: Atacado, Promoção de verão"), { target: { value: "Atacado 2" } });
    fireEvent.click(screen.getByRole("button", { name: /Criar lista/ }));

    await waitFor(() => expect(post).toHaveBeenCalledWith(BASE, { name: "Atacado 2", isDefault: false }));
  });

  it("does not create a list without a name, and shows the server's refusal", async () => {
    post.mockRejectedValueOnce(new Error("Já existe uma lista com esse nome."));
    renderPage();
    await screen.findByRole("button", { name: "Tabela padrão" });

    expect(screen.getByRole("button", { name: /Criar lista/ })).toBeDisabled();
    fireEvent.change(screen.getByPlaceholderText("Ex.: Atacado, Promoção de verão"), { target: { value: "Atacado" } });
    fireEvent.click(screen.getByRole("button", { name: /Criar lista/ }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Já existe uma lista com esse nome."));
  });

  it("when the lists cannot be loaded it says so and lets the operator try again", async () => {
    get.mockRejectedValueOnce(new Error("offline"));
    renderPage();

    expect(await screen.findByText("Não foi possível carregar as listas")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Tentar novamente/ }));
    expect(await screen.findByRole("button", { name: "Tabela padrão" })).toBeInTheDocument();
  });
});

describe("ListasDePrecoPage — editing a list", () => {
  it("saves name, validity and priority, sending the validity as whole local days", async () => {
    put.mockImplementation(async (url: string) => {
      if (url !== `${BASE}/l2`) throw new Error(`PUT inesperado: ${url}`);
      lists = lists.map((l) => (l.id === "l2" ? { ...l, name: "Atacado Plus", priority: 7 } : l));
      details.l2 = { ...details.l2, name: "Atacado Plus", priority: 7 };
      return lists[1];
    });
    renderPage();
    await openList("Atacado");

    fireEvent.change(screen.getByDisplayValue("Atacado"), { target: { value: " Atacado Plus " } });
    fireEvent.change(screen.getByLabelText("Vigência: início"), { target: { value: "2026-12-01" } });
    fireEvent.change(screen.getByLabelText("Vigência: fim"), { target: { value: "2026-12-24" } });
    fireEvent.change(screen.getByLabelText("Prioridade"), { target: { value: "7.9" } });
    fireEvent.click(screen.getByRole("button", { name: "Salvar" }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Lista atualizada."));
    expect(put).toHaveBeenCalledWith(`${BASE}/l2`, {
      name: "Atacado Plus",
      validFrom: new Date("2026-12-01T00:00:00").toISOString(),
      validUntil: new Date("2026-12-24T23:59:59").toISOString(),
      priority: 7, // the API takes an integer
    });
    expect(await screen.findByRole("button", { name: "Atacado Plus" })).toBeInTheDocument();
    expect(listLoads()).toBeGreaterThan(1);
  });

  it("clearing the validity sends no dates, so the list has no deadline", async () => {
    put.mockResolvedValueOnce(lists[1]);
    renderPage();
    await openList("Atacado");

    fireEvent.change(screen.getByLabelText("Vigência: início"), { target: { value: "" } });
    fireEvent.change(screen.getByLabelText("Vigência: fim"), { target: { value: "" } });
    fireEvent.click(screen.getByRole("button", { name: "Salvar" }));

    await waitFor(() => expect(put).toHaveBeenCalledWith(`${BASE}/l2`, {
      name: "Atacado", validFrom: null, validUntil: null, priority: 5,
    }));
  });

  it("makes a list the default one and reloads the lists", async () => {
    post.mockImplementation(async (url: string) => {
      if (url !== `${BASE}/l2/set-default`) throw new Error(`POST inesperado: ${url}`);
      lists = lists.map((l) => ({ ...l, isDefault: l.id === "l2" }));
      return lists[1];
    });
    renderPage();
    await openList("Atacado");

    fireEvent.click(screen.getByRole("button", { name: "Tornar padrão" }));

    await waitFor(() => expect(post).toHaveBeenCalledWith(`${BASE}/l2/set-default`, {}));
    const atacado = (await screen.findByRole("button", { name: "Atacado" })).closest("tr")!;
    await waitFor(() => expect(within(atacado).getByText("Padrão")).toBeInTheDocument());
  });

  it("deactivates an active list and reloads the lists", async () => {
    post.mockImplementation(async (url: string) => {
      if (url !== `${BASE}/l2/deactivate`) throw new Error(`POST inesperado: ${url}`);
      lists = lists.map((l) => (l.id === "l2" ? { ...l, isActive: false } : l));
      return lists[1];
    });
    renderPage();
    await openList("Atacado");

    fireEvent.click(screen.getByRole("button", { name: "Desativar" }));

    await waitFor(() => expect(post).toHaveBeenCalledWith(`${BASE}/l2/deactivate`, {}));
    const atacado = screen.getByRole("button", { name: "Atacado" }).closest("tr")!;
    await waitFor(() => expect(within(atacado).getByText("Inativa")).toBeInTheDocument());
  });

  it("an inactive list offers to activate it", async () => {
    post.mockResolvedValueOnce(lists[2]);
    renderPage();
    await openList("Promoção de verão");

    fireEvent.click(screen.getByRole("button", { name: "Ativar" }));

    await waitFor(() => expect(post).toHaveBeenCalledWith(`${BASE}/l3/activate`, {}));
  });

  it("a refused change of default shows the server's reason", async () => {
    post.mockRejectedValueOnce(new Error("Lista inativa não pode ser padrão."));
    renderPage();
    await openList("Promoção de verão");

    fireEvent.click(screen.getByRole("button", { name: "Tornar padrão" }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Lista inativa não pode ser padrão."));
  });

  it("the default list applies to everyone, so it has no customers to link", async () => {
    renderPage();
    await openList("Tabela padrão");

    expect(screen.getByText("Lista padrão: vale para todos os clientes sem lista própria.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Tornar padrão" })).toBeNull();
    expect(screen.queryByText("Vincular cliente")).toBeNull();
    expect(screen.getByText("Nenhum produto nesta lista: a venda usa o preço do produto.")).toBeInTheDocument();
  });
});

describe("ListasDePrecoPage — prices and customers of a list", () => {
  it("sets a product's price in the list, accepting a decimal comma", async () => {
    put.mockImplementation(async (url: string, body: unknown) => {
      if (url !== `${BASE}/l2/products`) throw new Error(`PUT inesperado: ${url}`);
      expect(body).toEqual({ productId: "acucar", price: 4.99 });
      details.l2 = {
        ...details.l2,
        items: [...details.l2.items, { id: "i2", productId: "acucar", productName: "Açúcar 1kg", productCode: "ACU", price: 4.99 }],
      };
      return structuredClone(details.l2);
    });
    renderPage();
    await openList("Atacado");
    expect(screen.getByText("Café 500g")).toBeInTheDocument();

    choose("Produto", /Açúcar 1kg \(ACU\)/);
    fireEvent.change(screen.getByLabelText("Preço na lista"), { target: { value: "4,99" } });
    fireEvent.click(screen.getByRole("button", { name: "Definir preço" }));

    expect(await screen.findByRole("button", { name: "Tirar Açúcar 1kg da lista" })).toBeInTheDocument();
    expect(screen.getByText(/4,99/)).toBeInTheDocument();
    expect(screen.getByLabelText("Preço na lista")).toHaveValue("");
    expect(listLoads()).toBeGreaterThan(1);
  });

  it("does not send a price before a product and a valid amount are chosen", async () => {
    renderPage();
    await openList("Atacado");

    fireEvent.change(screen.getByLabelText("Preço na lista"), { target: { value: "4,99" } });
    expect(screen.getByRole("button", { name: "Definir preço" })).toBeDisabled();

    choose("Produto", /Açúcar 1kg/);
    fireEvent.change(screen.getByLabelText("Preço na lista"), { target: { value: "-1" } });
    expect(screen.getByRole("button", { name: "Definir preço" })).toBeDisabled();
    expect(put).not.toHaveBeenCalled();
  });

  it("takes a product out of the list", async () => {
    del.mockImplementationOnce(async () => {
      details.l2 = { ...details.l2, items: [] };
      return structuredClone(details.l2);
    });
    renderPage();
    await openList("Atacado");

    fireEvent.click(screen.getByRole("button", { name: "Tirar Café 500g da lista" }));

    expect(await screen.findByText("Nenhum produto nesta lista: a venda usa o preço do produto.")).toBeInTheDocument();
    expect(del).toHaveBeenCalledWith(`${BASE}/l2/products/cafe`);
  });

  it("links a customer to the list and unlinks another", async () => {
    put.mockImplementation(async (url: string) => {
      if (url !== `${BASE}/l2/customers/cli2`) throw new Error(`PUT inesperado: ${url}`);
      details.l2 = { ...details.l2, customers: [...(details.l2.customers ?? []), { customerId: "cli2", name: "Padaria Pão Quente" }] };
      return structuredClone(details.l2);
    });
    del.mockImplementationOnce(async () => {
      details.l2 = { ...details.l2, customers: [{ customerId: "cli2", name: "Padaria Pão Quente" }] };
      return structuredClone(details.l2);
    });
    renderPage();
    await openList("Atacado");
    expect(screen.getByText("Mercadinho Bom Preço")).toBeInTheDocument();

    choose("Vincular cliente", /Padaria Pão Quente/);
    fireEvent.click(screen.getByRole("button", { name: "Vincular" }));
    await waitFor(() => expect(put).toHaveBeenCalledWith(`${BASE}/l2/customers/cli2`, {}));
    expect(await screen.findAllByRole("button", { name: "Desvincular" })).toHaveLength(2);

    fireEvent.click(screen.getAllByRole("button", { name: "Desvincular" })[0]);
    await waitFor(() => expect(del).toHaveBeenCalledWith(`${BASE}/l2/customers/cli1`));
    await waitFor(() => expect(screen.queryByText("Mercadinho Bom Preço")).toBeNull());
  });

  it("a refused price shows the server's reason", async () => {
    put.mockRejectedValueOnce(new Error("Produto inativo não pode entrar na lista."));
    renderPage();
    await openList("Atacado");

    choose("Produto", /Açúcar 1kg/);
    fireEvent.change(screen.getByLabelText("Preço na lista"), { target: { value: "3" } });
    fireEvent.click(screen.getByRole("button", { name: "Definir preço" }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Produto inativo não pode entrar na lista."));
  });
});
