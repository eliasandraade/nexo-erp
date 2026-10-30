import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";
import { ApiError } from "@/services/api-client";
import * as twinApi from "../../api/build-twin.api";
import type { BuildStageDto } from "../../api/build.api";
import { ZonesSection } from "./ZonesSection";
import { SheetMapper } from "./SheetMapper";

vi.mock("three", () => ({}));
vi.mock("./viewer/ModelViewer3D", () => ({ default: () => <div data-testid="viewer-mock" /> }));
vi.mock("./viewer/Pano360Viewer", () => ({ default: () => <div data-testid="pano-mock" /> }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

vi.mock("../../api/build-twin.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-twin.api")>()),
  fetchProjectZones: vi.fn(),
  fetchBindings:     vi.fn(),
  fetchTwinState:    vi.fn(),
  createZone:        vi.fn(),
  updateZone:        vi.fn(),
  deleteZone:        vi.fn(),
  createBinding:     vi.fn(),
  deleteBinding:     vi.fn(),
}));

const stages: BuildStageDto[] = [
  { id: "s1", projectId: "p1", name: "Estrutura", description: null, order: 0, status: "InProgress", plannedStartDate: null,
    plannedEndDate: "2099-12-01", actualStartDate: null, actualEndDate: null, progressPercent: 30 },
  { id: "s2", projectId: "p1", name: "Acabamento", description: null, order: 1, status: "Pending", plannedStartDate: null,
    plannedEndDate: null, actualStartDate: null, actualEndDate: null, progressPercent: 0 },
];

const zone = (over: Partial<twinApi.BuildModelZoneDto> = {}): twinApi.BuildModelZoneDto => ({
  id: "z1", modelVersionId: null, kind: "Floor", name: "Térreo", externalId: null, parentZoneId: null, elevation: null,
  order: 0, outline: null, sheetVersionId: null, simplifiedBox: null, elementCount: 0, ...over,
});

const binding = (over: Partial<twinApi.BuildModelStageBindingDto> = {}): twinApi.BuildModelStageBindingDto => ({
  id: "b1", stageId: "s1", stageName: "Estrutura", scope: "Zone", modelVersionId: null, elementExternalId: null, zoneId: "z1",
  category: null, source: "Manual", createdAt: "2026-10-01T00:00:00Z", ...over,
});

const version = (over: Partial<twinApi.BuildModelVersionDto> = {}): twinApi.BuildModelVersionDto => ({
  id: "v1", modelId: "m1", number: 1, sourceFormat: "Image", sourceFileName: "planta.png", sizeBytes: 2048, sha256: null,
  geometrySource: "AssistedMapping", viewerSource: "None", hasViewer: false, status: "Ready", statusMessage: null, elementCount: 0,
  storeyCount: 0, schema: null, layers: null, processedAt: null, createdAt: "2026-10-01T10:00:00Z", ...over,
});

const model = (versions: twinApi.BuildModelVersionDto[], over: Partial<twinApi.BuildModelDto> = {}): twinApi.BuildModelDto => ({
  id: "m1", projectId: "p1", name: "Planta baixa", discipline: "Arquitetura", currentVersionId: versions[0]?.id ?? null,
  versions, createdAt: "2026-10-01T10:00:00Z", ...over,
});

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

const emptyState: twinApi.BuildTwinStateDto = { date: "2026-10-30", counts: {}, states: {}, zoneStates: {}, unbound: 0, total: 0 };

