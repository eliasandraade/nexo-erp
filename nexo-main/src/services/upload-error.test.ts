import { describe, expect, it, vi } from "vitest";
import { apiClient, ApiError } from "./api-client";
import { uploadFile, uploadPrivateFile } from "./storage.api";
import { uploadErrorMessage } from "./upload-error";

describe("uploadErrorMessage", () => {
  it("shows the server's words for too many uploads and for a file too large", () => {
    expect(uploadErrorMessage(new ApiError(429, "Há muitos envios em andamento agora. Aguarde 5 segundos e tente de novo.", "upload_busy"), "Falha."))
      .toBe("Há muitos envios em andamento agora. Aguarde 5 segundos e tente de novo.");
    expect(uploadErrorMessage(new ApiError(413, "Arquivo muito grande. Máximo: 10MB.", "file_too_large"), "Falha."))
      .toBe("Arquivo muito grande. Máximo: 10MB.");
  });

  it("keeps the screen's messages for everything else", () => {
    expect(uploadErrorMessage(new ApiError(404, "off"), "Falha.", "Indisponível.")).toBe("Indisponível.");
    expect(uploadErrorMessage(new ApiError(404, "off"), "Falha.")).toBe("Falha.");
    expect(uploadErrorMessage(new ApiError(503, "x"), "Falha.", "Indisponível.")).toBe("Falha.");
    expect(uploadErrorMessage(new Error("rede"), "Falha.")).toBe("Falha.");
  });
});

describe("storage uploads", () => {
  it("send the context in the address, so the server decides before reading the file, and keep it in the form", async () => {
    const postForm = vi.spyOn(apiClient, "postForm").mockResolvedValue({ key: "k" });
    const file = new File(["x"], "foto.png", { type: "image/png" });

    await uploadFile(file, "product-image");
    await uploadPrivateFile(file, "build-daily-log");

    const [publicUrl, publicForm] = postForm.mock.calls[0];
    expect(publicUrl).toBe("/integrations/storage/upload?context=product-image");
    expect(publicForm.get("context")).toBe("product-image");
    expect(publicForm.get("file")).toBeInstanceOf(File);
    expect(postForm.mock.calls[1][0]).toBe("/integrations/storage/upload?context=build-daily-log");
    postForm.mockRestore();
  });
});
