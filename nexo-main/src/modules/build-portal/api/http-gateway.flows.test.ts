import { describe, it, expect, vi, afterEach } from "vitest";
import { actionError, createHttpGateway, PortalHttpError } from "./http-gateway";
import type { Access } from "../types";
import type { PublicBuildBootstrapDto } from "./generated/portal-types";

const bootstrap = (
  sections: string[] = [],
  extra: Partial<PublicBuildBootstrapDto> = {},
): PublicBuildBootstrapDto => ({
  contractVersion: "1.7",
  sections,
  readOnly: false,
  portal: { access: "Public" },
  company: { name: "Construtora Teste" },
  project: { name: "Casa Jardim", type: "House", status: "InProgress" },
  phase: "construction",
  ...extra,
});
const json = (body: unknown, status = 200, headers: HeadersInit = {}) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json", ...headers },
  });
const signal = () => new AbortController().signal;
const png = (name = "foto.png", size = 4) =>
  new File([new Uint8Array(size)], name, { type: "image/png" });
/** Answers each request by the first route whose fragment the URL contains (the bootstrap is the bare portal path). */
const routes =
  (table: Record<string, () => Response>, fallback: () => Response) =>
  (url: string) => {
    const hit = Object.keys(table).find((k) => url.includes(k));
    return Promise.resolve(hit ? table[hit]() : fallback());
  };

afterEach(() => vi.useRealTimers());

describe("action error messages", () => {
  it("explains a conflict and the time to retry after a rate limit", () => {
    expect(actionError(new PortalHttpError(422, "invalid", "Dados inválidos."))).toBe(
      "Dados inválidos.",
    );
    expect(actionError(new PortalHttpError(409, "conflict", "Já decidido."))).toBe(
      "Já decidido. O registro foi atualizado com a situação atual.",
    );
    const at = Date.parse("2026-11-13T15:30:00Z");
    expect(actionError(new PortalHttpError(429, "rate_limited", "Muitas tentativas.", at))).toBe(
      `Muitas tentativas. Tente após ${new Date(at).toLocaleTimeString("pt-BR")}.`,
    );
  });

  it("a network failure gets a generic message", () => {
    expect(actionError(new TypeError("Failed to fetch"))).toBe(
      "Não foi possível concluir. Confira sua conexão e tente novamente.",
    );
  });
});

