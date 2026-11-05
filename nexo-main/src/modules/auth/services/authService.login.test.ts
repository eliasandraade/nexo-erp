import { describe, it, expect, vi, afterEach } from "vitest";
import { login } from "./authService";

const input = { login: "ana", password: "x".repeat(8) };

const answer = (status: number, body: unknown) =>
  vi.fn().mockResolvedValue(new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } }));

describe("authService.login: mensagem de erro", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("explains the connection failure when the server cannot be reached", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("Failed to fetch")));

    await expect(login(input)).resolves.toEqual({ success: false, error: "Erro ao conectar com o servidor." });
  });

  it("keeps the explanation sent by the server", async () => {
    vi.stubGlobal("fetch", answer(403, { error: "Conta bloqueada." }));

    await expect(login(input)).resolves.toEqual({ success: false, error: "Conta bloqueada." });
  });

  it("says the credentials are wrong on a 401 without explanation", async () => {
    vi.stubGlobal("fetch", answer(401, {}));

    await expect(login(input)).resolves.toEqual({ success: false, error: "Login ou senha incorretos." });
  });
});
