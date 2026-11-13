import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach } from "vitest";
import { renderHook, act, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider, type QueryKey } from "@tanstack/react-query";
import * as api from "../api/build.api";
import {
  BUILD_KEYS,
  useProjects, useProjectDetails,
  useUpdateProject, useStartProject, usePauseProject, useCompleteProject, useCancelProject,
  useCreateBudget, useConvertBudget, useSetBudgetMargin,
  useAddBudgetItem, useUpdateBudgetItem, useRemoveBudgetItem,
  useCreateDailyLog, useUpdateDailyLog, useAddDailyLogPhoto, useRemoveDailyLogPhoto,
} from "./use-build";

vi.mock("../api/build.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/build.api")>()),
  fetchProjects:       vi.fn(),
  fetchProjectDetails: vi.fn(),
  updateProject:       vi.fn(),
  startProject:        vi.fn(),
  pauseProject:        vi.fn(),
  completeProject:     vi.fn(),
  cancelProject:       vi.fn(),
  createBudget:        vi.fn(),
  convertBudget:       vi.fn(),
  setBudgetMargin:     vi.fn(),
  addBudgetItem:       vi.fn(),
  updateBudgetItem:    vi.fn(),
  removeBudgetItem:    vi.fn(),
  createDailyLog:      vi.fn(),
  updateDailyLog:      vi.fn(),
  addDailyLogPhoto:    vi.fn(),
  removeDailyLogPhoto: vi.fn(),
}));

afterEach(() => { vi.clearAllMocks(); });

// O cache como fica depois de navegar pelo módulo: obra p1 aberta, outra obra (p2) e o orçamento b2 também
// em cache. Cada teste confere exatamente quais destas entradas ficaram desatualizadas após a ação.
const CACHE = {
  carteira:         BUILD_KEYS.projects(),
  carteiraAtivas:   BUILD_KEYS.projects("InProgress"),
  obra:             BUILD_KEYS.project("p1"),
  detalhes:         BUILD_KEYS.projectDetails("p1"),
  financeiro:       BUILD_KEYS.projectFinancial("p1"),
  etapas:           BUILD_KEYS.stages("p1"),
  outraObra:        BUILD_KEYS.project("p2"),
  outrosDetalhes:   BUILD_KEYS.projectDetails("p2"),
  painel:           BUILD_KEYS.dashboard(),
  orcamentos:       BUILD_KEYS.budgets(),
  orcamentosDaObra: BUILD_KEYS.budgets("p1"),
  orcamento:        BUILD_KEYS.budget("b1"),
  outroOrcamento:   BUILD_KEYS.budget("b2"),
  diarios:          [...BUILD_KEYS.dailyLogs("p1"), "pages", 30],
  diario:           BUILD_KEYS.dailyLog("log1"),
  outroDiario:      BUILD_KEYS.dailyLog("log2"),
} satisfies Record<string, QueryKey>;

type Entrada = keyof typeof CACHE;

function setup() {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: Infinity }, mutations: { retry: false } },
  });
  for (const key of Object.values(CACHE)) qc.setQueryData(key, { emCache: true });
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

const ordenado = (xs: Entrada[]) => [...xs].sort();

const projeto = { id: "p1", name: "Residencial Aurora" } as api.BuildProjectDto;
const orcamento = (over: Partial<api.BuildBudgetDto> = {}) =>
  ({ id: "b1", projectId: "p1", name: "Orçamento base", items: [], ...over }) as api.BuildBudgetDto;
const diario = { id: "log1", projectId: "p1", photos: [] } as unknown as api.BuildDailyLogDto;

// Uma obra (detalhe e o próprio registro, que é prefixo do financeiro) e a carteira inteira, em todos os filtros.
const OBRA_E_CARTEIRA: Entrada[] = ["obra", "detalhes", "financeiro", "carteira", "carteiraAtivas"];

