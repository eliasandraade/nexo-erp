import type { ReactNode } from "react";
import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  activateCustomer,
  createCustomer,
  deactivateCustomer,
  fetchCustomerById,
  fetchCustomers,
  updateCustomer,
} from "../api/customers.api";
import { emptyCustomerForm, type CustomerDto, type CustomerFormInput } from "../types";
import { useCreateCustomer, useCustomer, useCustomers, useSetCustomerActive, useUpdateCustomer } from "./use-customers";

vi.mock("../api/customers.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/customers.api")>()),
  fetchCustomers: vi.fn(),
  fetchCustomerById: vi.fn(),
  createCustomer: vi.fn(),
  updateCustomer: vi.fn(),
  activateCustomer: vi.fn(),
  deactivateCustomer: vi.fn(),
}));

function cliente(extra: Partial<CustomerDto> = {}): CustomerDto {
  return {
    id: "cli-1", personType: "Individual", name: "Maria Souza", tradeName: null, documentType: "CPF",
    documentNumber: "52998224725", email: "maria@exemplo.com.br", phone: null, whatsApp: null, addressJson: null,
    creditLimit: 500, notes: null, isActive: true, createdAt: "2026-11-01T10:00:00Z", updatedAt: "2026-11-01T10:00:00Z",
    ...extra,
  };
}

const formulario: CustomerFormInput = {
  ...emptyCustomerForm, name: "Maria Souza", documentType: "CPF", documentNumber: "52998224725", email: "maria@exemplo.com.br",
  creditLimit: "500",
};

// A lista de clientes e a ficha de um cliente abertas, como na tela de cadastro.
function renderClientes(id = "cli-1") {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>;
  const hook = renderHook(
    () => ({
      lista: useCustomers(),
      ficha: useCustomer(id),
      criar: useCreateCustomer(),
      editar: useUpdateCustomer(id),
      ativo: useSetCustomerActive(id),
    }),
    { wrapper },
  );
  return { ...hook, client };
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(fetchCustomers).mockResolvedValue([cliente()]);
  vi.mocked(fetchCustomerById).mockResolvedValue(cliente());
});

describe("cadastro de clientes: o que muda no servidor aparece na lista e na ficha", () => {
  it("o cliente novo é enviado como foi digitado e entra na lista recarregada", async () => {
    const novo = cliente({ id: "cli-2", name: "João Lima" });
    vi.mocked(fetchCustomers).mockResolvedValueOnce([cliente()]).mockResolvedValue([cliente(), novo]);
    vi.mocked(createCustomer).mockResolvedValue(novo);
    const { result } = renderClientes();
    await waitFor(() => expect(result.current.lista.data).toHaveLength(1));

    const form = { ...formulario, name: "João Lima" };
    await act(() => result.current.criar.mutateAsync(form));

    expect(createCustomer).toHaveBeenCalledWith(form);
    await waitFor(() => expect(result.current.lista.data?.map((c) => c.name)).toEqual(["Maria Souza", "João Lima"]));
  });

  it("a edição chega à ficha e à lista, que são buscadas de novo", async () => {
    const editado = cliente({ email: "maria.souza@exemplo.com.br" });
    vi.mocked(updateCustomer).mockResolvedValue(editado);
    const { result } = renderClientes();
    await waitFor(() => expect(result.current.ficha.data?.email).toBe("maria@exemplo.com.br"));
    vi.mocked(fetchCustomerById).mockResolvedValue(editado);
    vi.mocked(fetchCustomers).mockResolvedValue([editado]);

    await act(() => result.current.editar.mutateAsync({ ...formulario, email: "maria.souza@exemplo.com.br" }));

    expect(updateCustomer).toHaveBeenCalledWith("cli-1", expect.objectContaining({ email: "maria.souza@exemplo.com.br" }));
    await waitFor(() => expect(result.current.ficha.data?.email).toBe("maria.souza@exemplo.com.br"));
    await waitFor(() => expect(fetchCustomers).toHaveBeenCalledTimes(2));
    expect(result.current.lista.data?.[0].email).toBe("maria.souza@exemplo.com.br");
  });

  it("inativar chama a rota de inativação e recarrega a ficha e a lista", async () => {
    vi.mocked(deactivateCustomer).mockResolvedValue(undefined);
    const { result } = renderClientes();
    await waitFor(() => expect(result.current.ficha.data?.isActive).toBe(true));
    vi.mocked(fetchCustomerById).mockResolvedValue(cliente({ isActive: false }));

    await act(() => result.current.ativo.mutateAsync(false));

    expect(deactivateCustomer).toHaveBeenCalledWith("cli-1");
    expect(activateCustomer).not.toHaveBeenCalled();
    await waitFor(() => expect(result.current.ficha.data?.isActive).toBe(false));
    expect(fetchCustomers).toHaveBeenCalledTimes(2);
  });

  it("reativar chama a rota de ativação", async () => {
    vi.mocked(fetchCustomerById).mockResolvedValue(cliente({ isActive: false }));
    vi.mocked(activateCustomer).mockResolvedValue(undefined);
    const { result } = renderClientes();
    await waitFor(() => expect(result.current.ficha.data?.isActive).toBe(false));
    vi.mocked(fetchCustomerById).mockResolvedValue(cliente({ isActive: true }));

    await act(() => result.current.ativo.mutateAsync(true));

    expect(activateCustomer).toHaveBeenCalledWith("cli-1");
    expect(deactivateCustomer).not.toHaveBeenCalled();
    await waitFor(() => expect(result.current.ficha.data?.isActive).toBe(true));
  });

  it("um cadastro recusado mostra o erro e não recarrega nada", async () => {
    vi.mocked(createCustomer).mockRejectedValue(new Error("Já existe um cliente com este CPF."));
    const { result } = renderClientes();
    await waitFor(() => expect(result.current.lista.isSuccess).toBe(true));

    await act(async () => {
      await expect(result.current.criar.mutateAsync(formulario)).rejects.toThrow("Já existe um cliente com este CPF.");
    });

    await waitFor(() => expect(result.current.criar.error?.message).toBe("Já existe um cliente com este CPF."));
    expect(fetchCustomers).toHaveBeenCalledTimes(1);
    expect(fetchCustomerById).toHaveBeenCalledTimes(1);
  });
});
