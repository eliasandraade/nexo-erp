import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, act } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { toast } from "sonner";
import BuildProjectDetailPage from "./BuildProjectDetailPage";
import * as api from "../api/build.api";
import { uploadPrivateFile, type PrivateUploadResult } from "@/services/storage.api";
import { ApiError } from "@/services/api-client";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

vi.mock("../api/build.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/build.api")>()),
  fetchProjectDetails: vi.fn(),
  fetchDailyLogs:      vi.fn(),
  addDailyLogPhoto:    vi.fn(),
}));

vi.mock("@/services/storage.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/services/storage.api")>()),
  uploadPrivateFile: vi.fn(),
}));

afterEach(() => { cleanup(); vi.clearAllMocks(); });

const registro: api.BuildDailyLogDto = {
  id: "log1", projectId: "p1", date: "2026-11-12", weatherSummary: "Ensolarado 28°C",
  notes: "Concretagem da laje do 2º pavimento.", createdAt: "", updatedAt: "", photos: [],
  clientVisible: false, clientSummary: null,
};

const obra = {
  id: "p1", name: "Residencial Aurora", clientName: "Construtora Lima", location: null,
  status: "InProgress", type: "House", startDate: "2026-09-01", expectedEndDate: null, actualEndDate: null,
  budgetEstimated: null, budgetApproved: null, stageCount: 0, completedStageCount: 0, logCount: 1,
  createdAt: "", updatedAt: "", stages: [], recentLogs: [registro],
} as api.BuildProjectDetailsDto;

function abrirDiario() {
  vi.mocked(api.fetchProjectDetails).mockResolvedValue(obra);
  vi.mocked(api.fetchDailyLogs).mockResolvedValue({ items: [registro], total: 1, page: 1, pageSize: 30 });
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={["/build/p1?aba=diario"]}>
        <Routes>
          <Route path="/build/:id" element={<BuildProjectDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

async function escolherFoto(file: File) {
  await screen.findByText(registro.notes);
  const campo = screen.getByLabelText("Adicionar foto");
  fireEvent.change(campo, { target: { files: [file] } });
}

const imagem = () => new File(["png"], "laje.png", { type: "image/png" });

/** Envio ao armazenamento que o teste conclui ou recusa no momento que escolher. */
function envioPendente() {
  let concluir!: (v: PrivateUploadResult) => void;
  let recusar!: (e: unknown) => void;
  vi.mocked(uploadPrivateFile).mockReturnValue(
    new Promise((resolve, reject) => { concluir = resolve; recusar = reject; }) as ReturnType<typeof uploadPrivateFile>,
  );
  return {
    concluir: (key: string) => act(async () => { concluir({ key, url: null }); }),
    recusar:  (e: unknown) => act(async () => { recusar(e); }),
  };
}

describe("BuildProjectDetailPage — foto no diário de obra", () => {
  it("escolher uma imagem envia ao armazenamento privado, grava a chave no registro e recarrega o diário", async () => {
    const envio = envioPendente();
    vi.mocked(api.addDailyLogPhoto).mockResolvedValue({ ...registro, photos: [] });
    abrirDiario();

    const foto = imagem();
    await escolherFoto(foto);

    expect(uploadPrivateFile).toHaveBeenCalledWith(foto, "build-daily-log");
    expect(await screen.findByText("Enviando…")).toBeInTheDocument();
    vi.mocked(api.fetchDailyLogs).mockClear();

    await envio.concluir("t1/build-daily-log/laje.png");

    await waitFor(() => expect(api.addDailyLogPhoto).toHaveBeenCalledWith("log1", { storageKey: "t1/build-daily-log/laje.png" }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Foto adicionada!"));
    await waitFor(() => expect(api.fetchDailyLogs).toHaveBeenCalledWith("p1", { page: 1, pageSize: 30 }));
    expect(screen.getByText("Adicionar foto")).toBeInTheDocument();
  });

  it("um arquivo que não é imagem é recusado antes de qualquer envio", async () => {
    abrirDiario();

    await escolherFoto(new File(["%PDF"], "laudo.pdf", { type: "application/pdf" }));

    expect(toast.error).toHaveBeenCalledWith("Selecione uma imagem (JPG, PNG ou WebP).");
    expect(uploadPrivateFile).not.toHaveBeenCalled();
    expect(api.addDailyLogPhoto).not.toHaveBeenCalled();
  });

  it("com o armazenamento desligado no ambiente, avisa que o upload está indisponível e não grava nada", async () => {
    const envio = envioPendente();
    abrirDiario();

    await escolherFoto(imagem());
    expect(await screen.findByText("Enviando…")).toBeInTheDocument();

    await envio.recusar(new ApiError(404, "Not Found"));

    expect(toast.error).toHaveBeenCalledWith("Upload de fotos indisponível neste ambiente.");
    expect(api.addDailyLogPhoto).not.toHaveBeenCalled();
    expect(screen.getByText("Adicionar foto")).toBeInTheDocument();
  });
});