describe("obra: editar e mudar de situação", () => {
  it("após editar a obra, o detalhe dela e a carteira ficam desatualizados — as outras obras não", async () => {
    vi.mocked(api.updateProject).mockResolvedValue(projeto);
    const { qc, wrapper } = setup();
    const { result } = renderHook(() => useUpdateProject("p1"), { wrapper });

    const req: api.UpdateBuildProjectRequest = { name: "Residencial Aurora II", clientName: "Construtora Lima", type: "House",
      latitude: -23.5505, longitude: -46.6333, areaM2: 180 };
    await act(() => result.current.mutateAsync(req));

    expect(api.updateProject).toHaveBeenCalledWith("p1", req);
    expect(desatualizadas(qc)).toEqual(ordenado(OBRA_E_CARTEIRA));
  });

  it.each([
    ["iniciar",   useStartProject,    api.startProject],
    ["pausar",    usePauseProject,    api.pauseProject],
    ["concluir",  useCompleteProject, api.completeProject],
    ["cancelar",  useCancelProject,   api.cancelProject],
  ] as const)("%s a obra chama a transição dela e desatualiza o detalhe e a carteira", async (_, useTransicao, fn) => {
    vi.mocked(fn).mockResolvedValue(projeto);
    const { qc, wrapper } = setup();
    const { result } = renderHook(() => useTransicao("p1"), { wrapper });

    await act(() => result.current.mutateAsync());

    expect(fn).toHaveBeenCalledWith("p1");
    expect(desatualizadas(qc)).toEqual(ordenado(OBRA_E_CARTEIRA));
  });

  it("depois de iniciar a obra, a tela aberta busca de novo o detalhe e a carteira", async () => {
    vi.mocked(api.fetchProjectDetails).mockResolvedValue({ ...projeto, stages: [], recentLogs: [] } as api.BuildProjectDetailsDto);
    vi.mocked(api.fetchProjects).mockResolvedValue({ items: [projeto], total: 1, page: 1, pageSize: 50 });
    vi.mocked(api.startProject).mockResolvedValue({ ...projeto, status: "InProgress" });
    const { wrapper } = setup();
    const { result } = renderHook(() => ({
      detalhes: useProjectDetails("p1"),
      carteira: useProjects(),
      iniciar:  useStartProject("p1"),
    }), { wrapper });
    await waitFor(() => expect(result.current.detalhes.isSuccess && result.current.carteira.isSuccess).toBe(true));
    vi.mocked(api.fetchProjectDetails).mockClear();
    vi.mocked(api.fetchProjects).mockClear();

    await act(() => result.current.iniciar.mutateAsync());

    await waitFor(() => expect(api.fetchProjectDetails).toHaveBeenCalledWith("p1"));
    await waitFor(() => expect(api.fetchProjects).toHaveBeenCalledTimes(1));
  });

  it("se a transição falha, o erro chega à tela e nada do cache é descartado", async () => {
    const falha = new Error("Obra cancelada não pode ser iniciada.");
    vi.mocked(api.startProject).mockRejectedValue(falha);
    const { qc, wrapper } = setup();
    const { result } = renderHook(() => useStartProject("p1"), { wrapper });

    await act(() => result.current.mutateAsync().catch(() => undefined));

    await waitFor(() => expect(result.current.error).toBe(falha));
    expect(desatualizadas(qc)).toEqual([]);
  });
});

describe("orçamentos", () => {
  it("criar um orçamento para a obra desatualiza as listas de orçamentos, não os orçamentos já abertos", async () => {
    vi.mocked(api.createBudget).mockResolvedValue(orcamento({ id: "b9" }));
    const { qc, wrapper } = setup();
    const { result } = renderHook(() => useCreateBudget(), { wrapper });

    await act(() => result.current.mutateAsync({ name: "Orçamento base", projectId: "p1" }));

    expect(api.createBudget).toHaveBeenCalledWith({ name: "Orçamento base", projectId: "p1" });
    expect(desatualizadas(qc)).toEqual(ordenado(["orcamentos", "orcamentosDaObra"]));
  });

  it("converter o orçamento em obra desatualiza orçamentos, carteira e a obra de destino", async () => {
    vi.mocked(api.convertBudget).mockResolvedValue(orcamento({ status: "Converted" }));
    const { qc, wrapper } = setup();
    const { result } = renderHook(() => useConvertBudget(), { wrapper });

    await act(() => result.current.mutateAsync({ id: "b1", req: { projectId: "p1" } }));

    expect(api.convertBudget).toHaveBeenCalledWith("b1", { projectId: "p1" });
    expect(desatualizadas(qc)).toEqual(ordenado(["orcamentos", "orcamentosDaObra", ...OBRA_E_CARTEIRA]));
  });

  it("se a conversão não devolve obra, só as listas de orçamentos e a carteira ficam desatualizadas", async () => {
    vi.mocked(api.convertBudget).mockResolvedValue(orcamento({ projectId: null }));
    const { qc, wrapper } = setup();
    const { result } = renderHook(() => useConvertBudget(), { wrapper });

    await act(() => result.current.mutateAsync({ id: "b1", req: { projectId: "p1" } }));

    expect(desatualizadas(qc)).toEqual(ordenado(["orcamentos", "orcamentosDaObra", "carteira", "carteiraAtivas"]));
  });

  it("mudar a margem desatualiza o orçamento devolvido e as listas — não outro orçamento", async () => {
    vi.mocked(api.setBudgetMargin).mockResolvedValue(orcamento({ marginPercent: 15 }));
    const { qc, wrapper } = setup();
    const { result } = renderHook(() => useSetBudgetMargin(), { wrapper });

    await act(() => result.current.mutateAsync({ id: "b1", req: { marginPercent: 15 } }));

    expect(api.setBudgetMargin).toHaveBeenCalledWith("b1", { marginPercent: 15 });
    expect(desatualizadas(qc)).toEqual(ordenado(["orcamento", "orcamentos", "orcamentosDaObra"]));
  });

  describe("itens do orçamento: só o orçamento editado fica desatualizado", () => {
    const item = { name: "Cimento CP-II", category: "Materiais", quantity: 40, unit: "sc", unitCost: 38.9 };

    it("adicionar item", async () => {
      vi.mocked(api.addBudgetItem).mockResolvedValue(orcamento());
      const { qc, wrapper } = setup();
      const { result } = renderHook(() => useAddBudgetItem("b1"), { wrapper });

      await act(() => result.current.mutateAsync(item));

      expect(api.addBudgetItem).toHaveBeenCalledWith("b1", item);
      expect(desatualizadas(qc)).toEqual(["orcamento"]);
    });

    it("alterar item", async () => {
      vi.mocked(api.updateBudgetItem).mockResolvedValue(orcamento());
      const { qc, wrapper } = setup();
      const { result } = renderHook(() => useUpdateBudgetItem("b1"), { wrapper });

      await act(() => result.current.mutateAsync({ id: "it1", req: { ...item, quantity: 50 } }));

      expect(api.updateBudgetItem).toHaveBeenCalledWith("it1", { ...item, quantity: 50 });
      expect(desatualizadas(qc)).toEqual(["orcamento"]);
    });

    it("remover item", async () => {
      vi.mocked(api.removeBudgetItem).mockResolvedValue(orcamento());
      const { qc, wrapper } = setup();
      const { result } = renderHook(() => useRemoveBudgetItem("b1"), { wrapper });

      await act(() => result.current.mutateAsync("it1"));

      expect(api.removeBudgetItem).toHaveBeenCalledWith("it1");
      expect(desatualizadas(qc)).toEqual(["orcamento"]);
    });
  });
});

