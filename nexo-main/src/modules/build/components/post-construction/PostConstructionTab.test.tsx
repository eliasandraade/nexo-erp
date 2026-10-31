import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ApiError } from "@/services/api-client";
import { PostConstructionTab } from "./PostConstructionTab";
import * as buildApi from "../../api/build.api";
import * as qualityApi from "../../api/build-quality.api";

vi.mock("../../api/build.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build.api")>()),
  fetchStages: vi.fn(),
}));

vi.mock("../../api/build-quality.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-quality.api")>()),
  fetchHandover:          vi.fn(),
  deliverHandover:        vi.fn(),
  fetchWarranty:          vi.fn(),
  createDefaultWarranty:  vi.fn(),
  fetchAssistance:        vi.fn(),
  triageAssistance:       vi.fn(),
  fetchChecklistTemplates: vi.fn(),
}));

const handover = (over: Partial<qualityApi.BuildHandoverDto> = {}): qualityApi.BuildHandoverDto => ({
  id: "h1", projectId: "p1", status: "InspectionDone", scheduledFor: "2026-12-01", walkthroughInspectionId: "in9",
  walkthroughStatus: "PassedWithRemarks", deliveredAt: null, deliveredByName: null, keysDelivered: false,
  documents: [{ name: "Manual do proprietário", fileKey: "t/build/handover/m.pdf", fileUrl: "https://signed/m.pdf" }],
  notes: null, acceptedAt: null, acceptedByName: null, acceptanceChannel: null, acceptanceComment: null,
  projectStatus: "InProgress", projectStatusNote: null, openCriticalNonConformities: 1,
  createdAt: "2026-11-01T10:00:00Z", updatedAt: "2026-11-01T10:00:00Z", ...over,
});

const request = (over: Partial<qualityApi.BuildAssistanceDto> = {}): qualityApi.BuildAssistanceDto => ({
  id: "a1", publicId: "pub", projectId: "p1", projectName: "Casa", number: 4, category: "Impermeabilização",
  title: "Infiltração no banheiro", description: "Mancha no teto", location: "Banheiro social", openedVia: "Portal",
  openedByName: "Maria", contactPhone: null, openedOn: "2027-03-02", status: "Triaged", triageNotes: null,
  warrantyCovered: true, warrantyTermId: "w1", warrantySystem: "Impermeabilização", triagedAt: "2027-03-03T10:00:00Z",
  triagedByName: "Ana", scheduledFor: null, startedAt: null, resolvedAt: null, resolutionNotes: null, closedAt: null,
  rejectedAt: null, rejectionReason: null, clientRating: null, clientFeedback: null, photoKeys: [], photoUrls: [],
  createdAt: "2027-03-02T10:00:00Z", updatedAt: "2027-03-02T10:00:00Z", ...over,
});

