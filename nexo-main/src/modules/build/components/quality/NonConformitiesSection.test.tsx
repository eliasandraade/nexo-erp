import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { NonConformitiesSection } from "./NonConformitiesSection";
import * as buildApi from "../../api/build.api";
import * as qualityApi from "../../api/build-quality.api";

vi.mock("../../api/build.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build.api")>()),
  fetchStages: vi.fn(),
}));

vi.mock("../../api/build-quality.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-quality.api")>()),
  fetchNonConformities: vi.fn(),
  resolveNonConformity: vi.fn(),
}));

const nc = (over: Partial<qualityApi.BuildNonConformityDto> = {}): qualityApi.BuildNonConformityDto => ({
  id: "nc1", projectId: "p1", number: 3, stageId: "s1", stageName: "Estrutura", inspectionId: null, inspectionItemId: null,
  title: "Fissura na viga V2", description: "Fissura de 0,5 mm", severity: "Critical", status: "InTreatment",
  responsibleName: "Equipe A", dueDate: "2026-11-10", isOverdue: true, rootCause: null, correctiveAction: null,
  photoKeys: [], photoUrls: [], costImpact: 1200, resolvedAt: null, resolvedByName: null, verifiedAt: null,
  verifiedByName: null, verifiedBySameUser: false, cancelReason: null, createdByName: "Ana",
  createdAt: "2026-11-01T10:00:00Z", updatedAt: "2026-11-01T10:00:00Z", ...over,
});

function renderSection(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("NonConformitiesSection", () => {
  beforeEach(() => { vi.mocked(buildApi.fetchStages).mockResolvedValue([]); });
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("states the critical rule, filters by severity and resolves with root cause and corrective action", async () => {
    vi.mocked(qualityApi.fetchNonConformities).mockResolvedValue([nc()]);
    vi.mocked(qualityApi.resolveNonConformity).mockResolvedValue(nc({ status: "Resolved", rootCause: "Cura", correctiveAction: "Injeção de epóxi" }));
    renderSection(<NonConformitiesSection projectId="p1" />);

    expect(screen.getByText(/Não conformidade crítica aberta impede concluir a etapa \(100%\)/)).toBeInTheDocument();
    fireEvent.click(await screen.findByText("Fissura na viga V2"));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/impede concluir a etapa \(100%\) e a entrega do imóvel/)).toBeInTheDocument();
    expect(within(dialog).getByText("Vencida")).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole("button", { name: /Resolver/ }));
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirmar resolução" }));
    expect(within(dialog).getByRole("alert")).toHaveTextContent("Informe a ação corretiva");
    expect(qualityApi.resolveNonConformity).not.toHaveBeenCalled();

    fireEvent.change(within(dialog).getByLabelText("Causa raiz"), { target: { value: "Cura insuficiente" } });
    fireEvent.change(within(dialog).getByLabelText(/Ação corretiva/), { target: { value: "Injeção de epóxi" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirmar resolução" }));
    await waitFor(() => expect(qualityApi.resolveNonConformity)
      .toHaveBeenCalledWith("nc1", { rootCause: "Cura insuficiente", correctiveAction: "Injeção de epóxi" }));
    expect(await within(dialog).findByRole("button", { name: /Verificar/ })).toBeInTheDocument();
  });

  it("sends the filters to the API", async () => {
    vi.mocked(qualityApi.fetchNonConformities).mockResolvedValue([]);
    renderSection(<NonConformitiesSection projectId="p1" />);
    await screen.findByText(/Nenhuma não conformidade nesta obra/);

    fireEvent.change(screen.getByLabelText("Filtrar por gravidade"), { target: { value: "Critical" } });
    fireEvent.click(screen.getByLabelText("Só vencidas"));
    await waitFor(() => expect(qualityApi.fetchNonConformities)
      .toHaveBeenLastCalledWith("p1", { severity: "Critical", overdue: true }));
  });
});