describe("loading a portal", () => {
  it("uses the configured API base and reads only the announced sections", async () => {
    const fetch = vi.fn().mockImplementation(
      routes(
        {
          "/progress": () => json({ asOf: "2026-10-30", actualPercent: 40, methodology: "Peso", curves: { actual: [] } }),
          "/timeline": () => json({ items: [] }),
          "/documents": () => json({ items: [] }),
        },
        () => json(bootstrap(["overallProgress", "documents", "stageProgress"])),
      ),
    );
    const result = await createHttpGateway({
      fetch,
      base: "https://api.example.test/api/",
    }).load("minha empresa", "obra/1", signal());
    const urls = fetch.mock.calls.map((c) => c[0] as string);
    expect(urls[0]).toBe("https://api.example.test/api/public/build/minha%20empresa/obra%2F1");
    expect(urls.slice(1).sort()).toEqual([
      "https://api.example.test/api/public/build/minha%20empresa/obra%2F1/documents",
      "https://api.example.test/api/public/build/minha%20empresa/obra%2F1/progress",
      "https://api.example.test/api/public/build/minha%20empresa/obra%2F1/timeline?take=20",
    ]);
    for (const [, init] of fetch.mock.calls) {
      expect(init).toMatchObject({ credentials: "omit", cache: "no-store", redirect: "error" });
      expect(init.headers.get("Accept")).toBe("application/json");
      expect(init.headers.has("Content-Type")).toBe(false);
    }
    expect(result).toMatchObject({ kind: "ready", portal: { progress: 40, documents: [] } });
  });

  it.each([
    [{ ...bootstrap(), contractVersion: "2.0" }],
    [{ ...bootstrap(), sections: "photos" }],
  ])("refuses an unsupported contract as unavailable", async (body) => {
    const fetch = vi.fn().mockResolvedValue(json(body));
    expect(await createHttpGateway({ fetch }).load("e", "o", signal())).toEqual({
      kind: "unavailable",
    });
  });

  it.each([
    [404, "portal_not_found", "not-found"],
    [500, "server_error", "unavailable"],
    [401, "other", "expired"],
  ])("maps HTTP %i (%s) to %s", async (status, code, kind) => {
    const fetch = vi.fn().mockResolvedValue(json({ code, error: "Falha" }, status));
    expect(await createHttpGateway({ fetch }).load("e", "o", signal())).toMatchObject({ kind });
  });

  it("an error body that is not JSON still becomes a readable failure", async () => {
    const fetch = vi.fn().mockImplementation((url: string) =>
      Promise.resolve(
        url.includes("/photos")
          ? new Response("<html>Bad gateway</html>", { status: 502 })
          : url.includes("/timeline")
            ? json({ items: [] })
            : json(bootstrap(["photos"])),
      ),
    );
    expect(await createHttpGateway({ fetch }).load("e", "o", signal())).toMatchObject({
      kind: "ready",
      portal: {
        sectionErrors: [{ section: "photos", message: "Não foi possível concluir esta solicitação." }],
      },
    });
  });

  it("a handover not created yet is simply absent, not an error", async () => {
    const fetch = vi.fn().mockImplementation(
      routes(
        {
          "/handover": () => json({ code: "portal_item_not_found", error: "Sem entrega" }, 404),
          "/warranty": () => json({ items: [], notice: "Garantias" }),
          "/assistance": () => json({ items: [], categories: [], canOpen: true }),
        },
        () => json(bootstrap(["postConstruction"])),
      ),
    );
    const result = await createHttpGateway({ fetch }).load("e", "o", signal());
    expect(result).toMatchObject({ kind: "ready", portal: { sectionErrors: [], warrantyNotice: "Garantias" } });
    if (result.kind !== "ready") throw new Error("expected ready");
    expect(result.portal.handover).toBeUndefined();
  });

  it("a section read that turns into an expired session fails the whole load", async () => {
    const fetch = vi.fn().mockImplementation(
      routes(
        { "/selections": () => json({ code: "portal_session_expired", error: "Expirou" }, 401) },
        () => json(bootstrap(["selections"])),
      ),
    );
    const g = createHttpGateway({ fetch });
    const events: (Access | "changed")[] = [];
    g.subscribe!((e) => events.push(e));
    expect(await g.load("e", "o", signal())).toMatchObject({ kind: "expired" });
    expect(events).toEqual([{ kind: "expired", retryAt: undefined, publicIdentity: undefined }]);
  });

  it("a portal removed while reading its sections resets and announces not-found", async () => {
    const fetch = vi.fn().mockImplementation(
      routes(
        { "/documents": () => json({ code: "portal_not_found", error: "Removido" }, 404) },
        () => json(bootstrap(["documents"])),
      ),
    );
    const g = createHttpGateway({ fetch });
    const listener = vi.fn();
    g.subscribe!(listener);
    expect(await g.load("e", "o", signal())).toMatchObject({ kind: "not-found" });
    expect(listener).toHaveBeenCalledWith({ kind: "not-found" });
  });

  it("an aborted load is rethrown, never shown as an access state", async () => {
    const controller = new AbortController();
    const fetch = vi.fn().mockImplementation((url: string) => {
      if (url.includes("/documents")) {
        controller.abort();
        return Promise.reject(new DOMException("Aborted", "AbortError"));
      }
      return Promise.resolve(json(bootstrap(["documents"])));
    });
    await expect(
      createHttpGateway({ fetch }).load("e", "o", controller.signal),
    ).rejects.toMatchObject({ name: "AbortError" });
  });

  it("aborted after every section was read still does not publish a portal", async () => {
    const controller = new AbortController();
    const fetch = vi.fn().mockImplementation((url: string) => {
      if (url.includes("/documents")) controller.abort();
      return Promise.resolve(url.includes("/documents") ? json({ items: [] }) : json(bootstrap(["documents"])));
    });
    await expect(
      createHttpGateway({ fetch }).load("e", "o", controller.signal),
    ).rejects.toMatchObject({ name: "AbortError" });
  });

  it("an answer for a portal the user already left is dropped", async () => {
    let release!: (r: Response) => void;
    const fetch = vi
      .fn()
      .mockImplementationOnce(() => new Promise<Response>((r) => (release = r)))
      .mockResolvedValue(json(bootstrap()));
    const g = createHttpGateway({ fetch });
    const first = g.load("e", "obra-a", signal());
    expect(await g.load("e", "obra-b", signal())).toMatchObject({ kind: "ready" });
    release(json(bootstrap()));
    // the stale response is discarded: an AbortError is not an access error, so it ends as unavailable
    expect(await first).toEqual({ kind: "unavailable" });
  });
});

