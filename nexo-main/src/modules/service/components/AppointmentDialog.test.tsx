import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ApiError } from "@/services/api-client";
import type { SvcAppointmentDto, SvcCatalogItemDto, SvcProfessionalDto, SvcSubjectDto } from "../api/service.api";
import { installRadixPolyfills, pickOption } from "@/test/helpers/radixInteractions";
import { AppointmentDialog } from "./AppointmentDialog";

const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock("sonner", () => ({ toast }));

const api = vi.hoisted(() => ({
  fetchProfessionals: vi.fn(),
  fetchCatalog: vi.fn(),
  fetchSubjects: vi.fn(),
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
    labels: { customer: "Tutor", professional: "Tosador", catalogItem: "Serviço", appointment: "Agendamento",
      order: "Comanda", subject: "Pet" },
  }),
}));

const professionals = [
  { id: "p1", name: "João Barbeiro", isActive: true },
  { id: "p2", name: "Ana Lima", isActive: true },
] as SvcProfessionalDto[];
const catalog = [
  { id: "s1", name: "Corte", durationMinutes: 30, price: 45, requiresSubject: false, isActive: true },
  { id: "s2", name: "Banho e tosa", durationMinutes: 90, price: 120, requiresSubject: true, isActive: true },
] as SvcCatalogItemDto[];
const subjects = [{ id: "sub1", customerId: "c1", kind: "Pet", displayName: "Rex", isActive: true }] as SvcSubjectDto[];

/** Uma hora local do dia escolhido, em UTC, como a API recebe. */
const at = (day: string, time: string) => new Date(`${day}T${time}`).toISOString();

function renderDialog(props: { appointment?: SvcAppointmentDto | null; defaultDate?: string } = {}) {
  const onClose = vi.fn();
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={qc}>
      <AppointmentDialog open onClose={onClose} {...props} />
    </QueryClientProvider>,
  );
  return { onClose, dialog: screen.getByRole("dialog") };
}

/** Os selects do diálogo, na ordem da tela: tutor, tosador, serviço e (quando exigido) pet. */
const selects = (dialog: HTMLElement) => within(dialog).getAllByRole("combobox");

async function fillRequired(dialog: HTMLElement, service = "Corte") {
  await waitFor(() => expect(api.fetchCatalog).toHaveBeenCalled());
  await pickOption(selects(dialog)[0], "Maria Souza");
  await pickOption(selects(dialog)[1], "João Barbeiro");
  await pickOption(selects(dialog)[2], service);
}

beforeEach(() => {
  installRadixPolyfills();
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date(2026, 10, 13, 10, 0));
  api.fetchProfessionals.mockResolvedValue(professionals);
  api.fetchCatalog.mockResolvedValue(catalog);
  api.fetchSubjects.mockImplementation(async (p: { customerId?: string }) => (p.customerId === "c1" ? subjects : []));
  api.createAppointment.mockResolvedValue({ id: "novo" });
  api.updateAppointment.mockResolvedValue({ id: "a1" });
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.clearAllMocks();
});

