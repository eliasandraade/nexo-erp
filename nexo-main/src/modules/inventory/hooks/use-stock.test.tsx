import { afterEach, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";
import { act, cleanup, renderHook } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { apiClient } from "@/services/api-client";
import { useAdjustStock } from "./use-stock";

vi.mock("@/services/api-client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/services/api-client")>()),
  apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn(), patch: vi.fn() },
}));

const post = vi.mocked(apiClient.post);

function setup() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  // what the stock screens already have on hand when the adjustment is made
  qc.setQueryData(["stock"], [{ productId: "p1", currentQuantity: 10 }]);
  qc.setQueryData(["stock", "movements", "p1"], []);
  qc.setQueryData(["products"], [{ id: "p1" }]);
  const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
  const stale = (key: readonly unknown[]) => qc.getQueryState(key)?.isInvalidated;
  return { qc, wrapper, stale };
}

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe("useAdjustStock", () => {
  it("sends the adjustment and makes the balance and the product's movements reload", async () => {
    post.mockResolvedValueOnce({ id: "m1", productId: "p1" });
    const { wrapper, stale } = setup();
    const { result } = renderHook(() => useAdjustStock(), { wrapper });

    const request = { productId: "p1", quantity: -2, movementType: "Loss", notes: "quebra no transporte" };
    await act(() => result.current.mutateAsync(request));

    expect(post).toHaveBeenCalledWith("/stock/adjust", request);
    expect(stale(["stock"])).toBe(true);
    expect(stale(["stock", "movements", "p1"])).toBe(true);
    // the product catalogue is not affected by a stock adjustment
    expect(stale(["products"])).toBe(false);
  });

  it("a refused adjustment keeps the balance on screen as it was", async () => {
    post.mockRejectedValueOnce(new Error("Estoque insuficiente."));
    const { wrapper, stale } = setup();
    const { result } = renderHook(() => useAdjustStock(), { wrapper });

    await act(async () => {
      await expect(result.current.mutateAsync({ productId: "p1", quantity: -50, movementType: "Loss" }))
        .rejects.toThrow("Estoque insuficiente.");
    });

    expect(stale(["stock"])).toBe(false);
    expect(stale(["stock", "movements", "p1"])).toBe(false);
  });
});
