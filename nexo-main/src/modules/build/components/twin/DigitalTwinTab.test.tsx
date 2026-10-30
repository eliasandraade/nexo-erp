import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";
import * as twinApi from "../../api/build-twin.api";
import type { BuildProjectDetailsDto, BuildStageDto } from "../../api/build.api";
import { DigitalTwinTab } from "./DigitalTwinTab";

vi.mock("three", () => ({}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

// The sections have their own tests: here they are stubs that expose what the tab hands them.
vi.mock("./TwinOnboarding", () => ({
  TwinOnboarding: (p: { initialStep?: number; build3DEnabled: boolean; onCancel?: () => void; onNoProject: () => void;
    onBind: (v: twinApi.BuildModelVersionDto) => void }) => (
    <div data-testid="onboarding" data-step={p.initialStep} data-enabled={String(p.build3DEnabled)}>
      {p.onCancel && <button type="button" onClick={p.onCancel}>stub-cancel</button>}
      <button type="button" onClick={p.onNoProject}>stub-no-project</button>
      <button type="button" onClick={() => p.onBind({ id: "v9", geometrySource: "AssistedMapping" } as twinApi.BuildModelVersionDto)}>stub-bind-sheet</button>
      <button type="button" onClick={() => p.onBind({ id: "v1", geometrySource: "NativeBim" } as twinApi.BuildModelVersionDto)}>stub-bind-3d</button>
    </div>
  ),
}));
vi.mock("./TwinViewerPanel", () => ({
  TwinViewerPanel: ({ version }: { version: twinApi.BuildModelVersionDto }) => <div data-testid="viewer-panel">{version.id}</div>,
}));
vi.mock("./ZonesSection", () => ({
  ZonesSection: ({ models }: { models: twinApi.BuildModelDto[] }) => <div data-testid="zones-section">{models.length} modelos</div>,
}));
vi.mock("./BindingsSection", () => ({
  BindingsSection: ({ version, coveragePercent }: { version: twinApi.BuildModelVersionDto | null; coveragePercent: number | null }) =>
    <div data-testid="bindings-section">{version?.id ?? "sem versão"} · {String(coveragePercent)}</div>,
}));
vi.mock("../ai/ModelMapperAi", () => ({
  ModelMapperAi: ({ version }: { version: twinApi.BuildModelVersionDto | null }) => <div data-testid="mapper-ai">{version?.id ?? "nenhuma"}</div>,
}));
vi.mock("./RealityCaptureSection", () => ({
  RealityCaptureSection: ({ zones }: { zones: twinApi.BuildModelZoneDto[] }) => <div data-testid="capture-section">{zones.length} zonas</div>,
}));
vi.mock("./ModelUploadForm", () => ({
  ModelUploadForm: ({ modelId, onCancel, onUploaded }: { modelId?: string; onCancel: () => void;
    onUploaded: (r: twinApi.BuildModelUploadResultDto) => void }) => (
    <div data-testid="upload-form">
      {modelId}
      <button type="button" onClick={onCancel}>stub-cancel-upload</button>
      <button type="button" onClick={() => onUploaded({ version: { id: "v3" } } as twinApi.BuildModelUploadResultDto)}>stub-uploaded</button>
    </div>
  ),
}));

vi.mock("../../api/build-twin.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-twin.api")>()),
  fetchBuildSettings:    vi.fn(),
  fetchTwinOverview:     vi.fn(),
  fetchModels:           vi.fn(),
  fetchProjectZones:     vi.fn(),
  reprocessModelVersion: vi.fn(),
}));

const stage: BuildStageDto = {
  id: "s1", projectId: "p1", name: "Fundação", description: null, order: 0, status: "InProgress", plannedStartDate: null,
  plannedEndDate: null, actualStartDate: null, actualEndDate: null, progressPercent: 30,
};
const project = { id: "p1", startDate: "2026-10-01", stages: [stage], createdAt: "2026-09-01T00:00:00Z" } as unknown as BuildProjectDetailsDto;

const version = (over: Partial<twinApi.BuildModelVersionDto> = {}): twinApi.BuildModelVersionDto => ({
  id: "v1", modelId: "m1", number: 1, sourceFormat: "Ifc", sourceFileName: "casa.ifc", sizeBytes: 2048, sha256: null,
  geometrySource: "NativeBim", viewerSource: "Uploaded", hasViewer: true, status: "Ready", statusMessage: null, elementCount: 120,
  storeyCount: 2, schema: "IFC4", layers: null, processedAt: null, createdAt: "2026-10-01T10:00:00Z", ...over,
});

const model = (versions: twinApi.BuildModelVersionDto[], over: Partial<twinApi.BuildModelDto> = {}): twinApi.BuildModelDto => ({
  id: "m1", projectId: "p1", name: "Arquitetônico", discipline: "Arquitetura", currentVersionId: versions[0]?.id ?? null,
  versions, createdAt: "2026-10-01T10:00:00Z", ...over,
});

