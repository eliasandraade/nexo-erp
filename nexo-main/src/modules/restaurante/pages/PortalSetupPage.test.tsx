import { type ReactNode } from "react";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, cleanup, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { toast } from "sonner";
import PortalSetupPage from "./PortalSetupPage";

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));
vi.mock("@/modules/auth/context/AuthContext", () => ({ useAuth: () => ({ session: { storeId: "s1" } }) }));
vi.mock("@/modules/stores/services/storesApi", () => ({
  fetchMyStores: vi.fn().mockResolvedValue([{ id: "s1", publicSlug: "meu-restaurante" }]),
  setPublicSlug: vi.fn(),
  checkSlugAvailability: vi.fn().mockResolvedValue({ available: true }),
}));
vi.mock("../api/restaurante.api", () => ({
  getFoodSettings: vi.fn().mockResolvedValue({ acceptingOrders: true, deliveryEnabled: true, takeawayEnabled: true }),
  updatePortalInfo: vi.fn(),
  getRestauranteDeliveryZones: vi.fn().mockResolvedValue([]),
  upsertDeliveryZones: vi.fn(),
  getCoupons: vi.fn().mockResolvedValue([]),
  createCoupon: vi.fn(),
  updateCoupon: vi.fn(),
  revokeCoupon: vi.fn(),
}));

function stubClipboard(writeText: (text: string) => Promise<void>) {
  Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
}

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  // The page re-renders without end while the zones query has no data, and act() never returns.
  // Known defect, recorded in docs/audits/2026-11-04-sonarcloud-revalidacao.md: seeded here to test the copy button.
  qc.setQueryData(["delivery-zones"], []);
  return <QueryClientProvider client={qc}><MemoryRouter>{children}</MemoryRouter></QueryClientProvider>;
}

beforeEach(() => vi.mocked(toast.error).mockClear());
afterEach(cleanup);

describe("PortalSetupPage copy link", () => {
  it("copies the portal link without an error", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    stubClipboard(writeText);
    render(<PortalSetupPage />, { wrapper });
    fireEvent.click(await screen.findByTitle("Copiar link"));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith("https://erp.orken.com.br/meu-restaurante"));
    expect(toast.error).not.toHaveBeenCalled();
  });

  it("tells the user when the browser refuses", async () => {
    stubClipboard(vi.fn().mockRejectedValue(new DOMException("denied", "NotAllowedError")));
    render(<PortalSetupPage />, { wrapper });
    fireEvent.click(await screen.findByTitle("Copiar link"));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Não foi possível copiar o link."));
  });
});
