import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, act } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ApiError } from "@/services/api-client";
import * as twinApi from "../../api/build-twin.api";
import type { BuildProjectDetailsDto, BuildStageDto } from "../../api/build.api";
import { TwinOnboarding } from "./TwinOnboarding";
import { VersionProcessing } from "./VersionProcessing";
import { TwinViewerPanel } from "./TwinViewerPanel";
import { SLIDER_DEBOUNCE_MS } from "../../lib/twin-forms";
import { BindingsSection } from "./BindingsSection";
import { SimplifiedRepresentation } from "./SimplifiedRepresentation";

// No WebGL in jsdom: three.js, web-ifc and the lazy viewer modules are mocked.
vi.mock("three", () => ({}));
vi.mock("web-ifc", () => ({ IfcAPI: vi.fn() }));
vi.mock("./viewer/ModelViewer3D", () => ({ default: () => <div data-testid="viewer-mock" /> }));
vi.mock("./viewer/Pano360Viewer", () => ({ default: () => <div data-testid="pano-mock" /> }));
vi.mock("./viewer/convert-ifc", () => ({ convertIfcToGlb: vi.fn() }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

vi.mock("../../api/build-twin.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-twin.api")>()),
  uploadModel:        vi.fn(),
  uploadModelVersion: vi.fn(),
  fetchModelVersion:  vi.fn(),
  fetchTwinState:     vi.fn(),
  fetchModelViewer:   vi.fn(),
  fetchElements:      vi.fn(),
  fetchElementEvents: vi.fn(),
  fetchVersionZones:  vi.fn(),
  fetchBindings:      vi.fn(),
  bulkBind:           vi.fn(),
  reprocessModelVersion: vi.fn(),
}));

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

const version = (over: Partial<twinApi.BuildModelVersionDto> = {}): twinApi.BuildModelVersionDto => ({
  id: "v1", modelId: "m1", number: 1, sourceFormat: "Ifc", sourceFileName: "casa.ifc", sizeBytes: 2048, sha256: null,
  geometrySource: "NativeBim", viewerSource: "None", hasViewer: false, status: "Queued", statusMessage: null, elementCount: 0,
  storeyCount: 0, schema: null, layers: null, processedAt: null, createdAt: "2026-10-01T10:00:00Z", ...over,
});

const stage = (id: string, name: string): BuildStageDto => ({
  id, projectId: "p1", name, description: null, order: 0, status: "InProgress", plannedStartDate: "2026-09-01",
  plannedEndDate: "2026-12-01", actualStartDate: null, actualEndDate: null, progressPercent: 30,
});

afterEach(() => { cleanup(); vi.clearAllMocks(); vi.useRealTimers(); });

describe("TwinOnboarding", () => {
  it("explains the sources, with the RVT/DWG export instructions, and asks for the file", () => {
    wrap(<TwinOnboarding projectId="p1" build3DEnabled onNoProject={vi.fn()} onBind={vi.fn()} />);
    expect(screen.getByText("Conecte o projeto arquitetônico para visualizar digitalmente a evolução da obra.")).toBeInTheDocument();
    const formats = screen.getByTestId("twin-formats");
    expect(formats).toHaveTextContent("IFC (IFC2X3 / IFC4)");
    expect(formats).toHaveTextContent("Revit: Arquivo → Exportar → IFC; AutoCAD/Civil: exportar IFC");
    expect(formats).toHaveTextContent("Representação simplificada");
    fireEvent.click(screen.getByRole("button", { name: "Conectar projeto" }));
    expect(screen.getByLabelText("Arquivo do projeto")).toBeInTheDocument();
    expect(screen.getByText(/até 150 MB/)).toBeInTheDocument();
  });

  it("a .rvt picked locally shows the export instructions and cannot be sent", () => {
    wrap(<TwinOnboarding projectId="p1" build3DEnabled initialStep={2} onNoProject={vi.fn()} onBind={vi.fn()} />);
    const file = new File(["x"], "casa.rvt", { type: "application/octet-stream" });
    fireEvent.change(screen.getByLabelText("Arquivo do projeto"), { target: { files: [file] } });
    expect(screen.getByRole("alert")).toHaveTextContent("Revit: Arquivo → Exportar → IFC");
    expect(screen.getByRole("button", { name: "Enviar projeto" })).toBeDisabled();
    expect(twinApi.uploadModel).not.toHaveBeenCalled();
  });

  it("403 build_3d_disabled → 'O 3D não está habilitado para sua empresa…'", async () => {
    vi.mocked(twinApi.uploadModel).mockRejectedValue(
      new ApiError(403, "desligado", "build_3d_disabled", undefined, undefined, "O módulo 3D está desligado."));
    wrap(<TwinOnboarding projectId="p1" build3DEnabled initialStep={2} onNoProject={vi.fn()} onBind={vi.fn()} />);
    fireEvent.change(screen.getByLabelText("Nome do modelo"), { target: { value: "Arquitetônico" } });
    fireEvent.change(screen.getByLabelText("Arquivo do projeto"), { target: { files: [new File(["ISO-10303-21;"], "casa.ifc")] } });
    fireEvent.click(screen.getByRole("button", { name: "Enviar projeto" }));
    expect(await screen.findByTestId("model-upload-error"))
      .toHaveTextContent("O 3D não está habilitado para sua empresa. Fale com o suporte Orken.");
    const [projectId, input] = vi.mocked(twinApi.uploadModel).mock.calls[0];
    expect(projectId).toBe("p1");
    expect(input).toMatchObject({ name: "Arquitetônico", discipline: "Arquitetura" });
  });

  it("422 from the server keeps its own sentence", async () => {
    vi.mocked(twinApi.uploadModel).mockRejectedValue(new ApiError(422, "x", "model_format_compressed", undefined, undefined,
      "Arquivos compactados (.ifczip/.zip) não são aceitos: descompacte e envie o .ifc."));
    wrap(<TwinOnboarding projectId="p1" build3DEnabled initialStep={2} onNoProject={vi.fn()} onBind={vi.fn()} />);
    fireEvent.change(screen.getByLabelText("Arquivo do projeto"), { target: { files: [new File(["PK"], "casa.ifczip")] } });
    fireEvent.click(screen.getByRole("button", { name: "Enviar projeto" }));
    expect(await screen.findByTestId("model-upload-error")).toHaveTextContent("descompacte e envie o .ifc");
  });

  it("3D off for the company: the start is blocked with the support message", () => {
    wrap(<TwinOnboarding projectId="p1" build3DEnabled={false} onNoProject={vi.fn()} onBind={vi.fn()} />);
    expect(screen.getByRole("alert")).toHaveTextContent("O 3D não está habilitado para sua empresa");
    expect(screen.getByRole("button", { name: "Conectar projeto" })).toBeDisabled();
  });
});