describe("rate limiting", () => {
  it("accepts Retry-After as an HTTP date", async () => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-11-13T12:00:00Z"));
    const fetch = vi.fn().mockResolvedValue(
      json({ code: "portal_locked", error: "Aguarde" }, 429, {
        "Retry-After": "Fri, 13 Nov 2026 12:05:00 GMT",
      }),
    );
    expect(await createHttpGateway({ fetch }).load("e", "o", signal())).toEqual({
      kind: "rate-limited",
      retryAt: "2026-11-13T12:05:00.000Z",
      publicIdentity: undefined,
    });
  });

  it("without Retry-After uses the seconds of the body, then 60 s", async () => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-11-13T12:00:00Z"));
    const body = vi.fn().mockResolvedValue(json({ code: "x", error: "Aguarde", retryAfterSeconds: 120 }, 429));
    expect(await createHttpGateway({ fetch: body }).load("e", "o", signal())).toMatchObject({
      retryAt: "2026-11-13T12:02:00.000Z",
    });
    const none = vi.fn().mockResolvedValue(json({ code: "x", error: "Aguarde" }, 429));
    expect(await createHttpGateway({ fetch: none }).load("e", "o", signal())).toMatchObject({
      retryAt: "2026-11-13T12:01:00.000Z",
    });
  });

  it("an unreadable Retry-After falls back to a minute", async () => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-11-13T12:00:00Z"));
    const fetch = vi.fn().mockResolvedValue(
      json({ code: "x", error: "Aguarde" }, 429, { "Retry-After": "logo mais" }),
    );
    expect(await createHttpGateway({ fetch }).load("e", "o", signal())).toMatchObject({
      kind: "rate-limited",
      retryAt: "2026-11-13T12:01:00.000Z",
    });
  });

  it("refuses actions during the cooldown without calling the server", async () => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-11-13T12:00:00Z"));
    const fetch = vi.fn().mockResolvedValue(json({ code: "x", error: "Aguarde" }, 429, { "Retry-After": "30" }));
    const g = createHttpGateway({ fetch });
    await g.load("e", "o", signal());
    const error = await g.actions!("e", "o").feedback!("t", 5, "").catch((e: unknown) => e);
    expect(error).toBeInstanceOf(PortalHttpError);
    expect(error).toMatchObject({ status: 429, code: "rate_limited", retryAt: Date.parse("2026-11-13T12:00:30Z") });
    expect(fetch).toHaveBeenCalledTimes(1);
  });
});

