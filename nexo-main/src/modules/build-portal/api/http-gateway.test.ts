import { describe, it, expect, vi, afterEach } from "vitest";
import { createHttpGateway, PortalHttpError } from "./http-gateway";
import { adaptPortal, adaptTwin } from "./adapter";
import type {
  PublicBuildBootstrapDto,
  PublicBuildTwinDto,
} from "./generated/portal-types";
const bootstrap = (sections: string[] = []): PublicBuildBootstrapDto => ({
  contractVersion: "1.5",
  sections,
  portal: { access: "Public" },
  company: { name: "Empresa de teste" },
  project: { name: "Obra de teste", type: "House", status: "InProgress" },
  phase: "construction",
});
const json = (body: unknown, status = 200, headers: HeadersInit = {}) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json", ...headers },
  });
const signal = () => new AbortController().signal;
afterEach(() => vi.useRealTimers());
describe("backend v1.5 gateway", () => {
  it("reads only announced sections, with no administrative credentials", async () => {
    const fetch = vi.fn().mockResolvedValue(
      json({
        ...bootstrap([]),
        financialSummary: {
          realizedTotal: 999,
          currency: "BRL",
          asOf: "2026-10-30",
        },
      }),
    );
    const result = await createHttpGateway({ fetch }).load(
      "empresa",
      "obra",
      signal(),
    );
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(fetch.mock.calls[0][0]).toBe("/api/public/build/empresa/obra");
    const init = fetch.mock.calls[0][1];
    expect(init.credentials).toBe("omit");
    expect(init.headers.has("Authorization")).toBe(false);
    expect(init.headers.has("X-Build-Portal-Session")).toBe(false);
    expect(result).toMatchObject({
      kind: "ready",
      portal: { finance: null, progress: null, stages: [] },
    });
  });
  it("uses a memory-only portal session and discards it when slugs change", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(
        json(
          {
            code: "portal_password_required",
            error: "Senha",
            portal: { name: "Marca pública" },
          },
          401,
        ),
      )
      .mockResolvedValueOnce(
        json({ token: "session-test", expiresAt: "2099-01-01T00:00:00Z" }),
      )
      .mockResolvedValueOnce(
        json({ ...bootstrap(), portal: { access: "Password" } }),
      )
      .mockResolvedValueOnce(json(bootstrap()));
    const g = createHttpGateway({ fetch });
    expect(await g.load("empresa", "obra", signal())).toMatchObject({
      kind: "private",
      publicIdentity: { company: "Marca pública", name: "" },
    });
    await g.unlock("empresa", "obra", "segredo de teste", signal());
    expect(fetch.mock.calls[2][1].headers.get("X-Build-Portal-Session")).toBe(
      "session-test",
    );
    await g.load("empresa", "outra", signal());
    expect(fetch.mock.calls[3][1].headers.has("X-Build-Portal-Session")).toBe(
      false,
    );
  });
  it("respects Retry-After without making requests during the cooldown", async () => {
    vi.useFakeTimers();
    const fetch = vi.fn().mockResolvedValue(
      json({ code: "portal_locked", error: "Aguarde" }, 429, {
        "Retry-After": "45",
      }),
    );
    const g = createHttpGateway({ fetch });
    expect(await g.load("empresa", "obra", signal())).toMatchObject({
      kind: "rate-limited",
    });
    await g.load("empresa", "obra", signal());
    expect(fetch).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(46000);
    await g.load("empresa", "obra", signal());
    expect(fetch).toHaveBeenCalledTimes(2);
  });
  it.each(["portal_password_invalid", "portal_session_expired"])(
    "maps %s without leaking authenticated data",
    async (code) => {
      const fetch = vi
        .fn()
        .mockResolvedValue(json({ code, error: "Senha" }, 401));
      expect(
        await createHttpGateway({ fetch }).load("empresa", "obra", signal()),
      ).toEqual({
        kind: code === "portal_password_invalid" ? "wrong-password" : "expired",
        retryAt: undefined,
        publicIdentity: undefined,
      });
    },
  );
  it("retains other sections and marks a failed read instead of claiming empty success", async () => {
    const fetch = vi
      .fn()
      .mockImplementation((url: string) =>
        Promise.resolve(
          url.endsWith("/photos?take=24")
            ? json({ error: "Fotos indisponíveis" }, 503)
            : url.includes("/timeline")
              ? json({ items: [] })
              : json(bootstrap(["photos"])),
        ),
      );
    expect(
      await createHttpGateway({ fetch }).load("empresa", "obra", signal()),
    ).toMatchObject({
      kind: "ready",
      portal: {
        sectionErrors: [{ section: "photos", message: "Fotos indisponíveis" }],
      },
    });
  });
  it("revalidates snapshots using ETag and reuses a 304 without fetching geometry", async () => {
    const state = {
      date: "2026-10-20",
      states: { NeedsReview: ["element"] },
      zoneStates: {},
      counts: { NeedsReview: 1 },
      unbound: 2,
    };
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(json(state, 200, { ETag: '"state-1"' }))
      .mockResolvedValueOnce(new Response(null, { status: 304 }));
    const a = createHttpGateway({ fetch }).actions!("empresa", "obra");
    const first = await a.twinState!("2026-10-20", signal());
    expect(first.states.element).toBe("review");
    expect(await a.twinState!("2026-10-20", signal())).toEqual(first);
    expect(fetch.mock.calls[1][1].headers.get("If-None-Match")).toBe(
      '"state-1"',
    );
  });
  it.each([409, 413, 422, 429])(
    "preserves action error %s and never automatically retries a POST",
    async (status) => {
      const fetch = vi
        .fn()
        .mockResolvedValue(
          json({ code: "conflict", error: "Mensagem pública" }, status),
        );
      const g = createHttpGateway({ fetch });
      await expect(
        g.actions!("empresa", "obra").changeOrder!(
          "order",
          "approve",
          "Cliente",
          "",
        ),
      ).rejects.toBeInstanceOf(PortalHttpError);
      expect(fetch).toHaveBeenCalledTimes(1);
    },
  );
  it("uses exact decision payload without invented version fields", async () => {
    const fetch = vi.fn().mockResolvedValue(json({ status: "Submitted" }));
    await createHttpGateway({ fetch }).actions!("empresa", "obra").decide!(
      "opaque-id",
      "option-id",
      "legacy-version",
    );
    expect(fetch.mock.calls[0][0]).toContain("/selections/opaque-id/choice");
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({
      optionId: "option-id",
    });
  });
  it("does not duplicate a ticket when evidence upload fails after creation", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(json({ id: "ticket", number: 7 }))
      .mockResolvedValueOnce(
        json(
          { code: "storage_unavailable", error: "Armazenamento indisponível" },
          503,
        ),
      );
    const actions = createHttpGateway({ fetch }).actions!("empresa", "obra");
    await expect(
      actions.requestAssistance!({
        category: "Outros",
        title: "Teste",
        description: "Teste",
        files: [new File(["test"], "test.png", { type: "image/png" })],
      }),
    ).rejects.toThrow("Chamado 7 criado. Não abra outro");
    expect(fetch).toHaveBeenCalledTimes(2);
  });
  it("keeps missing values nullable, gates nested finance and labels submitted selections", () => {
    const p = adaptPortal(bootstrap(["selections", "changeOrders"]), {
      selections: {
        items: [
          {
            id: "s",
            title: "Piso",
            status: "Submitted",
            deadlinePassed: false,
            canChoose: false,
            chosenOptionId: "o",
            options: [
              { id: "o", name: "Padrão", priceDelta: 0, isStandard: true },
            ],
          },
        ],
      },
      orders: {
        items: [
          {
            id: "c",
            number: 1,
            title: "Muro",
            reason: "Other",
            costImpact: 999,
            scheduleImpactDays: 0,
            status: "Pending",
            canDecide: true,
            items: [],
          },
        ],
      },
    });
    expect(p.progress).toBeNull();
    expect(p.changeOrders![0].cost).toBeNull();
    expect(p.decisions[0].statusLabel).toContain("aguardando");
    expect(p.decisions[0].options[0].days).toBeNull();
  });
  it("uses actual external IDs, preserves simplified provenance and schedules URL renewal", () => {
    const twin: PublicBuildTwinDto = {
      geometrySource: "SimplifiedRepresentation",
      zones: [
        {
          id: "zone",
          name: "Sala",
          kind: "Area",
          order: 0,
          simplifiedBox: { width: 3, depth: 4, height: 2.8, elevation: 0 },
        },
      ],
      state: {
        date: "2026-10-30",
        states: { Completed: ["external-id"] },
        zoneStates: { InProgress: ["zone"] },
        counts: {},
        unbound: 2,
      },
      maxDate: "2026-10-30",
      disclaimer: "Representação simplificada",
    };
    const m = adaptTwin(twin);
    expect(m.elements[0].node).toBe("external-id");
    expect(m.elements[0].stage).toBe("");
    expect(m.zoneStates!.zone).toBe("active");
    expect(m.disclaimer).toBe(twin.disclaimer);
    const expiry = new Date(Date.now() + 60000).toISOString();
    const p = adaptPortal(bootstrap(["photos"]), {
      photos: {
        items: [
          {
            date: "2026-10-30",
            url: "https://example.test/signed",
            urlExpiresAt: expiry,
          },
        ],
        total: 1,
        skip: 0,
        take: 24,
      },
    });
    expect(Date.parse(p.refreshAt!)).toBeLessThan(Date.parse(expiry));
  });
});

it("does not offer an empty simplified scene without supplied dimensions", () => {
  const model = adaptTwin({
    geometrySource: "SimplifiedRepresentation",
    zones: [],
    state: {
      date: "2026-10-30",
      counts: {},
      states: {},
      zoneStates: {},
      unbound: 0,
    },
    maxDate: "2026-10-30",
    disclaimer: "Sem geometria publicada",
  });
  expect(model.status).toBe("unavailable");
  expect(model.elements).toEqual([]);
});
