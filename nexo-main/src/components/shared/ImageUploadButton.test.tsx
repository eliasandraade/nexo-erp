import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { toast } from "sonner";
import { ApiError } from "@/services/api-client";
import { uploadFile, uploadPrivateFile } from "@/services/storage.api";
import { PrivateUploadButton } from "@/modules/build/components/quality/PrivateUploadButton";
import { ImageUploadButton } from "./ImageUploadButton";

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));
vi.mock("@/services/storage.api", () => ({ uploadFile: vi.fn(), uploadPrivateFile: vi.fn() }));

const BUSY = "Há muitos envios em andamento agora. Aguarde 5 segundos e tente de novo.";

const pick = (container: HTMLElement) => {
  const input = container.querySelector("input[type=file]") as HTMLInputElement;
  fireEvent.change(input, { target: { files: [new File(["x"], "foto.png", { type: "image/png" })] } });
};

describe("upload buttons show why the server refused", () => {
  beforeEach(() => vi.clearAllMocks());

  it("ImageUploadButton shows the 429 message", async () => {
    vi.mocked(uploadFile).mockRejectedValue(new ApiError(429, BUSY, "upload_busy"));
    const { container } = render(<ImageUploadButton context="product-image" value={null} onChange={vi.fn()} />);
    pick(container);
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith(BUSY));
    expect(screen.getByRole("button", { name: /adicionar imagem/i })).toBeEnabled();
  });

  it("ImageUploadButton shows the 413 message and keeps its own for other failures", async () => {
    vi.mocked(uploadFile).mockRejectedValueOnce(new ApiError(413, "Arquivo muito grande. Máximo: 5MB.", "file_too_large"));
    const { container } = render(<ImageUploadButton context="product-image" value={null} onChange={vi.fn()} />);
    pick(container);
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Arquivo muito grande. Máximo: 5MB."));

    vi.mocked(uploadFile).mockRejectedValueOnce(new ApiError(503, "indisponível"));
    pick(container);
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Falha ao enviar imagem. Tente novamente."));
  });

  it("PrivateUploadButton shows the 429 message and keeps the unavailable one for a 404", async () => {
    vi.mocked(uploadPrivateFile).mockRejectedValueOnce(new ApiError(429, BUSY, "upload_busy"));
    const { container } = render(<PrivateUploadButton context="build-quality" label="Foto" onUploaded={vi.fn()} />);
    pick(container);
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith(BUSY));

    vi.mocked(uploadPrivateFile).mockRejectedValueOnce(new ApiError(404, "off"));
    pick(container);
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Upload de arquivos indisponível neste ambiente."));
  });
});