describe("VersionProcessing", () => {
  it("polls every 3 s while queued/processing and stops once Ready", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.mocked(twinApi.fetchModelVersion)
      .mockResolvedValueOnce(version({ status: "Queued" }))
      .mockResolvedValueOnce(version({ status: "Processing" }))
      .mockResolvedValue(version({ status: "Ready", elementCount: 42, storeyCount: 3 }));
    const onContinue = vi.fn();
    wrap(<VersionProcessing projectId="p1" versionId="v1" build3DEnabled onContinue={onContinue} />);

    expect(await screen.findByText("Na fila")).toBeInTheDocument();
    await act(async () => { await vi.advanceTimersByTimeAsync(3000); });
    expect(await screen.findByText("Processando")).toBeInTheDocument();
    await act(async () => { await vi.advanceTimersByTimeAsync(3000); });
    expect(await screen.findByText("Pronto")).toBeInTheDocument();
    expect(screen.getByText("42")).toBeInTheDocument();
    const calls = vi.mocked(twinApi.fetchModelVersion).mock.calls.length;
    expect(calls).toBe(3);

    await act(async () => { await vi.advanceTimersByTimeAsync(12_000); });
    expect(vi.mocked(twinApi.fetchModelVersion).mock.calls.length).toBe(calls);   // stopped
    fireEvent.click(screen.getByRole("button", { name: "Continuar: vincular etapas" }));
    expect(onContinue).toHaveBeenCalled();
  });

  it("a failed version offers 'Reprocessar'", async () => {
    vi.mocked(twinApi.fetchModelVersion).mockResolvedValue(version({ status: "Failed", statusMessage: "Arquivo ilegível." }));
    vi.mocked(twinApi.reprocessModelVersion).mockResolvedValue(version({ status: "Queued" }));
    wrap(<VersionProcessing projectId="p1" versionId="v1" build3DEnabled />);
    expect(await screen.findByText("Arquivo ilegível.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Reprocessar/ }));
    await waitFor(() => expect(twinApi.reprocessModelVersion).toHaveBeenCalledWith("v1"));
  });
});

describe("TwinViewerPanel", () => {
  const project = {
    id: "p1", startDate: "2026-10-01", stages: [stage("s1", "Fundação")], createdAt: "2026-09-01T00:00:00Z",
  } as unknown as BuildProjectDetailsDto;

  beforeEach(() => {
    vi.mocked(twinApi.fetchTwinState).mockImplementation(async (_v, date) => ({
      date: date!, counts: { Completed: 1 }, states: { Completed: ["a"] }, zoneStates: {}, unbound: 4, total: 5,
    }));
  });

  it("the time slider asks the state of the chosen date ('como estava a obra em X')", async () => {
    wrap(<TwinViewerPanel projectId="p1" project={project} version={version({ sourceFormat: "Glb", status: "Ready", geometrySource: "Converted3D" })} build3DEnabled />);
    await waitFor(() => expect(twinApi.fetchTwinState).toHaveBeenCalled());
    const today = vi.mocked(twinApi.fetchTwinState).mock.calls[0][1];
    expect(today).toMatch(/^\d{4}-\d{2}-\d{2}$/);

    const slider = screen.getByLabelText(/Como estava a obra em/);
    fireEvent.change(slider, { target: { value: "0" } });
    await waitFor(() => expect(twinApi.fetchTwinState).toHaveBeenCalledWith("v1", "2026-10-01"), { timeout: SLIDER_DEBOUNCE_MS + 2000 });
    const legend = screen.getByTestId("twin-legend");
    expect(legend).toHaveTextContent("Concluído");
    expect(legend).toHaveTextContent("Sem vínculo");
  });

  it("IFC Ready without viewer offers the conversion in the browser", async () => {
    wrap(<TwinViewerPanel projectId="p1" project={project} version={version({ status: "Ready", elementCount: 10 })} build3DEnabled />);
    expect(await screen.findByRole("button", { name: "Gerar visualização 3D no navegador" })).toBeEnabled();
    expect(screen.getByTestId("ifc-browser-convert")).toHaveTextContent("casa.ifc");
  });
});

describe("BindingsSection", () => {
  const items: twinApi.BuildModelElementDto[] = ["w1", "w2", "w3"].map((id) => ({
    externalId: id, category: "IfcWall", name: `Parede ${id}`, typeName: null, material: null, zoneId: "z1", zoneName: "Térreo",
    properties: null, state: null, stageId: null, explicitState: false,
  }));

  beforeEach(() => {
    vi.mocked(twinApi.fetchElements).mockResolvedValue({ items, total: 3, skip: 0, take: 50, categories: ["IfcWall", "IfcSlab"] });
    vi.mocked(twinApi.fetchVersionZones).mockResolvedValue([{ id: "z1", modelVersionId: "v1", kind: "Storey", name: "Térreo",
      externalId: null, parentZoneId: null, elevation: 0, order: 0, outline: null, sheetVersionId: null, simplifiedBox: null, elementCount: 3 }]);
    vi.mocked(twinApi.fetchBindings).mockResolvedValue([]);
    vi.mocked(twinApi.bulkBind).mockResolvedValue({ matched: 2, created: 2, replaced: 0 });
  });

  const renderSection = () => wrap(<BindingsSection projectId="p1" version={version({ status: "Ready", elementCount: 3 })}
    stages={[stage("s1", "Fundação"), stage("s2", "Alvenaria")]} coveragePercent={25} />);

  it("multi-select → 'Vincular à etapa' sends the chosen ids of the version", async () => {
    renderSection();
    fireEvent.click(await screen.findByLabelText("Selecionar Parede w1"));
    fireEvent.click(screen.getByLabelText("Selecionar Parede w3"));
    fireEvent.change(screen.getByLabelText("Etapa para os selecionados"), { target: { value: "s2" } });
    fireEvent.click(screen.getByRole("button", { name: "Vincular à etapa" }));
    await waitFor(() => expect(twinApi.bulkBind).toHaveBeenCalledTimes(1));
    expect(vi.mocked(twinApi.bulkBind).mock.calls[0]).toEqual(["p1",
      { stageId: "s2", modelVersionId: "v1", externalIds: ["w1", "w3"], replaceExisting: true }]);
    expect(screen.getByTestId("binding-coverage")).toHaveTextContent("25%");
  });

  it("bulk by category + zone sends the filters, never an empty filter", async () => {
    renderSection();
    await screen.findByLabelText("Selecionar Parede w1");
    fireEvent.change(screen.getByLabelText("Etapa"), { target: { value: "s2" } });
    const go = screen.getByRole("button", { name: "Vincular em lote" });
    expect(go).toBeDisabled();                                   // no category nor zone yet
    fireEvent.change(screen.getByLabelText("Categoria"), { target: { value: "IfcWall" } });
    fireEvent.change(screen.getByLabelText("Zona"), { target: { value: "z1" } });
    fireEvent.click(go);
    await waitFor(() => expect(twinApi.bulkBind).toHaveBeenCalledWith("p1",
      { stageId: "s2", modelVersionId: "v1", category: "IfcWall", zoneId: "z1", replaceExisting: true }));
  });
});

describe("SimplifiedRepresentation", () => {
  it("always carries the mandatory label and draws only the typed boxes", () => {
    const zone = (id: string, name: string, elevation: number | null) => ({
      id, modelVersionId: null, kind: "Floor" as const, name, externalId: null, parentZoneId: null, elevation: null, order: 0,
      outline: null, sheetVersionId: null, elementCount: 0,
      simplifiedBox: elevation == null ? null : { width: 10, depth: 8, height: 3, elevation },
    });
    render(<SimplifiedRepresentation zones={[zone("a", "Térreo", 0), zone("b", "Superior", 3), zone("c", "Sem medida", null)]}
      zoneStates={new Map([["a", "Completed"]])} />);
    expect(screen.getByText("Representação simplificada — não corresponde ao projeto arquitetônico")).toBeInTheDocument();
    expect(screen.getByText("Térreo")).toBeInTheDocument();
    expect(screen.getByText("Superior")).toBeInTheDocument();
    expect(screen.queryByText("Sem medida")).not.toBeInTheDocument();
    expect(screen.getByText("Concluído")).toBeInTheDocument();
  });
});
