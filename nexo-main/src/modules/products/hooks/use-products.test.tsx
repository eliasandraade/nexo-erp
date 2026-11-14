import { afterEach, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";
import { act, cleanup, renderHook } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { apiClient } from "@/services/api-client";
import { useCreateProduct, useDeleteCategory, useSetProductActive, useUpdateProduct } from "./use-products";
import type { CreateProductPayload, UpdateProductPayload } from "../api/products.api";

vi.mock("@/services/api-client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/services/api-client")>()),
  apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn(), patch: vi.fn() },
}));

const post = vi.mocked(apiClient.post);
const put = vi.mocked(apiClient.put);

function setup() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  qc.setQueryData(["products", { includeInactive: false, isIngredient: undefined }], [{ id: "p1", name: "Café 500g" }]);
  qc.setQueryData(["products", "p1"], { id: "p1", name: "Café 500g", salePrice: 18.9 });
  qc.setQueryData(["categories", false], [{ id: "c1", name: "Mercearia" }]);
  qc.setQueryData(["stock"], []);
  const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
  const stale = (key: readonly unknown[]) => qc.getQueryState(key)?.isInvalidated;
  return { qc, wrapper, stale };
}

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe("product mutations", () => {
  it("creating a product posts it and makes every product list reload", async () => {
    post.mockResolvedValueOnce({ id: "p2", name: "Açúcar 1kg" });
    const { wrapper, stale } = setup();
    const { result } = renderHook(() => useCreateProduct(), { wrapper });

    const payload = { code: "ACU-1", name: "Açúcar 1kg", unit: "Un", salePrice: 5.49, costPrice: 3.2 } as unknown as CreateProductPayload;
    await act(() => result.current.mutateAsync(payload));

    expect(post).toHaveBeenCalledWith("/products", payload);
    expect(stale(["products", { includeInactive: false, isIngredient: undefined }])).toBe(true);
    expect(stale(["categories", false])).toBe(false);
  });

  it("editing a product shows the saved version at once and reloads the lists", async () => {
    const saved = { id: "p1", name: "Café 500g Tradicional", salePrice: 19.9 };
    put.mockResolvedValueOnce(saved);
    const { qc, wrapper, stale } = setup();
    const { result } = renderHook(() => useUpdateProduct("p1"), { wrapper });

    const payload = { name: "Café 500g Tradicional", unit: "Un", salePrice: 19.9 } as unknown as UpdateProductPayload;
    await act(() => result.current.mutateAsync(payload));

    expect(put).toHaveBeenCalledWith("/products/p1", payload);
    expect(qc.getQueryData(["products", "p1"])).toEqual(saved);
    expect(stale(["products", { includeInactive: false, isIngredient: undefined }])).toBe(true);
  });

  it("deactivating and reactivating hit the matching endpoint and reload the product", async () => {
    post.mockResolvedValue(undefined);
    const { wrapper, stale } = setup();
    const { result } = renderHook(() => useSetProductActive("p1"), { wrapper });

    await act(() => result.current.mutateAsync(false));
    expect(post).toHaveBeenLastCalledWith("/products/p1/deactivate");
    expect(stale(["products", "p1"])).toBe(true);
    expect(stale(["products", { includeInactive: false, isIngredient: undefined }])).toBe(true);
    expect(stale(["stock"])).toBe(false);

    await act(() => result.current.mutateAsync(true));
    expect(post).toHaveBeenLastCalledWith("/products/p1/activate");
  });

  it("a failed deactivation leaves the product as it was", async () => {
    post.mockRejectedValueOnce(new Error("Produto com estoque não pode ser desativado."));
    const { wrapper, stale } = setup();
    const { result } = renderHook(() => useSetProductActive("p1"), { wrapper });

    await act(async () => {
      await expect(result.current.mutateAsync(false)).rejects.toThrow("Produto com estoque");
    });
    expect(stale(["products", "p1"])).toBe(false);
  });

  it("removing a category reloads the categories and the products that showed it", async () => {
    post.mockResolvedValueOnce(undefined);
    const { wrapper, stale } = setup();
    const { result } = renderHook(() => useDeleteCategory(), { wrapper });

    await act(() => result.current.mutateAsync("c1"));

    expect(post).toHaveBeenCalledWith("/categories/c1/deactivate");
    expect(stale(["categories", false])).toBe(true);
    expect(stale(["products", "p1"])).toBe(true);
    expect(stale(["stock"])).toBe(false);
  });
});
