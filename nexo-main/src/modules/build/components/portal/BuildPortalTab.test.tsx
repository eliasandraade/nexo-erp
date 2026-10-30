import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ApiError } from "@/services/api-client";
import { BuildPortalTab } from "./BuildPortalTab";
import * as portalApi from "../../api/build-portal.api";

vi.mock("../../api/build-portal.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-portal.api")>()),
  fetchPortalCompany:   vi.fn(),
  fetchProjectPortal:   vi.fn(),
  fetchClientUpdates:   vi.fn(),
  updateProjectPortal:  vi.fn(),
  publishProjectPortal: vi.fn(),
  createClientUpdate:   vi.fn(),
}));

const company: portalApi.BuildPortalCompanyDto = {
  configured: true, slug: "construtora-silva", displayName: "Construtora Silva",
  logoKey: null, logoUrl: null, contactEmail: null, contactPhone: null,
};

const portal: portalApi.BuildProjectPortalDto = {
  projectId: "p1", exists: true, slug: "parque-sul", companySlug: "construtora-silva",
  publicPath: "/construtora-silva/parque-sul", isPublished: false, publishedAt: null,
  access: "Public", hasPassword: false, isLocked: false,
  title: "Residencial Parque Sul", welcomeMessage: null, logoKey: null, logoUrl: null, coverKey: null, coverUrl: null,
  primaryColor: "#112233", accentColor: null, contactName: null, contactPhone: null, contactEmail: null, contactWhatsApp: null,
  visibility: { ...portalApi.DEFAULT_PORTAL_VISIBILITY },
};

function renderTab(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("BuildPortalTab", () => {
  beforeEach(() => {
    vi.mocked(portalApi.fetchPortalCompany).mockResolvedValue(company);
    vi.mocked(portalApi.fetchProjectPortal).mockResolvedValue(portal);
    vi.mocked(portalApi.fetchClientUpdates).mockResolvedValue([]);
  });
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("shows the public URL with the company and project slugs", async () => {
    renderTab(<BuildPortalTab projectId="p1" />);
    expect(await screen.findByTestId("portal-public-url"))
      .toHaveTextContent("https://www.orken.com.br/construtora-silva/parque-sul");
    expect(screen.getByText(/compartilhado por todas as obras/)).toBeInTheDocument();
  });

  it("financial toggle warns, and saving sends the whole visibility matrix (future flags untouched)", async () => {
    vi.mocked(portalApi.updateProjectPortal).mockImplementation(async (_id, req) => ({ ...portal, ...req }));
    renderTab(<BuildPortalTab projectId="p1" />);

    const financial = await screen.findByRole("switch", { name: /Resumo financeiro/ });
    expect(screen.queryByText(/o cliente passará a ver valores financeiros/)).not.toBeInTheDocument();

    fireEvent.click(financial);
    expect(financial).toHaveAttribute("aria-checked", "true");
    expect(screen.getByText(/o cliente passará a ver valores financeiros/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("switch", { name: /^Fotos/ }));               // turn photos off
    expect(screen.getByRole("switch", { name: /Vídeos/ })).toBeDisabled();        // "em breve"

    fireEvent.click(screen.getByRole("button", { name: "Salvar portal" }));
    await waitFor(() => expect(portalApi.updateProjectPortal).toHaveBeenCalledTimes(1));
    const [projectId, req] = vi.mocked(portalApi.updateProjectPortal).mock.calls[0];
    expect(projectId).toBe("p1");
    expect(req.visibility).toEqual({ ...portalApi.DEFAULT_PORTAL_VISIBILITY, financialSummary: true, photos: false });
    // full replace: the fields not edited go back as they were
    expect(req).toMatchObject({ slug: "parque-sul", access: "Public", title: "Residencial Parque Sul", primaryColor: "#112233" });
    expect(req).not.toHaveProperty("password");
  });

  it("Wave 2 flags: aditivos and escolhas on by default, medições off; turning medições on is saved", async () => {
    vi.mocked(portalApi.updateProjectPortal).mockImplementation(async (_id, req) => ({ ...portal, ...req }));
    renderTab(<BuildPortalTab projectId="p1" />);

    const changeOrders = await screen.findByRole("switch", { name: /^Aditivos/ });
    expect(changeOrders).toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("switch", { name: /^Escolhas do cliente/ })).toHaveAttribute("aria-checked", "true");
    const measurements = screen.getByRole("switch", { name: /^Medições aprovadas/ });
    expect(measurements).toHaveAttribute("aria-checked", "false");

    fireEvent.click(measurements);
    fireEvent.click(screen.getByRole("button", { name: "Salvar portal" }));
    await waitFor(() => expect(portalApi.updateProjectPortal).toHaveBeenCalledTimes(1));
    expect(vi.mocked(portalApi.updateProjectPortal).mock.calls[0][1].visibility)
      .toMatchObject({ changeOrders: true, selections: true, measurements: true });
  });

  it("publish shows the backend's reason when it refuses", async () => {
    vi.mocked(portalApi.publishProjectPortal).mockRejectedValue(
      new ApiError(422, "Defina o endereço da empresa no portal do cliente antes de publicar.", "portal_company_missing"));
    renderTab(<BuildPortalTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("button", { name: "Publicar portal" }));
    expect(await screen.findByText("Defina o endereço da empresa no portal do cliente antes de publicar."))
      .toBeInTheDocument();
    expect(portalApi.publishProjectPortal).toHaveBeenCalledWith("p1");
  });

  it("with access by password and no password, the checklist says what is missing", async () => {
    vi.mocked(portalApi.fetchProjectPortal).mockResolvedValue({ ...portal, access: "Password" });
    renderTab(<BuildPortalTab projectId="p1" />);
    expect(await screen.findByText("Senha do portal definida")).toBeInTheDocument();
    expect(screen.getByText("Nenhuma senha definida")).toBeInTheDocument();
  });
});
