import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";
import { ApiError } from "@/services/api-client";
import * as twinApi from "../../api/build-twin.api";
import type { BuildStageDto } from "../../api/build.api";
import { RealityCaptureSection } from "./RealityCaptureSection";

vi.mock("three", () => ({}));
vi.mock("./viewer/ModelViewer3D", () => ({ default: () => <div data-testid="viewer-mock" /> }));
vi.mock("./viewer/Pano360Viewer", () => ({
  default: ({ url, title }: { url: string; title: string }) => <div data-testid="pano-mock" data-url={url}>{title}</div>,
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

vi.mock("../../api/build-twin.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-twin.api")>()),
  fetchRealityCaptures:        vi.fn(),
  uploadRealityCapture:        vi.fn(),
  updateRealityCapture:        vi.fn(),
  setRealityCaptureVisibility: vi.fn(),
  deleteRealityCapture:        vi.fn(),
}));

const stages: BuildStageDto[] = [
  { id: "s1", projectId: "p1", name: "Estrutura", description: null, order: 0, status: "InProgress", plannedStartDate: null,
    plannedEndDate: null, actualStartDate: null, actualEndDate: null, progressPercent: 30 },
];
const zones: twinApi.BuildModelZoneDto[] = [
  { id: "z1", modelVersionId: null, kind: "Floor", name: "Térreo", externalId: null, parentZoneId: null, elevation: null,
    order: 0, outline: null, sheetVersionId: null, simplifiedBox: null, elementCount: 0 },
];

const capture = (over: Partial<twinApi.BuildRealityCaptureDto> = {}): twinApi.BuildRealityCaptureDto => ({
  id: "c1", projectId: "p1", type: "Photo", capturedAt: "2026-10-10T12:00:00", stageId: null, zoneId: null,
  elementExternalId: null, title: "Laje", notes: null, contentType: "image/jpeg", sizeBytes: 2048, clientVisible: false,
  url: "https://cdn/laje.jpg", urlExpiresAt: null, processed: true, createdAt: "2026-10-10T12:00:00Z", ...over,
});

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

const renderSection = () => wrap(<RealityCaptureSection projectId="p1" stages={stages} zones={zones} />);

