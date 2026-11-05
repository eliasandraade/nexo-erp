import { type ReactNode } from "react";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, cleanup, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { toast } from "sonner";
import ServicePortalPage from "./ServicePortalPage";

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));
vi.mock("@/modules/auth/context/AuthContext", () => ({ useAuth: () => ({ session: { storeId: "s1" } }) }));
vi.mock("@/modules/stores/services/storesApi", () => ({
  fetchMyStores: vi.fn().mockResolvedValue([{ id: "s1", publicSlug: "minha-clinica" }]),
}));
vi.mock("../context/ServicePresetContext", () => ({
  useServicePreset: () => ({
    preset: { displayName: "Clínica", labels: { professional: "Profissional" } },
    presetKey: "clinica",
    isConfigured: true,
  }),
}));
vi.mock("../hooks/usePublicBookingSettings", () => ({
  usePublicBookingSettings: () => ({ isLoading: false, data: { publicBookingEnabled: true } }),
}));
vi.mock("../api/service.api", () => ({
  fetchProfessionals: vi.fn().mockResolvedValue([{ id: "p1", name: "Ana", workingHoursJson: "{}" }]),
  fetchCatalog: vi.fn().mockResolvedValue([{ id: "c1" }]),
}));
vi.mock("../lib/working-hours", () => ({ professionalHasHours: () => true }));
vi.mock("../components/PortalSlugSection", () => ({
  PortalSlugSection: () => null,
  publicBookingUrl: (slug: string) => `erp.orken.com.br/agendar/${slug}`,
}));
vi.mock("../components/ProfessionalHoursDialog", () => ({ ProfessionalHoursDialog: () => null }));
vi.mock("../components/PortalBookingSettingsForm", () => ({ PortalBookingSettingsForm: () => null }));
vi.mock("../components/PortalBrandingForm", () => ({ PortalBrandingForm: () => null }));
vi.mock("../components/PortalCustomerEmailsForm", () => ({ PortalCustomerEmailsForm: () => null }));

function stubClipboard(writeText: (text: string) => Promise<void>) {
  Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
}

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}><MemoryRouter>{children}</MemoryRouter></QueryClientProvider>;
}

beforeEach(() => {
  vi.mocked(toast.error).mockClear();
  vi.mocked(toast.success).mockClear();
});
afterEach(cleanup);

describe("ServicePortalPage copy link", () => {
  it("copies the public link and confirms", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    stubClipboard(writeText);
    render(<ServicePortalPage />, { wrapper });
    fireEvent.click(await screen.findByRole("button", { name: /Copiar link/ }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Link copiado."));
    expect(writeText).toHaveBeenCalledWith("https://erp.orken.com.br/agendar/minha-clinica");
    expect(toast.error).not.toHaveBeenCalled();
  });

  it("tells the user when the browser refuses", async () => {
    stubClipboard(vi.fn().mockRejectedValue(new DOMException("denied", "NotAllowedError")));
    render(<ServicePortalPage />, { wrapper });
    fireEvent.click(await screen.findByRole("button", { name: /Copiar link/ }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Não foi possível copiar o link."));
    expect(toast.success).not.toHaveBeenCalled();
  });
});