describe("AppointmentDialog — novo agendamento", () => {
  it("cria no dia da agenda com a duração do serviço e as observações aparadas", async () => {
    const { dialog, onClose } = renderDialog({ defaultDate: "2026-11-20" });
    expect(within(dialog).getByRole("heading", { name: "Novo agendamento" })).toBeInTheDocument();
    expect(within(dialog).getByText("Tutor *")).toBeInTheDocument();
    expect(within(dialog).getByLabelText("Data *")).toHaveValue("2026-11-20");
    expect(within(dialog).getByLabelText("Hora *")).toHaveValue("09:00");
    // só profissionais e serviços ativos são oferecidos
    await waitFor(() => expect(api.fetchProfessionals).toHaveBeenCalledWith(true));
    expect(api.fetchCatalog).toHaveBeenCalledWith(true);

    await fillRequired(dialog);
    fireEvent.change(within(dialog).getByLabelText("Hora *"), { target: { value: "14:30" } });
    fireEvent.change(within(dialog).getByLabelText("Observações"), { target: { value: "  Primeira vez  " } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar" }));

    await waitFor(() => expect(api.createAppointment).toHaveBeenCalledWith({
      customerId: "c1", professionalId: "p1", catalogItemId: "s1",
      startsAt: at("2026-11-20", "14:30"), endsAt: at("2026-11-20", "15:00"),
      subjectId: null, notes: "Primeira vez",
    }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Agendamento criado."));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("sem dia da agenda, abre em hoje; duração ajustada à mão vale para o fim", async () => {
    const { dialog } = renderDialog();
    expect(within(dialog).getByLabelText("Data *")).toHaveValue("2026-11-13");
    await fillRequired(dialog);
    expect(within(dialog).getByLabelText("Duração (min)")).toHaveValue(30);
    fireEvent.change(within(dialog).getByLabelText("Duração (min)"), { target: { value: "50" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar" }));

    await waitFor(() => expect(api.createAppointment).toHaveBeenCalledWith(expect.objectContaining({
      startsAt: at("2026-11-13", "09:00"), endsAt: at("2026-11-13", "09:50"), notes: null,
    })));
  });

  it("pede cada campo obrigatório, na ordem, antes de enviar", async () => {
    const { dialog, onClose } = renderDialog({ defaultDate: "2026-11-20" });
    await waitFor(() => expect(api.fetchCatalog).toHaveBeenCalled());
    const save = within(dialog).getByRole("button", { name: "Salvar" });

    fireEvent.click(save);
    expect(toast.error).toHaveBeenLastCalledWith("Selecione o cliente.");
    await pickOption(selects(dialog)[0], "Maria Souza");
    fireEvent.click(save);
    expect(toast.error).toHaveBeenLastCalledWith("Selecione o profissional.");
    await pickOption(selects(dialog)[1], "Ana Lima");
    fireEvent.click(save);
    expect(toast.error).toHaveBeenLastCalledWith("Selecione o serviço.");
    await pickOption(selects(dialog)[2], "Corte");
    fireEvent.change(within(dialog).getByLabelText("Hora *"), { target: { value: "" } });
    fireEvent.click(save);
    expect(toast.error).toHaveBeenLastCalledWith("Informe data e horário.");

    expect(api.createAppointment).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();
  });

  it("serviço que exige pet: pede o pet do tutor e envia junto", async () => {
    const { dialog } = renderDialog({ defaultDate: "2026-11-20" });
    await fillRequired(dialog, "Banho e tosa");

    // adota a duração do serviço
    expect(within(dialog).getByLabelText("Duração (min)")).toHaveValue(90);
    expect(within(dialog).getByText("Pet *")).toBeInTheDocument();
    await waitFor(() => expect(api.fetchSubjects).toHaveBeenCalledWith({ customerId: "c1", active: true }));

    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar" }));
    expect(toast.error).toHaveBeenLastCalledWith("Este serviço exige um pet.");
    expect(api.createAppointment).not.toHaveBeenCalled();

    await pickOption(selects(dialog)[3], "Rex");
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar" }));

    await waitFor(() => expect(api.createAppointment).toHaveBeenCalledWith(expect.objectContaining({
      catalogItemId: "s2", subjectId: "sub1",
      startsAt: at("2026-11-20", "09:00"), endsAt: at("2026-11-20", "10:30"),
    })));
  });

  it("trocar o tutor limpa o pet escolhido", async () => {
    const { dialog } = renderDialog({ defaultDate: "2026-11-20" });
    await fillRequired(dialog, "Banho e tosa");
    await pickOption(selects(dialog)[3], "Rex");
    expect(selects(dialog)[3]).toHaveTextContent("Rex");

    await pickOption(selects(dialog)[0], "Pedro Alves");
    expect(selects(dialog)[3]).not.toHaveTextContent("Rex");
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar" }));
    expect(toast.error).toHaveBeenLastCalledWith("Este serviço exige um pet.");
  });

  it("conflito de horário (409) avisa e mantém o diálogo aberto", async () => {
    api.createAppointment.mockRejectedValueOnce(new ApiError(409, "Conflict"));
    const { dialog, onClose } = renderDialog({ defaultDate: "2026-11-20" });
    await fillRequired(dialog);
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar" }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Esse profissional já tem um agendamento nesse horário."));
    expect(onClose).not.toHaveBeenCalled();
    expect(toast.success).not.toHaveBeenCalled();
  });

  it("outros erros mostram a mensagem da API ou a padrão", async () => {
    const { dialog } = renderDialog({ defaultDate: "2026-11-20" });
    await fillRequired(dialog);
    const save = within(dialog).getByRole("button", { name: "Salvar" });

    api.createAppointment.mockRejectedValueOnce(new ApiError(422, "Fora do horário de atendimento do profissional."));
    fireEvent.click(save);
    await waitFor(() => expect(toast.error).toHaveBeenLastCalledWith("Fora do horário de atendimento do profissional."));

    api.createAppointment.mockRejectedValueOnce(new ApiError(400, ""));
    fireEvent.click(save);
    await waitFor(() => expect(toast.error).toHaveBeenLastCalledWith("Não foi possível salvar o agendamento."));

    api.createAppointment.mockRejectedValueOnce(new TypeError("Failed to fetch"));
    fireEvent.click(save);
    await waitFor(() => expect(toast.error).toHaveBeenCalledTimes(3));
    expect(toast.error).toHaveBeenLastCalledWith("Não foi possível salvar o agendamento.");
  });

  it("cancelar fecha sem salvar", () => {
    const { dialog, onClose } = renderDialog();
    fireEvent.click(within(dialog).getByRole("button", { name: "Cancelar" }));
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(api.createAppointment).not.toHaveBeenCalled();
  });
});

describe("AppointmentDialog — remarcar", () => {
  const existing: SvcAppointmentDto = {
    id: "a1", storeId: "st", customerId: "c2", professionalId: "p2", catalogItemId: "s1", subjectId: null,
    startsAt: new Date(2026, 10, 20, 9, 15).toISOString(), endsAt: new Date(2026, 10, 20, 10, 0).toISOString(),
    status: "Scheduled", notes: "Prefere tesoura", cancellationReason: null, priceSnapshot: 45,
    createdAt: "2026-11-01T12:00:00Z", updatedAt: "2026-11-01T12:00:00Z",
  };

  it("carrega o agendamento, mantém a duração dele e envia a nova hora", async () => {
    const { dialog, onClose } = renderDialog({ appointment: existing });
    expect(within(dialog).getByRole("heading", { name: "Remarcar agendamento" })).toBeInTheDocument();
    expect(within(dialog).getByLabelText("Data *")).toHaveValue("2026-11-20");
    expect(within(dialog).getByLabelText("Hora *")).toHaveValue("09:15");
    expect(within(dialog).getByLabelText("Observações")).toHaveValue("Prefere tesoura");
    await waitFor(() => expect(selects(dialog)[2]).toHaveTextContent("Corte"));
    expect(selects(dialog)[0]).toHaveTextContent("Pedro Alves");
    expect(selects(dialog)[1]).toHaveTextContent("Ana Lima");
    // 45 min do agendamento, não os 30 padrão do serviço
    expect(within(dialog).getByLabelText("Duração (min)")).toHaveValue(45);

    fireEvent.change(within(dialog).getByLabelText("Hora *"), { target: { value: "10:00" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar" }));

    await waitFor(() => expect(api.updateAppointment).toHaveBeenCalledWith("a1", {
      customerId: "c2", professionalId: "p2", catalogItemId: "s1",
      startsAt: at("2026-11-20", "10:00"), endsAt: at("2026-11-20", "10:45"),
      subjectId: null, notes: "Prefere tesoura",
    }));
    expect(api.createAppointment).not.toHaveBeenCalled();
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Agendamento remarcado."));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("conflito ao remarcar também é avisado", async () => {
    api.updateAppointment.mockRejectedValueOnce(new ApiError(409, "Conflict"));
    const { dialog, onClose } = renderDialog({ appointment: { ...existing, subjectId: "sub9", notes: null } });
    await waitFor(() => expect(selects(dialog)[2]).toHaveTextContent("Corte"));
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Esse profissional já tem um agendamento nesse horário."));
    // o pet informado segue mesmo quando o serviço não o exige
    expect(api.updateAppointment.mock.calls[0][1]).toMatchObject({ subjectId: "sub9", notes: null });
    expect(onClose).not.toHaveBeenCalled();
  });
});