describe("RealityCaptureSection", () => {
  beforeEach(() => { vi.mocked(twinApi.fetchRealityCaptures).mockResolvedValue([]); });
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("empty selection shows the empty message; the filters re-query with the chosen values", async () => {
    renderSection();
    expect(await screen.findByText("Nenhuma captura nesta seleção.")).toBeInTheDocument();
    expect(twinApi.fetchRealityCaptures).toHaveBeenCalledWith("p1", {});

    fireEvent.change(screen.getByLabelText("Filtrar por tipo"), { target: { value: "Video" } });
    await waitFor(() => expect(twinApi.fetchRealityCaptures).toHaveBeenLastCalledWith("p1", { type: "Video" }));
    fireEvent.change(screen.getByLabelText("Filtrar por etapa"), { target: { value: "s1" } });
    fireEvent.change(screen.getByLabelText("Filtrar por zona"), { target: { value: "z1" } });
    fireEvent.change(screen.getByLabelText("De"), { target: { value: "2026-10-01" } });
    fireEvent.change(screen.getByLabelText("Até"), { target: { value: "2026-10-20" } });
    await waitFor(() => expect(twinApi.fetchRealityCaptures).toHaveBeenLastCalledWith("p1", {
      type: "Video", stageId: "s1", zoneId: "z1", from: "2026-10-01", to: "2026-10-20",
    }));
    // clearing a filter drops it
    fireEvent.change(screen.getByLabelText("Filtrar por etapa"), { target: { value: "" } });
    await waitFor(() => expect(twinApi.fetchRealityCaptures).toHaveBeenLastCalledWith("p1", expect.objectContaining({ stageId: undefined })));
  });

  it("load failure shows an alert", async () => {
    vi.mocked(twinApi.fetchRealityCaptures).mockRejectedValue(new Error("boom"));
    renderSection();
    expect(await screen.findByRole("alert")).toHaveTextContent("Não foi possível carregar as capturas.");
  });

  it("renders each capture kind: photo, video, 360°, point cloud with download, and links", async () => {
    vi.mocked(twinApi.fetchRealityCaptures).mockResolvedValue([
      capture({ id: "c1", title: "Laje", stageId: "s1", zoneId: "z1", elementExternalId: "GUID-1" }),
      capture({ id: "c2", type: "Video", title: "Vídeo da fachada", contentType: "video/mp4", url: "https://cdn/v.mp4" }),
      capture({ id: "c3", type: "Photo360", title: "Sala 360", url: "https://cdn/360.jpg" }),
      capture({ id: "c4", type: "PointCloud", title: "Nuvem", contentType: "application/octet-stream", url: "https://cdn/n.las",
        processed: false, sizeBytes: 3 * 1024 * 1024 }),
      capture({ id: "c5", type: "Drone", title: "Sem url", url: null, contentType: "image/jpeg" }),
    ]);
    const { container } = renderSection();

    expect(await screen.findByText("Laje")).toBeInTheDocument();
    expect(screen.getByText("Etapa: Estrutura · Zona: Térreo · Elemento: GUID-1")).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "Laje" })).toHaveAttribute("src", "https://cdn/laje.jpg");
    expect(container.querySelector("video")).toHaveAttribute("src", "https://cdn/v.mp4");
    expect(screen.getByText("Nuvem de pontos guardada sem processamento.")).toBeInTheDocument();
    expect(screen.getByText("Pré-visualização indisponível.")).toBeInTheDocument();
    expect(screen.getByText(/Nuvem de pontos · .* · 3 MB/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Baixar Nuvem" })).toHaveAttribute("href", "https://cdn/n.las");

    // the 360° opens the panorama viewer in a dialog
    fireEvent.click(screen.getByRole("button", { name: "Abrir 360° de Sala 360" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Arraste para olhar em volta; role para aproximar.")).toBeInTheDocument();
    expect(await within(dialog).findByTestId("pano-mock")).toHaveAttribute("data-url", "https://cdn/360.jpg");
  });

  it("toggling client visibility calls the API; an error is toasted", async () => {
    vi.mocked(twinApi.fetchRealityCaptures).mockResolvedValue([capture()]);
    vi.mocked(twinApi.setRealityCaptureVisibility).mockResolvedValueOnce(capture({ clientVisible: true }))
      .mockRejectedValueOnce(new ApiError(503, "x"));
    renderSection();

    const sw = await screen.findByRole("switch", { name: "Visível ao cliente: Laje" });
    fireEvent.click(sw);
    await waitFor(() => expect(twinApi.setRealityCaptureVisibility).toHaveBeenCalledWith("c1", true));
    await waitFor(() => expect(sw).not.toBeDisabled());
    fireEvent.click(sw);
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith(
      "Armazenamento de arquivos indisponível no momento. Tente novamente mais tarde."));
  });

  it("deleting a capture calls the API; a failure toasts the reason", async () => {
    vi.mocked(twinApi.fetchRealityCaptures).mockResolvedValue([capture()]);
    vi.mocked(twinApi.deleteRealityCapture).mockRejectedValue(new Error("Sem permissão"));
    renderSection();

    fireEvent.click(await screen.findByRole("button", { name: "Excluir Laje" }));
    await waitFor(() => expect(twinApi.deleteRealityCapture).toHaveBeenCalledWith("c1", expect.anything()));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Sem permissão"));
  });

  it("linking a capture to stage/zone/element sends the update and closes the editor", async () => {
    vi.mocked(twinApi.fetchRealityCaptures).mockResolvedValue([capture({ notes: "obs" })]);
    vi.mocked(twinApi.updateRealityCapture).mockResolvedValue(capture());
    renderSection();

    fireEvent.click(await screen.findByRole("button", { name: "Vincular Laje" }));
    fireEvent.change(screen.getByLabelText("Etapa da captura"), { target: { value: "s1" } });
    fireEvent.change(screen.getByLabelText("Zona da captura"), { target: { value: "z1" } });
    fireEvent.change(screen.getByLabelText("Elemento da captura"), { target: { value: "  GUID-9 " } });
    fireEvent.click(screen.getByRole("button", { name: "Salvar vínculos" }));

    await waitFor(() => expect(twinApi.updateRealityCapture).toHaveBeenCalledWith("c1", {
      title: "Laje", notes: "obs", capturedAt: "2026-10-10T12:00:00", stageId: "s1", zoneId: "z1", elementExternalId: "GUID-9",
    }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Vínculos da captura salvos."));
    await waitFor(() => expect(screen.queryByLabelText("Etapa da captura")).not.toBeInTheDocument());
  });

  it("a failed link update toasts and keeps the editor open", async () => {
    vi.mocked(twinApi.fetchRealityCaptures).mockResolvedValue([capture({ stageId: "s1" })]);
    vi.mocked(twinApi.updateRealityCapture).mockRejectedValue(new Error("Etapa inválida"));
    renderSection();

    fireEvent.click(await screen.findByRole("button", { name: "Vincular Laje" }));
    expect(screen.getByLabelText("Etapa da captura")).toHaveValue("s1");
    fireEvent.change(screen.getByLabelText("Etapa da captura"), { target: { value: "" } });
    fireEvent.click(screen.getByRole("button", { name: "Salvar vínculos" }));
    await waitFor(() => expect(twinApi.updateRealityCapture).toHaveBeenCalledWith("c1", expect.objectContaining({
      stageId: null, zoneId: null, elementExternalId: null,
    })));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Etapa inválida"));
    expect(screen.getByLabelText("Etapa da captura")).toBeInTheDocument();
  });

  describe("upload form", () => {
    it("requires a title, then uploads with every field and reports progress", async () => {
      let resolveUpload: (v: twinApi.BuildRealityCaptureDto) => void = () => {};
      vi.mocked(twinApi.uploadRealityCapture).mockImplementation((_p, _i, onProgress) => {
        onProgress?.(50, 100);
        return new Promise((r) => { resolveUpload = r; });
      });
      renderSection();

      fireEvent.click(await screen.findByRole("button", { name: /Enviar captura/ }));
      const form = screen.getByTestId("capture-upload-form");
      const send = within(form).getByRole("button", { name: "Enviar" });
      expect(send).toBeDisabled();
      expect(within(form).getByText("Foto: imagem até 25 MB.")).toBeInTheDocument();

      const file = new File(["img"], "laje.jpg", { type: "image/jpeg" });
      fireEvent.change(within(form).getByLabelText("Arquivo"), { target: { files: [file] } });
      fireEvent.click(send);
      expect(await within(form).findByRole("alert")).toHaveTextContent("Informe um título.");
      expect(twinApi.uploadRealityCapture).not.toHaveBeenCalled();

      fireEvent.change(within(form).getByLabelText("Título"), { target: { value: " Laje 2º pav " } });
      fireEvent.change(within(form).getByLabelText("Data da captura"), { target: { value: "2026-10-05" } });
      fireEvent.change(within(form).getByLabelText("Etapa"), { target: { value: "s1" } });
      fireEvent.change(within(form).getByLabelText("Zona"), { target: { value: "z1" } });
      fireEvent.change(within(form).getByLabelText("Elemento (id)"), { target: { value: "GUID-1" } });
      fireEvent.change(within(form).getByLabelText("Observação interna"), { target: { value: "ok" } });
      fireEvent.click(within(form).getByRole("switch", { name: "Visível ao cliente" }));
      fireEvent.click(send);

      expect(await screen.findByRole("progressbar", { name: "Enviando a captura" })).toHaveAttribute("aria-valuenow", "50");
      expect(twinApi.uploadRealityCapture).toHaveBeenCalledWith("p1", {
        file, type: "Photo", title: "Laje 2º pav", capturedAt: "2026-10-05T12:00:00", stageId: "s1", zoneId: "z1",
        elementExternalId: "GUID-1", notes: "ok", clientVisible: true,
      }, expect.any(Function));
      resolveUpload(capture());
      await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Captura enviada."));
      await waitFor(() => expect(screen.queryByTestId("capture-upload-form")).not.toBeInTheDocument());
    });

    it("a file over the type limit is refused; changing the type resets the file", async () => {
      renderSection();
      fireEvent.click(await screen.findByRole("button", { name: /Enviar captura/ }));
      const form = screen.getByTestId("capture-upload-form");
      const big = new File(["x"], "big.jpg", { type: "image/jpeg" });
      Object.defineProperty(big, "size", { value: 30 * 1024 * 1024 });
      fireEvent.change(within(form).getByLabelText("Arquivo"), { target: { files: [big] } });
      expect(within(form).getByRole("alert")).toHaveTextContent("Arquivo de 30 MB acima do limite de 25 MB.");
      expect(within(form).getByRole("button", { name: "Enviar" })).toBeDisabled();

      fireEvent.change(within(form).getByLabelText("Tipo"), { target: { value: "Video" } });
      expect(within(form).queryByRole("alert")).not.toBeInTheDocument();
      expect(within(form).getByText("Vídeo: vídeo até 200 MB.")).toBeInTheDocument();
      expect(within(form).getByRole("button", { name: "Enviar" })).toBeDisabled();
    });

    it("an upload error shows the mapped message (413) and keeps the form; Cancelar closes it", async () => {
      vi.mocked(twinApi.uploadRealityCapture).mockRejectedValue(new ApiError(413, "too big", undefined, undefined, undefined, "Acima de 25 MB."));
      renderSection();
      fireEvent.click(await screen.findByRole("button", { name: /Enviar captura/ }));
      const form = screen.getByTestId("capture-upload-form");
      fireEvent.change(within(form).getByLabelText("Título"), { target: { value: "Laje" } });
      fireEvent.change(within(form).getByLabelText("Data da captura"), { target: { value: "" } });
      fireEvent.change(within(form).getByLabelText("Arquivo"), { target: { files: [new File(["x"], "a.png", { type: "image/png" })] } });
      fireEvent.click(within(form).getByRole("button", { name: "Enviar" }));

      expect(await within(form).findByRole("alert")).toHaveTextContent("Acima de 25 MB.");
      expect(twinApi.uploadRealityCapture).toHaveBeenCalledWith("p1", expect.objectContaining({
        capturedAt: null, stageId: null, zoneId: null, elementExternalId: null, notes: null, clientVisible: false,
      }), expect.any(Function));
      fireEvent.click(within(form).getByRole("button", { name: "Cancelar" }));
      expect(screen.queryByTestId("capture-upload-form")).not.toBeInTheDocument();
    });
  });
});