describe("password session", () => {
  it("a session that expired on the device clock is dropped before the request", async () => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-11-13T12:00:00Z"));
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(json({ token: "sessao", expiresAt: "2026-11-13T12:15:00Z" }))
      .mockResolvedValue(json(bootstrap()));
    const g = createHttpGateway({ fetch });
    const events: (Access | "changed")[] = [];
    g.subscribe!((e) => events.push(e));
    expect(await g.unlock("e", "o", "senha-teste", signal())).toMatchObject({ kind: "ready" });
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ password: "senha-teste" });
    expect(fetch.mock.calls[0][1].method).toBe("POST");
    expect(fetch.mock.calls[0][1].headers.get("Content-Type")).toBe("application/json");

    vi.setSystemTime(new Date("2026-11-13T12:16:00Z"));
    expect(await g.load("e", "o", signal())).toMatchObject({ kind: "expired" });
    expect(fetch).toHaveBeenCalledTimes(2);
    expect(events).toEqual([{ kind: "expired", retryAt: undefined, publicIdentity: undefined }]);
  });

  it("a session that already looks expired on the device trusts the server instead", async () => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-11-13T12:00:00Z"));
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(json({ token: "sessao", expiresAt: "2026-11-13T11:50:00Z" }))
      .mockImplementation(() => Promise.resolve(json(bootstrap())));
    const g = createHttpGateway({ fetch });
    await g.unlock("e", "o", "senha", signal());
    vi.setSystemTime(new Date("2026-11-14T12:00:00Z"));
    expect(await g.load("e", "o", signal())).toMatchObject({ kind: "ready" });
    expect(fetch.mock.calls[2][1].headers.get("X-Build-Portal-Session")).toBe("sessao");
  });

  it("a wrong password returns the public teaser of the company", async () => {
    const fetch = vi.fn().mockResolvedValue(
      json(
        {
          code: "portal_password_invalid",
          error: "Senha incorreta",
          portal: { name: "Construtora", title: "Casa Jardim", logoUrl: "https://cdn.example.test/l.png" },
        },
        401,
      ),
    );
    expect(await createHttpGateway({ fetch }).unlock("e", "o", "errada", signal())).toEqual({
      kind: "wrong-password",
      retryAt: undefined,
      publicIdentity: { company: "Construtora", name: "Casa Jardim", logo: "https://cdn.example.test/l.png" },
    });
  });

  it("an unlock aborted while waiting is rethrown", async () => {
    const controller = new AbortController();
    const fetch = vi.fn().mockImplementation(() => {
      controller.abort();
      return Promise.resolve(json({ token: "t", expiresAt: "2099-01-01T00:00:00Z" }));
    });
    await expect(
      createHttpGateway({ fetch }).unlock("e", "o", "senha", controller.signal),
    ).rejects.toMatchObject({ name: "AbortError" });
  });

  it("a 401 on an action discards the session and announces it", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(json({ token: "sessao", expiresAt: "2099-01-01T00:00:00Z" }))
      .mockResolvedValueOnce(json(bootstrap()))
      .mockResolvedValueOnce(json({ code: "portal_session_expired", error: "Expirou" }, 401))
      .mockResolvedValue(json(bootstrap()));
    const g = createHttpGateway({ fetch });
    await g.unlock("e", "o", "senha", signal());
    const listener = vi.fn();
    g.subscribe!(listener);
    await expect(g.actions!("e", "o").acceptHandover!("Cliente", "")).rejects.toMatchObject({ status: 401 });
    expect(listener).toHaveBeenCalledWith({ kind: "expired", retryAt: undefined, publicIdentity: undefined });
    await g.load("e", "o", signal());
    expect(fetch.mock.calls[3][1].headers.has("X-Build-Portal-Session")).toBe(false);
  });

  it("logout deletes the session on the server and goes back to the password screen even if it fails", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(json({ token: "sessao", expiresAt: "2099-01-01T00:00:00Z" }))
      .mockResolvedValueOnce(json(bootstrap()))
      .mockRejectedValueOnce(new TypeError("offline"))
      .mockResolvedValue(json(bootstrap()));
    const g = createHttpGateway({ fetch });
    await g.unlock("e", "o", "senha", signal());
    const listener = vi.fn();
    g.subscribe!(listener);
    await expect(g.actions!("e", "o").logout!()).rejects.toThrow("offline");
    expect(fetch.mock.calls[2][0]).toBe("/api/public/build/e/o/session");
    expect(fetch.mock.calls[2][1].method).toBe("DELETE");
    expect(listener).toHaveBeenCalledWith({ kind: "private" });
    await g.load("e", "o", signal());
    expect(fetch.mock.calls[3][1].headers.has("X-Build-Portal-Session")).toBe(false);
  });

  it("after dispose nobody hears about later changes", async () => {
    const g = createHttpGateway({ fetch: vi.fn() });
    const listener = vi.fn();
    const unsubscribe = g.subscribe!(listener);
    await g.actions!("e", "o").refresh!();
    expect(listener).toHaveBeenCalledWith("changed");
    unsubscribe();
    await g.actions!("e", "o").refresh!();
    expect(listener).toHaveBeenCalledTimes(1);
    const other = vi.fn();
    g.subscribe!(other);
    g.dispose!();
    await g.actions!("e", "o").refresh!();
    expect(other).not.toHaveBeenCalled();
  });
});