function renderTab(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("PostConstructionTab", () => {
  beforeEach(() => {
    vi.mocked(buildApi.fetchStages).mockResolvedValue([]);
    vi.mocked(qualityApi.fetchChecklistTemplates).mockResolvedValue([]);
    vi.mocked(qualityApi.fetchWarranty).mockResolvedValue([]);
  });
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("delivering with an open critical NC shows the backend's 422 reason", async () => {
    vi.mocked(qualityApi.fetchHandover).mockResolvedValue(handover());
    vi.mocked(qualityApi.deliverHandover).mockRejectedValue(new ApiError(422,
      "A obra tem 1 não conformidade(s) crítica(s) aberta(s); resolva antes da entrega.", "handover_critical_nonconformity"));
    renderTab(<PostConstructionTab projectId="p1" />);

    expect(await screen.findByText("Vistoria concluída")).toBeInTheDocument();
    expect(await screen.findByText("Manual do proprietário")).toHaveAttribute("href", "https://signed/m.pdf");
    expect(screen.getByText(/1 não conformidade\(s\) crítica\(s\) aberta\(s\): resolva antes/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /Entregar imóvel/ }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/a entrega é recusada/)).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Entregar imóvel" }));

    await waitFor(() => expect(qualityApi.deliverHandover).toHaveBeenCalledWith("p1", false));
    expect(await screen.findByTestId("handover-error"))
      .toHaveTextContent("A obra tem 1 não conformidade(s) crítica(s) aberta(s); resolva antes da entrega.");
  });

  it("shows the client acceptance with its channel", async () => {
    vi.mocked(qualityApi.fetchHandover).mockResolvedValue(handover({
      status: "Delivered", deliveredAt: "2026-12-01T15:00:00Z", acceptedAt: "2026-12-02T10:00:00Z",
      acceptedByName: "Maria Souza", acceptanceChannel: "Portal", acceptanceComment: "Tudo certo", openCriticalNonConformities: 0,
    }));
    renderTab(<PostConstructionTab projectId="p1" />);

    expect(await screen.findByText("Aceito")).toBeInTheDocument();
    expect(screen.getByText("Portal")).toBeInTheDocument();
    expect(screen.getByText(/Maria Souza em/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Entregar imóvel/ })).not.toBeInTheDocument();
  });

  it("warranty: the defaults button carries the 'not legal advice' disclaimer and creates the reference set", async () => {
    vi.mocked(qualityApi.fetchHandover).mockResolvedValue(null);
    vi.mocked(qualityApi.createDefaultWarranty).mockResolvedValue([]);
    renderTab(<PostConstructionTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("tab", { name: "Garantias" }));
    expect(await screen.findByTestId("warranty-disclaimer")).toHaveTextContent("referência, não é orientação jurídica");
    fireEvent.click(await screen.findByRole("button", { name: /Criar garantias padrão/ }));
    await waitFor(() => expect(qualityApi.createDefaultWarranty).toHaveBeenCalledWith("p1"));
  });

  it("warranty list: expiry and active badge", async () => {
    vi.mocked(qualityApi.fetchHandover).mockResolvedValue(null);
    vi.mocked(qualityApi.fetchWarranty).mockResolvedValue([
      { id: "w1", projectId: "p1", system: "Estrutura", months: 60, startsAt: "2026-12-01", expiresAt: "2031-12-01",
        isActive: true, description: null, isDefault: true },
      { id: "w2", projectId: "p1", system: "Pintura", months: 24, startsAt: null, expiresAt: null,
        isActive: false, description: null, isDefault: false },
    ]);
    renderTab(<PostConstructionTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("tab", { name: "Garantias" }));
    expect(await screen.findByText("Ativa")).toBeInTheDocument();
    expect(screen.getByText(/até 01 de dez\. de 2031/)).toBeInTheDocument();
    expect(screen.getByText("Começa na entrega")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Criar garantias padrão/ })).not.toBeInTheDocument();
  });

  it("assistance triage shows the computed coverage and sends the company's override", async () => {
    vi.mocked(qualityApi.fetchHandover).mockResolvedValue(null);
    vi.mocked(qualityApi.fetchAssistance).mockResolvedValue([request()]);
    vi.mocked(qualityApi.triageAssistance).mockResolvedValue(request({ warrantyCovered: false, triageNotes: "Mau uso" }));
    renderTab(<PostConstructionTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("tab", { name: "Assistência técnica" }));
    fireEvent.click(await screen.findByText("Infiltração no banheiro"));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Aberto via Portal")).toBeInTheDocument();
    const coverage = within(dialog).getByTestId("assistance-coverage");
    expect(coverage).toHaveTextContent("Coberto pela garantia — garantia de Impermeabilização");

    fireEvent.change(within(dialog).getByLabelText("Cobertura"), { target: { value: "no" } });
    fireEvent.change(within(dialog).getByLabelText(/Notas da triagem/), { target: { value: "Mau uso" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Triar chamado" }));
    await waitFor(() => expect(qualityApi.triageAssistance)
      .toHaveBeenCalledWith("a1", { notes: "Mau uso", warrantyCovered: false }));
    await waitFor(() => expect(within(dialog).getByTestId("assistance-coverage"))
      .toHaveTextContent("Fora da garantia — garantia de Impermeabilização"));
  });

  it("assistance triage on automatic sends no coverage (the server evaluates it)", async () => {
    vi.mocked(qualityApi.fetchHandover).mockResolvedValue(null);
    vi.mocked(qualityApi.fetchAssistance).mockResolvedValue([request({ status: "Open", warrantyCovered: null, triagedAt: null, clientRating: 4, clientFeedback: "Rápido" })]);
    vi.mocked(qualityApi.triageAssistance).mockResolvedValue(request());
    renderTab(<PostConstructionTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("tab", { name: "Assistência técnica" }));
    fireEvent.click(await screen.findByText("Infiltração no banheiro"));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByTestId("assistance-rating")).toHaveTextContent("“Rápido”");
    fireEvent.click(within(dialog).getByRole("button", { name: "Triar chamado" }));
    await waitFor(() => expect(qualityApi.triageAssistance).toHaveBeenCalledWith("a1", { notes: null }));
  });
});
