import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { ApiError } from "@/services/api-client";
import type { SvcCatalogItemDto, SvcOrderDto, SvcOrderItemDto, SvcProfessionalDto } from "../api/service.api";
import { installRadixPolyfills, openMenu, pickOption } from "@/test/helpers/radixInteractions";
import OrderDetailPage from "./OrderDetailPage";

const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock("sonner", () => ({ toast }));

const api = vi.hoisted(() => ({
  fetchOrder: vi.fn(),
  fetchProfessionals: vi.fn(),
  fetchCatalog: vi.fn(),
  changeOrderStatus: vi.fn(),
  addOrderItem: vi.fn(),
  updateOrderItem: vi.fn(),
  removeOrderItem: vi.fn(),
}));
vi.mock("../api/service.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/service.api")>()),
  ...api,
}));

vi.mock("@/modules/customers/hooks/use-customers", () => ({
  useCustomers: () => ({ data: [{ id: "c1", name: "Maria Souza" }] }),
}));

vi.mock("../context/ServicePresetContext", () => ({
  useServicePreset: () => ({
    labels: { customer: "Cliente", professional: "Barbeiro", catalogItem: "Serviço", appointment: "Agendamento",
      order: "Comanda", subject: "—" },
  }),
}));

// Os pagamentos têm o próprio painel e testes; aqui importa o que a comanda passa para ele.
vi.mock("../components/PaymentsPanel", () => ({
  PaymentsPanel: ({ target, canRecord }: { target: { kind: string; id: string }; canRecord: boolean }) => (
    <p>Pagamentos de {target.kind} {target.id} — {canRecord ? "pode registrar" : "somente leitura"}</p>
  ),
}));

const professionals = [
  { id: "p1", name: "João Barbeiro", isActive: true },
  { id: "p2", name: "Ana Lima", isActive: true },
  { id: "p3", name: "Carlos Mendes", isActive: false },
] as SvcProfessionalDto[];
const catalog = [
  { id: "s1", name: "Corte", price: 45, durationMinutes: 30, isActive: true },
  { id: "s3", name: "Hidratação", price: 60, durationMinutes: 40, isActive: true },
] as SvcCatalogItemDto[];

const item = (over: Partial<SvcOrderItemDto>): SvcOrderItemDto => ({
  id: "i", orderId: "o1", catalogItemId: "s1", professionalId: null, nameSnapshot: "", descriptionSnapshot: null,
  quantity: 1, unitPriceSnapshot: 0, commissionPercentSnapshot: null, totalAmount: 0,
  createdAt: "2026-11-13T12:00:00Z", updatedAt: "2026-11-13T12:00:00Z", ...over,
});

