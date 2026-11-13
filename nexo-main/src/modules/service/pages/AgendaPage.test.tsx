import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes, useParams } from "react-router-dom";
import { ApiError } from "@/services/api-client";
import type {
  SvcAppointmentDto, SvcAppointmentStatus, SvcCatalogItemDto, SvcProfessionalDto, SvcTimeBlockDto,
} from "../api/service.api";
import { installRadixPolyfills, openMenu, pickOption, selectTab } from "@/test/helpers/radixInteractions";
import AgendaPage from "./AgendaPage";

const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock("sonner", () => ({ toast }));

const api = vi.hoisted(() => ({
  fetchAppointments: vi.fn(),
  listTimeBlocks: vi.fn(),
  createTimeBlock: vi.fn(),
  deleteTimeBlock: vi.fn(),
  fetchOrders: vi.fn(),
  fetchProfessionals: vi.fn(),
  fetchCatalog: vi.fn(),
  fetchSubjects: vi.fn(),
  changeAppointmentStatus: vi.fn(),
  createOrderFromAppointment: vi.fn(),
  createAppointment: vi.fn(),
  updateAppointment: vi.fn(),
}));
vi.mock("../api/service.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/service.api")>()),
  ...api,
}));

vi.mock("@/modules/customers/hooks/use-customers", () => ({
  useCustomers: () => ({ data: [{ id: "c1", name: "Maria Souza" }, { id: "c2", name: "Pedro Alves" }] }),
}));

vi.mock("../context/ServicePresetContext", () => ({
  useServicePreset: () => ({
    labels: { customer: "Cliente", professional: "Profissional", catalogItem: "Serviço", appointment: "Agendamento",
      order: "Comanda", subject: "—" },
  }),
}));

const professionals = [
  { id: "p1", name: "João Barbeiro", isActive: true },
  { id: "p2", name: "Ana Lima", isActive: true },
  { id: "p3", name: "Carlos (desligado)", isActive: false },
] as SvcProfessionalDto[];
const catalog = [
  { id: "s1", name: "Corte", durationMinutes: 30, requiresSubject: false, isActive: true },
  { id: "s2", name: "Barba", durationMinutes: 20, requiresSubject: false, isActive: true },
] as SvcCatalogItemDto[];

/** Agendamento em hora local (o que a agenda mostra), guardado em UTC como a API devolve. */
function appt(id: string, start: Date, minutes: number, over: Partial<SvcAppointmentDto> = {}): SvcAppointmentDto {
  return {
    id, storeId: "st", customerId: "c1", professionalId: "p1", catalogItemId: "s1", subjectId: null,
    startsAt: start.toISOString(), endsAt: new Date(start.getTime() + minutes * 60000).toISOString(),
    status: "Scheduled" as SvcAppointmentStatus, notes: null, cancellationReason: null, priceSnapshot: 45,
    createdAt: "2026-11-01T12:00:00Z", updatedAt: "2026-11-01T12:00:00Z", ...over,
  };
}
const local = (d: number, h: number, m = 0, month = 11) => new Date(2026, month - 1, d, h, m);
const block = (id: string, start: Date, end: Date, over: Partial<SvcTimeBlockDto> = {}): SvcTimeBlockDto => ({
  id, professionalId: null, startsAt: start.toISOString(), endsAt: end.toISOString(), reason: null,
  createdAt: "2026-11-01T12:00:00Z", ...over,
});

const a1 = appt("a1", local(13, 9), 30);
const a2 = appt("a2", local(13, 8), 45, { customerId: "c2", professionalId: "p2", catalogItemId: "s2", status: "Completed" });
const a3 = appt("a3", local(14, 10), 30, { customerId: "c9" });
const a4 = appt("a4", local(13, 15), 30, { status: "Cancelled" });
const lunch = block("b1", local(13, 12), local(13, 13), { professionalId: "p1", reason: "Almoço" });

const dayRange = (day: string) => ({
  from: new Date(`${day}T00:00:00`).toISOString(),
  to: new Date(`${day}T23:59:59.999`).toISOString(),
});

