import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SelectionsTab } from "./SelectionsTab";
import * as buildApi from "../../api/build.api";
import * as commercialApi from "../../api/build-commercial.api";

vi.mock("../../api/build.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build.api")>()),
  fetchStages: vi.fn(),
}));

vi.mock("../../api/build-commercial.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-commercial.api")>()),
  fetchSelections:  vi.fn(),
  confirmSelection: vi.fn(),
}));

const selection = (over: Partial<commercialApi.BuildSelectionDto> = {}): commercialApi.BuildSelectionDto => ({
  id: "s1", publicId: "pub", projectId: "p1", title: "Piso da sala", description: null, stageId: "st1",
  stageName: "Acabamento", deadline: "2026-11-15", deadlinePassed: false, status: "Submitted", clientVisible: true,
  chosenOptionId: "o2", clientComment: "Gostei do porcelanato", decidedAt: "2026-10-20T12:00:00Z", chosenVia: "Portal",
  confirmedAt: null, confirmedByName: null, changeOrderId: null,
  options: [
    { id: "o1", order: 0, name: "Cerâmica", description: null, priceDelta: 0, isStandard: true, imageKey: null, imageUrl: null, leadTimeDays: null },
    { id: "o2", order: 1, name: "Porcelanato", description: null, priceDelta: 1800, isStandard: false, imageKey: null, imageUrl: null, leadTimeDays: 7 },
  ],
  createdAt: "2026-10-01T10:00:00Z", updatedAt: "2026-10-01T10:00:00Z", ...over,
});

function renderTab(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("SelectionsTab", () => {
  beforeEach(() => { vi.mocked(buildApi.fetchStages).mockResolvedValue([]); });
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("shows the deadline, status, the client's chosen option and comment", async () => {
    vi.mocked(commercialApi.fetchSelections).mockResolvedValue([selection()]);
    renderTab(<SelectionsTab projectId="p1" onOpenChangeOrders={() => {}} />);

    expect(await screen.findByText("Piso da sala")).toBeInTheDocument();
    expect(screen.getByText("Cliente escolheu")).toBeInTheDocument();
    expect(screen.getByText(/Prazo: 15 de nov/)).toBeInTheDocument();
    expect(screen.getByText(/Escolha do cliente:/).parentElement).toHaveTextContent("Porcelanato (pelo portal)");
    expect(screen.getByText(/Gostei do porcelanato/)).toBeInTheDocument();
    expect(screen.getByText("+R$ 1.800,00 · +7 dias")).toBeInTheDocument();
  });

  it("confirming an option that changes price / lead time explains and then links the draft aditivo", async () => {
    vi.mocked(commercialApi.fetchSelections).mockResolvedValue([selection()]);
    vi.mocked(commercialApi.confirmSelection).mockResolvedValue(selection({ status: "Confirmed", changeOrderId: "co9" }));
    const openChangeOrders = vi.fn();
    renderTab(<SelectionsTab projectId="p1" onOpenChangeOrders={openChangeOrders} />);

    fireEvent.click(await screen.findByRole("button", { name: /Confirmar/ }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByTestId("draft-change-order-info"))
      .toHaveTextContent(/\+R\$ 1\.800,00, \+7 dias de prazo.*aditivo em rascunho/);

    fireEvent.click(within(dialog).getByRole("button", { name: "Confirmar escolha" }));
    // a Submitted selection confirms the client's own choice: no option id is sent
    await waitFor(() => expect(commercialApi.confirmSelection).toHaveBeenCalledWith("s1", undefined));
    expect(await within(dialog).findByText(/Aditivo em rascunho criado/)).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Ver aditivos" }));
    expect(openChangeOrders).toHaveBeenCalled();
  });

  it("an open selection records the option picked here", async () => {
    vi.mocked(commercialApi.fetchSelections).mockResolvedValue([
      selection({ status: "Open", chosenOptionId: null, clientComment: null, decidedAt: null, chosenVia: null }),
    ]);
    vi.mocked(commercialApi.confirmSelection).mockResolvedValue(selection({ status: "Confirmed", chosenOptionId: "o1" }));
    renderTab(<SelectionsTab projectId="p1" onOpenChangeOrders={() => {}} />);

    fireEvent.click(await screen.findByRole("button", { name: /Confirmar/ }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByRole("button", { name: "Confirmar escolha" })).toBeDisabled();
    fireEvent.click(within(dialog).getByRole("radio", { name: /Cerâmica/ }));
    expect(within(dialog).getByTestId("draft-change-order-info")).toHaveTextContent(/Esta não muda/);
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirmar escolha" }));
    await waitFor(() => expect(commercialApi.confirmSelection).toHaveBeenCalledWith("s1", "o1"));
  });
});
