import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { apiClient, ApiError, TOKEN_KEYS, leaveImpersonation, beginImpersonation, getImpersonation, IMPERSONATION_KEY } from "./api-client";

const API = "http://localhost:5000/api";

/** A scripted XMLHttpRequest: each send() is answered by the next responder in the queue. */
class FakeXhr {
  static sent: FakeXhr[] = [];
  static responders: Array<(x: FakeXhr) => void> = [];
  method = "";
  url = "";
  headers: Record<string, string> = {};
  body: FormData | null = null;
  status = 0;
  statusText = "";
  responseText = "";
  upload: { onprogress: ((e: { loaded: number; total: number; lengthComputable: boolean }) => void) | null } = { onprogress: null };
  onload: (() => void) | null = null;
  onerror: (() => void) | null = null;
  onabort: (() => void) | null = null;
  open(method: string, url: string) { this.method = method; this.url = url; }
  setRequestHeader(k: string, v: string) { this.headers[k] = v; }
  send(body: FormData) {
    this.body = body;
    FakeXhr.sent.push(this);
    const next = FakeXhr.responders.shift();
    queueMicrotask(() => next?.(this));
  }
  reply(status: number, body: unknown = "", statusText = "") {
    this.status = status;
    this.statusText = statusText;
    this.responseText = typeof body === "string" ? body : JSON.stringify(body);
    this.onload?.();
  }
}

const respond = (...rs: Array<(x: FakeXhr) => void>) => { FakeXhr.responders.push(...rs); };

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

