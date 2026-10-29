import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { renderTax } from "../test/render";
import { SplitTable } from "./SplitTable";
import { ImpactBadge, OpportunityStatusBadge, RiskBadge } from "./TaxBadges";
import * as api from "../api/tax.api";
import type { SplitExpectationDto } from "../api/tax.api";

vi.mock("../api/tax.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tax.api")>()),
  recordSplitSettlement: vi.fn(),
  reverseSplitSettlement: vi.fn(),
}));

const settlement = (id: string, amount: number, ref: string, extra: Partial<SplitExpectationDto["settlements"][number]> = {}) => ({
  id, actualAmount: amount, source: "Manual" as const, externalReference: ref, settledAt: "2026-10-27T12:00:00Z",
  reversesSettlementId: null, reversed: false, reason: null, ...extra,
});

const expectation = (over: Partial<SplitExpectationDto> = {}): SplitExpectationDto => ({
  id: "e1", calculationId: "c1", tax: "Cbs", procedure: "Standard", expected: 7.56, actual: 8.56, difference: 1,
  status: "Excess",
  settlements: [settlement("s1", 7.56, "psp-1"), settlement("s2", 1, "psp-2")],
  createdAt: "2026-10-27T11:00:00Z", ...over,
});

describe("SplitTable", () => {
  beforeEach(() => vi.clearAllMocks());
  afterEach(cleanup);

  it("shows expected against retained and the reconciliation status", () => {
    renderTax(<SplitTable items={[expectation(), expectation({ id: "e2", tax: "IbsState", expected: 0.84, actual: 0.84, difference: 0, status: "Matched", settlements: [settlement("s3", 0.84, "psp-3")] })]} />);

    expect(screen.getByText("CBS")).toBeInTheDocument();
    expect(screen.getByText("R$ 7,56")).toBeInTheDocument();
    expect(screen.getByText("R$ 8,56")).toBeInTheDocument();
    expect(screen.getAllByText(/registros?/).length).toBe(2);
    expect(screen.getAllByText(/Padrão/).length).toBe(2);
  });

  it("reverses a wrong retention only with a reason, keeping the original on record", async () => {
    vi.mocked(api.reverseSplitSettlement).mockResolvedValue(expectation({ status: "Matched" }));
    renderTax(<SplitTable items={[expectation()]} />);

    fireEvent.click(screen.getByRole("button", { name: "Registros" }));
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getAllByRole("button", { name: "Estornar" })).toHaveLength(2);

    fireEvent.click(within(dialog).getAllByRole("button", { name: "Estornar" })[1]);
    const confirm = within(dialog).getByRole("button", { name: "Confirmar estorno" });
    expect(confirm).toBeDisabled();
    fireEvent.change(within(dialog).getByLabelText(/Motivo do estorno/), { target: { value: "digitado em duplicidade" } });
    fireEvent.click(confirm);

    await waitFor(() => expect(api.reverseSplitSettlement).toHaveBeenCalledWith("s2", "digitado em duplicidade"));
  });

  it("never offers to reverse a reversal or an entry already reversed", () => {
    renderTax(<SplitTable items={[expectation({
      status: "Matched",
      settlements: [
        settlement("s1", 7.56, "psp-1"),
        settlement("s2", 1, "psp-2", { reversed: true }),
        settlement("s3", -1, "estorno:psp-2", { reversesSettlementId: "s2", reason: "duplicado" }),
      ],
    })]} />);

    fireEvent.click(screen.getByRole("button", { name: "Registros" }));
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getAllByRole("button", { name: "Estornar" })).toHaveLength(1);
    expect(within(dialog).getByText(/· estornada/)).toBeInTheDocument();
    expect(within(dialog).getByText(/estorno: duplicado/)).toBeInTheDocument();
  });

  it("records a retention only with a value and a reference", async () => {
    vi.mocked(api.recordSplitSettlement).mockResolvedValue(expectation());
    renderTax(<SplitTable items={[expectation({ settlements: [], status: "Pending", actual: 0, difference: -7.56 })]} />);

    fireEvent.click(screen.getByRole("button", { name: "Registrar retenção" }));
    fireEvent.click(screen.getByRole("button", { name: "Registrar" }));
    expect(screen.getByText("Informe o valor retido.")).toBeInTheDocument();
    expect(api.recordSplitSettlement).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText("Valor retido (R$)"), { target: { value: "7,56" } });
    fireEvent.change(screen.getByLabelText("Referência"), { target: { value: "psp-9" } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar" }));
    await waitFor(() => expect(api.recordSplitSettlement).toHaveBeenCalledWith("e1", 7.56, "psp-9", expect.stringMatching(/T12:00:00Z$/)));
  });
});

describe("Tax badges", () => {
  afterEach(cleanup);

  it("names each impact so a deferral is never read as a saving", () => {
    renderTax(<><ImpactBadge nature="DefinitiveSaving" /><ImpactBadge nature="Deferral" /><ImpactBadge nature="Credit" /></>);
    expect(screen.getByText("Economia")).toBeInTheDocument();
    expect(screen.getByText("Diferimento — não é economia")).toBeInTheDocument();
    expect(screen.getByText("Crédito")).toBeInTheDocument();
  });

  it("labels risk and workflow status in Portuguese", () => {
    renderTax(<><RiskBadge risk="High" /><OpportunityStatusBadge status="Blocked" /></>);
    expect(screen.getByText(/alto/i)).toBeInTheDocument();
    expect(screen.getByText(/Bloqueada/)).toBeInTheDocument();
  });
});
