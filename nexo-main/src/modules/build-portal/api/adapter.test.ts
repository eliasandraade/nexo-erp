import { describe, it, expect, afterEach, vi } from "vitest";
import {
  adaptPortal,
  adaptState,
  adaptTwin,
  categoryLabel,
  nullableNumber,
  phase,
  refreshDelay,
  type PortalResources,
} from "./adapter";
import type {
  PublicBuildBootstrapDto,
  PublicBuildRealityCaptureDto,
  PublicBuildSelectionDto,
  PublicBuildTwinDto,
  PublicBuildTwinStateDto,
} from "./generated/portal-types";

const bootstrap = (
  sections: string[],
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

const emptyState = (
  over: Partial<PublicBuildTwinStateDto> = {},
): PublicBuildTwinStateDto => ({
  date: "2026-10-30",
  counts: {},
  states: {},
  zoneStates: {},
  unbound: 0,
  ...over,
});

const twin = (over: Partial<PublicBuildTwinDto> = {}): PublicBuildTwinDto => ({
  zones: [],
  state: emptyState(),
  maxDate: "2026-10-30",
  disclaimer: "Modelo de referência",
  ...over,
});

afterEach(() => vi.useRealTimers());

describe("status and number helpers", () => {
  it.each([
    ["Pending", "waiting"],
    ["NotStarted", "waiting"],
    ["InProgress", "active"],
    ["Completed", "complete"],
    ["Delayed", "delayed"],
    ["Blocked", "blocked"],
    ["NeedsReview", "review"],
    ["Paused", "unknown"],
    ["", "unknown"],
  ])("maps the API status %s to the portal phase %s", (status, expected) => {
    expect(phase(status)).toBe(expected);
  });

  it("keeps only finite numbers", () => {
    expect(nullableNumber(42.5)).toBe(42.5);
    expect(nullableNumber(0)).toBe(0);
    expect(nullableNumber(null)).toBeNull();
    expect(nullableNumber(undefined)).toBeNull();
    expect(nullableNumber(Number.NaN)).toBeNull();
    expect(nullableNumber(Number.POSITIVE_INFINITY)).toBeNull();
  });

  it("labels known IFC classes in Portuguese and keeps unknown ones", () => {
    expect(categoryLabel("IfcWallStandardCase")).toBe("Paredes");
    expect(categoryLabel("IfcFooting")).toBe("Fundações");
    expect(categoryLabel("Mesh")).toBe("Elementos do modelo");
    expect(categoryLabel("IfcFlowTerminal")).toBe("IfcFlowTerminal");
  });

  it("flattens element and zone states by id", () => {
    expect(
      adaptState(
        emptyState({
          date: "2026-11-01",
          states: { Completed: ["a", "b"], Blocked: ["c"], Weird: ["d"] },
          zoneStates: { InProgress: ["z1"] },
        }),
      ),
    ).toEqual({
      id: "2026-11-01",
      date: "2026-11-01",
      states: { a: "complete", b: "complete", c: "blocked", d: "unknown" },
      zoneStates: { z1: "active" },
    });
  });

  it("refreshes after a minute at least, even for a link about to expire", () => {
    const now = Date.parse("2026-11-13T12:00:00Z");
    expect(refreshDelay([now + 20 * 60000, now + 2 * 60000], now)).toBe(
      2 * 60000 - 30000,
    );
  });
});

describe("digital twin", () => {
  it("is ready with a signed viewer URL and keeps provenance fields", () => {
    const m = adaptTwin(
      twin({
        viewerUrl: "https://cdn.example.test/viewer.glb",
        geometrySource: "NativeBim",
        minDate: "2026-01-05",
        state: emptyState({ unbound: 3 }),
      }),
    );
    expect(m).toMatchObject({
      status: "ready",
      url: "https://cdn.example.test/viewer.glb",
      geometrySource: "NativeBim",
      minDate: "2026-01-05",
      maxDate: "2026-10-30",
      unbound: 3,
      disclaimer: "Modelo de referência",
    });
    expect(m.snapshots).toHaveLength(1);
  });

  it("without viewer nor known geometry it is unavailable and optional fields stay undefined", () => {
    const m = adaptTwin(twin({ viewerUrl: null, geometrySource: null }));
    expect(m.status).toBe("unavailable");
    expect(m.url).toBeUndefined();
    expect(m.geometrySource).toBeUndefined();
    expect(m.minDate).toBeUndefined();
  });

  it("assisted mapping is ready only once a zone was drawn", () => {
    const zone = { id: "z", kind: "Room", name: "Sala", order: 0 };
    expect(adaptTwin(twin({ geometrySource: "AssistedMapping" })).status).toBe(
      "unavailable",
    );
    expect(
      adaptTwin(twin({ geometrySource: "AssistedMapping", zones: [zone] }))
        .status,
    ).toBe("ready");
  });

  it("a simplified representation needs at least one box with positive sizes", () => {
    const zone = (box: { width: number; depth: number; height: number }) => ({
      id: "z",
      kind: "Room",
      name: "Sala",
      order: 0,
      simplifiedBox: { ...box, elevation: 0 },
    });
    const ready = (box: { width: number; depth: number; height: number }) =>
      adaptTwin(
        twin({ geometrySource: "SimplifiedRepresentation", zones: [zone(box)] }),
      ).status;
    expect(ready({ width: 0, depth: 4, height: 2.8 })).toBe("unavailable");
    expect(ready({ width: 3, depth: -1, height: 2.8 })).toBe("unavailable");
    expect(ready({ width: 3, depth: 4, height: Number.NaN })).toBe(
      "unavailable",
    );
    expect(ready({ width: 3, depth: 4, height: 2.8 })).toBe("ready");
  });

  it("maps zones with their published node binding and optional parent and box", () => {
    const m = adaptTwin(
      twin({
        zones: [
          {
            id: "z1",
            kind: "Storey",
            name: "Térreo",
            order: 0,
            externalId: "storey-node",
          },
          { id: "z2", kind: "Room", name: "Sala", order: 1, parentId: "z1" },
        ],
      }),
    );
    expect(m.zones).toEqual([
      {
        id: "z1",
        name: "Térreo",
        kind: "Storey",
        node: "storey-node",
        parentId: undefined,
        box: undefined,
      },
      {
        id: "z2",
        name: "Sala",
        kind: "Room",
        node: undefined,
        parentId: "z1",
        box: undefined,
      },
    ]);
  });

  it("finds the floor of an element by climbing the zone tree up to its storey", () => {
    const m = adaptTwin(
      twin({
        zones: [
          { id: "s1", kind: "Storey", name: "Térreo", order: 0, externalId: "T" },
          { id: "s2", kind: "Storey", name: "Superior", order: 1 },
          { id: "r1", kind: "Room", name: "Sala", order: 2, parentId: "s1" },
          { id: "a1", kind: "Area", name: "Canto", order: 3, parentId: "r1" },
          { id: "lost", kind: "Room", name: "Solta", order: 4, parentId: "x" },
        ],
        elementIndex: {
          stages: [{ name: "Alvenaria", elementIds: ["wall-000001"] }],
          categories: [
            { name: "IfcWall", elementIds: ["wall-000001"] },
            { name: "IfcSlab", elementIds: ["slab-000002"] },
            { name: "IfcRoof", elementIds: ["roof-000003"] },
          ],
          zones: {
            a1: ["wall-000001"],
            s2: ["slab-000002"],
            lost: ["roof-000003"],
          },
        },
        state: emptyState({ states: { Completed: ["wall-000001"] } }),
      }),
    );
    const byId = Object.fromEntries(m.elements.map((e) => [e.id, e]));
    expect(byId["wall-000001"]).toEqual({
      id: "wall-000001",
      node: "wall-000001",
      name: "Paredes · 000001",
      floor: "T",
      stage: "Alvenaria",
      category: "Paredes",
      status: "complete",
    });
    // a storey without a published node and a zone whose parent is unknown give no floor
    expect(byId["slab-000002"]).toMatchObject({
      floor: "",
      stage: "",
      category: "Lajes e pisos",
      status: "unknown",
    });
    expect(byId["roof-000003"].floor).toBe("");
  });

  it("without storeys the floor filter uses the element's own zone node", () => {
    const m = adaptTwin(
      twin({
        zones: [
          { id: "r1", kind: "Room", name: "Sala", order: 0, externalId: "SALA" },
          { id: "r2", kind: "Room", name: "Quarto", order: 1 },
        ],
        elementIndex: {
          stages: [],
          categories: [],
          zones: { r1: ["el-aaaaaa"], r2: ["el-bbbbbb"] },
        },
        state: emptyState({ states: { InProgress: ["el-aaaaaa", "el-bbbbbb"] } }),
      }),
    );
    expect(m.elements).toEqual([
      {
        id: "el-aaaaaa",
        node: "el-aaaaaa",
        name: "Elemento aaaaaa",
        floor: "SALA",
        stage: "",
        category: undefined,
        status: "active",
      },
      expect.objectContaining({ id: "el-bbbbbb", floor: "" }),
    ]);
  });

  it("an element without a zone has no floor", () => {
    const m = adaptTwin(
      twin({
        zones: [{ id: "s1", kind: "Storey", name: "Térreo", order: 0 }],
        state: emptyState({ states: { Delayed: ["solto-123456"] } }),
      }),
    );
    expect(m.elements).toEqual([
      expect.objectContaining({ id: "solto-123456", floor: "", status: "delayed" }),
    ]);
  });
});

describe("portal from the bootstrap and its resources", () => {
  it("hides every gated section when nothing is announced", () => {
    const p = adaptPortal(
      bootstrap([], {
        progress: {
          asOf: "2026-10-01",
          actualPercent: 40,
          methodology: "Peso",
          forecastDelivery: "2026-12-20",
        },
        stages: [{ name: "Fundação", order: 0, status: "Completed", actualPercent: 100 }],
        schedule: { stages: [{ name: "Fundação", order: 0 }] },
        financialSummary: { realizedTotal: 1000, currency: "BRL", asOf: "2026-10-01" },
        contact: { whatsApp: "+55 (11) 99999-0000" },
        latestUpdates: [
          { kind: "WeeklyDigest", title: "Semana", body: "Resumo", publishedAt: "2026-10-01" },
        ],
        weather: {
          date: "2026-10-01",
          temperatureMax: 30,
          temperatureMin: 20,
          precipitationMm: 1,
          description: "Sol",
          source: "Open-Meteo",
          notice: "Previsão.",
        },
      }),
      {
        photos: { items: [{ date: "2026-10-01", urlExpiresAt: "x" }], total: 1, skip: 0, take: 24 },
        documents: { items: [] },
        warranty: { items: [], notice: "Garantia" },
        twin: twin(),
      },
    );
    expect(p).toMatchObject({
      progress: null,
      planned: null,
      delivery: null,
      contact: undefined,
      curves: undefined,
      schedule: undefined,
      stages: [],
      weekly: null,
      updates: [],
      photos: [],
      documents: [],
      finance: null,
      decisions: [],
      warranties: [],
      assistance: null,
      changeOrders: undefined,
      measurements: undefined,
      weather: undefined,
      model: undefined,
      warrantyNotice: undefined,
    });
    expect(p.updatedAt).toBeNull();
    expect(p.handover).toBeUndefined();
    expect(p.captures).toBeUndefined();
  });

  it("maps identity, branding and lifecycle", () => {
    const p = adaptPortal(
      bootstrap(["contact"], {
        readOnly: true,
        portal: {
          access: "Password",
          title: "Minha casa",
          welcomeMessage: "Bem-vindo",
          primaryColor: "#123456",
          accentColor: "#abcdef",
          coverUrl: "https://cdn.example.test/capa.jpg",
        },
        company: { name: "Construtora", logoUrl: "https://cdn.example.test/empresa.png" },
        contact: { whatsApp: "+55 (11) 98888-7777" },
      }),
      {},
    );
    expect(p).toMatchObject({
      visibility: "private",
      readOnly: true,
      indexable: false,
      title: "Minha casa",
      welcome: "Bem-vindo",
      company: "Construtora",
      name: "Casa Jardim",
      logo: "https://cdn.example.test/empresa.png",
      cover: "https://cdn.example.test/capa.jpg",
      color: "#123456",
      secondaryColor: "#abcdef",
      contact: "https://wa.me/5511988887777",
      lifecycle: "building",
      forecast: null,
    });
    expect(p.sections).toEqual(["contact"]);

    const own = adaptPortal(
      bootstrap(["contact"], {
        portal: { access: "Public", logoUrl: "https://cdn.example.test/portal.png" },
        company: { name: "C", logoUrl: "https://cdn.example.test/empresa.png" },
        contact: { phone: "1133334444" },
      }),
      {},
    );
    expect(own.visibility).toBe("public");
    expect(own.logo).toBe("https://cdn.example.test/portal.png");
    expect(own.contact).toBeUndefined();
    expect(own.title).toBeUndefined();
  });

  it.each([
    ["delivered", "InProgress", "handover"],
    ["construction", "Completed", "completed"],
    ["construction", "InProgress", "building"],
  ])("phase %s with project %s is the lifecycle %s", (phaseName, status, lifecycle) => {
    const p = adaptPortal(
      bootstrap([], {
        phase: phaseName,
        project: { name: "Obra", type: "House", status },
      }),
      {},
    );
    expect(p.lifecycle).toBe(lifecycle);
  });

  it("takes progress and curves from the resource, forecast only when announced", () => {
    const progress = {
      asOf: "2026-10-30",
      actualPercent: 42.5,
      plannedPercent: 50,
      forecastDelivery: "2026-12-20",
      methodology: "Peso",
      curves: {
        actual: [{ date: "2026-10-01", percent: 30 }],
        planned: [{ date: "2026-10-01", percent: 35 }],
        forecast: [{ date: "2026-12-20", percent: 100 }],
      },
    };
    const withForecast = adaptPortal(
      bootstrap(["overallProgress", "forecastDelivery"]),
      { progress },
    );
    expect(withForecast).toMatchObject({
      progress: 42.5,
      planned: 50,
      updatedAt: "2026-10-30",
      delivery: "2026-12-20",
      curves: progress.curves,
    });

    const without = adaptPortal(bootstrap(["overallProgress"]), {
      progress: { ...progress, curves: { actual: [], planned: null, forecast: null } },
    });
    expect(without.delivery).toBeNull();
    expect(without.curves).toEqual({ actual: [], planned: undefined, forecast: undefined });
  });

  it("falls back to the bootstrap summary when the progress read failed", () => {
    const p = adaptPortal(
      bootstrap(["overallProgress", "forecastDelivery"], {
        progress: {
          asOf: "2026-10-15",
          actualPercent: 30,
          plannedPercent: null,
          methodology: "Peso",
          forecastDelivery: null,
        },
      }),
      {},
    );
    expect(p).toMatchObject({
      progress: 30,
      planned: null,
      updatedAt: "2026-10-15",
      delivery: null,
      curves: undefined,
    });
  });

  it("builds the timeline from the announced item types with readable fallbacks", () => {
    const p = adaptPortal(
      bootstrap(["clientUpdates", "dailyLogs", "stageProgress", "photos"]),
      {
        timeline: {
          items: [
            { type: "update", occurredAt: "2026-10-05T10:00:00Z", title: "Visita", text: "Tudo certo" },
            { type: "daily_log", occurredAt: "2026-10-04T10:00:00Z" },
            { type: "photo", occurredAt: "2026-10-03T10:00:00Z", caption: "Fachada" },
            { type: "stage_progress", occurredAt: "2026-10-02T10:00:00Z", stageName: "Alvenaria", percent: 37.5 },
            { type: "stage_completed", occurredAt: "2026-10-01T10:00:00Z" },
            { type: "unknown_type", occurredAt: "2026-09-30T10:00:00Z", title: "Nunca" },
          ],
          nextBefore: "2026-10-01T10:00:00Z",
        },
      },
    );
    expect(p.updates).toEqual([
      { id: "update-2026-10-05T10:00:00Z-0", title: "Visita", body: "Tudo certo", publishedAt: "2026-10-05T10:00:00Z", planned: false },
      { id: "daily_log-2026-10-04T10:00:00Z-1", title: "Diário da obra", body: "", publishedAt: "2026-10-04T10:00:00Z", planned: false },
      { id: "photo-2026-10-03T10:00:00Z-2", title: "Imagem publicada", body: "Fachada", publishedAt: "2026-10-03T10:00:00Z", planned: false },
      { id: "stage_progress-2026-10-02T10:00:00Z-3", title: "Alvenaria", body: "Avanço registrado: 37,5%", publishedAt: "2026-10-02T10:00:00Z", planned: false },
      { id: "stage_completed-2026-10-01T10:00:00Z-4", title: "Avanço da obra", body: "", publishedAt: "2026-10-01T10:00:00Z", planned: false },
    ]);
    expect(p.pagination!.timeline).toBe("2026-10-01T10:00:00Z");
  });

  it("drops timeline items of sections that are not announced", () => {
    const p = adaptPortal(bootstrap(["dailyLogs"]), {
      timeline: {
        items: [
          { type: "update", occurredAt: "2026-10-05T10:00:00Z", title: "Visita" },
          { type: "stage_progress", occurredAt: "2026-10-04T10:00:00Z", percent: 10 },
          { type: "photo", occurredAt: "2026-10-03T10:00:00Z" },
          { type: "daily_log", occurredAt: "2026-10-02T10:00:00Z", text: "Concretagem" },
        ],
      },
    });
    expect(p.updates.map((u) => u.body)).toEqual(["Concretagem"]);
    expect(p.pagination!.timeline).toBeUndefined();
  });

  it("without timeline items shows the latest updates and the weekly digest", () => {
    const p = adaptPortal(
      bootstrap(["clientUpdates"], {
        latestUpdates: [
          { kind: "Manual", title: "Laje concluída", body: "Concretagem da laje", publishedAt: "2026-10-10" },
          { kind: "WeeklyDigest", title: "Resumo", body: "Semana produtiva", publishedAt: "2026-10-08" },
        ],
      }),
      { timeline: { items: [] } },
    );
    expect(p.updates).toEqual([
      { id: "update-0", title: "Laje concluída", body: "Concretagem da laje", publishedAt: "2026-10-10", planned: false },
      { id: "update-1", title: "Resumo", body: "Semana produtiva", publishedAt: "2026-10-08", planned: false },
    ]);
    expect(p.weekly).toEqual({ body: "Semana produtiva", publishedAt: "2026-10-08" });
    expect(p.updatedAt).toBe("2026-10-10");
  });

  it("maps schedule and stage progress", () => {
    const p = adaptPortal(
      bootstrap(["schedule", "stageProgress"], {
        schedule: {
          stages: [
            { name: "Fundação", order: 0, plannedStart: "2026-01-05", plannedEnd: "2026-02-10" },
            { name: "Cobertura", order: 1 },
          ],
        },
        stages: [
          { name: "Fundação", order: 0, status: "Completed", actualPercent: 100 },
          { name: "Cobertura", order: 1, status: "Delayed", actualPercent: Number.NaN },
        ],
      }),
      {},
    );
    expect(p.schedule).toEqual([
      { name: "Fundação", order: 0, start: "2026-01-05", end: "2026-02-10" },
      { name: "Cobertura", order: 1, start: null, end: null },
    ]);
    expect(p.stages).toEqual([
      { id: "stage-0", name: "Fundação", status: "complete", progress: 100 },
      { id: "stage-1", name: "Cobertura", status: "delayed", progress: null },
    ]);

    expect(adaptPortal(bootstrap(["schedule", "stageProgress"]), {})).toMatchObject({
      schedule: undefined,
      stages: [],
    });
  });

  it("maps photos and documents with safe defaults", () => {
    const p = adaptPortal(bootstrap(["photos", "documents"]), {
      photos: {
        items: [
          { date: "2026-10-01", url: "https://cdn.example.test/1.jpg", caption: "Sala", urlExpiresAt: "2099-01-01T00:00:00Z" },
          { date: "2026-10-02", url: null, caption: null, urlExpiresAt: "2099-01-01T00:00:00Z" },
        ],
        total: 30,
        skip: 0,
        take: 24,
      },
      documents: {
        items: [
          {
            title: "Alvará",
            category: "Licenças",
            fileName: "alvara.pdf",
            contentType: "application/pdf",
            sizeBytes: 100,
            uploadedAt: "2026-01-01",
            url: "https://cdn.example.test/alvara.pdf",
          },
          {
            title: "ART",
            category: "Projetos",
            fileName: "art.pdf",
            contentType: "application/pdf",
            sizeBytes: 100,
            uploadedAt: "2026-01-01",
          },
        ],
      },
    });
    expect(p.photos).toEqual([
      { id: "photo-0", url: "https://cdn.example.test/1.jpg", caption: "Sala", date: "2026-10-01" },
      { id: "photo-1", url: "", caption: "Foto da obra", date: "2026-10-02" },
    ]);
    expect(p.pagination!.photos).toBe(2);
    expect(p.documents).toEqual([
      { id: "document-0", name: "Alvará", category: "Licenças", url: "https://cdn.example.test/alvara.pdf" },
      { id: "document-1", name: "ART", category: "Projetos", url: "" },
    ]);
  });

  it("does not paginate photos once every one was read", () => {
    const p = adaptPortal(bootstrap(["photos"]), {
      photos: { items: [{ date: "2026-10-01", urlExpiresAt: "2099-01-01T00:00:00Z" }], total: 1, skip: 0, take: 24 },
    });
    expect(p.pagination!.photos).toBeUndefined();
  });

  it("shows the financial summary and money amounts only when announced", () => {
    const resources: PortalResources = {
      orders: {
        items: [
          {
            id: "co-1",
            number: 3,
            title: "Ampliar varanda",
            description: "Mais 2 m²",
            reason: "ClientRequest",
            costImpact: 4500,
            scheduleImpactDays: 7,
            status: "Pending",
            canDecide: true,
            items: [{ name: "Concreto", kind: "Material", quantity: 2, unit: "m³" }],
          },
          {
            id: "co-2",
            number: 4,
            title: "Trocar piso",
            reason: "Other",
            costImpact: null,
            scheduleImpactDays: 0,
            status: "Approved",
            canDecide: false,
            items: [],
          },
        ],
      },
      measurements: {
        items: [
          {
            number: 1,
            periodStart: "2026-09-01",
            periodEnd: "2026-09-30",
            totalAmount: 12000,
            items: [{ stageName: "Alvenaria", previousPercent: 10, measuredPercent: 25 }],
          },
        ],
      },
    };
    const financial = bootstrap(["financialSummary", "changeOrders", "measurements"], {
      financialSummary: { approvedBudget: 500000, realizedTotal: 210000.5, currency: "BRL", asOf: "2026-10-30" },
    });
    const p = adaptPortal(financial, resources);
    expect(p.finance).toEqual({
      level: "summary",
      budget: 500000,
      executed: 210000.5,
      categories: [],
      transactions: [],
    });
    expect(p.changeOrders).toEqual([
      {
        id: "co-1",
        number: 3,
        title: "Ampliar varanda",
        description: "Mais 2 m²",
        status: "Pending",
        canDecide: true,
        cost: 4500,
        days: 7,
        items: [{ name: "Concreto", quantity: 2, unit: "m³" }],
      },
      expect.objectContaining({ id: "co-2", description: undefined, cost: null, days: 0, items: [] }),
    ]);
    expect(p.measurements).toEqual([
      {
        number: 1,
        start: "2026-09-01",
        end: "2026-09-30",
        amount: 12000,
        items: [{ name: "Alvenaria", previous: 10, measured: 25 }],
      },
    ]);

    const hidden = adaptPortal(
      bootstrap(["changeOrders", "measurements"], { financialSummary: financial.financialSummary }),
      resources,
    );
    expect(hidden.finance).toBeNull();
    expect(hidden.changeOrders![0].cost).toBeNull();
    expect(hidden.measurements![0].amount).toBeNull();

    const noBudget = adaptPortal(
      bootstrap(["financialSummary"], {
        financialSummary: { approvedBudget: null, realizedTotal: 10, currency: "BRL", asOf: "2026-10-30" },
      }),
      {},
    );
    expect(noBudget.finance).toMatchObject({ budget: null, executed: 10 });
  });

  it("labels each selection by its state and maps its options", () => {
    const selection = (
      over: Partial<PublicBuildSelectionDto>,
    ): PublicBuildSelectionDto => ({
      id: "sel",
      title: "Piso da sala",
      deadlinePassed: false,
      status: "Open",
      canChoose: true,
      options: [],
      ...over,
    });
    const p = adaptPortal(bootstrap(["selections"]), {
      selections: {
        items: [
          selection({
            id: "open",
            description: "Escolha o piso",
            deadline: "2026-11-30",
            options: [
              {
                id: "o1",
                name: "Porcelanato",
                description: "60x60",
                priceDelta: 1200,
                isStandard: false,
                imageUrl: "https://cdn.example.test/p.jpg",
                leadTimeDays: 10,
              },
              { id: "o2", name: "Cerâmica", priceDelta: 0, isStandard: true },
            ],
          }),
          selection({ id: "late", deadlinePassed: true, canChoose: false }),
          selection({ id: "sent", status: "Submitted", chosenOptionId: "o1" }),
          selection({ id: "done", status: "Confirmed", chosenOptionId: "o2" }),
        ],
      },
    });
    const [open, late, sent, done] = p.decisions;
    expect(open).toEqual({
      id: "open",
      version: "",
      title: "Piso da sala",
      description: "Escolha o piso",
      due: "2026-11-30",
      status: "pending",
      statusLabel: "Aguardando sua escolha",
      canChoose: true,
      selected: undefined,
      options: [
        { id: "o1", name: "Porcelanato", description: "60x60", extra: 1200, days: 10, leadTime: true, image: "https://cdn.example.test/p.jpg" },
        { id: "o2", name: "Cerâmica", description: "", extra: 0, days: null, leadTime: true, image: undefined },
      ],
    });
    expect(late).toMatchObject({ due: "", statusLabel: "Prazo encerrado", canChoose: false, description: undefined });
    expect(sent).toMatchObject({ status: "confirmed", selected: "o1", statusLabel: "Enviada, aguardando a construtora" });
    expect(done).toMatchObject({ status: "confirmed", statusLabel: "Confirmada pela construtora" });
  });

  it("after delivery shows warranties, assistance and handover", () => {
    const p = adaptPortal(bootstrap(["postConstruction"]), {
      warranty: {
        items: [
          { system: "Estrutura", months: 60, startsAt: "2026-01-01", expiresAt: "2031-01-01", isActive: true },
        ],
        notice: "Prazos conforme a NBR 15575.",
      },
      assistance: {
        canOpen: true,
        categories: ["Hidráulica", "Elétrica"],
        items: [
          {
            id: "t1",
            number: 12,
            category: "Hidráulica",
            title: "Vazamento",
            description: "Pia da cozinha",
            openedVia: "Portal",
            openedAt: "2026-10-01",
            status: "Resolved",
            resolutionNotes: "Sifão trocado",
            rejectionReason: "não usado",
            rating: 5,
            scheduledFor: "2026-10-03",
            photoUrls: ["https://cdn.example.test/a.jpg"],
            canAddPhotos: false,
            canGiveFeedback: true,
            timeline: [{ status: "Open", at: "2026-10-01" }],
          },
          {
            id: "t2",
            number: 13,
            category: "Elétrica",
            title: "Tomada",
            description: "Sem energia",
            openedVia: "Portal",
            openedAt: "2026-10-02",
            status: "Rejected",
            rejectionReason: "Uso indevido",
            photoUrls: [],
            canAddPhotos: true,
            canGiveFeedback: false,
            timeline: [],
          },
        ],
      },
      handover: {
        status: "Scheduled",
        scheduledFor: "2026-12-01",
        keysDelivered: false,
        canAccept: true,
        documents: [
          { name: "Manual do proprietário", url: "https://cdn.example.test/manual.pdf" },
          { name: "Habite-se" },
        ],
        walkthrough: {
          status: "Completed",
          totalItems: 2,
          okItems: 1,
          pendingItems: 1,
          items: [
            { text: "Portas", result: "Ok" },
            { text: "Rejunte", result: "Pending" },
          ],
        },
      },
    });
    expect(p.warranties).toEqual([{ id: "warranty-0", name: "Estrutura", until: "2031-01-01" }]);
    expect(p.warrantyNotice).toBe("Prazos conforme a NBR 15575.");
    expect(p.assistance).toEqual({
      canOpen: true,
      categories: ["Hidráulica", "Elétrica"],
      tickets: [
        {
          id: "t1",
          number: 12,
          title: "Vazamento",
          status: "Resolved",
          description: "Pia da cozinha",
          canAddPhotos: false,
          canGiveFeedback: true,
          photos: ["https://cdn.example.test/a.jpg"],
          rating: 5,
          resolution: "Sifão trocado",
          scheduled: "2026-10-03",
          timeline: [{ status: "Open", at: "2026-10-01" }],
        },
        expect.objectContaining({ id: "t2", rating: undefined, resolution: "Uso indevido", scheduled: undefined }),
      ],
    });
    expect(p.handover).toEqual({
      status: "Scheduled",
      scheduled: "2026-12-01",
      delivered: undefined,
      accepted: undefined,
      canAccept: true,
      keys: false,
      documents: [
        { name: "Manual do proprietário", url: "https://cdn.example.test/manual.pdf" },
        { name: "Habite-se", url: undefined },
      ],
      inspection: {
        status: "Completed",
        items: [
          { text: "Portas", result: "Ok" },
          { text: "Rejunte", result: "Pending" },
        ],
      },
    });
  });

  it("uses the property warranty terms when the warranty read is missing, and a delivered handover without inspection", () => {
    const p = adaptPortal(
      bootstrap(["postConstruction"], {
        property: {
          handoverAccepted: true,
          openAssistanceCount: 0,
          warrantyTerms: [
            { system: "Impermeabilização", months: 36, startsAt: "2026-01-01", expiresAt: "2029-01-01", isActive: true },
          ],
        },
      }),
      {
        handover: {
          status: "Accepted",
          deliveredAt: "2026-11-01",
          acceptedAt: "2026-11-02",
          keysDelivered: true,
          canAccept: false,
          documents: [],
          walkthrough: null,
        },
      },
    );
    expect(p.warranties).toEqual([{ id: "warranty-0", name: "Impermeabilização", until: "2029-01-01" }]);
    expect(p.warrantyNotice).toBeUndefined();
    expect(p.assistance).toBeNull();
    expect(p.handover).toMatchObject({
      delivered: "2026-11-01",
      accepted: "2026-11-02",
      keys: true,
      inspection: undefined,
    });
    expect(adaptPortal(bootstrap(["postConstruction"]), {}).warranties).toEqual([]);
  });

  it("describes the weather of the day with its source", () => {
    const p = adaptPortal(
      bootstrap(["weather"], {
        weather: {
          date: "2026-10-30",
          temperatureMax: 31,
          temperatureMin: 19,
          precipitationMm: 4.2,
          description: "Chuva fraca",
          source: "Open-Meteo",
          notice: "Pode atrasar a concretagem.",
        },
      }),
      {},
    );
    expect(p.weather).toEqual([
      {
        date: "2026-10-30",
        description: "Chuva fraca. 19–31 °C. Chuva: 4.2 mm.",
        impact: "Pode atrasar a concretagem. Fonte: Open-Meteo.",
      },
    ]);
    expect(adaptPortal(bootstrap(["weather"]), {}).weather).toBeUndefined();
  });

  it("adds the 3D model only when announced and read", () => {
    const resources = { twin: twin({ viewerUrl: "https://cdn.example.test/v.glb" }) };
    expect(adaptPortal(bootstrap(["model3d"]), resources).model?.status).toBe("ready");
    expect(adaptPortal(bootstrap(["model3d"]), {}).model).toBeUndefined();
  });

  it("splits reality captures into 360° tours, gallery media and point cloud downloads", () => {
    const capture = (
      over: Partial<PublicBuildRealityCaptureDto>,
    ): PublicBuildRealityCaptureDto => ({
      type: "Photo",
      capturedAt: "2026-10-01",
      title: "Captura",
      contentType: "image/jpeg",
      url: "https://cdn.example.test/c",
      ...over,
    });
    const p = adaptPortal(bootstrap(["realityCapture", "photos"]), {
      photos: { items: [{ date: "2026-09-01", url: "https://cdn.example.test/f.jpg", urlExpiresAt: "2099-01-01T00:00:00Z" }], total: 1, skip: 0, take: 24 },
      captures: {
        items: [
          capture({ type: "Photo360", title: "Sala 360", zoneName: "Térreo", url: "https://cdn.example.test/360.jpg" }),
          capture({ type: "Photo360", title: "Sem link", url: null }),
          capture({ type: "Photo", title: "Fachada", stageName: "Reboco", zoneName: "Frente" }),
          capture({ type: "Video", title: "Voo", contentType: "video/mp4", url: null }),
          capture({ type: "Drone", title: "Documento", contentType: "application/pdf" }),
          capture({ type: "PointCloud", title: "Nuvem", contentType: "application/octet-stream" }),
          capture({ type: "PointCloud", title: "Nuvem sem link", contentType: "application/octet-stream", url: null }),
        ],
        total: 10,
        skip: 0,
        take: 24,
      },
    });
    expect(p.captures).toEqual([
      { id: "capture-0", name: "Sala 360", date: "2026-10-01", floor: "Térreo", url: "https://cdn.example.test/360.jpg", hotspots: [] },
    ]);
    expect(p.photos).toEqual([
      { id: "photo-0", url: "https://cdn.example.test/f.jpg", caption: "Foto da obra", date: "2026-09-01" },
      { id: "capture-media-2", caption: "Fachada", date: "2026-10-01", url: "https://cdn.example.test/c", kind: "image", stage: "Reboco", zone: "Frente" },
      { id: "capture-media-3", caption: "Voo", date: "2026-10-01", url: "", kind: "video", stage: undefined, zone: undefined },
    ]);
    expect(p.downloads).toEqual([
      { name: "Nuvem", date: "2026-10-01", url: "https://cdn.example.test/c" },
      { name: "Nuvem sem link", date: "2026-10-01", url: undefined },
    ]);
    expect(p.pagination!.captures).toBe(7);
  });

  it("a 360° capture without zone has an empty floor and all captures read stop the pagination", () => {
    const p = adaptPortal(bootstrap(["realityCapture"]), {
      captures: {
        items: [{ type: "Photo360", capturedAt: "2026-10-01", title: "Quarto", contentType: "image/jpeg", url: "https://cdn.example.test/q.jpg" }],
        total: 1,
        skip: 0,
        take: 24,
      },
    });
    expect(p.captures![0].floor).toBe("");
    expect(p.pagination!.captures).toBeUndefined();
    expect(adaptPortal(bootstrap([]), { captures: { items: [], total: 0, skip: 0, take: 24 } }).captures).toBeUndefined();
  });

  it("schedules the refresh by the earliest signed link found anywhere in the resources", () => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-11-13T12:00:00Z"));
    const now = Date.now();
    const p = adaptPortal(bootstrap(["postConstruction", "documents"]), {
      documents: {
        items: [
          {
            title: "Alvará",
            category: "Licenças",
            fileName: "a.pdf",
            contentType: "application/pdf",
            sizeBytes: 1,
            uploadedAt: "2026-01-01",
            urlExpiresAt: new Date(now + 10 * 60000).toISOString(),
          },
        ],
      },
      handover: {
        status: "Scheduled",
        keysDelivered: false,
        canAccept: false,
        documents: [],
        documentUrlsExpireAt: new Date(now + 5 * 60000).toISOString(),
      },
      twin: twin({ viewerExpiresAt: "não é data" }),
    });
    expect(Date.parse(p.refreshAt!)).toBe(now + 5 * 60000 - 30000);
  });

  it("keeps the section errors it was given", () => {
    const errors = [{ section: "photos", message: "Fotos indisponíveis" }];
    expect(adaptPortal(bootstrap(["photos"]), {}, errors).sectionErrors).toBe(errors);
    expect(adaptPortal(bootstrap([]), {}).sectionErrors).toEqual([]);
  });
});
