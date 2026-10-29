import { describe, it, expect, vi, afterEach } from "vitest";
import { ApiError, apiClient, errorOf } from "./api-client";

const json = (status: number, body: unknown, statusText = "Erro") =>
  new Response(JSON.stringify(body), { status, statusText, headers: { "Content-Type": "application/json" } });

describe("errorOf", () => {
  it("keeps the server's explanation and adds each reason after it", async () => {
    const e = await errorOf(json(422, {
      error: "Operação recusada.", code: "tax_pilot_approval_missing", details: ["Regra A sem aprovação", "Regra B sem aprovação"],
    }));

    expect(e).toBeInstanceOf(ApiError);
    expect(e.status).toBe(422);
    expect(e.code).toBe("tax_pilot_approval_missing");
    expect(e.message).toBe("Operação recusada. Regra A sem aprovação | Regra B sem aprovação");
    expect(e.details).toEqual(["Regra A sem aprovação", "Regra B sem aprovação"]);
  });

  it("does not repeat a reason the explanation already contains", async () => {
    const e = await errorOf(json(422, { error: "Recusada: Regra A sem aprovação.", details: ["Regra A sem aprovação", "Regra B sem aprovação"] }));
    expect(e.message).toBe("Recusada: Regra A sem aprovação. Regra B sem aprovação");

    const all = await errorOf(json(422, { error: "Recusada: Regra A sem aprovação.", details: ["Regra A sem aprovação"] }));
    expect(all.message).toBe("Recusada: Regra A sem aprovação.");
    expect(all.details).toEqual(["Regra A sem aprovação"]);
  });

  it("shows only the details of a bare validation failure", async () => {
    const e = await errorOf(json(400, { error: "Validation failed.", details: ["Nome é obrigatório.", "Valor deve ser positivo."] }));
    expect(e.message).toBe("Nome é obrigatório. | Valor deve ser positivo.");

    const noDetails = await errorOf(json(400, { error: "Validation failed." }));
    expect(noDetails.message).toBe("Validation failed.");
  });

  it("uses the message field, ignores non-text details and falls back to the status text", async () => {
    const e = await errorOf(json(409, { message: "Alterado por outra pessoa.", code: null, details: [42, "Recarregue.", null] }));
    expect(e.message).toBe("Alterado por outra pessoa. Recarregue.");
    expect(e.details).toEqual(["Recarregue."]);
    expect(e.code).toBeUndefined();

    const bare = await errorOf(json(404, {}, "Not Found"));
    expect(bare.message).toBe("Not Found");
    expect(bare.details).toEqual([]);
  });

  it("carries the request id of an unexpected error, for support", async () => {
    const e = await errorOf(json(500, { error: "Erro inesperado.", requestId: "req-123" }));
    expect(e.message).toBe("Erro inesperado. (código: req-123)");
    expect(e.requestId).toBe("req-123");

    const client = await errorOf(json(422, { error: "Recusado.", requestId: "req-9" }));
    expect(client.message).toBe("Recusado.");
    expect(client.requestId).toBe("req-9");
  });

  it("survives an empty or non-JSON body", async () => {
    const e = await errorOf(new Response("<html>", { status: 502, statusText: "Bad Gateway" }));
    expect(e.message).toBe("Bad Gateway");
    expect(e.code).toBeUndefined();
    expect(e.details).toEqual([]);
  });

  it("explains a rate limit without a message", async () => {
    const e = await errorOf(new Response("", { status: 429, statusText: "" }));
    expect(e.message).toBe("Muitas tentativas. Aguarde alguns minutos.");
  });
});

describe("apiClient failures", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("turns a refused JSON request into an ApiError with the details", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(422, { error: "Fora do recorte.", code: "tax_operation_not_supported", details: ["Linha 1: falta NCM."] })));

    const e = await apiClient.post("/tax/calculations", {}).catch((x: unknown) => x);
    expect(e).toBeInstanceOf(ApiError);
    expect(e).toMatchObject({ status: 422, code: "tax_operation_not_supported", details: ["Linha 1: falta NCM."], message: "Fora do recorte. Linha 1: falta NCM." });
  });

  it("turns a refused upload into an ApiError with the code, the request id and the details", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(503, {
      error: "Armazenamento indisponível.", code: "tax_file_storage_unavailable", requestId: "req-7", details: ["Bucket fora do ar"],
    })));

    const e = await apiClient.postForm("/tax/evidence-files", new FormData()).catch((x: unknown) => x);
    expect(e).toBeInstanceOf(ApiError);
    expect(e).toMatchObject({
      status: 503, code: "tax_file_storage_unavailable", requestId: "req-7", details: ["Bucket fora do ar"],
      message: "Armazenamento indisponível. Bucket fora do ar (código: req-7)",
    });
  });

  it("still returns the body of a successful request, and nothing for 204", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(json(200, { ok: true })).mockResolvedValueOnce(new Response(null, { status: 204 })));
    await expect(apiClient.get("/tax/overview")).resolves.toEqual({ ok: true });
    await expect(apiClient.delete("/tax/x")).resolves.toBeUndefined();
  });

  it("still returns the body of a successful upload", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(200, { id: "f1" })));
    await expect(apiClient.postForm("/tax/evidence-files", new FormData())).resolves.toEqual({ id: "f1" });
  });
});