describe("client actions", () => {
  it("each decision posts its own payload and reports the change", async () => {
    const fetch = vi.fn().mockImplementation(() => Promise.resolve(json({})));
    const g = createHttpGateway({ fetch });
    const listener = vi.fn();
    g.subscribe!(listener);
    const a = g.actions!("e", "o");
    await a.changeOrder!("co/1", "reject", "Maria", "Caro demais");
    await a.acceptHandover!("Maria", "Tudo certo");
    await a.feedback!("t 1", 4, "Rápido");
    expect(fetch.mock.calls.map((c) => [c[0], JSON.parse(c[1].body)])).toEqual([
      ["/api/public/build/e/o/change-orders/co%2F1/decision", { decision: "reject", signerName: "Maria", comment: "Caro demais" }],
      ["/api/public/build/e/o/handover/accept", { signerName: "Maria", comment: "Tudo certo" }],
      ["/api/public/build/e/o/assistance/t%201/feedback", { rating: 4, comment: "Rápido" }],
    ]);
    expect(listener).toHaveBeenCalledTimes(3);
    expect(listener).toHaveBeenCalledWith("changed");
  });

  it("a conflict refreshes the portal but a validation error does not", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(json({ code: "already_decided", error: "Já decidido" }, 409))
      .mockResolvedValueOnce(json({ code: "invalid", error: "Inválido" }, 422));
    const g = createHttpGateway({ fetch });
    const listener = vi.fn();
    g.subscribe!(listener);
    const a = g.actions!("e", "o");
    await expect(a.decide!("s", "o")).rejects.toMatchObject({ status: 409 });
    expect(listener).toHaveBeenCalledWith("changed");
    listener.mockClear();
    await expect(a.decide!("s", "o")).rejects.toMatchObject({ status: 422, message: "Inválido" });
    expect(listener).not.toHaveBeenCalled();
  });

  it("opens a ticket, then sends each photo as form data reporting progress", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(json({ id: "t9", number: 9 }))
      .mockImplementation(() => Promise.resolve(json({})));
    const progress = vi.fn();
    await createHttpGateway({ fetch }).actions!("e", "o").requestAssistance!(
      {
        category: "Hidráulica",
        title: "Vazamento",
        description: "Pia",
        location: "Cozinha",
        files: [png("a.png"), png("b.png")],
      },
      progress,
    );
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({
      category: "Hidráulica",
      title: "Vazamento",
      description: "Pia",
      location: "Cozinha",
    });
    expect(fetch.mock.calls.slice(1).map((c) => c[0])).toEqual([
      "/api/public/build/e/o/assistance/t9/photos",
      "/api/public/build/e/o/assistance/t9/photos",
    ]);
    const form = fetch.mock.calls[1][1].body as FormData;
    expect(form).toBeInstanceOf(FormData);
    expect((form.get("file") as File).name).toBe("a.png");
    expect(fetch.mock.calls[1][1].headers.has("Content-Type")).toBe(false);
    expect(progress.mock.calls).toEqual([[0, 2], [1, 2], [2, 2]]);
  });

  it("refuses too many, too large or non-photo files before opening the ticket", async () => {
    const fetch = vi.fn();
    const a = createHttpGateway({ fetch }).actions!("e", "o");
    const ask = (files: File[]) =>
      a.requestAssistance!({ category: "Outros", description: "x", files });
    await expect(ask(Array.from({ length: 7 }, () => png()))).rejects.toMatchObject({
      status: 422,
      code: "assistance_photo_limit",
    });
    const big = png("grande.png");
    Object.defineProperty(big, "size", { value: 8 * 1024 * 1024 + 1 });
    await expect(ask([big])).rejects.toMatchObject({ status: 413, code: "file_too_large" });
    await expect(
      ask([new File(["%PDF"], "laudo.pdf", { type: "application/pdf" })]),
    ).rejects.toMatchObject({ status: 422, code: "invalid_file_type" });
    expect(fetch).not.toHaveBeenCalled();
  });

  it("a network failure while sending photos keeps the ticket and says so", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(json({ id: "t3", number: 3 }))
      .mockRejectedValueOnce(new TypeError("offline"));
    const g = createHttpGateway({ fetch });
    const listener = vi.fn();
    g.subscribe!(listener);
    const error = await g.actions!("e", "o")
      .requestAssistance!({ category: "Outros", description: "x", files: [png()] })
      .catch((e: unknown) => e);
    expect(error).toMatchObject({ status: 503, code: "partial_upload" });
    expect((error as Error).message).toContain("Chamado 3 criado");
    expect(listener).toHaveBeenCalledWith("changed");
  });

  it("evidence for an existing ticket reports the change also when an upload fails", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(json({}))
      .mockResolvedValueOnce(json({ code: "storage_unavailable", error: "Armazenamento indisponível" }, 503));
    const g = createHttpGateway({ fetch });
    const listener = vi.fn();
    g.subscribe!(listener);
    await expect(
      g.actions!("e", "o").uploadEvidence!("t1", [png("a.png"), png("b.png")]),
    ).rejects.toMatchObject({ status: 503, message: "Armazenamento indisponível" });
    expect(fetch).toHaveBeenCalledTimes(2);
    expect(listener).toHaveBeenCalledWith("changed");
  });

  it("a 304 for a date never read before is read again as a fresh state", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(new Response(null, { status: 304 }))
      .mockResolvedValueOnce(
        json({ date: "2026-10-01", states: { Blocked: ["e1"] }, zoneStates: {}, counts: {}, unbound: 0 }),
      );
    const a = createHttpGateway({ fetch }).actions!("e", "o");
    await expect(a.twinState!("2026-10-01", signal())).rejects.toThrow();
    const snapshot = await a.twinState!("2026-10-01", signal());
    expect(snapshot.states).toEqual({ e1: "blocked" });
    expect(fetch.mock.calls[1][0]).toBe("/api/public/build/e/o/twin/state?date=2026-10-01");
    expect(fetch.mock.calls[1][1].headers.has("If-None-Match")).toBe(false);
  });
});

