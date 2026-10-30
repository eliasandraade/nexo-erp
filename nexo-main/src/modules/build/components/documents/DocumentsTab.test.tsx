import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ApiError } from "@/services/api-client";
import * as aiApi from "../../api/build-ai.api";
import * as twinApi from "../../api/build-twin.api";
import { localToday } from "../../lib/format";
import { VISION_DISABLED_MESSAGE, validityBadge } from "../../lib/ai";
import { DocumentsTab } from "./DocumentsTab";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("../../api/build-ai.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-ai.api")>()),
  fetchSuggestions:      vi.fn(),
  fetchDocuments:        vi.fn(),
  uploadDocument:        vi.fn(),
  updateDocument:        vi.fn(),
  setDocumentVisibility: vi.fn(),
  deleteDocument:        vi.fn(),
  analyzeDocument:       vi.fn(),
  acceptSuggestion:      vi.fn(),
}));
vi.mock("../../api/build-twin.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-twin.api")>()),
  fetchBuildSettings: vi.fn(),
}));

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

const plusDays = (n: number) => {
  const d = new Date(`${localToday()}T12:00:00`);
  d.setDate(d.getDate() + n);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
};

const doc = (over: Partial<aiApi.BuildDocumentDto> = {}): aiApi.BuildDocumentDto => ({
  id: "d1", projectId: "p1", category: "Alvara", title: "Alvará de construção", fileName: "alvara.pdf",
  contentType: "application/pdf", sizeBytes: 120_000, sha256: "x", clientVisible: false, validUntil: plusDays(10),
  extracted: null, suggestionId: null, uploadedByName: "Ana", url: "https://files.example/alvara.pdf", urlExpiresAt: null,
  createdAt: "2026-10-30T10:00:00Z", ...over,
});

const settings = (ai: boolean, vision: boolean) => ({ tenantId: "t", build3DEnabled: true, buildAiEnabled: ai,
  buildVisionEnabled: vision, buildPredictEnabled: true, customized: true, updatedByName: null, updatedAt: null });

beforeEach(() => {
  vi.mocked(aiApi.fetchDocuments).mockResolvedValue([doc(), doc({ id: "d2", title: "ART do projeto", category: "ArtRrt", validUntil: plusDays(-1) })]);
  vi.mocked(aiApi.fetchSuggestions).mockResolvedValue([]);
  vi.mocked(twinApi.fetchBuildSettings).mockResolvedValue(settings(true, true));
});
afterEach(() => { cleanup(); vi.clearAllMocks(); });

describe("validityBadge", () => {
  it("expired / today / within 30 days / later", () => {
    expect(validityBadge("2026-10-29", "2026-10-30")?.label).toBe("Vencido");
    expect(validityBadge("2026-10-30", "2026-10-30")?.label).toBe("Vence hoje");
    expect(validityBadge("2026-11-01", "2026-10-30")?.label).toBe("Vence em 2 dias");
    expect(validityBadge("2027-03-01", "2026-10-30")?.label).toBe("Válido até 01/03/2027");
    expect(validityBadge(null, "2026-10-30")).toBeNull();
  });
});

