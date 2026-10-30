import { describe, it, expect, vi } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { adaptPortal, adaptTwin, refreshDelay } from "./adapter";
import { createHttpGateway } from "./http-gateway";
import { useSignedMedia } from "../media";
import type {
  PublicBuildBootstrapDto,
  PublicBuildTwinDto,
} from "./generated/portal-types";

const bootstrap = (sections: string[]): PublicBuildBootstrapDto => ({
  contractVersion: "1.6",
  sections,
  portal: { access: "Password" },
  company: { name: "Empresa de teste" },
  project: { name: "Obra de teste", type: "House", status: "InProgress" },
  phase: "delivered",
});
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });

describe("signed URL renewal", () => {
  const now = Date.parse("2026-10-30T12:00:00Z");
  it("renews 30 s before the first link expires, within 1 to 14 minutes", () => {
    expect(refreshDelay([now + 10 * 60000], now)).toBe(10 * 60000 - 30000);
    expect(refreshDelay([now + 60 * 60000], now)).toBe(14 * 60000);
    expect(refreshDelay([now + 45000], now)).toBe(60000);
    expect(refreshDelay([], now)).toBe(14 * 60000);
  });
  it("a device clock ahead of the server never turns into a refresh loop", () => {
    // links signed for 15 min by the server look already expired on a clock 20 min ahead
    expect(refreshDelay([now - 5 * 60000], now)).toBe(14 * 60000);
  });
  it("an expired warranty is a date of the property, not of a link", () => {
    const past = new Date(Date.now() - 86400000).toISOString().slice(0, 10);
    const p = adaptPortal(bootstrap(["postConstruction"]), {
      warranty: {
        items: [
          {
            system: "Impermeabilização",
            months: 12,
            startsAt: "2024-01-01",
            expiresAt: past,
            isActive: false,
          },
        ],
        notice: "Prazos de referência.",
      },
    });
    expect(Date.parse(p.refreshAt!) - Date.now()).toBeGreaterThan(13 * 60000);
  });
});

describe("element index (contract v1.6)", () => {
  const twin: PublicBuildTwinDto = {
    geometrySource: "NativeBim",
    viewerUrl: "https://storage.example/viewer.glb?sig=1",
    zones: [
      {
        id: "site",
        name: "Terreno",
        kind: "Site",
        order: 0,
        externalId: "site",
      },
      {
        id: "t",
        name: "Térreo",
        kind: "Storey",
        order: 1,
        parentId: "site",
        externalId: "t",
      },
      {
        id: "sala",
        name: "Sala",
        kind: "Space",
        order: 2,
        parentId: "t",
        externalId: "sala",
      },
    ],
    state: {
      date: "2026-10-30",
      counts: {},
      states: { InProgress: ["3hX1"], Completed: ["0aB2"] },
      zoneStates: {},
      unbound: 1,
    },
    maxDate: "2026-10-30",
    disclaimer: "Modelo BIM do projeto.",
    elementIndex: {
      stages: [
        { name: "Fundação", order: 1, elementIds: ["0aB2"] },
        { name: "Alvenaria", order: 2, elementIds: ["3hX1"] },
      ],
      categories: [
        { name: "IfcSlab", elementIds: ["0aB2"] },
        { name: "IfcWall", elementIds: ["3hX1"] },
        { name: "IfcFlowTerminal", elementIds: ["9zZ3"] },
      ],
      zones: { sala: ["3hX1"], t: ["0aB2"] },
    },
  };
  it("takes stage, category and storey from the API, every element included", () => {
    const m = adaptTwin(twin);
    const byId = Object.fromEntries(m.elements.map((e) => [e.id, e]));
    expect(byId["3hX1"]).toMatchObject({
      stage: "Alvenaria",
      category: "Paredes",
      floor: "t", // the storey above the room it sits in
      status: "active",
    });
    expect(byId["0aB2"]).toMatchObject({ stage: "Fundação", floor: "t" });
    // an element with no binding stays neutral and keeps its recorded class
    expect(byId["9zZ3"]).toMatchObject({
      stage: "",
      category: "IfcFlowTerminal",
      status: "unknown",
    });
  });
  it("never reads metadata from node names", () => {
    const m = adaptTwin({
      ...twin,
      elementIndex: undefined,
      state: { ...twin.state, states: { Completed: ["IfcWall_Alvenaria_T"] } },
    });
    expect(m.elements).toEqual([
      expect.objectContaining({
        id: "IfcWall_Alvenaria_T",
        stage: "",
        category: undefined,
        floor: "",
      }),
    ]);
  });
});

describe("evidence upload", () => {
  it("reports each photo before sending it and only completes after the server accepted all", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(json({ id: "ticket", number: 3 }))
      .mockResolvedValueOnce(json({ id: "ticket" }));
    const progress = vi.fn();
    const actions = createHttpGateway({ fetch }).actions!("empresa", "obra");
    await actions.uploadEvidence!(
      "ticket",
      [
        new File(["a"], "a.png", { type: "image/png" }),
        new File(["b"], "b.png", { type: "image/png" }),
      ],
      progress,
    );
    expect(progress.mock.calls).toEqual([
      [0, 2],
      [1, 2],
      [2, 2],
    ]);
    expect(fetch).toHaveBeenCalledTimes(2);
  });
  it("refuses a non-photo before any request, as the contract's 422", async () => {
    const fetch = vi.fn();
    const actions = createHttpGateway({ fetch }).actions!("empresa", "obra");
    await expect(
      actions.uploadEvidence!("ticket", [
        new File(["%PDF"], "laudo.pdf", { type: "application/pdf" }),
      ]),
    ).rejects.toMatchObject({ status: 422 });
    expect(fetch).not.toHaveBeenCalled();
  });
});

describe("signed media in the page", () => {
  it("a failed image renews the links once per minute, never in a loop", () => {
    const refresh = vi.fn().mockResolvedValue(undefined);
    const { result } = renderHook(() => useSignedMedia({ refresh }));
    act(() => {
      result.current.onError();
      result.current.onError();
      result.current.onError();
    });
    expect(refresh).toHaveBeenCalledTimes(1);
  });
  it("an expired link is renewed instead of opened", () => {
    const refresh = vi.fn().mockResolvedValue(undefined);
    const past = new Date(Date.now() - 1000).toISOString();
    const { result } = renderHook(() => useSignedMedia({ refresh }, past));
    const preventDefault = vi.fn();
    act(() =>
      result.current.guardLink({
        preventDefault,
      } as unknown as Parameters<typeof result.current.guardLink>[0]),
    );
    expect(preventDefault).toHaveBeenCalled();
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(result.current.notice).toMatch(/renovado/);
  });
  it("a fresh link opens normally", () => {
    const refresh = vi.fn();
    const future = new Date(Date.now() + 60000).toISOString();
    const { result } = renderHook(() => useSignedMedia({ refresh }, future));
    const preventDefault = vi.fn();
    result.current.guardLink({
      preventDefault,
    } as unknown as Parameters<typeof result.current.guardLink>[0]);
    expect(preventDefault).not.toHaveBeenCalled();
    expect(refresh).not.toHaveBeenCalled();
  });
});
