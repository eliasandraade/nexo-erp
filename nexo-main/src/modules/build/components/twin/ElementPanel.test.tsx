import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import * as twinApi from "../../api/build-twin.api";
import type { BuildStageDto } from "../../api/build.api";
import { ElementPanel } from "./ElementPanel";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("../../api/build-twin.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-twin.api")>()),
  fetchElements:      vi.fn(),
  fetchElementEvents: vi.fn(),
}));

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

const version: twinApi.BuildModelVersionDto = {
  id: "v1", modelId: "m1", number: 1, sourceFormat: "Ifc", sourceFileName: "casa.ifc", sizeBytes: 2048, sha256: null,
  geometrySource: "NativeBim", viewerSource: "ConvertedBrowser", hasViewer: true, status: "Ready", statusMessage: null,
  elementCount: 5000, storeyCount: 2, schema: "IFC4", layers: null, processedAt: null, createdAt: "2026-10-01T10:00:00Z",
};
const stages: BuildStageDto[] = [{ id: "s1", projectId: "p1", name: "Alvenaria", description: null, order: 0, status: "InProgress",
  plannedStartDate: null, plannedEndDate: null, actualStartDate: null, actualEndDate: null, progressPercent: 30 }];

const element: twinApi.BuildModelElementDto = {
  externalId: "2O2Fr$t4X7Zf8NOew3FLOH", category: "IfcWall", name: "Parede P-12", typeName: "Bloco 14", material: "Cerâmico",
  zoneId: "z1", zoneName: "Térreo", properties: { netArea: 12.5 }, state: "InProgress", stageId: "s1", explicitState: false,
};

beforeEach(() => vi.mocked(twinApi.fetchElementEvents).mockResolvedValue([]));
afterEach(() => { cleanup(); vi.clearAllMocks(); });

describe("ElementPanel", () => {
  it("looks the clicked element up directly by its external id (one request, no page scan)", async () => {
    vi.mocked(twinApi.fetchElements).mockResolvedValue({ items: [element], total: 1, skip: 0, take: 1, categories: ["IfcWall"] });
    wrap(<ElementPanel projectId="p1" version={version} externalId={element.externalId} date="2026-10-30" state={null}
      stages={stages} onClose={vi.fn()} />);

    expect(await screen.findByText("Parede P-12")).toBeInTheDocument();
    const panel = screen.getByTestId("element-panel");
    expect(panel).toHaveTextContent("IfcWall");
    expect(panel).toHaveTextContent("Térreo");
    expect(panel).toHaveTextContent("Alvenaria");
    expect(panel).toHaveTextContent("Área (m²)12,5");
    expect(twinApi.fetchElements).toHaveBeenCalledTimes(1);
    expect(twinApi.fetchElements).toHaveBeenCalledWith("v1", { externalId: element.externalId, date: "2026-10-30", take: 1 });
  });

  it("an id the version does not have says so", async () => {
    vi.mocked(twinApi.fetchElements).mockResolvedValue({ items: [], total: 0, skip: 0, take: 1, categories: [] });
    wrap(<ElementPanel projectId="p1" version={version} externalId="nao-existe" date="2026-10-30" state={null}
      stages={stages} onClose={vi.fn()} />);
    expect(await screen.findByText("Este elemento não foi encontrado nesta versão do modelo.")).toBeInTheDocument();
    await waitFor(() => expect(twinApi.fetchElements).toHaveBeenCalledTimes(1));
  });

  it("without a clicked element: asks to pick one and calls nothing", () => {
    wrap(<ElementPanel projectId="p1" version={version} externalId={null} date="2026-10-30" state={null} stages={stages} onClose={vi.fn()} />);
    expect(screen.getByText(/Toque ou clique em um elemento do modelo/)).toBeInTheDocument();
    expect(twinApi.fetchElements).not.toHaveBeenCalled();
  });
});