const settings = (build3DEnabled = true): twinApi.BuildTenantSettingsDto => ({
  tenantId: "t1", build3DEnabled, buildAiEnabled: false, buildVisionEnabled: false, buildPredictEnabled: false,
  customized: true, updatedByName: null, updatedAt: null,
});

const overview: twinApi.BuildTwinOverviewDto = {
  projectId: "p1", build3DEnabled: true, geometrySource: "NativeBim", models: [], manualZones: 1, totalElements: 120,
  boundElements: 30, bindingCoveragePercent: 25.5, pendingJobs: 0, stages: [],
};

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("DigitalTwinTab", () => {
  beforeEach(() => {
    vi.mocked(twinApi.fetchBuildSettings).mockResolvedValue(settings());
    vi.mocked(twinApi.fetchTwinOverview).mockResolvedValue(overview);
    vi.mocked(twinApi.fetchProjectZones).mockResolvedValue([]);
  });
  afterEach(() => { cleanup(); vi.clearAllMocks(); vi.useRealTimers(); });

  it("without models shows the onboarding (step 1, no cancel); 'sem projeto' goes to the zones", async () => {
    vi.mocked(twinApi.fetchModels).mockResolvedValue([]);
    wrap(<DigitalTwinTab project={project} />);

    const onboarding = await screen.findByTestId("onboarding");
    expect(onboarding).toHaveAttribute("data-step", "1");
    expect(within(onboarding).queryByText("stub-cancel")).not.toBeInTheDocument();
    fireEvent.click(within(onboarding).getByText("stub-no-project"));
    expect(await screen.findByTestId("zones-section")).toHaveTextContent("0 modelos");
    expect(screen.getByRole("tab", { name: "Zonas e planta" })).toHaveAttribute("aria-selected", "true");
  });

  it("shows the overview summary and the models with their versions; the Ready 3D version opens the viewer", async () => {
    vi.mocked(twinApi.fetchModels).mockResolvedValue([model([
      version(),
      version({ id: "v0", number: 0, status: "Failed", statusMessage: "IFC corrompido", hasViewer: false, elementCount: 0 }),
    ], { discipline: "Hidraulica" })]);
    wrap(<DigitalTwinTab project={project} />);

    const summary = await screen.findByTestId("twin-summary");
    expect(summary).toHaveTextContent("Modelo BIM (IFC)");
    expect(summary).toHaveTextContent("30 de 120 vinculados");
    expect(summary).toHaveTextContent("25,5%");
    expect(await screen.findByText("Modelos da obra")).toBeInTheDocument();
    expect(screen.getByText(/· Hidráulica/)).toBeInTheDocument();
    expect(screen.getByText(/v1 · casa\.ifc/)).toBeInTheDocument();
    expect(screen.getByText(/120 elementos .* · visualização 3D/)).toBeInTheDocument();
    expect(screen.getByText("IFC corrompido")).toBeInTheDocument();
    expect(screen.getByText("Pronto")).toBeInTheDocument();
    expect(screen.getByText("Falhou")).toBeInTheDocument();
    expect(screen.getByText("atual")).toBeInTheDocument();
    expect(screen.getByTestId("viewer-panel")).toHaveTextContent("v1");
    expect(screen.queryByTestId("onboarding")).not.toBeInTheDocument();
  });

  it("reprocessing a failed version calls the API; an error is toasted", async () => {
    vi.mocked(twinApi.fetchModels).mockResolvedValue([model([version({ id: "v0", number: 3, status: "Failed" })])]);
    vi.mocked(twinApi.reprocessModelVersion).mockRejectedValue(new Error("Fila cheia"));
    wrap(<DigitalTwinTab project={project} />);

    fireEvent.click(await screen.findByRole("button", { name: "Reprocessar v3" }));
    await waitFor(() => expect(twinApi.reprocessModelVersion).toHaveBeenCalledWith("v0"));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Fila cheia"));
  });

  it("3D disabled: shows the warning, hides upload actions and disables reprocessing", async () => {
    vi.mocked(twinApi.fetchBuildSettings).mockResolvedValue(settings(false));
    vi.mocked(twinApi.fetchModels).mockResolvedValue([model([version({ status: "Failed" })])]);
    wrap(<DigitalTwinTab project={project} />);

    expect(await screen.findByRole("alert")).toHaveTextContent("O 3D não está habilitado para sua empresa. Fale com o suporte Orken. As leituras continuam disponíveis.");
    expect(await screen.findByRole("button", { name: "Reprocessar v1" })).toBeDisabled();
    expect(screen.queryByRole("button", { name: /Novo modelo/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Nova versão/ })).not.toBeInTheDocument();
  });

  it("'Novo modelo' opens the onboarding at step 2 with cancel; binding a sheet goes to the zones", async () => {
    vi.mocked(twinApi.fetchModels).mockResolvedValue([model([version()])]);
    wrap(<DigitalTwinTab project={project} />);

    fireEvent.click(await screen.findByRole("button", { name: /Novo modelo/ }));
    const onboarding = screen.getByTestId("onboarding");
    expect(onboarding).toHaveAttribute("data-step", "2");
    fireEvent.click(within(onboarding).getByText("stub-cancel"));
    expect(screen.queryByTestId("onboarding")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /Novo modelo/ }));
    fireEvent.click(screen.getByText("stub-bind-sheet"));
    expect(await screen.findByTestId("zones-section")).toBeInTheDocument();
  });

  it("binding a 3D version goes to 'Vínculos' with that version for the mapper and the bindings", async () => {
    vi.mocked(twinApi.fetchModels).mockResolvedValue([model([version()])]);
    wrap(<DigitalTwinTab project={project} />);

    fireEvent.click(await screen.findByRole("button", { name: /Novo modelo/ }));
    fireEvent.click(screen.getByText("stub-bind-3d"));
    expect(await screen.findByTestId("mapper-ai")).toHaveTextContent("v1");
    expect(screen.getByTestId("bindings-section")).toHaveTextContent("v1 · 25.5");
  });

  it("selecting a 2D sheet version offers to draw zones; bindings fall back to a Ready version with elements", async () => {
    vi.mocked(twinApi.fetchModels).mockResolvedValue([
      model([version()]),
      model([version({ id: "v5", modelId: "m2", number: 1, sourceFormat: "Pdf", sourceFileName: "planta.pdf",
        geometrySource: "AssistedMapping", hasViewer: false, elementCount: 0 })],
      { id: "m2", name: "Planta", discipline: "Outro", createdAt: "2026-10-02T10:00:00Z" }),
    ]);
    wrap(<DigitalTwinTab project={project} />);

    fireEvent.click(await screen.findByRole("button", { name: /v1 · planta\.pdf/ }));
    expect(screen.getByRole("button", { name: /v1 · planta\.pdf/ })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByText(/Esta versão é uma planta 2D/)).toBeInTheDocument();
    expect(screen.queryByTestId("viewer-panel")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("tab", { name: "Vínculos" }));
    expect(await screen.findByTestId("bindings-section")).toHaveTextContent("v1 ·");
    expect(screen.getByTestId("mapper-ai")).toHaveTextContent("v5");

    fireEvent.click(screen.getByRole("tab", { name: "Modelo" }));
    fireEvent.click(screen.getByRole("button", { name: "Desenhar as zonas na planta" }));
    expect(await screen.findByTestId("zones-section")).toHaveTextContent("2 modelos");
  });

  it("'Nova versão' shows the upload form for that model; uploading selects the new version", async () => {
    vi.mocked(twinApi.fetchModels).mockResolvedValue([model([version()])]);
    wrap(<DigitalTwinTab project={project} />);

    fireEvent.click(await screen.findByRole("button", { name: /Nova versão/ }));
    expect(screen.getByTestId("upload-form")).toHaveTextContent("m1");
    fireEvent.click(screen.getByText("stub-cancel-upload"));
    expect(screen.queryByTestId("upload-form")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /Nova versão/ }));
    fireEvent.click(screen.getByText("stub-uploaded"));
    expect(toast.success).toHaveBeenCalledWith("Nova versão enviada.");
    expect(screen.queryByTestId("upload-form")).not.toBeInTheDocument();
  });

  it("the capture section gets the project zones", async () => {
    vi.mocked(twinApi.fetchModels).mockResolvedValue([]);
    vi.mocked(twinApi.fetchProjectZones).mockResolvedValue([{ id: "z1" } as twinApi.BuildModelZoneDto]);
    wrap(<DigitalTwinTab project={project} />);

    await screen.findByTestId("onboarding");
    fireEvent.click(screen.getByRole("tab", { name: "Captura da realidade" }));
    expect(await screen.findByTestId("capture-section")).toHaveTextContent("1 zonas");
  });

  it("a models load failure shows an alert; an unexpected overview body hides the summary", async () => {
    vi.mocked(twinApi.fetchModels).mockRejectedValue(new Error("404"));
    vi.mocked(twinApi.fetchTwinOverview).mockResolvedValue({} as twinApi.BuildTwinOverviewDto);
    wrap(<DigitalTwinTab project={project} />);

    expect(await screen.findByText("Não foi possível carregar os modelos da obra.")).toBeInTheDocument();
    expect(screen.queryByTestId("twin-summary")).not.toBeInTheDocument();
  });

  it("keeps refetching the models every 3 s while a version is still processing", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.mocked(twinApi.fetchModels).mockResolvedValue([model([version({ status: "Processing", hasViewer: false })])]);
    wrap(<DigitalTwinTab project={project} />);

    expect(await screen.findByText("Processando")).toBeInTheDocument();
    const before = vi.mocked(twinApi.fetchModels).mock.calls.length;
    await vi.advanceTimersByTimeAsync(3100);
    await waitFor(() => expect(vi.mocked(twinApi.fetchModels).mock.calls.length).toBeGreaterThan(before));
  });
});
