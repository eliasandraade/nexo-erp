import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ApiError } from "@/services/api-client";
import { InspectionRunner } from "./InspectionRunner";
import * as qualityApi from "../../api/build-quality.api";

vi.mock("../../api/build-quality.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-quality.api")>()),
  fetchInspection:      vi.fn(),
  recordInspectionItem: vi.fn(),
  completeInspection:   vi.fn(),
}));

const item = (over: Partial<qualityApi.BuildInspectionItemDto> = {}): qualityApi.BuildInspectionItemDto => ({
  id: "it1", order: 0, text: "Prumo das paredes", required: true, requiresPhoto: false, result: "Ok", comment: null,
  severity: null, photoKeys: [], photoUrls: [], nonConformityId: null, ...over,
});

const inspection = (items: qualityApi.BuildInspectionItemDto[], over: Partial<qualityApi.BuildInspectionDto> = {}): qualityApi.BuildInspectionDto => ({
  id: "in1", projectId: "p1", kind: "Quality", stageId: "s1", stageName: "Alvenaria", templateId: null,
  title: "Vistoria da alvenaria", scheduledFor: "2026-11-05", status: "InProgress", inspectorName: "Carlos",
  startedAt: "2026-11-05T10:00:00Z", completedAt: null, completedByName: null, notes: null,
  okItems: 0, notOkItems: 0, pendingItems: 0, items, createdAt: "2026-11-01T10:00:00Z", updatedAt: "2026-11-01T10:00:00Z",
  ...over,
});

function renderRunner(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("InspectionRunner", () => {
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("blocks completion while an item that requires a photo was judged without one", async () => {
    vi.mocked(qualityApi.fetchInspection).mockResolvedValue(inspection([
      item(),
      item({ id: "it2", order: 1, text: "Impermeabilização do box", requiresPhoto: true, result: "Ok" }),
    ]));
    renderRunner(<InspectionRunner projectId="p1" inspectionId="in1" onBack={() => {}} />);

    const card = await screen.findByTestId("inspection-item-it2");
    expect(within(card).getByText("foto obrigatória")).toBeInTheDocument();
    expect(within(card).getByRole("alert")).toHaveTextContent("Foto obrigatória: envie ao menos uma foto");
    expect(screen.getByTestId("inspection-blockers")).toHaveTextContent("Foto obrigatória faltando: Impermeabilização do box.");

    const complete = screen.getByRole("button", { name: /Concluir vistoria/ });
    expect(complete).toBeDisabled();
    fireEvent.click(complete);
    expect(qualityApi.completeInspection).not.toHaveBeenCalled();
  });

  it("explains the computed outcome and shows the backend's 422 reason when completing fails", async () => {
    vi.mocked(qualityApi.fetchInspection).mockResolvedValue(inspection([
      item(),
      item({ id: "it2", order: 1, text: "Rejunte", required: false, result: "NotOk" }),
    ]));
    vi.mocked(qualityApi.completeInspection).mockRejectedValue(new ApiError(
      422, "Rejunte", "inspection_photo_required", undefined, ["Rejunte"], "Há itens que exigem foto sem nenhuma foto."));
    renderRunner(<InspectionRunner projectId="p1" inspectionId="in1" onBack={() => {}} />);

    const outcome = await screen.findByTestId("inspection-outcome");
    expect(within(outcome).getByText("Aprovada com ressalvas")).toBeInTheDocument();
    expect(outcome).toHaveTextContent("1 item(ns) opcional(is) marcado(s) como “Não ok”.");

    fireEvent.click(screen.getByRole("button", { name: /Concluir vistoria/ }));
    await waitFor(() => expect(qualityApi.completeInspection).toHaveBeenCalledWith("in1", undefined));
    expect(await screen.findByTestId("inspection-error"))
      .toHaveTextContent("Há itens que exigem foto sem nenhuma foto. Rejunte.");
  });

  it("a required item judged “Não ok” announces a failed inspection; marking a result records it", async () => {
    vi.mocked(qualityApi.fetchInspection).mockResolvedValue(inspection([
      item({ result: "NotOk" }),
      item({ id: "it2", order: 1, text: "Esquadro", result: "Pending" }),
    ]));
    vi.mocked(qualityApi.recordInspectionItem).mockImplementation(async (_id, itemId, req) =>
      inspection([item({ result: "NotOk" }), item({ id: "it2", order: 1, text: "Esquadro", result: req.result })]));
    renderRunner(<InspectionRunner projectId="p1" inspectionId="in1" onBack={() => {}} />);

    expect(await screen.findByTestId("inspection-blockers")).toHaveTextContent("Itens obrigatórios sem resultado: Esquadro.");
    const card = screen.getByTestId("inspection-item-it2");
    fireEvent.click(within(card).getByRole("button", { name: "Ok" }));
    await waitFor(() => expect(qualityApi.recordInspectionItem)
      .toHaveBeenCalledWith("in1", "it2", { result: "Ok", comment: null, photoKeys: [], severity: null }));
    expect(await within(screen.getByTestId("inspection-outcome")).findByText("Reprovada")).toBeInTheDocument();
  });
});
