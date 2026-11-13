import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";
import { apiClient } from "@/services/api-client";
import InventarioPage from "./InventarioPage";
import type { InventoryCountDto, InventoryCountItemDto } from "../api/stock-operations.api";

vi.mock("@/services/api-client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/services/api-client")>()),
  apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn(), patch: vi.fn() },
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);
const put = vi.mocked(apiClient.put);

const item = (over: Partial<InventoryCountItemDto> & Pick<InventoryCountItemDto, "productId" | "productName">): InventoryCountItemDto => ({
  productCode: over.productId.toUpperCase(),
  expectedQuantity: 0,
  countedQuantity: null,
  systemQuantityAtCount: null,
  systemQuantityAtApply: null,
  difference: null,
  ...over,
});

const count = (over: Partial<InventoryCountDto> & Pick<InventoryCountDto, "id" | "number" | "status">): InventoryCountDto => ({
  notes: null,
  createdAt: "2026-11-10T12:00:00Z",
  closedAt: null,
  countedItems: 0,
  items: [],
  ...over,
});

const openCount3 = () => count({
  id: "c3", number: 3, status: "Open", countedItems: 1,
  items: [
    item({ productId: "p1", productName: "Café 500g", expectedQuantity: 10 }),
    item({ productId: "p2", productName: "Açúcar 1kg", expectedQuantity: 5, countedQuantity: 5, systemQuantityAtCount: 5, difference: 0 }),
  ],
});
const appliedCount2 = () => count({
  id: "c2", number: 2, status: "Applied", countedItems: 1, closedAt: "2026-11-01T18:00:00Z",
  items: [item({
    productId: "p1", productName: "Café 500g", expectedQuantity: 12, countedQuantity: 10,
    systemQuantityAtCount: 12, systemQuantityAtApply: 12, difference: -2,
  })],
});

/** What the server answers for the list of counts, changed by each test as the server would. */
let counts: InventoryCountDto[];

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(<QueryClientProvider client={client}><InventarioPage /></QueryClientProvider>);
}
const listLoads = () => get.mock.calls.filter(([url]) => url === "/stock/counts").length;
const countedInput = (product: string) => screen.getByLabelText(`Quantidade contada de ${product}`);

