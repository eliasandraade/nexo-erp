import { type ReactNode } from "react";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, cleanup, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { toast } from "sonner";
import PortalSetupPage from "./PortalSetupPage";
import {
  getCoupons,
  getRestauranteDeliveryZones,
  revokeCoupon,
  upsertDeliveryZones,
  type CouponDto,
} from "../api/restaurante.api";
import { fetchMyStores, setPublicSlug } from "@/modules/stores/services/storesApi";

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
  return <QueryClientProvider client={qc}><MemoryRouter>{children}</MemoryRouter></QueryClientProvider>;
}

beforeEach(() => vi.mocked(toast.error).mockClear());
afterEach(cleanup);

describe("PortalSetupPage delivery zones", () => {
  // Without a stable default the page re-rendered without end here: act() never returned and the run ran out of memory.
  it("settles while the zones query is still loading", async () => {
    vi.mocked(getRestauranteDeliveryZones).mockReturnValueOnce(new Promise(() => {}));
    render(<PortalSetupPage />, { wrapper });
    expect(await screen.findByTitle("Copiar link")).toBeTruthy();
  });

  it("settles when the zones query fails", async () => {
    vi.mocked(getRestauranteDeliveryZones).mockRejectedValueOnce(new Error("indisponível"));
    render(<PortalSetupPage />, { wrapper });
    expect(await screen.findByTitle("Copiar link")).toBeTruthy();
  });
});

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

// As abas são do Radix: trocam no mousedown.
const openTab = async (name: RegExp) => fireEvent.mouseDown(await screen.findByRole("tab", { name }), { button: 0 });

describe("PortalSetupPage portal address", () => {
  beforeEach(() => {
    vi.mocked(fetchMyStores).mockClear();
    vi.mocked(setPublicSlug).mockClear();
  });

  it("saves the new address and, once the store comes back with it, offers to open the live portal", async () => {
    vi.mocked(fetchMyStores)
      .mockResolvedValueOnce([{ id: "s1", publicSlug: "meu-restaurante" }] as Awaited<ReturnType<typeof fetchMyStores>>)
      .mockResolvedValueOnce([{ id: "s1", publicSlug: "bar-do-ze" }] as Awaited<ReturnType<typeof fetchMyStores>>);
    vi.mocked(setPublicSlug).mockResolvedValueOnce({ publicSlug: "bar-do-ze" });
    render(<PortalSetupPage />, { wrapper });

    fireEvent.change(await screen.findByDisplayValue("meu-restaurante"), { target: { value: "Bar do Zé" } });
    expect(await screen.findByText("Endereço disponível!", undefined, { timeout: 3000 })).toBeTruthy();
    expect(screen.queryByTitle("Abrir portal")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Salvar" }));

    const open = await screen.findByTitle("Abrir portal");
    expect(open.getAttribute("href")).toBe("https://erp.orken.com.br/bar-do-ze");
    expect(setPublicSlug).toHaveBeenCalledWith("s1", "bar-do-ze");
    expect(fetchMyStores).toHaveBeenCalledTimes(2);
  });
});

describe("PortalSetupPage delivery zones save", () => {
  beforeEach(() => {
    vi.mocked(getRestauranteDeliveryZones).mockClear();
    vi.mocked(upsertDeliveryZones).mockClear();
  });

  it("sends the neighborhoods with their fees and shows what the server kept", async () => {
    vi.mocked(getRestauranteDeliveryZones)
      .mockResolvedValueOnce([])
      .mockResolvedValueOnce([{ neighborhood: "Aldeota", fee: 7.5 }] as Awaited<ReturnType<typeof getRestauranteDeliveryZones>>);
    vi.mocked(upsertDeliveryZones).mockResolvedValueOnce(undefined as never);
    render(<PortalSetupPage />, { wrapper });
    await openTab(/zonas de entrega/i);

    fireEvent.click(await screen.findByRole("checkbox", { name: "Aldeota" }));
    fireEvent.change(await screen.findByRole("spinbutton"), { target: { value: "7.5" } });
    fireEvent.click(screen.getByRole("button", { name: /salvar zonas de entrega/i }));

    expect(await screen.findByText("Salvo")).toBeTruthy();
    expect(upsertDeliveryZones).toHaveBeenCalledWith({ zones: [{ neighborhood: "Aldeota", fee: 7.5 }] });
    await waitFor(() => expect(getRestauranteDeliveryZones).toHaveBeenCalledTimes(2));
    expect(screen.getByText("1 bairro ativo.")).toBeTruthy();
  });

  it("a save the server refuses says so and does not claim it was saved", async () => {
    vi.mocked(upsertDeliveryZones).mockRejectedValueOnce(new Error("indisponível"));
    render(<PortalSetupPage />, { wrapper });
    await openTab(/zonas de entrega/i);

    fireEvent.click(await screen.findByRole("checkbox", { name: "Meireles" }));
    fireEvent.click(screen.getByRole("button", { name: /salvar zonas de entrega/i }));

    expect(await screen.findByText("Erro ao salvar")).toBeTruthy();
    expect(screen.queryByText("Salvo")).toBeNull();
    expect(getRestauranteDeliveryZones).toHaveBeenCalledTimes(1);
  });
});

describe("PortalSetupPage coupons", () => {
  const coupon: CouponDto = {
    id: "c1", code: "BEMVINDO10", discountType: "Percentage", discountValue: 10, isActive: true, isFirstOrderOnly: true, usedCount: 3,
  };

  beforeEach(() => {
    vi.mocked(getCoupons).mockClear();
    vi.mocked(revokeCoupon).mockClear();
  });

  it("revoking a coupon asks first, then shows it as revoked", async () => {
    vi.mocked(getCoupons).mockResolvedValueOnce([coupon]).mockResolvedValueOnce([{ ...coupon, isActive: false }]);
    vi.mocked(revokeCoupon).mockResolvedValueOnce(undefined as never);
    render(<PortalSetupPage />, { wrapper });
    await openTab(/cupons/i);

    fireEvent.click(await screen.findByTitle("Revogar"));
    expect(await screen.findByText("Revogar cupom?")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Revogar" }));

    expect(await screen.findByText("Revogado")).toBeTruthy();
    expect(revokeCoupon).toHaveBeenCalledWith("c1");
    await waitFor(() => expect(screen.queryByText("Revogar cupom?")).toBeNull());
    expect(screen.queryByTitle("Revogar")).toBeNull();
  });
});