const order: SvcOrderDto = {
  id: "o1", storeId: "st", code: "OS-0042", customerId: "c1", subjectId: null, professionalId: "p1", appointmentId: "a1",
  status: "Open", notes: null, cancellationReason: null, totalAmount: 135,
  items: [
    item({ id: "i1", nameSnapshot: "Corte", unitPriceSnapshot: 45, totalAmount: 45 }),
    item({ id: "i2", catalogItemId: "s2", nameSnapshot: "Barba", professionalId: "p2", quantity: 2, unitPriceSnapshot: 45, totalAmount: 90 }),
  ],
  createdAt: "2026-11-13T12:00:00Z", updatedAt: "2026-11-13T12:00:00Z",
};

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={["/service/ordens/o1"]}>
        <Routes>
          <Route path="/service/ordens/:id" element={<OrderDetailPage />} />
          <Route path="/service/ordens" element={<p>Lista de comandas</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

const row = (name: string) => screen.getByRole("cell", { name }).closest("tr")!;
const cols = (name: string) => Array.from(row(name).querySelectorAll("td")).map((td) => td.textContent?.replace(/\s/g, " "));
const catalogSelect = () => screen.getAllByRole("combobox").find((c) => c.textContent?.includes("Adicionar item do catálogo"))!;

beforeEach(() => {
  installRadixPolyfills();
  api.fetchOrder.mockResolvedValue(order);
  api.fetchProfessionals.mockResolvedValue(professionals);
  api.fetchCatalog.mockResolvedValue(catalog);
  api.changeOrderStatus.mockImplementation(async (_id: string, body: { status: SvcOrderDto["status"] }) => ({ ...order, status: body.status }));
  api.addOrderItem.mockResolvedValue(order);
  api.updateOrderItem.mockResolvedValue(order);
  api.removeOrderItem.mockImplementation(async () => {
    const without = { ...order, items: [order.items[1]], totalAmount: 90 };
    api.fetchOrder.mockResolvedValue(without);
    return without;
  });
});
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe("OrderDetailPage — leitura", () => {
  it("mostra a comanda com cliente, situação, itens, valores e total", async () => {
    renderPage();
    expect(await screen.findByRole("heading", { name: "Maria Souza" })).toBeInTheDocument();
    expect(api.fetchOrder).toHaveBeenCalledWith("o1");
    expect(screen.getByText("Comanda OS-0042")).toBeInTheDocument();
    expect(screen.getByText("Aberta")).toBeInTheDocument();

    expect(cols("Corte")).toEqual(["Corte", "Da ordem (João Barbeiro)", "1", "R$ 45,00", "R$ 45,00", ""]);
    expect(cols("Barba")).toEqual(["Barba", "Ana Lima", "2", "R$ 45,00", "R$ 90,00", ""]);
    expect(screen.getByText("Total", { selector: "td" }).closest("tr")).toHaveTextContent("R$ 135,00");
    expect(screen.getByText("Pagamentos de order o1 — pode registrar")).toBeInTheDocument();
  });

  it("comanda concluída fica só para leitura, com os profissionais por extenso", async () => {
    api.fetchOrder.mockResolvedValue({
      ...order, status: "Completed",
      items: [...order.items, item({ id: "i3", nameSnapshot: "Sobrancelha", professionalId: "p9", unitPriceSnapshot: 15, totalAmount: 15 })],
    });
    api.fetchProfessionals.mockResolvedValue(professionals);
    renderPage();
    await screen.findByText("Concluída");

    expect(screen.queryByRole("combobox")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Remover item" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Adicionar/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Mudar status" })).not.toBeInTheDocument();
    // item sem profissional próprio mostra o da comanda; desconhecido cai no da comanda também
    expect(cols("Corte")[1]).toBe("João Barbeiro");
    expect(cols("Barba")[1]).toBe("Ana Lima");
    expect(cols("Sobrancelha")[1]).toBe("João Barbeiro");
    expect(screen.getByText("Pagamentos de order o1 — pode registrar")).toBeInTheDocument();
  });

  it("comanda cancelada sem profissional não deixa registrar pagamento", async () => {
    api.fetchOrder.mockResolvedValue({ ...order, status: "Cancelled", professionalId: null, items: [item({ id: "i1", nameSnapshot: "Corte" })] });
    renderPage();
    await screen.findByText("Cancelada");
    expect(cols("Corte")[1]).toBe("—");
    expect(screen.getByText("Pagamentos de order o1 — somente leitura")).toBeInTheDocument();
  });

  it("comanda sem itens avisa e ainda deixa adicionar", async () => {
    api.fetchOrder.mockResolvedValue({ ...order, items: [], totalAmount: 0, professionalId: null });
    renderPage();
    expect(await screen.findByText("Nenhum item nesta ordem.")).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "Barbeiro do item" })).toHaveTextContent("Sem barbeiro");
    expect(screen.getByRole("button", { name: "Adicionar" })).toBeDisabled();
  });

  it("comanda inexistente mostra não encontrado e volta para a lista", async () => {
    api.fetchOrder.mockRejectedValue(new ApiError(404, "Não encontrado"));
    renderPage();
    expect(await screen.findByText("Não encontrado")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Voltar" }));
    expect(await screen.findByText("Lista de comandas")).toBeInTheDocument();
  });

  it("o atalho do topo volta para as comandas", async () => {
    renderPage();
    await screen.findByText("Comanda OS-0042");
    fireEvent.click(screen.getByRole("button", { name: "Comandas" }));
    expect(await screen.findByText("Lista de comandas")).toBeInTheDocument();
  });
});