function OrderProbe() {
  const { id } = useParams();
  return <p>Comanda aberta: {id}</p>;
}

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={["/service/agenda"]}>
        <Routes>
          <Route path="/service/agenda" element={<AgendaPage />} />
          <Route path="/service/ordens/:id" element={<OrderProbe />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

/** A linha da agenda (agendamento ou bloqueio) que contém o texto. */
const line = (text: string | RegExp) => screen.getByText(text).closest<HTMLElement>('div[class*="px-5"]')!;

async function menuOf(customer: string) {
  await screen.findByText(customer);
  openMenu(within(line(customer)).getByRole("button", { name: "Ações do agendamento" }));
  await screen.findByRole("menu");
  return screen.getAllByRole("menuitem");
}
const menuItem = (name: string) => screen.getByRole("menuitem", { name });

beforeEach(() => {
  installRadixPolyfills();
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date(2026, 10, 13, 10, 0)); // sexta-feira
  api.fetchAppointments.mockResolvedValue([a1, a2, a3]);
  api.listTimeBlocks.mockResolvedValue([lunch]);
  api.fetchProfessionals.mockImplementation(async (onlyActive?: boolean) =>
    (onlyActive ? professionals.filter((p) => p.isActive) : professionals));
  api.fetchCatalog.mockResolvedValue(catalog);
  api.fetchSubjects.mockResolvedValue([]);
  api.fetchOrders.mockResolvedValue([]);
  api.changeAppointmentStatus.mockResolvedValue({});
  api.createOrderFromAppointment.mockResolvedValue({ id: "o9" });
  api.createTimeBlock.mockResolvedValue({});
  api.deleteTimeBlock.mockResolvedValue(undefined);
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.clearAllMocks();
});

describe("AgendaPage — o dia", () => {
  it("busca o dia de hoje em horário local e lista em ordem de horário", async () => {
    renderPage();
    expect(await screen.findByText("Maria Souza")).toBeInTheDocument();

    const { from, to } = dayRange("2026-11-13");
    expect(api.fetchAppointments).toHaveBeenCalledWith({ from, to, professionalId: undefined });
    expect(api.listTimeBlocks).toHaveBeenCalledWith(from, to, undefined);
    expect(screen.getByRole("heading", { name: "Agendamentos" })).toBeInTheDocument();
    expect(screen.getByLabelText("Data")).toHaveValue("2026-11-13");

    // só os do dia contam (o de amanhã fica de fora)
    expect(screen.getByText("2 agendamentos")).toBeInTheDocument();
    expect(screen.queryByText("até 10:30")).not.toBeInTheDocument();

    const pedro = line("Pedro Alves");
    const maria = line("Maria Souza");
    expect(pedro.compareDocumentPosition(maria) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    expect(maria).toHaveTextContent("09:00");
    expect(maria).toHaveTextContent("até 09:30");
    expect(maria).toHaveTextContent("Corte · João Barbeiro");
    expect(maria).toHaveTextContent("Agendado");
    expect(pedro).toHaveTextContent("08:00");
    expect(pedro).toHaveTextContent("até 08:45");
    expect(pedro).toHaveTextContent("Barba · Ana Lima");
    expect(pedro).toHaveTextContent("Concluído");

    const almoco = line("Bloqueado");
    expect(almoco).toHaveTextContent("12:00–13:00");
    expect(almoco).toHaveTextContent("João Barbeiro · Almoço");
  });

  it("mostra bloqueio da loja no dia todo e bloqueio que atravessa dias", async () => {
    api.fetchAppointments.mockResolvedValue([appt("x", local(13, 16), 30, { customerId: "c-sumido", catalogItemId: "s-sumido" })]);
    api.listTimeBlocks.mockResolvedValue([
      block("b2", local(13, 0), local(14, 0), { reason: "Feriado municipal" }),
      block("b3", local(12, 18), local(13, 10), { professionalId: "p2" }),
    ]);
    renderPage();
    await screen.findByText("1 agendamento");

    const feriado = line(/Feriado municipal/);
    expect(feriado).toHaveTextContent("Dia todo");
    expect(feriado).toHaveTextContent("Loja inteira · Feriado municipal");

    const plantao = line(/Ana Lima/);
    expect(plantao).toHaveTextContent("12/11 18:00 – 13/11 10:00");
    // sem motivo, a linha termina no nome do profissional
    expect(plantao).toHaveTextContent(/Bloqueado · Ana Lima$/);

    // cliente e serviço que não estão mais na lista aparecem como "—"
    expect(screen.getByText("—")).toBeInTheDocument();
    expect(screen.getByText("— · João Barbeiro")).toBeInTheDocument();
  });

  it("dia vazio mostra o convite para criar, já na data escolhida", async () => {
    api.fetchAppointments.mockResolvedValue([]);
    api.listTimeBlocks.mockResolvedValue([]);
    renderPage();
    expect(await screen.findByText("Nenhum agendamento neste dia")).toBeInTheDocument();
    expect(screen.getByText("0 agendamentos")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Data"), { target: { value: "2026-11-20" } });
    await waitFor(() => expect(api.fetchAppointments).toHaveBeenLastCalledWith({ ...dayRange("2026-11-20"), professionalId: undefined }));

    await screen.findByText("Nenhum agendamento neste dia");
    const buttons = screen.getAllByRole("button", { name: "Novo agendamento" });
    expect(buttons).toHaveLength(2);
    fireEvent.click(buttons[1]);
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByRole("heading", { name: "Novo agendamento" })).toBeInTheDocument();
    expect(within(dialog).getByLabelText("Data *")).toHaveValue("2026-11-20");
  });

  it("apagar a data volta para hoje", async () => {
    renderPage();
    await screen.findByText("Maria Souza");
    fireEvent.change(screen.getByLabelText("Data"), { target: { value: "2026-11-27" } });
    await waitFor(() => expect(screen.getByLabelText("Data")).toHaveValue("2026-11-27"));
    fireEvent.change(screen.getByLabelText("Data"), { target: { value: "" } });
    expect(screen.getByLabelText("Data")).toHaveValue("2026-11-13");
  });

  it("filtra por profissional na agenda e nos bloqueios", async () => {
    renderPage();
    await screen.findByText("Maria Souza");
    await pickOption(screen.getByRole("combobox", { name: "Profissional" }), "Ana Lima");

    const { from, to } = dayRange("2026-11-13");
    await waitFor(() => expect(api.fetchAppointments).toHaveBeenLastCalledWith({ from, to, professionalId: "p2" }));
    await waitFor(() => expect(api.listTimeBlocks).toHaveBeenLastCalledWith(from, to, "p2"));
  });

  it("mostra o erro da agenda e tenta de novo; erro só dos bloqueios é avisado à parte", async () => {
    api.fetchAppointments.mockRejectedValueOnce(new Error("500"));
    api.listTimeBlocks.mockRejectedValue(new Error("500"));
    renderPage();
    expect(await screen.findByText("Não foi possível carregar")).toBeInTheDocument();
    expect(await screen.findByText("Não foi possível carregar os bloqueios da agenda.")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /Tentar novamente/ }));
    expect(await screen.findByText("Maria Souza")).toBeInTheDocument();
    expect(api.fetchAppointments).toHaveBeenCalledTimes(2);
  });
});