describe("ZonesSection", () => {
  beforeEach(() => {
    vi.mocked(twinApi.fetchProjectZones).mockResolvedValue([]);
    vi.mocked(twinApi.fetchBindings).mockResolvedValue([]);
    vi.mocked(twinApi.fetchTwinState).mockResolvedValue(emptyState);
  });
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("lists the zones with box measures, outline, bound stages and today's state from the stages", async () => {
    vi.mocked(twinApi.fetchProjectZones).mockResolvedValue([
      zone({ simplifiedBox: { width: 10, depth: 8, height: 3, elevation: 0 } }),
      zone({ id: "z2", kind: "Room", name: "Cozinha", parentZoneId: "z1", outline: [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 1, y: 1 }] }),
    ]);
    vi.mocked(twinApi.fetchBindings).mockResolvedValue([binding()]);
    wrap(<ZonesSection projectId="p1" models={[]} stages={stages} />);

    expect(await screen.findByText("Zonas da obra (2)")).toBeInTheDocument();
    expect(screen.getByText("10 × 8 × 3 m, cota 0 m")).toBeInTheDocument();
    expect(screen.getByText(/sem caixa · contorno na planta \(3 pontos\)/)).toBeInTheDocument();
    expect(await screen.findByRole("button", { name: "Desvincular Estrutura de Térreo" })).toBeInTheDocument();
    // no processed version: the zone state is derived from the bound stage (30% → em andamento), inherited by the child
    expect(screen.getAllByText("Em andamento").length).toBeGreaterThanOrEqual(2);
    expect(twinApi.fetchTwinState).not.toHaveBeenCalled();
  });

  it("creates a zone with a simplified box", async () => {
    vi.mocked(twinApi.createZone).mockResolvedValue(zone());
    wrap(<ZonesSection projectId="p1" models={[]} stages={stages} />);

    fireEvent.click(await screen.findByRole("button", { name: /Nova zona/ }));
    const form = screen.getByTestId("zone-form");
    fireEvent.change(within(form).getByLabelText("Tipo"), { target: { value: "Area" } });
    fireEvent.change(within(form).getByLabelText("Nome"), { target: { value: " Garagem " } });
    fireEvent.change(within(form).getByLabelText("Ordem"), { target: { value: "2" } });
    fireEvent.click(within(form).getByRole("checkbox"));
    fireEvent.change(within(form).getByLabelText("Largura"), { target: { value: "5,5" } });
    fireEvent.change(within(form).getByLabelText("Profundidade"), { target: { value: "6" } });
    fireEvent.change(within(form).getByLabelText("Altura"), { target: { value: "2.8" } });
    fireEvent.click(within(form).getByRole("button", { name: "Salvar zona" }));

    await waitFor(() => expect(twinApi.createZone).toHaveBeenCalledWith("p1", {
      kind: "Area", name: "Garagem", order: 2, parentZoneId: null,
      simplifiedBox: { width: 5.5, depth: 6, height: 2.8, elevation: 0 },
    }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Zona salva."));
    await waitFor(() => expect(screen.queryByTestId("zone-form")).not.toBeInTheDocument());
  });

  it("validation: no name and invalid box measures are shown and nothing is sent; Cancelar closes", async () => {
    wrap(<ZonesSection projectId="p1" models={[]} stages={stages} />);
    fireEvent.click(await screen.findByRole("button", { name: /Nova zona/ }));
    const form = screen.getByTestId("zone-form");
    fireEvent.click(within(form).getByRole("button", { name: "Salvar zona" }));
    expect(within(form).getByRole("alert")).toHaveTextContent("Informe o nome da zona.");

    fireEvent.change(within(form).getByLabelText("Nome"), { target: { value: "Bloco A" } });
    fireEvent.click(within(form).getByRole("checkbox"));
    fireEvent.change(within(form).getByLabelText("Largura"), { target: { value: "0" } });
    fireEvent.click(within(form).getByRole("button", { name: "Salvar zona" }));
    expect(within(form).getByRole("alert")).toHaveTextContent("Informe largura, profundidade e altura maiores que zero");
    expect(twinApi.createZone).not.toHaveBeenCalled();

    fireEvent.click(within(form).getByRole("button", { name: "Cancelar" }));
    expect(screen.queryByTestId("zone-form")).not.toBeInTheDocument();
  });

  it("editing a model zone falls back to 'Área', pre-fills the box and updates; a server error is shown", async () => {
    vi.mocked(twinApi.fetchProjectZones).mockResolvedValue([
      zone({ id: "z9", kind: "Storey", name: "Pav. 1", order: 3, simplifiedBox: { width: 4, depth: 5, height: 3, elevation: 3 } }),
      zone({ id: "z1", name: "Térreo" }),
    ]);
    vi.mocked(twinApi.updateZone).mockRejectedValue(new ApiError(422, "x", undefined, undefined, undefined, "Nome repetido"));
    wrap(<ZonesSection projectId="p1" models={[]} stages={stages} />);

    fireEvent.click(await screen.findByRole("button", { name: "Editar Pav. 1" }));
    const form = screen.getByTestId("zone-form");
    expect(within(form).getByLabelText("Tipo")).toHaveValue("Area");
    expect(within(form).getByLabelText("Nome")).toHaveValue("Pav. 1");
    expect(within(form).getByLabelText("Ordem")).toHaveValue("3");
    expect(within(form).getByLabelText("Largura")).toHaveValue("4");
    expect(within(form).getByLabelText("Cota (nível)")).toHaveValue("3");
    // the zone itself is not offered as its own parent
    const parent = within(form).getByLabelText("Dentro de");
    expect(within(parent).queryByRole("option", { name: "Pav. 1" })).not.toBeInTheDocument();
    fireEvent.change(parent, { target: { value: "z1" } });
    fireEvent.click(within(form).getByRole("button", { name: "Salvar zona" }));

    await waitFor(() => expect(twinApi.updateZone).toHaveBeenCalledWith("z9", expect.objectContaining({
      kind: "Area", name: "Pav. 1", order: 3, parentZoneId: "z1", simplifiedBox: { width: 4, depth: 5, height: 3, elevation: 3 },
    })));
    expect(await within(form).findByRole("alert")).toHaveTextContent("Nome repetido");
  });

  it("deletes a zone and toasts a failure", async () => {
    vi.mocked(twinApi.fetchProjectZones).mockResolvedValue([zone()]);
    vi.mocked(twinApi.deleteZone).mockRejectedValue(new Error("Zona com vínculos"));
    wrap(<ZonesSection projectId="p1" models={[]} stages={stages} />);

    fireEvent.click(await screen.findByRole("button", { name: "Excluir Térreo" }));
    await waitFor(() => expect(twinApi.deleteZone).toHaveBeenCalledWith("z1", expect.anything()));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Zona com vínculos"));
  });

  it("binds a stage to a zone (scope Zone) and unbinds an existing one", async () => {
    vi.mocked(twinApi.fetchProjectZones).mockResolvedValue([zone()]);
    vi.mocked(twinApi.fetchBindings).mockResolvedValue([binding()]);
    vi.mocked(twinApi.createBinding).mockResolvedValue(binding({ id: "b2", stageId: "s2", stageName: "Acabamento" }));
    vi.mocked(twinApi.deleteBinding).mockResolvedValue(undefined);
    wrap(<ZonesSection projectId="p1" models={[]} stages={stages} />);

    const vincular = await screen.findByRole("button", { name: "Vincular" });
    expect(vincular).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Etapa para Térreo"), { target: { value: "s2" } });
    fireEvent.click(vincular);
    await waitFor(() => expect(twinApi.createBinding).toHaveBeenCalledWith("p1", { stageId: "s2", scope: "Zone", zoneId: "z1" }));
    await waitFor(() => expect(screen.getByLabelText("Etapa para Térreo")).toHaveValue(""));

    fireEvent.click(await screen.findByRole("button", { name: "Desvincular Estrutura de Térreo" }));
    await waitFor(() => expect(twinApi.deleteBinding).toHaveBeenCalledWith("b1", expect.anything()));
  });

  it("a failed bind is toasted", async () => {
    vi.mocked(twinApi.fetchProjectZones).mockResolvedValue([zone()]);
    vi.mocked(twinApi.createBinding).mockRejectedValue(new ApiError(403, "x", "build_3d_disabled"));
    wrap(<ZonesSection projectId="p1" models={[]} stages={stages} />);

    fireEvent.change(await screen.findByLabelText("Etapa para Térreo"), { target: { value: "s1" } });
    fireEvent.click(screen.getByRole("button", { name: "Vincular" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("O 3D não está habilitado para sua empresa. Fale com o suporte Orken."));
  });

  it("with a processed sheet: reads the zone states from the version and shows the 2D mapper", async () => {
    vi.mocked(twinApi.fetchProjectZones).mockResolvedValue([zone()]);
    vi.mocked(twinApi.fetchTwinState).mockResolvedValue({ ...emptyState, zoneStates: { Completed: ["z1"] } });
    wrap(<ZonesSection projectId="p1" models={[model([version()])]} stages={stages} />);

    expect(await screen.findByText("Mapeamento sobre a planta (2D)")).toBeInTheDocument();
    await waitFor(() => expect(twinApi.fetchTwinState).toHaveBeenCalledWith("v1", expect.any(String)));
    expect(await screen.findByText("Concluído")).toBeInTheDocument();
  });
});

describe("SheetMapper", () => {
  const sheets = [
    { model: model([version()]), version: version() },
    { model: model([version({ id: "v2", number: 2, sourceFormat: "Pdf", sourceFileName: "planta.pdf" })]),
      version: version({ id: "v2", number: 2, sourceFormat: "Pdf", sourceFileName: "planta.pdf" }) },
  ];

  beforeEach(() => {
    vi.mocked(twinApi.fetchTwinState).mockResolvedValue({ ...emptyState, zoneStates: { Delayed: ["z5"] } });
    Object.assign(URL, { createObjectURL: vi.fn(() => "blob:planta"), revokeObjectURL: vi.fn() });
    vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockReturnValue(
      { left: 0, top: 0, width: 200, height: 100, right: 200, bottom: 100, x: 0, y: 0, toJSON: () => ({}) } as DOMRect);
  });
  afterEach(() => { cleanup(); vi.clearAllMocks(); vi.restoreAllMocks(); });

  const openImage = () => fireEvent.change(screen.getByLabelText("Abrir a imagem da planta (do seu computador)"),
    { target: { files: [new File(["png"], "planta.png", { type: "image/png" })] } });

  it("asks for the local image first, then draws an outline by clicks and saves it normalized", async () => {
    vi.mocked(twinApi.createZone).mockResolvedValue(zone());
    const outlined = zone({ id: "z5", sheetVersionId: "v1", outline: [{ x: 0, y: 0 }, { x: 0.5, y: 0 }, { x: 0.5, y: 0.5 }] });
    const { container } = wrap(<SheetMapper projectId="p1" sheets={sheets} zones={[outlined]} />);

    expect(screen.getByText("Abra a mesma imagem enviada como planta para desenhar por cima.")).toBeInTheDocument();
    expect(screen.getByLabelText("Planta")).toHaveValue("v1");
    openImage();
    expect(screen.getByRole("img", { name: "Planta da obra" })).toHaveAttribute("src", "blob:planta");
    // the saved outline of this sheet is drawn
    expect(container.querySelectorAll("polygon")).toHaveLength(1);

    const canvas = screen.getByTestId("sheet-canvas");
    fireEvent.click(canvas, { clientX: 0, clientY: 0 });
    fireEvent.click(canvas, { clientX: 100, clientY: 0 });
    expect(screen.getByText("2 ponto(s)")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Salvar zona" }));
    expect(screen.getByRole("alert")).toHaveTextContent("Desenhe pelo menos 3 pontos.");

    fireEvent.click(canvas, { clientX: 100, clientY: 50 });
    expect(container.querySelectorAll("circle")).toHaveLength(3);
    fireEvent.click(screen.getByRole("button", { name: "Salvar zona" }));
    expect(screen.getByRole("alert")).toHaveTextContent("Informe o nome da zona.");

    fireEvent.change(screen.getByLabelText("Nome da zona"), { target: { value: " Cozinha " } });
    fireEvent.change(screen.getByLabelText("Tipo"), { target: { value: "Area" } });
    fireEvent.click(screen.getByRole("button", { name: "Salvar zona" }));
    await waitFor(() => expect(twinApi.createZone).toHaveBeenCalledWith("p1", {
      kind: "Area", name: "Cozinha", sheetVersionId: "v1",
      outline: [{ x: 0, y: 0 }, { x: 0.5, y: 0 }, { x: 0.5, y: 0.5 }],
    }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Zona desenhada salva."));
    await waitFor(() => expect(screen.getByText("0 ponto(s)")).toBeInTheDocument());
    expect(screen.getByLabelText("Nome da zona")).toHaveValue("");
  });

  it("undo, clear and the keyboard (Backspace/Escape) edit the points", async () => {
    wrap(<SheetMapper projectId="p1" sheets={sheets} zones={[]} />);
    openImage();
    const canvas = screen.getByTestId("sheet-canvas");
    for (let i = 0; i < 4; i++) fireEvent.click(canvas, { clientX: i * 10, clientY: 10 });
    expect(screen.getByText("4 ponto(s)")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Desfazer ponto/ }));
    expect(screen.getByText("3 ponto(s)")).toBeInTheDocument();
    fireEvent.keyDown(canvas, { key: "Backspace" });
    expect(screen.getByText("2 ponto(s)")).toBeInTheDocument();
    fireEvent.keyDown(canvas, { key: "Escape" });
    expect(screen.getByText("0 ponto(s)")).toBeInTheDocument();
    fireEvent.click(canvas, { clientX: 5, clientY: 5 });
    fireEvent.click(screen.getByRole("button", { name: /Limpar/ }));
    expect(screen.getByText("0 ponto(s)")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Limpar/ })).toBeDisabled();
  });

  it("a save error is shown; switching the sheet clears the points", async () => {
    vi.mocked(twinApi.createZone).mockRejectedValue(new ApiError(503, "x"));
    wrap(<SheetMapper projectId="p1" sheets={sheets} zones={[]} />);
    openImage();
    const canvas = screen.getByTestId("sheet-canvas");
    for (let i = 0; i < 3; i++) fireEvent.click(canvas, { clientX: i * 50, clientY: i * 20 });
    fireEvent.change(screen.getByLabelText("Nome da zona"), { target: { value: "Sala" } });
    fireEvent.click(screen.getByRole("button", { name: "Salvar zona" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Armazenamento de arquivos indisponível");

    fireEvent.change(screen.getByLabelText("Planta"), { target: { value: "v2" } });
    expect(screen.getByText("0 ponto(s)")).toBeInTheDocument();
    await waitFor(() => expect(twinApi.fetchTwinState).toHaveBeenCalledWith("v2", expect.any(String)));
  });

  it("a PDF is shown for reference only (no drawing)", () => {
    wrap(<SheetMapper projectId="p1" sheets={sheets} zones={[]} />);
    fireEvent.change(screen.getByLabelText("Abrir a imagem da planta (do seu computador)"),
      { target: { files: [new File(["%PDF"], "planta.pdf", { type: "application/pdf" })] } });
    expect(screen.getByLabelText("Planta em PDF")).toBeInTheDocument();
    expect(screen.getByText("Para desenhar as zonas, abra uma imagem (PNG/JPEG) da página da planta.")).toBeInTheDocument();
    expect(screen.queryByTestId("sheet-canvas")).not.toBeInTheDocument();
  });
});
