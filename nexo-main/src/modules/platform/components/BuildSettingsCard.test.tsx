import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import * as platformApi from "../services/platformApi";
import { BuildSettingsCard } from "./BuildSettingsCard";

const { allowed } = vi.hoisted(() => ({ allowed: { value: true as boolean | null } }));

vi.mock("../hooks/usePlatformSecurity", () => ({ useAdminActionsAllowed: () => allowed.value }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("../services/platformApi", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../services/platformApi")>()),
  fetchTenantBuildSettings: vi.fn(),
  updateTenantBuildSettings: vi.fn(),
}));

const defaults: platformApi.PlatformBuildSettings = {
  tenantId: "t1", build3DEnabled: true, buildAiEnabled: false, buildVisionEnabled: false, buildPredictEnabled: true,
  customized: false, updatedByName: null, updatedAt: null,
};

function renderCard() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}><BuildSettingsCard tenantId="t1" /></QueryClientProvider>);
}

describe("BuildSettingsCard (SuperAdmin)", () => {
  afterEach(() => { cleanup(); vi.clearAllMocks(); allowed.value = true; });

  it("shows the four flags and PUTs only the flag changed", async () => {
    vi.mocked(platformApi.fetchTenantBuildSettings).mockResolvedValue(defaults);
    vi.mocked(platformApi.updateTenantBuildSettings).mockResolvedValue({ ...defaults, build3DEnabled: false, customized: true });
    renderCard();
    const threeD = await screen.findByRole("switch", { name: /3D/ });
    expect(threeD).toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("switch", { name: "IA" })).toHaveAttribute("aria-checked", "false");
    expect(screen.getByRole("switch", { name: "Visão" })).toHaveAttribute("aria-checked", "false");
    expect(screen.getByRole("switch", { name: "Previsões" })).toHaveAttribute("aria-checked", "true");

    fireEvent.click(threeD);
    await waitFor(() => expect(platformApi.updateTenantBuildSettings).toHaveBeenCalledWith("t1", { build3DEnabled: false }));
    await waitFor(() => expect(screen.getByRole("switch", { name: /3D/ })).toHaveAttribute("aria-checked", "false"));

    fireEvent.click(screen.getByRole("switch", { name: "IA" }));
    await waitFor(() => expect(platformApi.updateTenantBuildSettings).toHaveBeenLastCalledWith("t1", { buildAiEnabled: true }));
  });

  it("without a two-step sign-in the switches are disabled (administrative act)", async () => {
    allowed.value = false;
    vi.mocked(platformApi.fetchTenantBuildSettings).mockResolvedValue(defaults);
    renderCard();
    expect(await screen.findByRole("switch", { name: /3D/ })).toBeDisabled();
    expect(screen.getByText(/verificação em duas etapas/)).toBeInTheDocument();
  });
});
