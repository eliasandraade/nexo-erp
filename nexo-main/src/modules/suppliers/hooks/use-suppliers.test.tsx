import type { ReactNode } from "react";
import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  activateSupplier,
  createSupplier,
  deactivateSupplier,
  fetchSupplierById,
  fetchSuppliers,
  updateSupplier,
} from "../api/suppliers.api";
import { emptySupplierForm, type SupplierDto, type SupplierFormInput } from "../types";
import { useCreateSupplier, useSetSupplierActive, useSupplier, useSuppliers, useUpdateSupplier } from "./use-suppliers";

vi.mock("../api/suppliers.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/suppliers.api")>()),
  fetchSuppliers: vi.fn(),
  fetchSupplierById: vi.fn(),
  createSupplier: vi.fn(),
  updateSupplier: vi.fn(),
  activateSupplier: vi.fn(),
  deactivateSupplier: vi.fn(),
}));

function fornecedor(extra: Partial<SupplierDto> = {}): SupplierDto {
  return {
    id: "for-1", personType: "Company", name: "Distribuidora Alfa Ltda", tradeName: "Alfa", documentType: "CNPJ",
    documentNumber: "11222333000181", email: "compras@alfa.com.br", phone: null, contactName: "Carlos", addressJson: null,
    paymentTermsDays: 30, bankInfoJson: null, notes: null, isActive: true, createdAt: "2026-11-01T10:00:00Z",
    updatedAt: "2026-11-01T10:00:00Z", ...extra,
  };
}

const formulario: SupplierFormInput = {
  ...emptySupplierForm, name: "Distribuidora Alfa Ltda", tradeName: "Alfa", documentNumber: "11222333000181",
  email: "compras@alfa.com.br", contactName: "Carlos", paymentTermsDays: "30",
};

// A lista de fornecedores e a ficha de um deles abertas, como na tela de cadastro.
function renderFornecedores(id = "for-1") {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>;
  return renderHook(
    () => ({
      lista: useSuppliers(),
      ficha: useSupplier(id),
      criar: useCreateSupplier(),
      editar: useUpdateSupplier(id),
      ativo: useSetSupplierActive(id),
    }),
    { wrapper },
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(fetchSuppliers).mockResolvedValue([fornecedor()]);
  vi.mocked(fetchSupplierById).mockResolvedValue(fornecedor());
});

describe("cadastro de fornecedores: o que muda no servidor aparece na lista e na ficha", () => {
  it("o fornecedor novo é enviado como foi digitado e entra na lista recarregada", async () => {
    const novo = fornecedor({ id: "for-2", name: "Atacado Beta S.A.", tradeName: "Beta" });
    vi.mocked(fetchSuppliers).mockResolvedValueOnce([fornecedor()]).mockResolvedValue([fornecedor(), novo]);
    vi.mocked(createSupplier).mockResolvedValue(novo);
    const { result } = renderFornecedores();
    await waitFor(() => expect(result.current.lista.data).toHaveLength(1));

    const form = { ...formulario, name: "Atacado Beta S.A.", tradeName: "Beta" };
    await act(() => result.current.criar.mutateAsync(form));

    expect(createSupplier).toHaveBeenCalledWith(form);
    await waitFor(() => expect(result.current.lista.data?.map((f) => f.tradeName)).toEqual(["Alfa", "Beta"]));
  });

  it("a edição chega à ficha e à lista, que são buscadas de novo", async () => {
    const editado = fornecedor({ paymentTermsDays: 45 });
    vi.mocked(updateSupplier).mockResolvedValue(editado);
    const { result } = renderFornecedores();
    await waitFor(() => expect(result.current.ficha.data?.paymentTermsDays).toBe(30));
    vi.mocked(fetchSupplierById).mockResolvedValue(editado);
    vi.mocked(fetchSuppliers).mockResolvedValue([editado]);

    await act(() => result.current.editar.mutateAsync({ ...formulario, paymentTermsDays: "45" }));

    expect(updateSupplier).toHaveBeenCalledWith("for-1", expect.objectContaining({ paymentTermsDays: "45" }));
    await waitFor(() => expect(result.current.ficha.data?.paymentTermsDays).toBe(45));
    await waitFor(() => expect(fetchSuppliers).toHaveBeenCalledTimes(2));
    expect(result.current.lista.data?.[0].paymentTermsDays).toBe(45);
  });

  it("inativar chama a rota de inativação e recarrega a ficha e a lista", async () => {
    vi.mocked(deactivateSupplier).mockResolvedValue(undefined);
    const { result } = renderFornecedores();
    await waitFor(() => expect(result.current.ficha.data?.isActive).toBe(true));
    vi.mocked(fetchSupplierById).mockResolvedValue(fornecedor({ isActive: false }));

    await act(() => result.current.ativo.mutateAsync(false));

    expect(deactivateSupplier).toHaveBeenCalledWith("for-1");
    expect(activateSupplier).not.toHaveBeenCalled();
    await waitFor(() => expect(result.current.ficha.data?.isActive).toBe(false));
    expect(fetchSuppliers).toHaveBeenCalledTimes(2);
  });

  it("reativar chama a rota de ativação", async () => {
    vi.mocked(fetchSupplierById).mockResolvedValue(fornecedor({ isActive: false }));
    vi.mocked(activateSupplier).mockResolvedValue(undefined);
    const { result } = renderFornecedores();
    await waitFor(() => expect(result.current.ficha.data?.isActive).toBe(false));
    vi.mocked(fetchSupplierById).mockResolvedValue(fornecedor({ isActive: true }));

    await act(() => result.current.ativo.mutateAsync(true));

    expect(activateSupplier).toHaveBeenCalledWith("for-1");
    expect(deactivateSupplier).not.toHaveBeenCalled();
    await waitFor(() => expect(result.current.ficha.data?.isActive).toBe(true));
  });

  it("uma edição recusada mostra o erro e mantém a ficha como estava", async () => {
    vi.mocked(updateSupplier).mockRejectedValue(new Error("CNPJ inválido."));
    const { result } = renderFornecedores();
    await waitFor(() => expect(result.current.ficha.isSuccess).toBe(true));

    await act(async () => {
      await expect(result.current.editar.mutateAsync({ ...formulario, documentNumber: "123" })).rejects.toThrow("CNPJ inválido.");
    });

    await waitFor(() => expect(result.current.editar.error?.message).toBe("CNPJ inválido."));
    expect(result.current.ficha.data?.documentNumber).toBe("11222333000181");
    expect(fetchSuppliers).toHaveBeenCalledTimes(1);
    expect(fetchSupplierById).toHaveBeenCalledTimes(1);
  });
});
