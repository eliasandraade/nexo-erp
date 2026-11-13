import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";
import { apiClient } from "@/services/api-client";
import { IngredientPriceSection } from "./IngredientPriceSection";

vi.mock("@/services/api-client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/services/api-client")>()),
  apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn(), patch: vi.fn() },
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);

const URL = "/products/farinha/purchase-prices";

/** Price history as the server holds it, changed by each test as the server would. */
let history: { lastPrice: number | null; averagePrice: number | null; history: { id: string; price: number; purchasedAt: string }[] };

function renderSection() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const { container } = render(<QueryClientProvider client={client}><IngredientPriceSection productId="farinha" /></QueryClientProvider>);
  return {
    price: () => screen.getByPlaceholderText("0,0000"),
    date: () => container.querySelector<HTMLInputElement>('input[type="date"]')!,
  };
}

beforeEach(() => {
  history = {
    lastPrice: 4.25, averagePrice: 4.1,
    history: [
      { id: "h2", price: 4.25, purchasedAt: "2026-11-05T12:00:00Z" },
      { id: "h1", price: 3.95, purchasedAt: "2026-10-20T12:00:00Z" },
    ],
  };
  get.mockImplementation(async (url: string) => {
    if (url === URL) return structuredClone(history);
    throw new Error(`GET inesperado: ${url}`);
  });
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe("IngredientPriceSection", () => {
  it("shows the last purchase price, the average and the history, to four decimals", async () => {
    renderSection();

    expect(await screen.findAllByText("R$ 4.2500")).toHaveLength(2); // last price and its history line
    expect(screen.getByText("R$ 4.1000")).toBeInTheDocument();
    expect(screen.getByText("R$ 3.9500")).toBeInTheDocument();
  });

  it("with no purchase yet it shows dashes and no history", async () => {
    history = { lastPrice: null, averagePrice: null, history: [] };
    renderSection();

    await waitFor(() => expect(get).toHaveBeenCalled());
    expect(screen.getAllByText("—")).toHaveLength(2);
    expect(screen.queryByRole("table")).toBeNull();
  });

  it("records a new purchase price and reloads the figures", async () => {
    post.mockImplementation(async (url: string, body: unknown) => {
      if (url !== URL) throw new Error(`POST inesperado: ${url}`);
      const { price, purchasedAt } = body as { price: number; purchasedAt: string };
      history = {
        lastPrice: price, averagePrice: 4.3,
        history: [{ id: "h3", price, purchasedAt: `${purchasedAt}T12:00:00Z` }, ...history.history],
      };
      return { id: "h3", price, purchasedAt };
    });
    const { price, date } = renderSection();
    await screen.findByText("R$ 4.1000");

    expect(screen.getByRole("button", { name: /Registrar/ })).toBeDisabled();
    fireEvent.change(price(), { target: { value: "4.6125" } });
    fireEvent.change(date(), { target: { value: "2026-11-12" } });
    fireEvent.click(screen.getByRole("button", { name: /Registrar/ }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Preço registrado."));
    expect(post).toHaveBeenCalledWith(URL, { price: 4.6125, purchasedAt: "2026-11-12" });
    expect(await screen.findByText("R$ 4.3000")).toBeInTheDocument();
    expect(screen.getAllByText("R$ 4.6125")).toHaveLength(2);
    expect(price()).toHaveValue(null);
  });

  it("keeps the typed price when the server refuses it", async () => {
    post.mockRejectedValueOnce(new Error("Preço inválido."));
    const { price } = renderSection();
    await screen.findByText("R$ 4.1000");

    fireEvent.change(price(), { target: { value: "5" } });
    fireEvent.click(screen.getByRole("button", { name: /Registrar/ }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    expect(toast.success).not.toHaveBeenCalled();
    expect(price()).toHaveValue(5);
    expect(get).toHaveBeenCalledTimes(1);
  });
});