describe("apiClient.upload (XMLHttpRequest)", () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    FakeXhr.sent = [];
    FakeXhr.responders = [];
    vi.stubGlobal("XMLHttpRequest", FakeXhr);
    fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
  });
  afterEach(() => vi.unstubAllGlobals());

  it("sends the form with the bearer token, reports progress and parses the JSON answer", async () => {
    localStorage.setItem(TOKEN_KEYS.access, "tok");
    const progress = vi.fn();
    respond((x) => {
      x.upload.onprogress?.({ loaded: 50, total: 200, lengthComputable: true });
      x.upload.onprogress?.({ loaded: 80, total: 0, lengthComputable: false });
      x.reply(201, { id: "m1" });
    });
    const form = new FormData();
    form.append("file", new File(["x"], "a.ifc"));

    await expect(apiClient.upload("POST", "/v1/build/projects/p1/models", form, progress)).resolves.toEqual({ id: "m1" });
    const xhr = FakeXhr.sent[0];
    expect([xhr.method, xhr.url, xhr.body]).toEqual(["POST", `${API}/v1/build/projects/p1/models`, form]);
    expect(xhr.headers.Authorization).toBe("Bearer tok");
    expect(progress).toHaveBeenNthCalledWith(1, 50, 200);
    expect(progress).toHaveBeenNthCalledWith(2, 80, 0);
  });

  it("without a token sends no Authorization; 204 / empty bodies resolve to undefined", async () => {
    respond((x) => x.reply(204), (x) => x.reply(200, ""));
    await expect(apiClient.upload("PUT", "/v1/x", new FormData())).resolves.toBeUndefined();
    await expect(apiClient.upload("PUT", "/v1/x", new FormData())).resolves.toBeUndefined();
    expect(FakeXhr.sent[0].headers.Authorization).toBeUndefined();
    expect(FakeXhr.sent[0].upload.onprogress).toBeNull();
  });

  it("maps error bodies: details, server message, status text and the request id of 5xx", async () => {
    respond(
      (x) => x.reply(422, { error: "Inválido", code: "viewer_mismatch", details: ["a", 2] }),
      (x) => x.reply(413, { message: "Grande demais" }),
      (x) => x.reply(400, "not json", "Bad Request"),
      (x) => x.reply(500, { error: "Falhou", requestId: "req-9" }),
    );

    const e1 = await apiClient.upload("POST", "/v1/u", new FormData()).catch((e) => e);
    expect(e1).toBeInstanceOf(ApiError);
    expect(e1).toMatchObject({ status: 422, message: "a | 2", code: "viewer_mismatch", details: ["a", "2"], serverMessage: "Inválido" });

    const e2 = await apiClient.upload("POST", "/v1/u", new FormData()).catch((e) => e);
    expect(e2).toMatchObject({ status: 413, message: "Grande demais", serverMessage: "Grande demais" });

    const e3 = await apiClient.upload("POST", "/v1/u", new FormData()).catch((e) => e);
    expect(e3).toMatchObject({ status: 400, message: "Bad Request", serverMessage: undefined });

    const e4 = await apiClient.upload("POST", "/v1/u", new FormData()).catch((e) => e);
    expect(e4).toMatchObject({ status: 500, message: "Falhou (código: req-9)", requestId: "req-9" });
  });

  it("network failure and abort become status-0 errors", async () => {
    respond((x) => x.onerror?.(), (x) => x.onabort?.());
    await expect(apiClient.upload("POST", "/v1/u", new FormData())).rejects.toMatchObject({
      status: 0, message: "Falha de conexão ao enviar o arquivo.",
    });
    await expect(apiClient.upload("POST", "/v1/u", new FormData())).rejects.toMatchObject({ status: 0, message: "Envio cancelado." });
  });

  it("on 401 renews the token once and sends the upload again", async () => {
    localStorage.setItem(TOKEN_KEYS.access, "old");
    localStorage.setItem(TOKEN_KEYS.refresh, "r1");
    fetchMock.mockResolvedValueOnce(json({ accessToken: "new", refreshToken: "r2" }));
    respond((x) => x.reply(401, { error: "expired" }), (x) => x.reply(200, { ok: true }));

    await expect(apiClient.upload("POST", "/v1/u", new FormData())).resolves.toEqual({ ok: true });
    expect(fetchMock).toHaveBeenCalledWith(`${API}/auth/refresh`, expect.objectContaining({ method: "POST" }));
    expect(FakeXhr.sent.map((x) => x.headers.Authorization)).toEqual(["Bearer old", "Bearer new"]);
    expect(localStorage.getItem(TOKEN_KEYS.refresh)).toBe("r2");
  });

  it("a 401 that cannot be renewed drops the session and throws the server reason", async () => {
    localStorage.setItem(TOKEN_KEYS.access, "old");
    localStorage.setItem(TOKEN_KEYS.refresh, "r1");
    fetchMock.mockResolvedValueOnce(json({ error: "revoked" }, 401));
    respond((x) => x.reply(401, { message: "Sessão expirada", code: "token_expired" }));

    await expect(apiClient.upload("POST", "/v1/u", new FormData())).rejects.toMatchObject({
      status: 401, message: "Sessão expirada", code: "token_expired",
    });
    expect(localStorage.getItem(TOKEN_KEYS.access)).toBeNull();
    expect(FakeXhr.sent).toHaveLength(1);
  });

  it("a 401 retry that fails again does not loop", async () => {
    localStorage.setItem(TOKEN_KEYS.access, "old");
    localStorage.setItem(TOKEN_KEYS.refresh, "r1");
    fetchMock.mockResolvedValueOnce(json({ accessToken: "new", refreshToken: "r2" }));
    respond((x) => x.reply(401, ""), (x) => x.reply(401, ""));
    await expect(apiClient.upload("POST", "/v1/u", new FormData())).rejects.toMatchObject({ status: 401, message: "Unauthorized" });
    expect(FakeXhr.sent).toHaveLength(2);
  });
});

