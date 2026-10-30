import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";
import { MovementStageSelect } from "./MovementStageSelect";
import * as progressApi from "../../api/build-progress.api";
import type { BuildStageDto } from "../../api/build.api";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("../../api/build-progress.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-progress.api")>()),
  assignMovementStage: vi.fn(),
}));

const stages = [
  { id: "s1", name: "Fundação" }, { id: "s2", name: "Estrutura" },
] as BuildStageDto[];

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

const select = () => screen.getByLabelText("Etapa de Cimento");

describe("MovementStageSelect", () => {
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("unknown current stage shows the 'Etapa…' placeholder; choosing it does nothing", () => {
    wrap(<MovementStageSelect projectId="p1" movementId="mv1" description="Cimento" stageId={undefined} stages={stages} />);
    expect(select()).toHaveValue("__unknown__");
    expect(within(select()).getAllByRole("option").map((o) => o.textContent)).toEqual(["Etapa…", "Sem etapa", "Fundação", "Estrutura"]);
    fireEvent.change(select(), { target: { value: "__unknown__" } });
    expect(progressApi.assignMovementStage).not.toHaveBeenCalled();
  });

  it("links the expense to a stage and keeps the saved stage shown", async () => {
    vi.mocked(progressApi.assignMovementStage).mockResolvedValue({ movementId: "mv1", projectId: "p1", stageId: "s2", stageName: "Estrutura" });
    wrap(<MovementStageSelect projectId="p1" movementId="mv1" description="Cimento" stageId={null} stages={stages} />);
    expect(select()).toHaveValue("__none__");
    expect(within(select()).queryByText("Etapa…")).not.toBeInTheDocument();

    fireEvent.change(select(), { target: { value: "s2" } });
    await waitFor(() => expect(progressApi.assignMovementStage).toHaveBeenCalledWith("p1", "mv1", "s2"));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Despesa ligada à etapa Estrutura."));
    expect(select()).toHaveValue("s2");
  });

  it("'Sem etapa' unlinks it (null); an error is toasted and the value is kept", async () => {
    vi.mocked(progressApi.assignMovementStage)
      .mockResolvedValueOnce({ movementId: "mv1", projectId: "p1", stageId: null, stageName: null })
      .mockRejectedValueOnce(new Error("Despesa de outra obra"));
    wrap(<MovementStageSelect projectId="p1" movementId="mv1" description="Cimento" stageId="s1" stages={stages} />);
    expect(select()).toHaveValue("s1");

    fireEvent.change(select(), { target: { value: "__none__" } });
    await waitFor(() => expect(progressApi.assignMovementStage).toHaveBeenCalledWith("p1", "mv1", null));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Despesa sem etapa."));
    await waitFor(() => expect(select()).toHaveValue("__none__"));

    await waitFor(() => expect(select()).not.toBeDisabled());
    fireEvent.change(select(), { target: { value: "s1" } });
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Despesa de outra obra"));
    expect(select()).toHaveValue("__none__");
  });
});