describe("AgendaPage — a semana", () => {
  it("busca de segunda a domingo e agrupa por dia, sem dias vazios", async () => {
    renderPage();
    await screen.findByText("Maria Souza");
    selectTab(screen.getByRole("tab", { name: "Semana" }));

    await waitFor(() => expect(api.fetchAppointments).toHaveBeenLastCalledWith({
      from: dayRange("2026-11-09").from, to: dayRange("2026-11-15").to, professionalId: undefined,
    }));
    expect(await screen.findByText("sexta-feira, 13/11")).toBeInTheDocument();
    expect(screen.getByText("sábado, 14/11")).toBeInTheDocument();
    expect(screen.queryByText("segunda-feira, 09/11")).not.toBeInTheDocument();
    expect(screen.getByText("3 agendamentos")).toBeInTheDocument();
    // o de sábado (cliente fora da lista) aparece sob o sábado
    expect(screen.getByText("até 10:30")).toBeInTheDocument();
  });

  it("semana vazia diz \"nesta semana\"", async () => {
    api.fetchAppointments.mockResolvedValue([]);
    api.listTimeBlocks.mockResolvedValue([]);
    renderPage();
    await screen.findByText("Nenhum agendamento neste dia");
    selectTab(screen.getByRole("tab", { name: "Semana" }));
    expect(await screen.findByText("Nenhum agendamento nesta semana")).toBeInTheDocument();
  });
});

