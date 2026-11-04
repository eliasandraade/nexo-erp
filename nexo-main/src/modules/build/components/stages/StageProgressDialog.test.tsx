import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ApiError } from "@/services/api-client";
import { StageProgressDialog } from "./StageProgressDialog";
import * as buildApi from "../../api/build.api";

vi.mock("../../api/build.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build.api")>()),
  updateStageProgress: vi.fn(),
}));

const stage: buildApi.BuildStageDto = {
  id: "s1", projectId: "p1", name: "Estrutura", description: null, order: 0, status: "InProgress", plannedStartDate: null,
  plannedEndDate: null, actualStartDate: null, actualEndDate: null, progressPercent: 90,
} as buildApi.BuildStageDto;

function renderDialog(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("StageProgressDialog", () => {
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("shows the backend's critical non-conformity reason when 100% is refused", async () => {
    vi.mocked(buildApi.updateStageProgress).mockRejectedValue(
      new ApiError(422, "Etapa com não conformidade crítica aberta.", "stage_has_critical_nonconformity"));
    const onClose = vi.fn();
    renderDialog(<StageProgressDialog projectId="p1" stage={stage} onClose={onClose} />);

    fireEvent.change(screen.getByLabelText("Progresso da etapa"), { target: { value: "100" } });
    expect(screen.getByText(/não conformidade crítica aberta impede concluir a etapa \(100%\)/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Salvar progresso" }));

    await waitFor(() => expect(buildApi.updateStageProgress).toHaveBeenCalledWith("s1", expect.objectContaining({ progressPercent: 100 })));
    const alert = await screen.findByTestId("stage-progress-error");
    expect(alert).toHaveTextContent("Etapa com não conformidade crítica aberta.");
    expect(alert).toHaveTextContent("Resolva ou cancele a não conformidade crítica (aba Qualidade)");
    expect(onClose).not.toHaveBeenCalled();
  });
});