describe("DocumentsTab", () => {
  it("lists category, title, validity badge and the client-visible toggle", async () => {
    vi.mocked(aiApi.setDocumentVisibility).mockResolvedValue(doc({ clientVisible: true }));
    wrap(<DocumentsTab projectId="p1" />);
    const rows = await screen.findAllByTestId("document-row");
    const alvara = rows.find((r) => r.textContent?.includes("Alvará de construção"))!;
    expect(alvara).toHaveTextContent("Alvará");
    expect(alvara).toHaveTextContent("Vence em 10 dias");
    expect(rows.find((r) => r.textContent?.includes("ART do projeto"))).toHaveTextContent("Vencido");
    fireEvent.click(within(alvara).getByRole("checkbox", { name: "Visível ao cliente: Alvará de construção" }));
    await waitFor(() => expect(aiApi.setDocumentVisibility).toHaveBeenCalledWith("d1", true));
  });

  it("upload validates type and size before sending", async () => {
    wrap(<DocumentsTab projectId="p1" />);
    fireEvent.click(await screen.findByRole("button", { name: /Enviar documento/ }));
    const input = screen.getByLabelText(/Arquivo \(PDF, JPEG, PNG ou WEBP, até 20 MB\)/);
    const form = screen.getByTestId("document-upload");

    fireEvent.change(input, { target: { files: [new File(["x"], "planilha.xlsx", { type: "application/vnd.ms-excel" })] } });
    expect(within(form).getByRole("alert")).toHaveTextContent("Formato não aceito. Envie PDF, JPEG, PNG ou WEBP.");
    expect(within(form).getByRole("button", { name: /Enviar documento/ })).toBeDisabled();

    const big = new File(["x"], "contrato.pdf", { type: "application/pdf" });
    Object.defineProperty(big, "size", { value: 21 * 1024 * 1024 });
    fireEvent.change(input, { target: { files: [big] } });
    expect(within(form).getByRole("alert")).toHaveTextContent("o limite é 20 MB");
    expect(within(form).getByRole("button", { name: /Enviar documento/ })).toBeDisabled();
    expect(aiApi.uploadDocument).not.toHaveBeenCalled();
  });

  it("uploads a valid file with category, title and visibility (multipart via uploadDocument)", async () => {
    vi.mocked(aiApi.uploadDocument).mockResolvedValue(doc({ id: "d3" }));
    wrap(<DocumentsTab projectId="p1" />);
    fireEvent.click(await screen.findByRole("button", { name: /Enviar documento/ }));
    const file = new File(["%PDF-"], "contrato.pdf", { type: "application/pdf" });
    fireEvent.change(screen.getByLabelText(/Arquivo \(PDF/), { target: { files: [file] } });
    fireEvent.change(screen.getByLabelText("Categoria", { selector: "#doc-category" }), { target: { value: "Contrato" } });
    fireEvent.click(screen.getByRole("checkbox", { name: "Visível no portal do cliente" }));
    fireEvent.click(within(screen.getByTestId("document-upload")).getByRole("button", { name: /Enviar documento/ }));
    await waitFor(() => expect(aiApi.uploadDocument).toHaveBeenCalled());
    const [projectId, input, onProgress] = vi.mocked(aiApi.uploadDocument).mock.calls[0];
    expect(projectId).toBe("p1");
    expect(input).toMatchObject({ file, category: "Contrato", title: "contrato", clientVisible: true });
    expect(typeof onProgress).toBe("function");
  });

  it("413 from the server is explained", async () => {
    vi.mocked(aiApi.uploadDocument).mockRejectedValue(new ApiError(413, "x", "file_too_large"));
    wrap(<DocumentsTab projectId="p1" />);
    fireEvent.click(await screen.findByRole("button", { name: /Enviar documento/ }));
    fireEvent.change(screen.getByLabelText(/Arquivo \(PDF/), { target: { files: [new File(["x"], "a.png", { type: "image/png" })] } });
    fireEvent.click(within(screen.getByTestId("document-upload")).getByRole("button", { name: /Enviar documento/ }));
    expect(await within(screen.getByTestId("document-upload")).findByRole("alert")).toHaveTextContent("máximo 20 MB");
  });

  it("'Analisar com IA' is disabled without vision, enabled with it, and shows the extracted fields", async () => {
    vi.mocked(twinApi.fetchBuildSettings).mockResolvedValue(settings(true, false));
    const { unmount } = wrap(<DocumentsTab projectId="p1" />);
    await screen.findAllByTestId("document-row");
    expect(await screen.findByText(VISION_DISABLED_MESSAGE)).toBeInTheDocument();
    for (const b of screen.getAllByRole("button", { name: /Analisar com IA/ })) expect(b).toBeDisabled();
    unmount();

    vi.mocked(twinApi.fetchBuildSettings).mockResolvedValue(settings(true, true));
    vi.mocked(aiApi.analyzeDocument).mockResolvedValue({
      id: "a1", projectId: "p1", capability: "DocumentIntelligence", status: "Pending", source: "Llm", subjectId: "d1",
      confidence: 0.9, output: { documentType: "Alvará", issuer: "Prefeitura", issuerCnpj: "12345678000199", number: "42/2026",
        issueDate: "2026-01-10", dueDate: null, validUntil: "2027-01-10", totalAmount: null, items: [], summary: "Alvará válido", confidence: 0.9 },
      inputRefs: { documentId: "d1" }, applied: null, provider: "Claude", model: "m", promptType: "build-document-intelligence",
      promptVersion: null, promptVersionId: null, promptHash: null, inputTokens: 0, outputTokens: 0, costMicros: 0, latencyMs: 0,
      createdByName: "Ana", createdAt: "2026-10-30T10:00:00Z", decidedByName: null, decidedAt: null, decisionNote: null,
      expiresAt: new Date(Date.now() + 86_400_000).toISOString(),
    });
    wrap(<DocumentsTab projectId="p1" />);
    const rows = await screen.findAllByTestId("document-row");
    const alvara = rows.find((r) => r.textContent?.includes("Alvará de construção"))!;
    const btn = within(alvara).getByRole("button", { name: /Analisar com IA/ });
    await waitFor(() => expect(btn).toBeEnabled());
    fireEvent.click(btn);
    const view = await within(alvara).findByTestId("document-extraction");
    expect(aiApi.analyzeDocument).toHaveBeenCalledWith("d1");
    expect(view).toHaveTextContent("Prefeitura");
    expect(view).toHaveTextContent("42/2026");
    expect(within(alvara).getByRole("button", { name: /Aplicar ao documento/ })).toBeInTheDocument();
  });

  it("delete asks for confirmation", async () => {
    vi.mocked(aiApi.deleteDocument).mockResolvedValue(undefined);
    globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;
    wrap(<DocumentsTab projectId="p1" />);
    const rows = await screen.findAllByTestId("document-row");
    fireEvent.click(within(rows[0]).getByRole("button", { name: /Excluir/ }));
    expect(aiApi.deleteDocument).not.toHaveBeenCalled();
    const dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Excluir" }));
    await waitFor(() => expect(aiApi.deleteDocument).toHaveBeenCalledTimes(1));
  });
});
