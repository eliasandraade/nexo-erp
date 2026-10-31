import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";
import { ApiError } from "@/services/api-client";
import * as storageApi from "@/services/storage.api";
import { HandoverSection } from "./HandoverSection";
import * as buildApi from "../../api/build.api";
import * as qualityApi from "../../api/build-quality.api";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("@/services/storage.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/services/storage.api")>()),
  uploadPrivateFile: vi.fn(),
}));
vi.mock("../../api/build.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build.api")>()),
  fetchStages: vi.fn(),
}));
vi.mock("../../api/build-quality.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-quality.api")>()),
  fetchHandover:           vi.fn(),
  saveHandover:            vi.fn(),
  deliverHandover:         vi.fn(),
  acceptHandover:          vi.fn(),
  createWalkthrough:       vi.fn(),
  fetchChecklistTemplates: vi.fn(),
}));

const handover = (over: Partial<qualityApi.BuildHandoverDto> = {}): qualityApi.BuildHandoverDto => ({
  id: "h1", projectId: "p1", status: "Scheduled", scheduledFor: "2026-12-01", walkthroughInspectionId: null,
  walkthroughStatus: null, deliveredAt: null, deliveredByName: null, keysDelivered: false,
  documents: [{ name: "Manual do proprietário", fileKey: "t/build/handover/m.pdf", fileUrl: "https://signed/m.pdf" }],
  notes: "Portão novo", acceptedAt: null, acceptedByName: null, acceptanceChannel: null, acceptanceComment: null,
  projectStatus: "InProgress", projectStatusNote: null, openCriticalNonConformities: 0,
  createdAt: "2026-11-01T10:00:00Z", updatedAt: "2026-11-01T10:00:00Z", ...over,
});

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("HandoverSection", () => {
  beforeEach(() => {
    vi.mocked(buildApi.fetchStages).mockResolvedValue([]);
    vi.mocked(qualityApi.fetchChecklistTemplates).mockResolvedValue([]);
  });
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("not scheduled yet: schedules the delivery with the chosen date", async () => {
    vi.mocked(qualityApi.fetchHandover).mockResolvedValue(null);
    vi.mocked(qualityApi.saveHandover).mockResolvedValue(handover());
    wrap(<HandoverSection projectId="p1" onRunInspection={vi.fn()} />);

    expect(await screen.findByText("A entrega do imóvel ainda não foi agendada.")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Data da entrega"), { target: { value: "2026-12-10" } });
    fireEvent.click(screen.getByRole("button", { name: /Agendar entrega/ }));
    await waitFor(() => expect(qualityApi.saveHandover).toHaveBeenCalledWith("p1", { scheduledFor: "2026-12-10" }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Entrega agendada."));
    // the saved handover replaces the empty state
    expect(await screen.findByText("Entrega do imóvel")).toBeInTheDocument();
  });

  it("scheduling without a date sends null; a failure is toasted", async () => {
    vi.mocked(qualityApi.fetchHandover).mockResolvedValue(null);
    vi.mocked(qualityApi.saveHandover).mockRejectedValue(new Error("Obra cancelada"));
    wrap(<HandoverSection projectId="p1" onRunInspection={vi.fn()} />);

    fireEvent.click(await screen.findByRole("button", { name: /Agendar entrega/ }));
    await waitFor(() => expect(qualityApi.saveHandover).toHaveBeenCalledWith("p1", { scheduledFor: null }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Obra cancelada"));
  });

  it("a load error shows the message", async () => {
    vi.mocked(qualityApi.fetchHandover).mockRejectedValue(new Error("500"));
    wrap(<HandoverSection projectId="p1" onRunInspection={vi.fn()} />);
    expect(await screen.findByText("Erro ao carregar a entrega.")).toBeInTheDocument();
  });

  it("saves date, keys, notes and documents (with and without a file); removes a document", async () => {
    vi.mocked(qualityApi.fetchHandover).mockResolvedValue(handover());
    vi.mocked(qualityApi.saveHandover).mockResolvedValue(handover());
    wrap(<HandoverSection projectId="p1" onRunInspection={vi.fn()} />);

    expect(await screen.findByRole("link", { name: "Manual do proprietário" })).toHaveAttribute("href", "https://signed/m.pdf");
    expect(screen.getByLabelText("Data da entrega")).toHaveValue("2026-12-01");
    expect(screen.getByLabelText("Observações (interno)")).toHaveValue("Portão novo");

    // a document without a name is refused
    fireEvent.click(screen.getByRole("button", { name: "Sem arquivo" }));
    expect(toast.error).toHaveBeenCalledWith("Informe o nome do documento.");
    fireEvent.change(screen.getByLabelText("Nome do documento"), { target: { value: " Habite-se " } });
    fireEvent.click(screen.getByRole("button", { name: "Sem arquivo" }));
    expect(screen.getByText("Habite-se")).toBeInTheDocument();
    expect(screen.getByText("(sem arquivo)")).toBeInTheDocument();
    expect(screen.getByLabelText("Nome do documento")).toHaveValue("");

    // uploading a file without typing a name uses the file name
    vi.mocked(storageApi.uploadPrivateFile).mockResolvedValue({ key: "t/build/handover/art.pdf", url: "https://signed/art.pdf" });
    const file = new File(["%PDF"], "ART.pdf", { type: "application/pdf" });
    fireEvent.change(screen.getByTestId("handover-doc-input"), { target: { files: [file] } });
    await waitFor(() => expect(storageApi.uploadPrivateFile).toHaveBeenCalledWith(file, "build-handover"));
    expect(await screen.findByRole("link", { name: "ART.pdf" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Remover documento Manual do proprietário" }));
    fireEvent.change(screen.getByLabelText("Data da entrega"), { target: { value: "2026-12-15" } });
    fireEvent.click(screen.getByLabelText(/Chaves entregues/));
    fireEvent.change(screen.getByLabelText("Observações (interno)"), { target: { value: "  " } });
    fireEvent.click(screen.getByRole("button", { name: "Salvar entrega" }));

    await waitFor(() => expect(qualityApi.saveHandover).toHaveBeenCalledWith("p1", {
      scheduledFor: "2026-12-15", keysDelivered: true, notes: null,
      documents: [{ name: "Habite-se", fileKey: null }, { name: "ART.pdf", fileKey: "t/build/handover/art.pdf" }],
    }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Entrega salva."));
  });

  it("the upload button refuses other formats, empty and oversized files, and maps upload errors", async () => {
    vi.mocked(qualityApi.fetchHandover).mockResolvedValue(handover());
    vi.mocked(qualityApi.saveHandover).mockRejectedValue(new Error("Conflito"));
    wrap(<HandoverSection projectId="p1" onRunInspection={vi.fn()} />);
    const input = await screen.findByTestId("handover-doc-input");

    fireEvent.change(input, { target: { files: [new File(["x"], "a.txt", { type: "text/plain" })] } });
    expect(toast.error).toHaveBeenLastCalledWith("Formato não permitido. Use PDF, JPG, PNG ou WebP.");
    fireEvent.change(input, { target: { files: [new File([], "vazio.pdf", { type: "application/pdf" })] } });
    expect(toast.error).toHaveBeenLastCalledWith("Arquivo vazio não é permitido.");
    const big = new File(["x"], "big.pdf", { type: "application/pdf" });
    Object.defineProperty(big, "size", { value: 11 * 1024 * 1024 });
    fireEvent.change(input, { target: { files: [big] } });
    expect(toast.error).toHaveBeenLastCalledWith("Arquivo muito grande. Máximo: 10MB.");
    expect(storageApi.uploadPrivateFile).not.toHaveBeenCalled();

    vi.mocked(storageApi.uploadPrivateFile).mockRejectedValueOnce(new ApiError(404, "nf"))
      .mockRejectedValueOnce(new Error("rede"));
    fireEvent.change(input, { target: { files: [new File(["x"], "a.png", { type: "image/png" })] } });
    await waitFor(() => expect(toast.error).toHaveBeenLastCalledWith("Upload de arquivos indisponível neste ambiente."));
    fireEvent.change(input, { target: { files: [new File(["x"], "a.png", { type: "image/png" })] } });
    await waitFor(() => expect(toast.error).toHaveBeenLastCalledWith("Falha ao enviar o arquivo. Tente novamente."));
    expect(screen.getByRole("button", { name: /Enviar arquivo/ })).not.toBeDisabled();

    fireEvent.click(screen.getByRole("button", { name: "Salvar entrega" }));
    await waitFor(() => expect(toast.error).toHaveBeenLastCalledWith("Conflito"));
  });

  it("delivers after confirmation, with the project status note", async () => {
    vi.mocked(qualityApi.fetchHandover).mockResolvedValue(handover({ status: "InspectionDone" }));
    vi.mocked(qualityApi.deliverHandover).mockResolvedValue(handover({
      status: "Delivered", deliveredAt: "2026-12-01T15:00:00Z", deliveredByName: "Ana", projectStatusNote: "Obra concluída com pendências.",
    }));
    wrap(<HandoverSection projectId="p1" onRunInspection={vi.fn()} />);

    fireEvent.click(await screen.findByRole("button", { name: /Entregar imóvel/ }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Entregar o imóvel?")).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Entregar imóvel" }));

    await waitFor(() => expect(qualityApi.deliverHandover).toHaveBeenCalledWith("p1", false));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Imóvel entregue. Obra concluída com pendências."));
    expect(await screen.findByText(/entregue em .* por Ana/)).toBeInTheDocument();
    expect(screen.getByText("Obra concluída com pendências.")).toBeInTheDocument();
    expect(screen.getByText("Entregue sem vistoria com o cliente.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Entregar imóvel/ })).not.toBeInTheDocument();
  });

  it("delivering without a note uses the default message", async () => {
    vi.mocked(qualityApi.fetchHandover).mockResolvedValue(handover({ status: "InspectionDone", keysDelivered: true }));
    vi.mocked(qualityApi.deliverHandover).mockResolvedValue(handover({ status: "Delivered" }));
    wrap(<HandoverSection projectId="p1" onRunInspection={vi.fn()} />);

    fireEvent.click(await screen.findByRole("button", { name: /Entregar imóvel/ }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Entregar imóvel" }));
    await waitFor(() => expect(qualityApi.deliverHandover).toHaveBeenCalledWith("p1", true));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Imóvel entregue. A obra foi concluída."));
  });

  it("open critical NCs are warned about; the walkthrough can be opened", async () => {
    const onRun = vi.fn();
    vi.mocked(qualityApi.fetchHandover).mockResolvedValue(handover({
      status: "InspectionDone", openCriticalNonConformities: 2, walkthroughInspectionId: "in9", walkthroughStatus: "Passed",
    }));
    wrap(<HandoverSection projectId="p1" onRunInspection={onRun} />);

    expect(await screen.findByRole("note")).toHaveTextContent("2 não conformidade(s) crítica(s) aberta(s)");
    expect(screen.getByText("Aprovada")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Abrir vistoria" }));
    expect(onRun).toHaveBeenCalledWith("in9");
  });

  it("creates the walkthrough with the client and opens it", async () => {
    const onRun = vi.fn();
    vi.mocked(qualityApi.fetchHandover).mockResolvedValue(handover({ walkthroughInspectionId: "old", walkthroughStatus: "Cancelled" }));
    vi.mocked(qualityApi.createWalkthrough).mockResolvedValue({ id: "in10" } as qualityApi.BuildInspectionDto);
    wrap(<HandoverSection projectId="p1" onRunInspection={onRun} />);

    fireEvent.click(await screen.findByRole("button", { name: /Criar vistoria com o cliente/ }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByLabelText("Título")).toHaveValue("Vistoria de entrega com o cliente");
    expect(within(dialog).queryByLabelText("Etapa")).not.toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: /Adicionar item/ }));
    fireEvent.change(within(dialog).getByLabelText("Item 1"), { target: { value: "Portas" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Criar vistoria" }));

    await waitFor(() => expect(qualityApi.createWalkthrough).toHaveBeenCalledWith("p1", {
      title: "Vistoria de entrega com o cliente", templateId: null, scheduledFor: null, inspectorName: null,
      items: [{ text: "Portas", required: true, requiresPhoto: false }],
    }));
    await waitFor(() => expect(onRun).toHaveBeenCalledWith("in10"));
    expect(toast.success).toHaveBeenCalledWith("Vistoria com o cliente criada.");
  });

  it("a walkthrough creation error is toasted", async () => {
    vi.mocked(qualityApi.fetchHandover).mockResolvedValue(handover());
    vi.mocked(qualityApi.createWalkthrough).mockRejectedValue(new Error("Já existe"));
    wrap(<HandoverSection projectId="p1" onRunInspection={vi.fn()} />);

    fireEvent.click(await screen.findByRole("button", { name: /Criar vistoria com o cliente/ }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: /Adicionar item/ }));
    fireEvent.change(within(dialog).getByLabelText("Item 1"), { target: { value: "Portas" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Criar vistoria" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Já existe"));
  });

  it("acceptance: disabled while only scheduled; requires the signer; records it; shows the error inline", async () => {
    vi.mocked(qualityApi.fetchHandover).mockResolvedValue(handover());
    wrap(<HandoverSection projectId="p1" onRunInspection={vi.fn()} />);
    expect(await screen.findByRole("button", { name: "Registrar aceite" })).toBeDisabled();
    cleanup();

    vi.mocked(qualityApi.fetchHandover).mockResolvedValue(handover({ status: "InspectionDone" }));
    vi.mocked(qualityApi.acceptHandover)
      .mockRejectedValueOnce(new ApiError(422, "x", undefined, undefined, undefined, "Vistoria pendente"))
      .mockResolvedValueOnce(handover({ status: "InspectionDone", acceptedAt: "2026-12-02T10:00:00Z", acceptedByName: "João",
        acceptanceChannel: "Internal", acceptanceComment: "Tudo certo" }));
    wrap(<HandoverSection projectId="p1" onRunInspection={vi.fn()} />);

    const btn = await screen.findByRole("button", { name: "Registrar aceite" });
    fireEvent.click(btn);
    expect(toast.error).toHaveBeenCalledWith("Informe quem aceitou.");
    fireEvent.change(screen.getByLabelText("Quem aceitou"), { target: { value: " João " } });
    fireEvent.change(screen.getByLabelText("Comentário (opcional)"), { target: { value: " Tudo certo " } });
    fireEvent.click(btn);
    await waitFor(() => expect(qualityApi.acceptHandover).toHaveBeenCalledWith("p1", { signerName: "João", comment: "Tudo certo" }));
    expect(await screen.findByTestId("handover-error")).toHaveTextContent("x");

    fireEvent.click(screen.getByRole("button", { name: "Registrar aceite" }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Aceite registrado."));
    expect(await screen.findByText("Aceito")).toBeInTheDocument();
    expect(screen.getByText("“Tudo certo”")).toBeInTheDocument();
    expect(screen.getByText(/João em/)).toBeInTheDocument();
  });
});
