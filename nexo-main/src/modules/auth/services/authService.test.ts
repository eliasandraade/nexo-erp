import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { getCurrentSession, login, startImpersonationSession, validateSession } from "./authService";
import { TOKEN_KEYS } from "@/services/api-client";
import type { BackendSessionDto } from "../types";

const dto = (extra: Partial<BackendSessionDto>): BackendSessionDto => ({
  userId: "u1", tenantId: "t1", name: "Pessoa", role: "diretoria", login: "pessoa", email: "p@orken.test", activeModules: ["build"], ...extra,
});

const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } });

describe("authService, módulos em somente leitura na sessão", () => {
  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    localStorage.setItem(TOKEN_KEYS.access, "token");
  });

  afterEach(() => vi.unstubAllGlobals());

  it("o login guarda os módulos vencidos", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => json({ accessToken: "a", refreshToken: "r", accessTokenExpiresAt: "", session: dto({ readOnlyModules: ["varejo"] }) })));

    const result = await login({ login: "pessoa", password: "x" });

    expect(result).toMatchObject({ success: true, session: { modules: ["build"], readOnlyModules: ["varejo"] } });
    expect(getCurrentSession()?.readOnlyModules).toEqual(["varejo"]);
  });

  it("a validação em segundo plano atualiza a lista", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => json(dto({ activeModules: [], readOnlyModules: ["build"] }))));

    expect(await validateSession()).toMatchObject({ modules: [], readOnlyModules: ["build"] });
  });

  // a sessão da plataforma e uma resposta de versão anterior não trazem a lista
  it.each([null, undefined])("sem a lista (%s), fica vazia", async (readOnlyModules) => {
    vi.stubGlobal("fetch", vi.fn(async () => json(dto({ readOnlyModules }))));

    expect((await validateSession())?.readOnlyModules).toEqual([]);
  });

  it("a impersonação traz os módulos vencidos", () => {
    const session = startImpersonationSession({ accessToken: "imp", expiresAt: null, session: dto({ activeModules: [], readOnlyModules: ["varejo"] }) });

    expect(session.readOnlyModules).toEqual(["varejo"]);
  });
});

describe("validateSession: só uma recusa do servidor encerra a sessão", () => {
  const stored = () => localStorage.getItem(TOKEN_KEYS.access);

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    localStorage.setItem(TOKEN_KEYS.access, "token");
    localStorage.setItem(TOKEN_KEYS.refresh, "refresh");
  });

  afterEach(() => vi.unstubAllGlobals());

  it("sem conexão: rejeita e mantém os tokens", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => { throw new TypeError("Failed to fetch"); }));

    await expect(validateSession()).rejects.toThrow("Failed to fetch");
    expect(stored()).toBe("token");
  });

  it("erro do servidor: rejeita e mantém os tokens", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify({ error: "Erro interno." }), { status: 500 })));

    await expect(validateSession()).rejects.toMatchObject({ status: 500 });
    expect(stored()).toBe("token");
  });

  it("recusa do servidor: resolve sem sessão e limpa os tokens", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify({ error: "Recusado." }), { status: 401 })));

    expect(await validateSession()).toBeNull();
    expect(stored()).toBeNull();
    expect(localStorage.getItem(TOKEN_KEYS.refresh)).toBeNull();
  });

  it("saiu enquanto a resposta vinha: não traz a sessão de volta", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => {
      localStorage.removeItem(TOKEN_KEYS.access);
      return json(dto({}));
    }));

    expect(await validateSession()).toBeNull();
    expect(localStorage.getItem(TOKEN_KEYS.session)).toBeNull();
  });
});
