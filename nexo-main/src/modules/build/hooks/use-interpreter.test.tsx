import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach } from "vitest";
import { renderHook, act, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider, type QueryKey } from "@tanstack/react-query";
import * as buildApi from "../api/build.api";
import * as interpreterApi from "../api/interpreter.api";
import { BUILD_KEYS, useProjectFinancial } from "./use-build";
import { MOVEMENT_KEYS, useConfirmMovement, useProjectMovements, useVoidMovement } from "./use-interpreter";

vi.mock("../api/build.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/build.api")>()),
  fetchProjectFinancialSummary: vi.fn(),
}));

vi.mock("../api/interpreter.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/interpreter.api")>()),
  confirmMovement:       vi.fn(),
  voidMovement:          vi.fn(),
  fetchProjectMovements: vi.fn(),
}));

afterEach(() => { vi.clearAllMocks(); });

// Obra p1 aberta na aba Financeiro, com a obra p2 também em cache.
const CACHE = {
  financeiro:          BUILD_KEYS.projectFinancial("p1"),
  despesas:            [...MOVEMENT_KEYS.projectMovements("p1"), "pages"],
  painel:              BUILD_KEYS.dashboard(),
  custoPorEtapa:       [...BUILD_KEYS.progressAll("p1"), "cost-by-stage"],
  outroFinanceiro:     BUILD_KEYS.projectFinancial("p2"),
  outrasDespesas:      [...MOVEMENT_KEYS.projectMovements("p2"), "pages"],
  detalhes:            BUILD_KEYS.projectDetails("p1"),
} satisfies Record<string, QueryKey>;

type Entrada = keyof typeof CACHE;

function setup({ comCache = true } = {}) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: Infinity }, mutations: { retry: false } },
  });
  if (comCache) for (const key of Object.values(CACHE)) qc.setQueryData(key, { emCache: true });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
  return { qc, wrapper };
}

function desatualizadas(qc: QueryClient): Entrada[] {
  return (Object.keys(CACHE) as Entrada[])
    .filter((k) => qc.getQueryState(CACHE[k])?.isInvalidated)
    .sort();
}

// Uma despesa a mais ou a menos muda o resumo financeiro, a lista, o painel e o custo por etapa — só desta obra.
const AFETADAS_PELA_DESPESA: Entrada[] = ["custoPorEtapa", "despesas", "financeiro", "painel"];

const confirmacao: interpreterApi.ConfirmMovementRequest = {
  amount: 1280.5, date: "2026-11-12", description: "Areia média 6 m³",
  direction: "Out", nature: "Expense", contextType: "Obra", contextId: "p1",
  originalSuggestionId: "sug1",
} as interpreterApi.ConfirmMovementRequest;

describe("despesas da obra", () => {
  it("confirmar a despesa sugerida desatualiza o financeiro, as despesas, o painel e o custo por etapa da obra", async () => {
    vi.mocked(interpreterApi.confirmMovement).mockResolvedValue({ id: "m1", status: "Confirmed", confirmedAt: "", corrections: [] });
    const { qc, wrapper } = setup();
    const { result } = renderHook(() => useConfirmMovement("p1"), { wrapper });

    await act(() => result.current.mutateAsync({ draftId: "d1", req: confirmacao }));

    expect(interpreterApi.confirmMovement).toHaveBeenCalledWith("d1", confirmacao);
    expect(desatualizadas(qc)).toEqual(AFETADAS_PELA_DESPESA);
  });

  it("estornar uma despesa errada desatualiza as mesmas visões, e só desta obra", async () => {
    vi.mocked(interpreterApi.voidMovement).mockResolvedValue({ id: "m1", status: "Voided", voidedAt: "" });
    const { qc, wrapper } = setup();
    const { result } = renderHook(() => useVoidMovement("p1"), { wrapper });

    await act(() => result.current.mutateAsync("m1"));

    expect(interpreterApi.voidMovement).toHaveBeenCalledWith("m1");
    expect(desatualizadas(qc)).toEqual(AFETADAS_PELA_DESPESA);
  });

  it("depois do estorno, a aba aberta busca de novo o resumo financeiro e a lista de despesas", async () => {
    vi.mocked(buildApi.fetchProjectFinancialSummary).mockResolvedValue({ projectId: "p1" } as buildApi.BuildProjectFinancialSummaryDto);
    vi.mocked(interpreterApi.fetchProjectMovements).mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 30 });
    vi.mocked(interpreterApi.voidMovement).mockResolvedValue({ id: "m1", status: "Voided", voidedAt: "" });
    const { wrapper } = setup({ comCache: false });
    const { result } = renderHook(() => ({
      financeiro: useProjectFinancial("p1"),
      despesas:   useProjectMovements("p1"),
      estornar:   useVoidMovement("p1"),
    }), { wrapper });
    await waitFor(() => expect(result.current.financeiro.isSuccess && result.current.despesas.isSuccess).toBe(true));
    vi.mocked(buildApi.fetchProjectFinancialSummary).mockClear();
    vi.mocked(interpreterApi.fetchProjectMovements).mockClear();

    await act(() => result.current.estornar.mutateAsync("m1"));

    await waitFor(() => expect(buildApi.fetchProjectFinancialSummary).toHaveBeenCalledWith("p1"));
    await waitFor(() => expect(interpreterApi.fetchProjectMovements).toHaveBeenCalledWith("p1", { page: 1, pageSize: 30 }));
  });

  it("se a confirmação falha, o erro chega à tela e o financeiro continua como estava", async () => {
    const falha = new Error("Rascunho expirado.");
    vi.mocked(interpreterApi.confirmMovement).mockRejectedValue(falha);
    const { qc, wrapper } = setup();
    const { result } = renderHook(() => useConfirmMovement("p1"), { wrapper });

    await act(() => result.current.mutateAsync({ draftId: "d1", req: confirmacao }).catch(() => undefined));

    await waitFor(() => expect(result.current.error).toBe(falha));
    expect(desatualizadas(qc)).toEqual([]);
  });

  it("se o estorno falha, o erro chega à tela e nada do cache é descartado", async () => {
    const falha = new Error("Despesa já estornada.");
    vi.mocked(interpreterApi.voidMovement).mockRejectedValue(falha);
    const { qc, wrapper } = setup();
    const { result } = renderHook(() => useVoidMovement("p1"), { wrapper });

    await act(() => result.current.mutateAsync("m1").catch(() => undefined));

    await waitFor(() => expect(result.current.error).toBe(falha));
    expect(desatualizadas(qc)).toEqual([]);
  });
});
