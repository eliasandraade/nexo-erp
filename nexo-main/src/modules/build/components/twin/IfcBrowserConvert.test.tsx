import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";
import { ApiError } from "@/services/api-client";
import * as twinApi from "../../api/build-twin.api";
import { IfcBrowserConvert } from "./IfcBrowserConvert";

const h = vi.hoisted(() => ({ convert: vi.fn() }));

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
// the lazy converter chunk (three.js + web-ifc worker) is replaced by a scripted conversion
vi.mock("./load-ifc-converter", () => ({ loadIfcConverter: async () => ({ convertIfcToGlb: h.convert }) }));
vi.mock("../../api/build-twin.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-twin.api")>()),
  uploadViewer: vi.fn(),
}));

const version = (over: Partial<twinApi.BuildModelVersionDto> = {}): twinApi.BuildModelVersionDto => ({
  id: "v1", modelId: "m1", number: 2, sourceFormat: "Ifc", sourceFileName: "casa.ifc", sizeBytes: 2048, sha256: null,
  geometrySource: "NativeBim", viewerSource: "None", hasViewer: false, status: "Ready", statusMessage: null, elementCount: 120,
  storeyCount: 2, schema: "IFC4", layers: null, processedAt: null, createdAt: "2026-10-01T10:00:00Z", ...over,
});

const ifc = (content = "ISO-10303-21") => {
  const bytes = new TextEncoder().encode(content).buffer;
  return Object.assign(new File([content], "casa.ifc"), { arrayBuffer: async () => bytes }) as File;
};

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("IfcBrowserConvert", () => {
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("explains the conversion and is disabled without 3D", () => {
    wrap(<IfcBrowserConvert projectId="p1" version={version()} enabled={false} />);
    expect(screen.getByTestId("ifc-browser-convert")).toHaveTextContent("O modelo IFC foi lido (120 elementos)");
    expect(screen.getByText("casa.ifc")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Gerar visualização 3D no navegador/ })).toBeDisabled();
    expect(screen.getByText("O 3D não está habilitado para sua empresa.")).toBeInTheDocument();
  });

  it("converts the picked IFC with progress, uploads the GLB to the version's viewer and reports the elements", async () => {
    const glb = new ArrayBuffer(8);
    let finishConvert: (v: { glb: ArrayBuffer; elements: number }) => void = () => {};
    h.convert.mockImplementation((_file: File, progress: (p: string, d: number, t: number) => void) => {
      progress("geometry", 1, 2);
      return new Promise((r) => { finishConvert = r; });
    });
    vi.mocked(twinApi.uploadViewer).mockImplementation(async (_id, _form, onProgress) => {
      onProgress?.(50, 100);
      return version({ hasViewer: true });
    });
    wrap(<IfcBrowserConvert projectId="p1" version={version()} enabled />);

    const file = ifc();
    fireEvent.change(screen.getByTestId("ifc-convert-input"), { target: { files: [file] } });
    // the blocking progress dialog: geometry 1/2 = 10 + 60 × 0.5 = 40%
    const bar = await screen.findByRole("progressbar", { name: "Gerando a geometria dos elementos" });
    expect(bar).toHaveAttribute("aria-valuenow", "40");
    // the modal dialog hides the rest of the page from the accessibility tree
    expect(screen.getByRole("button", { name: /Gerar visualização 3D no navegador/, hidden: true })).toBeDisabled();
    expect(h.convert).toHaveBeenCalledWith(file, expect.any(Function));

    finishConvert({ glb, elements: 120 });
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Visualização 3D gerada (120 elementos)."));
    const [versionId, form] = vi.mocked(twinApi.uploadViewer).mock.calls[0];
    expect(versionId).toBe("v1");
    expect((form.get("file") as File).name).toBe("viewer.glb");
    await waitFor(() => expect(screen.queryByRole("progressbar")).not.toBeInTheDocument());
  });

  it("a different file than the version's (SHA-256) is refused before converting", async () => {
    wrap(<IfcBrowserConvert projectId="p1" version={version({ sha256: "ABCDEF" })} enabled />);
    fireEvent.change(screen.getByTestId("ifc-convert-input"), { target: { files: [ifc("outro")] } });
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Este não é o arquivo da versão 2 (casa.ifc). Escolha o mesmo .ifc enviado.");
    expect(h.convert).not.toHaveBeenCalled();
  });

  it("the same file (hash compared case-insensitively) converts; an upload error is mapped", async () => {
    const content = "ISO-10303-21";
    const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(content));
    const hex = Array.from(new Uint8Array(digest), (b) => b.toString(16).padStart(2, "0")).join("").toUpperCase();
    h.convert.mockResolvedValue({ glb: new ArrayBuffer(4), elements: 3 });
    vi.mocked(twinApi.uploadViewer).mockRejectedValue(
      new ApiError(422, "x", "viewer_mismatch", undefined, undefined, "Os GlobalIds não batem."));
    wrap(<IfcBrowserConvert projectId="p1" version={version({ sha256: hex })} enabled />);

    fireEvent.change(screen.getByTestId("ifc-convert-input"), { target: { files: [ifc(content)] } });
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Os GlobalIds não batem. Confira se o IFC escolhido é o mesmo desta versão.");
    expect(h.convert).toHaveBeenCalled();
    expect(toast.success).not.toHaveBeenCalled();
  });

  it("no file picked does nothing", () => {
    wrap(<IfcBrowserConvert projectId="p1" version={version()} enabled />);
    fireEvent.change(screen.getByTestId("ifc-convert-input"), { target: { files: [] } });
    expect(h.convert).not.toHaveBeenCalled();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
