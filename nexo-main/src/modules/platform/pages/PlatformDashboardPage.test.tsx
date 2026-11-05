import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, fireEvent, cleanup } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useParams } from "react-router-dom";
import PlatformDashboardPage from "./PlatformDashboardPage";

vi.mock("../hooks/usePlatformSystem", () => ({
  usePlatformStats: () => ({
    data: {
      recentTenants: [
        { id: "t-1", companyName: "Padaria Sol LTDA", tradeName: "Padaria Sol", email: "contato@padariasol.test", status: "Active" },
        { id: "t-2", companyName: "Mercado Lua", tradeName: null, email: "contato@mercadolua.test", status: "Suspended" },
      ],
    },
    isLoading: false,
    isError: false,
  }),
  usePlatformHealth: () => ({ data: undefined, isError: false }),
}));

vi.mock("../hooks/usePlatformTenants", () => ({
  useMrr: () => ({ data: undefined }),
  useChurn: () => ({ data: undefined }),
}));

function TenantDetail() {
  const { id } = useParams();
  return <p>Detalhe do cliente {id}</p>;
}

function renderPage() {
  render(
    <MemoryRouter initialEntries={["/platform"]}>
      <Routes>
        <Route path="/platform" element={<PlatformDashboardPage />} />
        <Route path="/platform/tenants/:id" element={<TenantDetail />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("PlatformDashboardPage: clientes recentes", () => {
  afterEach(() => cleanup());

  it("offers each recent client as a link to its page", () => {
    renderPage();

    expect(screen.getByRole("link", { name: /Padaria Sol/ })).toHaveAttribute("href", "/platform/tenants/t-1");
    expect(screen.getByRole("link", { name: /Mercado Lua/ })).toHaveAttribute("href", "/platform/tenants/t-2");
  });

  it("takes keyboard focus", () => {
    renderPage();
    const link = screen.getByRole("link", { name: /Padaria Sol/ });

    link.focus();

    expect(link).toHaveFocus();
  });

  it("opens the client page when activated", () => {
    renderPage();

    fireEvent.click(screen.getByRole("link", { name: /Mercado Lua/ }));

    expect(screen.getByText("Detalhe do cliente t-2")).toBeInTheDocument();
  });
});