describe("reading more", () => {
  it("does nothing before the portal was loaded", async () => {
    const fetch = vi.fn();
    const g = createHttpGateway({ fetch });
    const listener = vi.fn();
    g.subscribe!(listener);
    await g.actions!("e", "o").more!("photos");
    expect(fetch).not.toHaveBeenCalled();
    expect(listener).not.toHaveBeenCalled();
  });

  it("appends the next pages of timeline, photos and captures and publishes the portal", async () => {
    const photo = (n: number) => ({ date: `2026-10-0${n}`, url: `https://cdn.example.test/${n}.jpg`, urlExpiresAt: "2099-01-01T00:00:00Z" });
    const capture = (n: number) => ({ type: "Photo", capturedAt: "2026-10-01", title: `C${n}`, contentType: "image/jpeg", url: `https://cdn.example.test/c${n}.jpg` });
    const fetch = vi.fn().mockImplementation(
      routes(
        {
          "timeline?take=20&before=": () => json({ items: [{ type: "update", occurredAt: "2026-09-01T00:00:00Z", title: "Antiga" }] }),
          "/timeline": () => json({ items: [{ type: "update", occurredAt: "2026-10-01T00:00:00Z", title: "Nova" }], nextBefore: "2026-10-01T00:00:00Z" }),
          "photos?take=24&skip=1": () => json({ items: [photo(2)], total: 2, skip: 1, take: 24 }),
          "/photos": () => json({ items: [photo(1)], total: 2, skip: 0, take: 24 }),
          "reality-captures?take=24&skip=1": () => json({ items: [capture(2)], total: 2, skip: 1, take: 24 }),
          "/reality-captures": () => json({ items: [capture(1)], total: 2, skip: 0, take: 24 }),
        },
        () => json(bootstrap(["clientUpdates", "photos", "realityCapture"])),
      ),
    );
    const g = createHttpGateway({ fetch });
    const loaded = await g.load("e", "o", signal());
    expect(loaded).toMatchObject({ portal: { pagination: { timeline: "2026-10-01T00:00:00Z", photos: 1, captures: 1 } } });

    const events: (Access | "changed")[] = [];
    g.subscribe!((e) => events.push(e));
    const a = g.actions!("e", "o");
    await a.more!("timeline");
    await a.more!("photos");
    await a.more!("captures");

    expect(fetch.mock.calls.map((c) => c[0] as string).filter((u) => u.includes("before=") || u.includes("skip="))).toEqual([
      "/api/public/build/e/o/timeline?take=20&before=2026-10-01T00%3A00%3A00Z",
      "/api/public/build/e/o/photos?take=24&skip=1",
      "/api/public/build/e/o/reality-captures?take=24&skip=1",
    ]);
    expect(events).toHaveLength(3);
    const last = events[2];
    if (last === "changed" || last.kind !== "ready") throw new Error("expected a ready portal");
    expect(last.portal.updates.map((u) => u.title)).toEqual(["Nova", "Antiga"]);
    expect(last.portal.photos.map((p) => p.caption)).toEqual(["Foto da obra", "Foto da obra", "C1", "C2"]);
    expect(last.portal.pagination).toEqual({ timeline: undefined, photos: undefined, captures: undefined });
  });

  it("a section without more pages republishes the same portal without requests", async () => {
    const fetch = vi.fn().mockResolvedValue(json(bootstrap()));
    const g = createHttpGateway({ fetch });
    await g.load("e", "o", signal());
    const listener = vi.fn();
    g.subscribe!(listener);
    const a = g.actions!("e", "o");
    await a.more!("timeline");
    await a.more!("photos");
    await a.more!("captures");
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(listener).toHaveBeenCalledTimes(3);
    expect(listener.mock.calls[0][0]).toMatchObject({ kind: "ready" });
  });
});