describe("AgendaPage — ações do agendamento", () => {
  it("oferece as transições válidas para cada situação", async () => {
    api.fetchAppointments.mockResolvedValue([a1, a2, a4]);
    renderPage();

    expect((await menuOf("Pedro Alves")).map((i) => i.textContent)).toEqual(["Abrir comanda"]);
    fireEvent.keyDown(screen.getByRole("menu"), { key: "Escape" });
    await waitFor(() => expect(screen.queryByRole("menu")).not.toBeInTheDocument());

    const scheduled = await menuOf("09:00");
    expect(scheduled.map((i) => i.textContent)).toEqual([
      "Reagendar / editar", "Abrir comanda", "Confirmar", "Iniciar atendimento", "Concluir", "Cancelar", "Não compareceu",
    ]);
    fireEvent.keyDown(screen.getByRole("menu"), { key: "Escape" });
    await waitFor(() => expect(screen.queryByRole("menu")).not.toBeInTheDocument());

    const cancelled = await menuOf("15:00");
    expect(cancelled.map((i) => i.textContent)).toEqual(["Sem ações disponíveis"]);
    expect(cancelled[0]).toHaveAttribute("aria-disabled", "true");
  });

  it("confirmar muda o status direto e recarrega a agenda", async () => {
    renderPage();
    await menuOf("Maria Souza");
    fireEvent.click(menuItem("Confirmar"));

    await waitFor(() => expect(api.changeAppointmentStatus).toHaveBeenCalledWith("a1", { status: "Confirmed", reason: undefined }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Status: Confirmado."));
    await waitFor(() => expect(api.fetchAppointments).toHaveBeenCalledTimes(2));
  });

  it("falha ao mudar status mostra a mensagem da API ou a padrão", async () => {
    api.changeAppointmentStatus.mockRejectedValueOnce(new ApiError(422, "Agendamento já iniciado."));
    renderPage();
    await menuOf("Maria Souza");
    fireEvent.click(menuItem("Iniciar atendimento"));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Agendamento já iniciado."));
    expect(api.changeAppointmentStatus).toHaveBeenCalledWith("a1", { status: "InProgress", reason: undefined });

    api.changeAppointmentStatus.mockRejectedValueOnce(new Error("rede"));
    await menuOf("Maria Souza");
    fireEvent.click(menuItem("Concluir"));
    await waitFor(() => expect(toast.error).toHaveBeenLastCalledWith("Não foi possível alterar o status."));
  });

  it("cancelar pede o motivo antes de aplicar", async () => {
    renderPage();
    await menuOf("Maria Souza");
    fireEvent.click(menuItem("Cancelar"));

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByRole("heading", { name: "Cancelar" })).toBeInTheDocument();
    expect(api.changeAppointmentStatus).not.toHaveBeenCalled();
    fireEvent.change(within(dialog).getByLabelText("Motivo (opcional)"), { target: { value: "  Cliente desmarcou pelo WhatsApp " } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirmar" }));

    await waitFor(() => expect(api.changeAppointmentStatus).toHaveBeenCalledWith("a1", {
      status: "Cancelled", reason: "Cliente desmarcou pelo WhatsApp",
    }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Status alterado para \"Cancelado\"."));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("não compareceu também pede motivo", async () => {
    renderPage();
    await menuOf("Maria Souza");
    fireEvent.click(menuItem("Não compareceu"));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByRole("heading", { name: "Registrar não comparecimento" })).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirmar" }));
    await waitFor(() => expect(api.changeAppointmentStatus).toHaveBeenCalledWith("a1", { status: "NoShow", reason: null }));
  });

  it("reagendar abre o diálogo de remarcação com o agendamento", async () => {
    renderPage();
    await menuOf("Maria Souza");
    fireEvent.click(menuItem("Reagendar / editar"));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByRole("heading", { name: "Remarcar agendamento" })).toBeInTheDocument();
    expect(within(dialog).getByLabelText("Hora *")).toHaveValue("09:00");
  });

  it("abrir comanda cria a partir do agendamento e vai para ela", async () => {
    renderPage();
    await menuOf("Maria Souza");
    fireEvent.click(menuItem("Abrir comanda"));

    expect(await screen.findByText("Comanda aberta: o9")).toBeInTheDocument();
    expect(api.createOrderFromAppointment.mock.calls[0][0]).toBe("a1");
    expect(api.fetchOrders).not.toHaveBeenCalled();
  });

  it("se o agendamento já tem comanda (409), abre a existente", async () => {
    api.createOrderFromAppointment.mockRejectedValueOnce(new ApiError(409, "Já existe uma comanda para este agendamento."));
    api.fetchOrders.mockResolvedValueOnce([{ id: "o7" }]);
    renderPage();
    await menuOf("Maria Souza");
    fireEvent.click(menuItem("Abrir comanda"));

    expect(await screen.findByText("Comanda aberta: o7")).toBeInTheDocument();
    expect(api.fetchOrders).toHaveBeenCalledWith({ appointmentId: "a1" });
    expect(toast.error).not.toHaveBeenCalled();
  });

  it("409 sem comanda encontrada, ou falha na busca, avisa e fica na agenda", async () => {
    api.createOrderFromAppointment.mockRejectedValue(new ApiError(409, "Já existe uma comanda para este agendamento."));
    api.fetchOrders.mockResolvedValueOnce([]);
    renderPage();
    await menuOf("Maria Souza");
    fireEvent.click(menuItem("Abrir comanda"));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Já existe uma comanda para este agendamento."));

    api.fetchOrders.mockRejectedValueOnce(new Error("rede"));
    await menuOf("Maria Souza");
    fireEvent.click(menuItem("Abrir comanda"));
    await waitFor(() => expect(toast.error).toHaveBeenCalledTimes(2));
    expect(screen.queryByText(/Comanda aberta/)).not.toBeInTheDocument();
  });

  it("erro fora da API ao abrir comanda usa a mensagem padrão", async () => {
    api.createOrderFromAppointment.mockRejectedValueOnce(new Error("rede"));
    renderPage();
    await menuOf("Maria Souza");
    fireEvent.click(menuItem("Abrir comanda"));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Não foi possível abrir a comanda."));
    expect(api.fetchOrders).not.toHaveBeenCalled();
  });
});

describe("AgendaPage — bloqueios", () => {
  it("remover um bloqueio pede um segundo clique de confirmação", async () => {
    renderPage();
    await screen.findByText("Bloqueado");
    const remove = screen.getByRole("button", { name: "Remover bloqueio de João Barbeiro (12:00–13:00)" });

    fireEvent.click(remove);
    expect(api.deleteTimeBlock).not.toHaveBeenCalled();
    const confirm = screen.getByRole("button", { name: "Confirmar remoção do bloqueio de João Barbeiro (12:00–13:00)" });
    expect(confirm).toHaveTextContent("Remover");

    // sair do botão desiste da remoção
    fireEvent.blur(confirm);
    expect(screen.getByRole("button", { name: "Remover bloqueio de João Barbeiro (12:00–13:00)" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /^Remover bloqueio/ }));
    fireEvent.click(screen.getByRole("button", { name: /^Confirmar remoção/ }));

    await waitFor(() => expect(api.deleteTimeBlock).toHaveBeenCalledTimes(1));
    expect(api.deleteTimeBlock.mock.calls[0][0]).toBe("b1");
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Bloqueio removido."));
    await waitFor(() => expect(api.listTimeBlocks).toHaveBeenCalledTimes(2));
  });

  it("erro ao remover bloqueio é avisado", async () => {
    api.listTimeBlocks.mockResolvedValue([block("b2", local(13, 0), local(14, 0))]);
    api.deleteTimeBlock.mockRejectedValueOnce(new Error("Sem permissão para remover bloqueios."));
    renderPage();
    await screen.findByText("Bloqueado");
    fireEvent.click(screen.getByRole("button", { name: "Remover bloqueio de toda a loja (Dia todo)" }));
    fireEvent.click(screen.getByRole("button", { name: "Confirmar remoção do bloqueio de toda a loja (Dia todo)" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Sem permissão para remover bloqueios."));

    // depois do erro a confirmação continua armada: um clique tenta de novo
    api.deleteTimeBlock.mockRejectedValueOnce(new Error(""));
    fireEvent.click(await screen.findByRole("button", { name: /^Confirmar remoção/ }));
    await waitFor(() => expect(toast.error).toHaveBeenLastCalledWith("Não foi possível remover o bloqueio."));
  });

  async function openBlock() {
    fireEvent.click(screen.getByRole("button", { name: "Bloquear horário" }));
    return screen.findByRole("dialog");
  }

  it("bloqueia o horário de almoço de um profissional ativo", async () => {
    renderPage();
    await screen.findByText("Maria Souza");
    const dialog = await openBlock();
    const save = within(dialog).getByRole("button", { name: "Bloquear" });
    // nunca começa na loja inteira: precisa escolher
    expect(within(dialog).getByRole("combobox", { name: "Para" })).toHaveTextContent("Escolha o profissional ou a loja");
    expect(save).toBeDisabled();

    fireEvent.click(within(dialog).getByRole("combobox", { name: "Para" }));
    const options = (await screen.findAllByRole("option")).map((o) => o.textContent);
    expect(options).toEqual(["Loja inteira (feriado)", "João Barbeiro", "Ana Lima"]);
    fireEvent.click(screen.getByRole("option", { name: "João Barbeiro" }));

    expect(within(dialog).getByLabelText("Dia")).toHaveValue("2026-11-13");
    fireEvent.change(within(dialog).getByLabelText("De"), { target: { value: "12:30" } });
    fireEvent.change(within(dialog).getByLabelText("Até"), { target: { value: "13:30" } });
    fireEvent.change(within(dialog).getByLabelText("Motivo"), { target: { value: " Almoço " } });
    fireEvent.click(save);

    await waitFor(() => expect(api.createTimeBlock).toHaveBeenCalledWith({
      professionalId: "p1",
      startsAt: new Date("2026-11-13T12:30:00").toISOString(),
      endsAt: new Date("2026-11-13T13:30:00").toISOString(),
      reason: "Almoço",
    }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Horário bloqueado."));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    await waitFor(() => expect(api.listTimeBlocks).toHaveBeenCalledTimes(2));
  });

  it("feriado: a loja inteira, o dia todo, até a meia-noite seguinte", async () => {
    renderPage();
    await screen.findByText("Maria Souza");
    const dialog = await openBlock();
    await pickOption(within(dialog).getByRole("combobox", { name: "Para" }), "Loja inteira (feriado)");
    fireEvent.change(within(dialog).getByLabelText("Dia"), { target: { value: "2026-12-25" } });
    fireEvent.click(within(dialog).getByLabelText("O dia todo"));
    expect(within(dialog).queryByLabelText("De")).not.toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Bloquear" }));

    await waitFor(() => expect(api.createTimeBlock).toHaveBeenCalledWith({
      professionalId: null,
      startsAt: new Date("2026-12-25T00:00:00").toISOString(),
      endsAt: new Date("2026-12-26T00:00:00").toISOString(),
      reason: null,
    }));
  });

  it("não deixa bloquear com o fim antes do início nem sem dia", async () => {
    renderPage();
    await screen.findByText("Maria Souza");
    const dialog = await openBlock();
    await pickOption(within(dialog).getByRole("combobox", { name: "Para" }), "Ana Lima");
    const save = within(dialog).getByRole("button", { name: "Bloquear" });
    expect(save).toBeEnabled();

    fireEvent.change(within(dialog).getByLabelText("Até"), { target: { value: "11:00" } });
    expect(save).toBeDisabled();
    fireEvent.change(within(dialog).getByLabelText("Até"), { target: { value: "12:00" } });
    expect(save).toBeDisabled(); // fim igual ao início
    fireEvent.change(within(dialog).getByLabelText("Até"), { target: { value: "12:15" } });
    expect(save).toBeEnabled();
    fireEvent.change(within(dialog).getByLabelText("Dia"), { target: { value: "" } });
    expect(save).toBeDisabled();
  });

  it("com um profissional filtrado, o bloqueio já vem para ele", async () => {
    renderPage();
    await screen.findByText("Maria Souza");
    await pickOption(screen.getByRole("combobox", { name: "Profissional" }), "Ana Lima");
    const dialog = await openBlock();
    expect(within(dialog).getByRole("combobox", { name: "Para" })).toHaveTextContent("Ana Lima");
    fireEvent.click(within(dialog).getByRole("button", { name: "Bloquear" }));
    await waitFor(() => expect(api.createTimeBlock).toHaveBeenCalledWith(expect.objectContaining({ professionalId: "p2" })));
  });

  it("erro ao bloquear mantém o diálogo; cancelar fecha", async () => {
    api.createTimeBlock.mockRejectedValueOnce(new Error("Já existe um bloqueio nesse período."));
    renderPage();
    await screen.findByText("Maria Souza");
    const dialog = await openBlock();
    await pickOption(within(dialog).getByRole("combobox", { name: "Para" }), "João Barbeiro");
    fireEvent.click(within(dialog).getByRole("button", { name: "Bloquear" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Já existe um bloqueio nesse período."));
    expect(screen.getByRole("dialog")).toBeInTheDocument();

    api.createTimeBlock.mockRejectedValueOnce(new Error(""));
    fireEvent.click(within(dialog).getByRole("button", { name: "Bloquear" }));
    await waitFor(() => expect(toast.error).toHaveBeenLastCalledWith("Não foi possível bloquear."));

    fireEvent.click(within(dialog).getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });
});
