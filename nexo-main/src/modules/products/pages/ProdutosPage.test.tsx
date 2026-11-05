import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, cleanup, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import ProdutosPage from "./ProdutosPage";
import { useProductsPaged } from "../hooks/use-products";
import { fetchProductsPaged, productStatusParams } from "../api/products.api";
import { apiClient } from "@/services/api-client";

vi.mock("../hooks/use-products", () => ({
  useProductsPaged: vi.fn(),
  useCategories: () => ({ data: [] }),
}));
vi.mock("@/services/api-client", () => ({ apiClient: { get: vi.fn() } }));
vi.mock("../components/ProductFilters", () => ({
  ProductFilters: ({ status, onStatusChange }: { status: string; onStatusChange: (v: string) => void }) => (
    <div>
      <span data-testid="status">{status}</span>
      {["all", "active", "inactive"].map((s) => <button key={s} onClick={() => onStatusChange(s)}>{s}</button>)}
    </div>
  ),
}));
vi.mock("../components/ProductTable", () => ({
  ProductTable: ({ products }: { products: { id: string }[] }) => <ul>{products.map((p) => <li key={p.id}>{p.id}</li>)}</ul>,
}));
vi.mock("../components/ManageCategoriesDialog", () => ({ ManageCategoriesDialog: () => null }));

const listed = vi.mocked(useProductsPaged);

beforeEach(() => {
  listed.mockReset();
  listed.mockReturnValue({
    data: {
      items: [{ id: "ativo", isActive: true }, { id: "inativo", isActive: false }],
      totalCount: 2,
      totalPages: 1,
    },
    isLoading: false,
    isError: false,
  } as unknown as ReturnType<typeof useProductsPaged>);
});
afterEach(cleanup);

function lastRequest() {
  return listed.mock.calls[listed.mock.calls.length - 1][0];
}

describe("ProdutosPage status filter", () => {
  it("opens on the active products, as the screen always did", () => {
    render(<MemoryRouter><ProdutosPage /></MemoryRouter>);
    expect(screen.getByTestId("status")).toHaveTextContent("active");
    expect(lastRequest().includeInactive).toBeUndefined();
    expect(lastRequest().onlyInactive).toBeUndefined();
  });

  it("'Todos' asks for the inactive products too", () => {
    render(<MemoryRouter><ProdutosPage /></MemoryRouter>);
    fireEvent.click(screen.getByRole("button", { name: "all" }));
    expect(lastRequest().includeInactive).toBe(true);
    expect(lastRequest().onlyInactive).toBeUndefined();
  });

  it("'Inativo' is filtered by the server, so page and total match what is listed", () => {
    render(<MemoryRouter><ProdutosPage /></MemoryRouter>);
    fireEvent.click(screen.getByRole("button", { name: "inactive" }));
    expect(lastRequest().onlyInactive).toBe(true);
    expect(lastRequest().includeInactive).toBeUndefined();
    // the page lists what the server sent: no second filter over the current page
    expect(screen.getByText("ativo")).toBeInTheDocument();
    expect(screen.getByText("inativo")).toBeInTheDocument();
  });
});

describe("product status on the wire", () => {
  it("maps each status to its query string", async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ items: [] });
    const urlFor = async (status: string) => {
      await fetchProductsPaged({ ...productStatusParams(status) });
      const calls = vi.mocked(apiClient.get).mock.calls;
      return String(calls[calls.length - 1][0]);
    };

    const active = await urlFor("active");
    expect(active).not.toContain("includeInactive");
    expect(active).not.toContain("onlyInactive");
    expect(await urlFor("all")).toContain("includeInactive=true");
    expect(await urlFor("inactive")).toContain("onlyInactive=true");
  });
});