describe("apiClient.postForm and request error mapping", () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
  });
  afterEach(() => vi.unstubAllGlobals());

  it("postForm sends the multipart body without a JSON content type and parses the answer", async () => {
    localStorage.setItem(TOKEN_KEYS.access, "tok");
    fetchMock.mockResolvedValueOnce(json({ key: "k" })).mockResolvedValueOnce(new Response(null, { status: 204 }));
    const form = new FormData();
    await expect(apiClient.postForm("/integrations/storage/upload", form)).resolves.toEqual({ key: "k" });
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`${API}/integrations/storage/upload`);
    expect(init).toMatchObject({ method: "POST", body: form, headers: { Authorization: "Bearer tok" } });
    await expect(apiClient.postForm("/x", form)).resolves.toBeUndefined();
  });

  it("postForm maps errors (details or message) and renews on 401", async () => {
    fetchMock
      .mockResolvedValueOnce(json({ details: ["tipo", "tamanho"] }, 400))
      .mockResolvedValueOnce(json({ error: "Sem espaço" }, 507));
    await expect(apiClient.postForm("/x", new FormData())).rejects.toMatchObject({ status: 400, message: "tipo | tamanho" });
    await expect(apiClient.postForm("/x", new FormData())).rejects.toMatchObject({ status: 507, message: "Sem espaço" });

    localStorage.setItem(TOKEN_KEYS.access, "old");
    localStorage.setItem(TOKEN_KEYS.refresh, "r1");
    fetchMock
      .mockResolvedValueOnce(json({ error: "expired" }, 401))
      .mockResolvedValueOnce(json({ accessToken: "new", refreshToken: "r2" }))
      .mockResolvedValueOnce(json({ key: "k2" }));
    await expect(apiClient.postForm("/x", new FormData())).resolves.toEqual({ key: "k2" });
    expect(((fetchMock.mock.calls.at(-1) as [string, RequestInit])[1].headers as Record<string, string>).Authorization).toBe("Bearer new");

    sessionStorage.clear();
    localStorage.clear();
    fetchMock.mockResolvedValueOnce(json({ error: "Não autenticado", code: "no_token" }, 401));
    await expect(apiClient.postForm("/x", new FormData())).rejects.toMatchObject({ status: 401, message: "Não autenticado", code: "no_token" });
  });

  it("JSON requests: 5xx carries the request id, details join, and 429 without text gets a friendly message", async () => {
    fetchMock
      .mockResolvedValueOnce(json({ error: "Erro interno", requestId: "abc" }, 500))
      .mockResolvedValueOnce(json({ details: ["x", "y"], error: "Falhou" }, 502))
      .mockResolvedValueOnce(new Response("", { status: 429, statusText: "" }));
    await expect(apiClient.get("/a")).rejects.toMatchObject({ status: 500, message: "Erro interno (código: abc)", requestId: "abc" });
    await expect(apiClient.get("/a")).rejects.toMatchObject({ status: 502, message: "x | y", details: ["x", "y"], serverMessage: "Falhou" });
    await expect(apiClient.get("/a")).rejects.toMatchObject({ status: 429, message: "Muitas tentativas. Aguarde alguns minutos." });
  });

  it("a 401 without a refresh token does not stop later sessions from refreshing (regression)", async () => {
    localStorage.setItem(TOKEN_KEYS.access, "orphan");
    fetchMock.mockResolvedValueOnce(json({ error: "expired" }, 401));
    await expect(apiClient.get("/a")).rejects.toMatchObject({ status: 401 });

    // signs in again in the same page: the next expiry must renew the token
    localStorage.setItem(TOKEN_KEYS.access, "old");
    localStorage.setItem(TOKEN_KEYS.refresh, "r1");
    fetchMock
      .mockResolvedValueOnce(json({ error: "expired" }, 401))
      .mockResolvedValueOnce(json({ accessToken: "new", refreshToken: "r2" }))
      .mockResolvedValueOnce(json({ ok: true }));
    await expect(apiClient.get("/a")).resolves.toEqual({ ok: true });
    expect(fetchMock.mock.calls.map((c) => c[0])).toEqual([`${API}/a`, `${API}/a`, `${API}/auth/refresh`, `${API}/a`]);
  });

  it("a refresh that throws drops the session", async () => {
    const err = vi.spyOn(console, "error").mockImplementation(() => {});
    localStorage.setItem(TOKEN_KEYS.access, "old");
    localStorage.setItem(TOKEN_KEYS.refresh, "r1");
    fetchMock.mockResolvedValueOnce(json({ error: "expired" }, 401)).mockRejectedValueOnce(new Error("offline"));
    await expect(apiClient.get("/a")).rejects.toMatchObject({ status: 401, message: "expired" });
    expect(localStorage.getItem(TOKEN_KEYS.refresh)).toBeNull();
    expect(fetchMock.mock.calls.map((c) => c[0])).toEqual([`${API}/a`, `${API}/auth/refresh`]);
    expect(err).toHaveBeenCalled();
    err.mockRestore();
  });
});

describe("impersonation marker helpers", () => {
  beforeEach(() => { localStorage.clear(); sessionStorage.clear(); });

  it("a corrupted marker reads as no impersonation; leaving removes the tab's keys", () => {
    sessionStorage.setItem(IMPERSONATION_KEY, "{not json");
    expect(getImpersonation()).toBeNull();

    beginImpersonation("imp-token", "{}", { tenant: "ACME", user: "Ana", expiresAt: null });
    expect(getImpersonation()).toMatchObject({ tenant: "ACME", ended: false });
    leaveImpersonation();
    expect(getImpersonation()).toBeNull();
    expect(sessionStorage.getItem(TOKEN_KEYS.access)).toBeNull();
    expect(sessionStorage.getItem(TOKEN_KEYS.session)).toBeNull();
  });
});