describe("OrderDetailPage — status", () => {
  it("oferece só as transições válidas e aplica a escolhida", async () => {
    renderPage();
    await screen.findByText("Comanda OS-0042");
    openMenu(screen.getByRole("button", { name: "Mudar status" }));
    expect((await screen.findAllByRole("menuitem")).map((i) => i.textContent)).toEqual(["Iniciar", "Cancelar"]);

    api.fetchOrder.mockResolvedValue({ ...order, status: "InProgress" });
    fireEvent.click(screen.getByRole("menuitem", { name: "Iniciar" }));
    await waitFor(() => expect(api.changeOrderStatus).toHaveBeenCalledWith("o1", { status: "InProgress", reason: undefined }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Status: Em andamento."));
    // a comanda é recarregada e já mostra a nova situação
    expect(await screen.findByText("Em andamento")).toBeInTheDocument();
  });

  it("erro ao mudar status mostra a mensagem da API ou a padrão", async () => {
    api.changeOrderStatus.mockRejectedValueOnce(new ApiError(422, "Comanda com pagamento pendente."));
    renderPage();
    await screen.findByText("Comanda OS-0042");
    openMenu(screen.getByRole("button", { name: "Mudar status" }));
    fireEvent.click(await screen.findByRole("menuitem", { name: "Cancelar" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Comanda com pagamento pendente."));
    expect(api.changeOrderStatus).toHaveBeenCalledWith("o1", { status: "Cancelled", reason: undefined });

    api.changeOrderStatus.mockRejectedValueOnce(new Error("rede"));
    openMenu(screen.getByRole("button", { name: "Mudar status" }));
    fireEvent.click(await screen.findByRole("menuitem", { name: "Iniciar" }));
    await waitFor(() => expect(toast.error).toHaveBeenLastCalledWith("Não foi possível alterar o status."));
  });
});

describe("OrderDetailPage — itens", () => {
  it("adiciona um item do catálogo com quantidade e profissional e limpa o formulário", async () => {
    const withNewItem: SvcOrderDto = {
      ...order, totalAmount: 255,
      items: [...order.items, item({ id: "i3", catalogItemId: "s3", nameSnapshot: "Hidratação", professionalId: "p2",
        quantity: 2, unitPriceSnapshot: 60, totalAmount: 120 })],
    };
    api.addOrderItem.mockImplementationOnce(async () => {
      api.fetchOrder.mockResolvedValue(withNewItem);
      return withNewItem;
    });
    renderPage();
    await screen.findByText("Comanda OS-0042");

    fireEvent.click(catalogSelect());
    expect((await screen.findAllByRole("option")).map((o) => o.textContent?.replace(/\s/g, " ")))
      .toEqual(["Corte · R$ 45,00", "Hidratação · R$ 60,00"]);
    fireEvent.click(screen.getByRole("option", { name: /Hidratação/ }));

    const proSelect = screen.getByRole("combobox", { name: "Barbeiro do item" });
    expect(proSelect).toHaveTextContent("Da ordem (João Barbeiro)");
    fireEvent.click(proSelect);
    // inativos não são oferecidos
    expect((await screen.findAllByRole("option")).map((o) => o.textContent))
      .toEqual(["Da ordem (João Barbeiro)", "João Barbeiro", "Ana Lima"]);
    fireEvent.click(screen.getByRole("option", { name: "Ana Lima" }));

    fireEvent.change(screen.getByLabelText("Quantidade"), { target: { value: "2" } });
    fireEvent.click(screen.getByRole("button", { name: "Adicionar" }));

    await waitFor(() => expect(api.addOrderItem).toHaveBeenCalledWith("o1", { catalogItemId: "s3", quantity: 2, professionalId: "p2" }));
    expect(await screen.findByRole("cell", { name: "Hidratação" })).toBeInTheDocument();
    expect(cols("Hidratação").slice(2, 5)).toEqual(["2", "R$ 60,00", "R$ 120,00"]);
    expect(screen.getByText("Total", { selector: "td" }).closest("tr")).toHaveTextContent("R$ 255,00");
    await waitFor(() => expect(screen.getByLabelText("Quantidade")).toHaveValue(1));
    expect(catalogSelect()).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "Barbeiro do item" })).toHaveTextContent("Da ordem (João Barbeiro)");
  });

  it("sem escolher profissional, o item fica com o da comanda", async () => {
    renderPage();
    await screen.findByText("Comanda OS-0042");
    await pickOption(catalogSelect(), /Corte/);
    fireEvent.click(screen.getByRole("button", { name: "Adicionar" }));
    await waitFor(() => expect(api.addOrderItem).toHaveBeenCalledWith("o1", { catalogItemId: "s1", quantity: 1, professionalId: null }));
  });

  it("recusa quantidade inválida sem chamar a API", async () => {
    renderPage();
    await screen.findByText("Comanda OS-0042");
    await pickOption(catalogSelect(), /Corte/);
    for (const qty of ["0", "-1", ""]) {
      fireEvent.change(screen.getByLabelText("Quantidade"), { target: { value: qty } });
      fireEvent.click(screen.getByRole("button", { name: "Adicionar" }));
      expect(toast.error).toHaveBeenLastCalledWith("Quantidade inválida.");
    }
    expect(api.addOrderItem).not.toHaveBeenCalled();
  });

  it("erro ao adicionar mantém a escolha e avisa", async () => {
    api.addOrderItem.mockRejectedValueOnce(new ApiError(422, "Item inativo no catálogo."));
    renderPage();
    await screen.findByText("Comanda OS-0042");
    await pickOption(catalogSelect(), /Hidratação/);
    fireEvent.click(screen.getByRole("button", { name: "Adicionar" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Item inativo no catálogo."));
    expect(screen.getByRole("button", { name: "Adicionar" })).toBeEnabled();

    api.addOrderItem.mockRejectedValueOnce(new Error("rede"));
    fireEvent.click(screen.getByRole("button", { name: "Adicionar" }));
    await waitFor(() => expect(toast.error).toHaveBeenLastCalledWith("Não foi possível adicionar o item."));
  });

  it("troca quem fez o item (comissão) mantendo a quantidade", async () => {
    renderPage();
    await screen.findByText("Comanda OS-0042");
    await pickOption(screen.getByRole("combobox", { name: "Barbeiro de Barba" }), "João Barbeiro");
    await waitFor(() => expect(api.updateOrderItem).toHaveBeenCalledWith("o1", "i2", { quantity: 2, professionalId: "p1" }));

    await pickOption(screen.getByRole("combobox", { name: "Barbeiro de Corte" }), "Ana Lima");
    await waitFor(() => expect(api.updateOrderItem).toHaveBeenLastCalledWith("o1", "i1", { quantity: 1, professionalId: "p2" }));
  });

  it("voltar o item para o profissional da comanda envia nulo", async () => {
    renderPage();
    await screen.findByText("Comanda OS-0042");
    await pickOption(screen.getByRole("combobox", { name: "Barbeiro de Barba" }), "Da ordem (João Barbeiro)");
    await waitFor(() => expect(api.updateOrderItem).toHaveBeenCalledWith("o1", "i2", { quantity: 2, professionalId: null }));
  });

  it("item de profissional desligado continua mostrando quem fez", async () => {
    api.fetchOrder.mockResolvedValue({ ...order, items: [item({ id: "i5", nameSnapshot: "Pigmentação", professionalId: "p3" })] });
    renderPage();
    const select = await screen.findByRole("combobox", { name: "Barbeiro de Pigmentação" });
    expect(select).toHaveTextContent("Carlos Mendes");
    fireEvent.click(select);
    expect((await screen.findAllByRole("option")).map((o) => o.textContent))
      .toEqual(["Da ordem (João Barbeiro)", "João Barbeiro", "Ana Lima", "Carlos Mendes"]);
  });

  it("erro ao trocar o profissional do item é avisado", async () => {
    api.updateOrderItem.mockRejectedValueOnce(new ApiError(409, "Comissão já paga para este item."));
    renderPage();
    await screen.findByText("Comanda OS-0042");
    await pickOption(screen.getByRole("combobox", { name: "Barbeiro de Corte" }), "Ana Lima");
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Comissão já paga para este item."));

    api.updateOrderItem.mockRejectedValueOnce(new Error("rede"));
    await pickOption(screen.getByRole("combobox", { name: "Barbeiro de Corte" }), "João Barbeiro");
    await waitFor(() => expect(toast.error).toHaveBeenLastCalledWith("Não foi possível trocar o profissional."));
  });

  it("remove um item e o total acompanha", async () => {
    renderPage();
    await screen.findByText("Comanda OS-0042");
    fireEvent.click(within(row("Corte")).getByRole("button", { name: "Remover item" }));

    await waitFor(() => expect(api.removeOrderItem).toHaveBeenCalledWith("o1", "i1"));
    await waitFor(() => expect(screen.queryByRole("cell", { name: "Corte" })).not.toBeInTheDocument());
    expect(screen.getByText("Total", { selector: "td" }).closest("tr")).toHaveTextContent("R$ 90,00");
  });

  it("erro ao remover avisa e mantém o item", async () => {
    api.removeOrderItem.mockRejectedValueOnce(new ApiError(409, "Item já pago não pode ser removido."));
    renderPage();
    await screen.findByText("Comanda OS-0042");
    fireEvent.click(within(row("Corte")).getByRole("button", { name: "Remover item" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Item já pago não pode ser removido."));
    expect(screen.getByRole("cell", { name: "Corte" })).toBeInTheDocument();

    api.removeOrderItem.mockRejectedValueOnce(new Error("rede"));
    fireEvent.click(within(row("Corte")).getByRole("button", { name: "Remover item" }));
    await waitFor(() => expect(toast.error).toHaveBeenLastCalledWith("Não foi possível remover o item."));
  });
});
