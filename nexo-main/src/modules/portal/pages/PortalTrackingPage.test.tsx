import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import PortalTrackingPage from "./PortalTrackingPage";

vi.mock("../hooks/useOrderTracking", () => ({
  useOrderTracking: () => ({
    data: { orderNumber: 42, status: "Preparing", statusLabel: "Em preparo", estimatedMinutes: 20, orderType: "Delivery" },
    isLoading: false,
    isError: false,
    isFetching: false,
    refetch: vi.fn(),
  }),
}));

const renderAt = (state?: unknown) =>
  render(
    <MemoryRouter initialEntries={[{ pathname: "/rastrear/abc123", state }]}>
      <Routes>
        <Route path="/rastrear/:token" element={<PortalTrackingPage />} />
      </Routes>
    </MemoryRouter>,
  );

describe("PortalTrackingPage", () => {
  afterEach(cleanup);

  it("goes back to the store's menu when the cart opened it", () => {
    renderAt({ menuSlug: "cantina-da-ana" });
    expect(screen.getByRole("link", { name: "← Voltar ao cardápio" })).toHaveAttribute("href", "/cantina-da-ana");
  });

  it("never links to the root (the ERP's sign-in page or the Orken portal)", () => {
    renderAt();
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
    expect(screen.getByText("Em preparo")).toBeInTheDocument();
  });
});
