import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";
import { apiClient } from "@/services/api-client";
import { polyfillRadix } from "@/modules/platform/test/aiFixtures";
import TransferenciasPage from "./TransferenciasPage";
import type { StockTransferDto } from "../api/stock-operations.api";
import type { StockItemDto } from "../types";

vi.mock("@/services/api-client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/services/api-client")>()),
  apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn(), patch: vi.fn() },
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("@/modules/auth/context/AuthContext", () => ({ useAuth: () => ({ session: { storeId: "loja-centro" } }) }));

polyfillRadix();

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);

const store = (id: string, name: string) => ({ id, name, slug: id, publicSlug: null, status: "Active" });
const stockItem = (productId: string, productName: string, availableQuantity: number): StockItemDto => ({
  id: `s-${productId}`, productId, productName, productCode: productId.toUpperCase(),
  currentQuantity: availableQuantity, reservedQuantity: 0, availableQuantity, lastMovementAt: null,
});

/** What the server answers, changed by each test as the server would. */
let stores: ReturnType<typeof store>[];
let stock: StockItemDto[];
let transfers: StockTransferDto[];

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(<QueryClientProvider client={client}><TransferenciasPage /></QueryClientProvider>);
}
const loads = (path: string) => get.mock.calls.filter(([url]) => url === path).length;
const qtyInput = (product: string) => screen.getByLabelText(`Quantidade a transferir de ${product}`);

function chooseDestination(name: string) {
  fireEvent.keyDown(screen.getByLabelText("Loja de destino"), { key: "Enter" });
  fireEvent.keyDown(screen.getByRole("option", { name }), { key: "Enter" });
}

beforeEach(() => {
  stores = [store("loja-centro", "Loja Centro"), store("loja-norte", "Loja Norte"), store("loja-sul", "Loja Sul")];
  stock = [stockItem("cafe", "Café 500g", 20), stockItem("acucar", "Açúcar 1kg", 0), stockItem("leite", "Leite 1L", 12)];
  transfers = [{
    id: "t1", number: 7, fromStoreId: "loja-sul", toStoreId: "loja-centro", direction: "in", notes: null,
    createdAt: "2026-11-09T14:00:00Z", items: [{ productId: "leite", productName: "Leite 1L", quantity: 6 }],
  }];
  get.mockImplementation(async (url: string) => {
    if (url === "/stores") return structuredClone(stores);
    if (url === "/stock") return structuredClone(stock);
    if (url === "/stock/transfers") return structuredClone(transfers);
    throw new Error(`GET inesperado: ${url}`);
  });
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe("TransferenciasPage", () => {
  it("with a single store it explains transfers need another store", async () => {
    stores = [store("loja-centro", "Loja Centro")];
    renderPage();

    expect(await screen.findByText("Só existe uma loja")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Transferir/ })).toBeNull();
  });

  it("lists this store's stock and the history, naming the other store", async () => {
    renderPage();

    expect(await screen.findByText("Café 500g")).toBeInTheDocument();
    expect(screen.getByText("20")).toBeInTheDocument();
    // nothing available, nothing to send
    expect(qtyInput("Açúcar 1kg")).toBeDisabled();
    expect(await screen.findByText(/#7 · Recebida de Loja Sul/)).toBeInTheDocument();
    expect(screen.getByText(/6 × Leite 1L/)).toBeInTheDocument();
  });

  it("offers only the other stores as destination", async () => {
    renderPage();
    await screen.findByText("Café 500g");

    fireEvent.keyDown(screen.getByLabelText("Loja de destino"), { key: "Enter" });

    expect(screen.getByRole("option", { name: "Loja Norte" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Loja Sul" })).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: "Loja Centro" })).toBeNull();
  });

  it("does not let more than the available quantity leave the store", async () => {
    renderPage();
    await screen.findByText("Café 500g");
    chooseDestination("Loja Norte");

    fireEvent.change(qtyInput("Café 500g"), { target: { value: "25" } });

    expect(screen.getByText("Use quantidades positivas, até o disponível nesta loja.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Transferir 1 produto/ })).toBeDisabled();
  });

  it("needs a destination before sending", async () => {
    renderPage();
    await screen.findByText("Café 500g");

    fireEvent.change(qtyInput("Café 500g"), { target: { value: "5" } });

    expect(screen.getByRole("button", { name: /Transferir 1 produto/ })).toBeDisabled();
  });

  it("sends the typed lines to the chosen store and reloads the stock and the history", async () => {
    post.mockImplementation(async (url: string) => {
      if (url !== "/stock/transfers") throw new Error(`POST inesperado: ${url}`);
      stock = [stockItem("cafe", "Café 500g", 15), stockItem("acucar", "Açúcar 1kg", 0), stockItem("leite", "Leite 1L", 9.5)];
      const sent: StockTransferDto = {
        id: "t2", number: 8, fromStoreId: "loja-centro", toStoreId: "loja-norte", direction: "out", notes: "reposição",
        createdAt: "2026-11-13T10:00:00Z",
        items: [{ productId: "cafe", productName: "Café 500g", quantity: 5 }, { productId: "leite", productName: "Leite 1L", quantity: 2.5 }],
      };
      transfers = [sent, ...transfers];
      return sent;
    });
    renderPage();
    await screen.findByText("Café 500g");
    chooseDestination("Loja Norte");

    fireEvent.change(qtyInput("Café 500g"), { target: { value: "5" } });
    fireEvent.change(qtyInput("Leite 1L"), { target: { value: "2,5" } });
    fireEvent.change(screen.getByLabelText("Observação"), { target: { value: "  reposição  " } });
    fireEvent.click(screen.getByRole("button", { name: "Transferir 2 produto(s)" }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Transferência #8 concluída."));
    expect(post).toHaveBeenCalledWith("/stock/transfers", {
      toStoreId: "loja-norte",
      items: [{ productId: "cafe", quantity: 5 }, { productId: "leite", quantity: 2.5 }],
      notes: "reposição",
    });
    // the balance and the history come back from the server, and the form is cleared
    expect(await screen.findByText("15")).toBeInTheDocument();
    expect(await screen.findByText(/#8 · Enviada para Loja Norte/)).toBeInTheDocument();
    expect(loads("/stock")).toBeGreaterThan(1);
    expect(qtyInput("Café 500g")).toHaveValue("");
    expect(screen.getByLabelText("Observação")).toHaveValue("");
  });

  it("shows why the destination refused the transfer and keeps what was typed", async () => {
    post.mockRejectedValueOnce(new Error("Produto CAFE não está cadastrado na Loja Norte."));
    renderPage();
    await screen.findByText("Café 500g");
    chooseDestination("Loja Norte");

    fireEvent.change(qtyInput("Café 500g"), { target: { value: "5" } });
    fireEvent.click(screen.getByRole("button", { name: "Transferir 1 produto(s)" }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Produto CAFE não está cadastrado na Loja Norte."));
    expect(qtyInput("Café 500g")).toHaveValue("5");
    expect(loads("/stock")).toBe(1);
  });
});