beforeEach(() => {
  counts = [openCount3(), appliedCount2()];
  get.mockImplementation(async (url: string) => {
    if (url === "/stock/counts") return structuredClone(counts);
    throw new Error(`GET inesperado: ${url}`);
  });
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe("InventarioPage", () => {
  it("lists the counts and opens the one in progress, with what the system expects of each product", async () => {
    renderPage();

    expect(await screen.findByText("Inventário #3 — Em contagem")).toBeInTheDocument();
    const rows = screen.getAllByRole("row");
    expect(within(rows[1]).getByText("Café 500g")).toBeInTheDocument();
    expect(within(rows[1]).getByText("10")).toBeInTheDocument();
    expect(countedInput("Café 500g")).toHaveValue("");
    expect(countedInput("Açúcar 1kg")).toHaveValue("5");

    const history = screen.getByRole("list");
    expect(within(history).getByText("Aplicado")).toBeInTheDocument();
    expect(within(history).getByText(/1 de 2 contados/)).toBeInTheDocument();
    // only one count may be open at a time
    expect(screen.getByRole("button", { name: /Novo inventário/ })).toBeDisabled();
  });

  it("filters the products of the count by name or code", async () => {
    renderPage();
    await screen.findByText("Inventário #3 — Em contagem");

    fireEvent.change(screen.getByRole("textbox", { name: "Filtrar produto" }), { target: { value: "açú" } });

    expect(screen.queryByText("Café 500g")).toBeNull();
    expect(screen.getByText("Açúcar 1kg")).toBeInTheDocument();
  });

  it("saves only what was typed, accepting a decimal comma, and reloads the counts", async () => {
    put.mockImplementation(async (_url: string, body: unknown) => {
      counts[0] = { ...counts[0], countedItems: 2 };
      return { ...counts[0], body };
    });
    renderPage();
    await screen.findByText("Inventário #3 — Em contagem");
    expect(listLoads()).toBe(1);

    fireEvent.change(countedInput("Café 500g"), { target: { value: "7,5" } });
    fireEvent.click(screen.getByRole("button", { name: "Salvar contagem" }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Contagem salva."));
    expect(put).toHaveBeenCalledWith("/stock/counts/c3", { items: [{ productId: "p1", countedQuantity: 7.5 }] });
    await waitFor(() => expect(listLoads()).toBeGreaterThan(1));
    expect(await screen.findByText(/2 de 2 contados/)).toBeInTheDocument();
  });

  it("refuses a negative or non-numeric count before sending anything", async () => {
    renderPage();
    await screen.findByText("Inventário #3 — Em contagem");

    fireEvent.change(countedInput("Café 500g"), { target: { value: "-3" } });

    expect(screen.getByText("Use apenas números positivos na contagem.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Salvar contagem" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Aplicar ajustes" })).toBeDisabled();

    fireEvent.change(countedInput("Café 500g"), { target: { value: "abc" } });
    expect(screen.getByText("Use apenas números positivos na contagem.")).toBeInTheDocument();
    expect(put).not.toHaveBeenCalled();
  });

  it("applying saves the pending count first, then applies and reports how many products were adjusted", async () => {
    put.mockResolvedValue(openCount3());
    post.mockImplementation(async (url: string) => {
      if (url !== "/stock/counts/c3/apply") throw new Error(`POST inesperado: ${url}`);
      const applied = count({
        id: "c3", number: 3, status: "Applied", countedItems: 2,
        items: [
          item({ productId: "p1", productName: "Café 500g", expectedQuantity: 10, countedQuantity: 8, difference: -2, systemQuantityAtApply: 10 }),
          item({ productId: "p2", productName: "Açúcar 1kg", expectedQuantity: 5, countedQuantity: 5, difference: 0, systemQuantityAtApply: 5 }),
        ],
      });
      counts = [applied, appliedCount2()];
      return applied;
    });
    renderPage();
    await screen.findByText("Inventário #3 — Em contagem");

    fireEvent.change(countedInput("Café 500g"), { target: { value: "8" } });
    fireEvent.click(screen.getByRole("button", { name: "Aplicar ajustes" }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Inventário aplicado: 1 produto(s) ajustado(s)."));
    expect(put).toHaveBeenCalledWith("/stock/counts/c3", { items: [{ productId: "p1", countedQuantity: 8 }] });
    expect(put.mock.invocationCallOrder[0]).toBeLessThan(post.mock.invocationCallOrder[0]);
    // with no count open any more, a new one can be started
    await waitFor(() => expect(screen.getByRole("button", { name: /Novo inventário/ })).toBeEnabled());
  });

  it("shows the server's reason when a count cannot be saved", async () => {
    put.mockRejectedValueOnce(new Error("Inventário já foi aplicado por outro usuário."));
    renderPage();
    await screen.findByText("Inventário #3 — Em contagem");

    fireEvent.change(countedInput("Café 500g"), { target: { value: "9" } });
    fireEvent.click(screen.getByRole("button", { name: "Salvar contagem" }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Inventário já foi aplicado por outro usuário."));
    expect(toast.success).not.toHaveBeenCalled();
    // what was typed is kept, so nothing counted is lost
    expect(countedInput("Café 500g")).toHaveValue("9");
  });

  it("cancels the count in progress", async () => {
    post.mockImplementation(async (url: string) => {
      if (url !== "/stock/counts/c3/cancel") throw new Error(`POST inesperado: ${url}`);
      counts = [{ ...openCount3(), status: "Cancelled" }, appliedCount2()];
      return counts[0];
    });
    renderPage();
    await screen.findByText("Inventário #3 — Em contagem");

    fireEvent.click(screen.getByRole("button", { name: "Cancelar inventário" }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Inventário cancelado."));
    expect(post).toHaveBeenCalledWith("/stock/counts/c3/cancel", {});
  });

  it("an applied count from the history is read-only and shows the shortage found", async () => {
    renderPage();
    await screen.findByText("Inventário #3 — Em contagem");

    fireEvent.click(screen.getByRole("button", { name: "Inventário #2" }));

    expect(await screen.findByText("Inventário #2 — Aplicado")).toBeInTheDocument();
    expect(screen.queryByRole("textbox", { name: /Quantidade contada/ })).toBeNull();
    expect(screen.getByText("-2")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Aplicar ajustes" })).toBeNull();
  });

  it("with no count yet, opening one starts it and reloads the list", async () => {
    counts = [];
    post.mockImplementation(async (url: string, body: unknown) => {
      if (url !== "/stock/counts") throw new Error(`POST inesperado: ${url}`);
      expect(body).toEqual({});
      counts = [count({ id: "c1", number: 1, status: "Open", items: [item({ productId: "p1", productName: "Café 500g", expectedQuantity: 4 })] })];
      return counts[0];
    });
    renderPage();

    expect(await screen.findByText("Nenhum inventário ainda")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Novo inventário/ }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Inventário #1 aberto."));
    expect(await screen.findByText("Inventário #1 — Em contagem")).toBeInTheDocument();
    expect(listLoads()).toBeGreaterThan(1);
  });

  it("when opening a count fails the operator is told why", async () => {
    counts = [];
    post.mockRejectedValueOnce(new Error("Nenhum produto com controle de estoque."));
    renderPage();
    await screen.findByText("Nenhum inventário ainda");

    fireEvent.click(screen.getByRole("button", { name: /Novo inventário/ }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Nenhum produto com controle de estoque."));
  });

  it("when the list cannot be loaded it says so and lets the operator try again", async () => {
    get.mockRejectedValueOnce(new Error("offline"));
    renderPage();

    expect(await screen.findByText("Não foi possível carregar os inventários")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Tentar novamente/ }));

    expect(await screen.findByText("Inventário #3 — Em contagem")).toBeInTheDocument();
  });
});