describe("diário de obra", () => {
  it("registrar o dia desatualiza os diários, o detalhe, a carteira (contagem nos cards) e o painel", async () => {
    vi.mocked(api.createDailyLog).mockResolvedValue(diario);
    const { qc, wrapper } = setup();
    const { result } = renderHook(() => useCreateDailyLog("p1"), { wrapper });

    const req = { date: "2026-11-12", notes: "Concretagem da laje do 2º pavimento." };
    await act(() => result.current.mutateAsync(req));

    expect(api.createDailyLog).toHaveBeenCalledWith("p1", req);
    expect(desatualizadas(qc)).toEqual(ordenado(["diarios", "detalhes", "carteira", "carteiraAtivas", "painel"]));
  });

  it("se o dia já tem registro, o erro chega à tela e nada do cache é descartado", async () => {
    const falha = new Error("Já existe um diário para esta data.");
    vi.mocked(api.createDailyLog).mockRejectedValue(falha);
    const { qc, wrapper } = setup();
    const { result } = renderHook(() => useCreateDailyLog("p1"), { wrapper });

    await act(() => result.current.mutateAsync({ date: "2026-11-12", notes: "x" }).catch(() => undefined));

    await waitFor(() => expect(result.current.error).toBe(falha));
    expect(desatualizadas(qc)).toEqual([]);
  });

  it("editar o registro desatualiza ele e a lista da obra — não outro registro", async () => {
    vi.mocked(api.updateDailyLog).mockResolvedValue(diario);
    const { qc, wrapper } = setup();
    const { result } = renderHook(() => useUpdateDailyLog("p1"), { wrapper });

    await act(() => result.current.mutateAsync({ id: "log1", req: { notes: "Laje concluída." } }));

    expect(api.updateDailyLog).toHaveBeenCalledWith("log1", { notes: "Laje concluída." });
    expect(desatualizadas(qc)).toEqual(ordenado(["diario", "diarios"]));
  });

  it("anexar foto grava a chave no registro e desatualiza ele e a lista", async () => {
    vi.mocked(api.addDailyLogPhoto).mockResolvedValue(diario);
    const { qc, wrapper } = setup();
    const { result } = renderHook(() => useAddDailyLogPhoto("p1"), { wrapper });

    await act(() => result.current.mutateAsync({ logId: "log1", req: { storageKey: "t1/build/laje.jpg" } }));

    expect(api.addDailyLogPhoto).toHaveBeenCalledWith("log1", { storageKey: "t1/build/laje.jpg" });
    expect(desatualizadas(qc)).toEqual(ordenado(["diario", "diarios"]));
  });

  it("remover foto desatualiza a lista de diários da obra", async () => {
    vi.mocked(api.removeDailyLogPhoto).mockResolvedValue(diario);
    const { qc, wrapper } = setup();
    const { result } = renderHook(() => useRemoveDailyLogPhoto("p1"), { wrapper });

    await act(() => result.current.mutateAsync("ph1"));

    expect(api.removeDailyLogPhoto).toHaveBeenCalledWith("ph1");
    expect(desatualizadas(qc)).toEqual(["diarios"]);
  });
});
