import type { ReactNode } from "react";
import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { addMovement, closeSession, fetchAllSessions, fetchOpenSession, openSession } from "../api/cash.api";
import type { CashMovementDto, CashSessionDto } from "../types";
import { useAddCashMovement, useAllSessions, useCloseCashSession, useOpenCashSession, useOpenSession } from "./use-cash";

vi.mock("../api/cash.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/cash.api")>()),
  fetchOpenSession: vi.fn(),
  fetchAllSessions: vi.fn(),
  openSession: vi.fn(),
  closeSession: vi.fn(),
  addMovement: vi.fn(),
}));

function sessao(extra: Partial<CashSessionDto> = {}): CashSessionDto {
  return {
    id: "caixa-1", status: "Open", openedByUserId: "u1", openedByName: "Ana", closedByUserId: null, closedByName: null,
    openingBalance: 150, closingBalance: null, openedAt: "2026-11-13T08:00:00Z", closedAt: null, notes: null, movements: [],
    expectedBalance: null, difference: null, closingNotes: null, summary: null, ...extra,
  };
}

const sangria: CashMovementDto = {
  id: "mov-1", movementType: "Withdrawal", amount: 50, description: "Sangria para o cofre", referenceType: null,
  referenceId: null, createdByUserId: "u1", createdAt: "2026-11-13T12:00:00Z",
};

// A tela do caixa: o caixa aberto e o histórico, ambos visíveis enquanto o operador age.
function renderCaixa() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>;
  return renderHook(
    () => ({
      aberto: useOpenSession(),
      historico: useAllSessions(),
      abrir: useOpenCashSession(),
      fechar: useCloseCashSession(),
      movimentar: useAddCashMovement(),
    }),
    { wrapper },
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(fetchAllSessions).mockResolvedValue([]);
});

describe("caixa: abrir, movimentar e fechar recarregam o que a tela mostra", () => {
  it("abrir o caixa envia o troco inicial e a tela passa a mostrar o caixa aberto", async () => {
    vi.mocked(fetchOpenSession).mockResolvedValueOnce(null).mockResolvedValue(sessao());
    vi.mocked(openSession).mockResolvedValue(sessao());
    const { result } = renderCaixa();
    await waitFor(() => expect(result.current.aberto.isSuccess).toBe(true));
    expect(result.current.aberto.data).toBeNull();

    await act(() => result.current.abrir.mutateAsync({ openingBalance: 150, notes: "Turno da manhã" }));

    expect(openSession).toHaveBeenCalledWith({ openingBalance: 150, notes: "Turno da manhã" });
    await waitFor(() => expect(result.current.aberto.data?.id).toBe("caixa-1"));
    expect(fetchOpenSession).toHaveBeenCalledTimes(2);
    expect(fetchAllSessions).toHaveBeenCalledTimes(2);
  });

  it("a sangria vai para o caixa certo e o caixa recarregado já traz o movimento", async () => {
    vi.mocked(fetchOpenSession).mockResolvedValueOnce(sessao()).mockResolvedValue(sessao({ movements: [sangria] }));
    vi.mocked(addMovement).mockResolvedValue(sangria);
    const { result } = renderCaixa();
    await waitFor(() => expect(result.current.aberto.data?.id).toBe("caixa-1"));

    const req = { movementType: "Withdrawal" as const, amount: 50, description: "Sangria para o cofre" };
    await act(() => result.current.movimentar.mutateAsync({ id: "caixa-1", req }));

    expect(addMovement).toHaveBeenCalledWith("caixa-1", req);
    await waitFor(() => expect(result.current.aberto.data?.movements).toEqual([sangria]));
  });

  it("fechar o caixa envia o valor contado e a tela deixa de ter caixa aberto", async () => {
    const fechado = sessao({ status: "Closed", closingBalance: 412.5, closedAt: "2026-11-13T18:00:00Z" });
    vi.mocked(fetchOpenSession).mockResolvedValueOnce(sessao()).mockResolvedValue(null);
    vi.mocked(fetchAllSessions).mockResolvedValueOnce([]).mockResolvedValue([fechado]);
    vi.mocked(closeSession).mockResolvedValue(fechado);
    const { result } = renderCaixa();
    await waitFor(() => expect(result.current.aberto.data?.id).toBe("caixa-1"));

    const req = { closingBalance: 412.5, notes: "Faltaram R$ 2,50 de troco" };
    await act(() => result.current.fechar.mutateAsync({ id: "caixa-1", req }));

    expect(closeSession).toHaveBeenCalledWith("caixa-1", req);
    await waitFor(() => expect(result.current.aberto.data).toBeNull());
    expect(result.current.historico.data).toEqual([fechado]);
  });

  it("se o fechamento falha, o erro chega à tela e nada é recarregado", async () => {
    vi.mocked(fetchOpenSession).mockResolvedValue(sessao());
    vi.mocked(closeSession).mockRejectedValue(new Error("Informe o motivo da diferença."));
    const { result } = renderCaixa();
    await waitFor(() => expect(result.current.aberto.data?.id).toBe("caixa-1"));

    await act(async () => {
      await expect(
        result.current.fechar.mutateAsync({ id: "caixa-1", req: { closingBalance: 300 } }),
      ).rejects.toThrow("Informe o motivo da diferença.");
    });

    await waitFor(() => expect(result.current.fechar.error?.message).toBe("Informe o motivo da diferença."));
    expect(fetchOpenSession).toHaveBeenCalledTimes(1);
    expect(fetchAllSessions).toHaveBeenCalledTimes(1);
    expect(result.current.aberto.data?.status).toBe("Open");
  });
});
