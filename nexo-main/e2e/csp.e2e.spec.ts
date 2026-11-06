import { randomInt, randomUUID } from "node:crypto";
import { test as base, expect, type APIRequestContext, type BrowserContext, type Page } from "@playwright/test";

/**
 * The Content-Security-Policy of the app, as the production server really sends it (playwright.csp.config.ts). Two things are
 * proved here, and neither is enough without the other:
 *  - no real flow of the product produces a violation. A test fails on anything the browser raises in the page or reports to the
 *    API, and the API's report is the only signal that also sees inside a Web Worker;
 *  - the policy has teeth: an injected script, an eval or a request to an unknown host do produce a violation, the report reaches
 *    the endpoint, and each source the policy allows is needed by a flow (take it out and that flow is reported).
 *
 * Everything runs against the real API on a disposable PostgreSQL. The only thing answered here is the object store and the CEP
 * service, which are other companies' hosts: the browser's request still goes through the policy.
 */

const APP_ORIGIN = "http://127.0.0.1:5197";
const HOST_ORIGIN = "http://127.0.0.1:5196";
const API = `${HOST_ORIGIN}/api`;
const STORAGE_HOSTS = /^https:\/\/orken-e2e\.r2\.(dev|cloudflarestorage\.com)\/([^?]*)/;
const POLICY_HEADER = "content-security-policy-report-only";

interface Violation { directive: string; blocked: string; disposition: string; document: string }

// ── Test files: small, real, built here so nothing binary lives in the repository ───────────────────────────────────────

const PNG = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN1sAAAAASUVORK5CYII=", "base64");
const PDF = Buffer.from("%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
  "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");

/** The first bytes of an MP4: the server recognizes the file by them. A <video> asks for it through media-src all the same. */
const MP4 = Buffer.concat([Buffer.from([0, 0, 0, 24]), Buffer.from("ftypmp42"), Buffer.alloc(32)]);

/** One wall with real geometry: what web-ifc turns into a mesh inside the worker, and the server recognizes by its GlobalId. */
const IFC = Buffer.from(`ISO-10303-21;
HEADER;
FILE_DESCRIPTION(('ViewDefinition [CoordinationView]'),'2;1');
FILE_NAME('parede.ifc','2026-01-01T00:00:00',('Teste'),('Teste'),'Teste','Teste','');
FILE_SCHEMA(('IFC4'));
ENDSEC;
DATA;
#1=IFCPERSON($,$,'Teste',$,$,$,$,$);
#2=IFCORGANIZATION($,'Teste',$,$,$);
#3=IFCPERSONANDORGANIZATION(#1,#2,$);
#4=IFCAPPLICATION(#2,'1.0','Teste','Teste');
#5=IFCOWNERHISTORY(#3,#4,$,.ADDED.,1700000000,#3,#4,1700000000);
#6=IFCDIRECTION((0.,0.,1.));
#7=IFCDIRECTION((1.,0.,0.));
#8=IFCCARTESIANPOINT((0.,0.,0.));
#9=IFCAXIS2PLACEMENT3D(#8,#6,#7);
#10=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05,#9,$);
#11=IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.);
#12=IFCUNITASSIGNMENT((#11));
#13=IFCPROJECT('0YvctVUKr0kugbFTf53O9L',#5,'Projeto',$,$,$,$,(#10),#12);
#14=IFCLOCALPLACEMENT($,#9);
#15=IFCSITE('1YvctVUKr0kugbFTf53O9L',#5,'Terreno',$,$,#14,$,$,.ELEMENT.,$,$,$,$,$);
#16=IFCBUILDING('2YvctVUKr0kugbFTf53O9L',#5,'Edificio',$,$,#14,$,$,.ELEMENT.,$,$,$);
#17=IFCBUILDINGSTOREY('3YvctVUKr0kugbFTf53O9L',#5,'Terreo',$,$,#14,$,$,.ELEMENT.,0.);
#18=IFCRELAGGREGATES('4YvctVUKr0kugbFTf53O9L',#5,$,$,#13,(#15));
#19=IFCRELAGGREGATES('5YvctVUKr0kugbFTf53O9L',#5,$,$,#15,(#16));
#20=IFCRELAGGREGATES('6YvctVUKr0kugbFTf53O9L',#5,$,$,#16,(#17));
#21=IFCCARTESIANPOINT((0.,0.));
#22=IFCAXIS2PLACEMENT2D(#21,$);
#23=IFCRECTANGLEPROFILEDEF(.AREA.,$,#22,4.,0.2);
#24=IFCEXTRUDEDAREASOLID(#23,#9,#6,3.);
#25=IFCSHAPEREPRESENTATION(#10,'Body','SweptSolid',(#24));
#26=IFCPRODUCTDEFINITIONSHAPE($,$,(#25));
#27=IFCWALL('7YvctVUKr0kugbFTf53O9L',#5,'Parede',$,$,#14,#26,$,$);
#28=IFCRELCONTAINEDINSPATIALSTRUCTURE('8YvctVUKr0kugbFTf53O9L',#5,$,$,(#27),#17);
ENDSEC;
END-ISO-10303-21;
`);

/** A textured quad: GLTFLoader hands an embedded image to the page through a blob: address. */
function texturedGlb(): Buffer {
  const positions = Buffer.from(new Float32Array([-1, -1, 0, 1, -1, 0, 1, 1, 0, -1, 1, 0]).buffer);
  const normals = Buffer.from(new Float32Array([0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1]).buffer);
  const uvs = Buffer.from(new Float32Array([0, 0, 1, 0, 1, 1, 0, 1]).buffer);
  const indices = Buffer.from(new Uint16Array([0, 1, 2, 0, 2, 3]).buffer);
  const pad = (b: Buffer, fill = 0) => Buffer.concat([b, Buffer.alloc((4 - (b.length % 4)) % 4, fill)]);
  const parts = [positions, normals, uvs, pad(indices), pad(PNG)];
  let offset = 0;
  const bufferViews = parts.map((p, i) => {
    const view = { buffer: 0, byteOffset: offset, byteLength: i === 3 ? indices.length : i === 4 ? PNG.length : p.length };
    offset += p.length;
    return view;
  });
  const json = pad(Buffer.from(JSON.stringify({
    asset: { version: "2.0" },
    scene: 0,
    scenes: [{ nodes: [0] }],
    nodes: [{ name: "ELEMENTO-1", mesh: 0, extras: { category: "IfcWall" } }],
    meshes: [{ primitives: [{ attributes: { POSITION: 0, NORMAL: 1, TEXCOORD_0: 2 }, indices: 3, material: 0 }] }],
    materials: [{ pbrMetallicRoughness: { baseColorTexture: { index: 0 } } }],
    textures: [{ source: 0 }],
    images: [{ bufferView: 4, mimeType: "image/png" }],
    accessors: [
      { bufferView: 0, componentType: 5126, count: 4, type: "VEC3", min: [-1, -1, 0], max: [1, 1, 0] },
      { bufferView: 1, componentType: 5126, count: 4, type: "VEC3" },
      { bufferView: 2, componentType: 5126, count: 4, type: "VEC2" },
      { bufferView: 3, componentType: 5123, count: 6, type: "SCALAR" },
    ],
    bufferViews,
    buffers: [{ byteLength: offset }],
  })), 0x20);
  const bin = Buffer.concat(parts);
  const header = Buffer.alloc(12);
  header.writeUInt32LE(0x46546c67, 0);
  header.writeUInt32LE(2, 4);
  header.writeUInt32LE(12 + 8 + json.length + 8 + bin.length, 8);
  const chunk = (type: number, data: Buffer) => {
    const head = Buffer.alloc(8);
    head.writeUInt32LE(data.length, 0);
    head.writeUInt32LE(type, 4);
    return Buffer.concat([head, data]);
  };
  return Buffer.concat([header, chunk(0x4e4f534a, json), chunk(0x004e4942, bin)]);
}

// ── World: one company, created through the API, shared by the suite ─────────────────────────────────────────────────────

const clientAddress = () => `10.${randomInt(256)}.${randomInt(256)}.${randomInt(1, 255)}`;
const unique = (prefix: string) => `${prefix}-${randomUUID().replaceAll("-", "").slice(0, 8)}`;
const WEEK = JSON.stringify([0, 1, 2, 3, 4, 5, 6].map((weekday) => ({ weekday, windows: [{ start: "06:00", end: "22:00" }] })));
const BUILD_VISIBILITY = {
  overallProgress: true, stageProgress: true, schedule: true, forecastDelivery: true, clientUpdates: true, dailyLogs: true, photos: true,
  financialSummary: false, postConstruction: true, changeOrders: true, selections: true, documents: true, model3d: true, realityCapture: true,
};

interface World {
  admin: { login: string; password: string };
  publicSlug: string;
  productId: string;
  orderId: string;
  companySlug: string;
  portalSlug: string;
  sheetProject: string;
  captureProject: string;
}

let world: World;

class Api {
  constructor(private readonly http: APIRequestContext, private readonly bearer: string) {}

  private async read(what: string, response: Awaited<ReturnType<APIRequestContext["get"]>>) {
    expect(response.ok(), `${what}: ${response.status()} ${await response.text()}`).toBe(true);
    const text = await response.text();
    return text ? JSON.parse(text) : undefined;
  }

  private get headers() { return { Authorization: this.bearer, "X-Forwarded-For": "10.0.0.1" }; }
  get = async (path: string) => this.read(path, await this.http.get(`${API}${path}`, { headers: this.headers }));
  post = async (path: string, data?: unknown) => this.read(path, await this.http.post(`${API}${path}`, { headers: this.headers, data }));
  put = async (path: string, data?: unknown) => this.read(path, await this.http.put(`${API}${path}`, { headers: this.headers, data }));
  patch = async (path: string, data?: unknown) => this.read(path, await this.http.patch(`${API}${path}`, { headers: this.headers, data }));

  upload = async (path: string, name: string, buffer: Buffer, fields: Record<string, string> = {}, mimeType = "application/octet-stream") =>
    this.read(path, await this.http.post(`${API}${path}`, {
      headers: this.headers,
      multipart: { ...fields, file: { name, mimeType, buffer } },
    }));
}

const claimsOf = (token: string) => JSON.parse(Buffer.from(token.split(".")[1], "base64url").toString());

async function signInToApi(http: APIRequestContext, login: string, password: string): Promise<string> {
  const response = await http.post(`${API}/auth/login`, { headers: { "X-Forwarded-For": clientAddress() }, data: { login, password } });
  expect(response.ok(), await response.text()).toBe(true);
  return (await response.json()).accessToken;
}

/** A project with an IFC ready and no viewer: a conversion is one-shot, because the converted version has its viewer. */
async function createIfcProject(http: APIRequestContext): Promise<string> {
  const api = new Api(http, `Bearer ${await signInToApi(http, world.admin.login, world.admin.password)}`);
  const project = await api.post("/v1/build/projects", { name: "Obra com IFC", clientName: "Cliente de teste", type: "House", startDate: "2026-01-05", expectedEndDate: "2026-12-20" });
  await api.upload(`/v1/build/projects/${project.id}/models`, "parede.ifc", IFC, { name: "Estrutura", discipline: "Arquitetura" });
  expect((await http.post(`${HOST_ORIGIN}/__test/build/process-models`)).ok()).toBe(true);
  return project.id;
}

async function createWorld(http: APIRequestContext): Promise<World> {
  const setup = await (await http.get(`${HOST_ORIGIN}/__test/setup`)).json();
  const accessToken = await signInToApi(http, setup.adminLogin, setup.adminPassword);
  const { tenantId, storeId } = claimsOf(accessToken);
  const api = new Api(http, `Bearer ${accessToken}`);

  // The modules this company does not have by default, granted the way the platform grants them.
  const platformLogin = await http.post(`${API}/platform/auth/login`, { headers: { "X-Forwarded-For": clientAddress() }, data: { email: setup.platformEmail, password: setup.platformPassword } });
  expect(platformLogin.ok(), await platformLogin.text()).toBe(true);
  const platform = new Api(http, `Bearer ${(await platformLogin.json()).accessToken}`);
  for (const moduleKey of ["restaurante", "service"]) await platform.post(`/platform/tenants/${tenantId}/modules`, { moduleKey });

  // One store: its public menu and its booking page.
  const publicSlug = unique("loja");
  await api.patch(`/stores/${storeId}/public-slug`, { publicSlug });
  const image = await api.upload("/integrations/storage/upload", "produto.png", PNG, { context: "product-image" }, "image/png");
  const code = unique("PROD");
  const product = await api.post("/products", { code, name: `Produto ${code}`, unit: "Un", salePrice: 25, costPrice: 5, trackStock: false, menuVisible: true });
  await api.patch(`/products/${product.id}/image`, { imageUrl: image.publicUrl });
  const order = await api.post("/restaurante/orders", { orderType: "Counter" });
  await api.post(`/restaurante/orders/${order.id}/items`, { productId: product.id, quantity: 1 });
  await api.put("/v1/service/settings/preset", { presetKey: "salao-beleza" });
  await api.put("/v1/service/settings/public-booking", {
    publicBookingEnabled: true, bookingDaysAhead: 14, minLeadMinutes: 30, slotIntervalMinutes: 60, showPrices: true, autoConfirmAppointments: false, timeZoneId: "UTC",
  });
  await api.post("/v1/service/professionals", { name: `Profissional ${code}`, workingHoursJson: WEEK });
  await api.post("/v1/service/catalog", { name: `Servico ${code}`, durationMinutes: 60, price: 80 });

  // Build: a published portal with a 3D model and a photo, and projects for the conversion, the floor plan and the captures.
  const companySlug = unique("construtora");
  await api.put("/v1/build/portal/company", { slug: companySlug, displayName: "Construtora de teste" });
  const project = async (name: string) => {
    const created = await api.post("/v1/build/projects", { name, clientName: "Cliente de teste", type: "House", startDate: "2026-01-05", expectedEndDate: "2026-12-20" });
    return created.id as string;
  };
  const portalSlug = unique("obra");
  const portalProject = await project("Obra com portal");
  const stage = await api.post(`/v1/build/projects/${portalProject}/stages`, { name: "Alvenaria", plannedStart: "2026-01-05", plannedEnd: "2026-09-30", weight: 1 });
  await api.put(`/v1/build/stages/${stage.id}/progress`, { progressPercent: 40 });
  const glb = await api.upload(`/v1/build/projects/${portalProject}/models`, "edificio.glb", texturedGlb(), { name: "Arquitetura", discipline: "Arquitetura" });
  const capture = await api.upload(`/v1/build/projects/${portalProject}/reality-captures`, "fachada.png", PNG, { type: "Photo", title: "Fachada" });
  await api.put(`/v1/build/reality-captures/${capture.id}/client-visibility`, { clientVisible: true });
  const sheetProject = await project("Obra com planta");
  await api.upload(`/v1/build/projects/${sheetProject}/models`, "planta.pdf", PDF, { name: "Planta", discipline: "Arquitetura" });
  const captureProject = await project("Obra com captura");
  await api.upload(`/v1/build/projects/${captureProject}/reality-captures`, "panorama.png", PNG, { type: "Photo360", title: "Panorama" });
  await api.upload(`/v1/build/projects/${captureProject}/reality-captures`, "visita.mp4", MP4, { type: "Video", title: "Visita" });
  expect((await http.post(`${HOST_ORIGIN}/__test/build/process-models`)).ok()).toBe(true);
  expect((await api.get(`/v1/build/models/versions/${glb.version.id}`)).status).toBe("Ready");
  await api.put(`/v1/build/projects/${portalProject}/portal`, { slug: portalSlug, access: "Public", title: "Acompanhe sua obra", welcomeMessage: "Registros de teste.", visibility: BUILD_VISIBILITY });
  await api.post(`/v1/build/projects/${portalProject}/portal/publish`);

  return {
    admin: { login: setup.adminLogin, password: setup.adminPassword },
    publicSlug, productId: product.id, orderId: order.id, companySlug, portalSlug, sheetProject, captureProject,
  };
}

// ── Fixtures: every test ends proving what arrived ───────────────────────────────────────────────────────────────────────

const reportLines = async (http: APIRequestContext) =>
  (await (await http.get(`${HOST_ORIGIN}/__test/csp-reports`)).text()).split(/\r?\n/).filter(Boolean);

/**
 * Reports are sent in the background. This one is sent last and is awaited, so what was sent before it has arrived by then.
 * That is also the proof, in every test, that reporting works: a silent endpoint could not pass as a clean flow.
 */
async function waitForReports(page: Page, http: APIRequestContext): Promise<string> {
  const marker = unique("marcador");
  await page.evaluate((host) => { new Image().src = `https://${host}.invalid/x.png`; }, marker);
  await expect.poll(async () => (await reportLines(http)).some((l) => l.includes(marker)), { timeout: 20_000 }).toBe(true);
  return marker;
}

const test = base.extend<{
  http: APIRequestContext;
  violations: Violation[];
}>({
  http: async ({ playwright }, provide) => {
    const http = await playwright.request.newContext();
    await provide(http);
    await http.dispose();
  },
  context: async ({ context }, provide) => {
    await context.setExtraHTTPHeaders({ "X-Forwarded-For": clientAddress() });
    // The object store and the CEP service answer from here. The page's request has already passed the policy by this point.
    await context.route(STORAGE_HOSTS, async (route) => {
      const key = STORAGE_HOSTS.exec(route.request().url())![2];
      const response = await route.fetch({ url: `${HOST_ORIGIN}/__test/media/${key}`, maxRedirects: 0 });
      await route.fulfill({
        status: response.status(),
        body: await response.body(),
        headers: { "access-control-allow-origin": "*", "content-type": response.headers()["content-type"] ?? "application/octet-stream" },
      });
    });
    await context.route("https://viacep.com.br/**", (route) => route.fulfill({
      json: { cep: "60000-000", logradouro: "Rua de teste", bairro: "Centro", localidade: "Fortaleza", uf: "CE" },
      headers: { "access-control-allow-origin": "*" },
    }));
    await provide(context);
  },
  violations: async ({ context }, provide) => {
    const seen: Violation[] = [];
    await context.exposeBinding("__cspViolation", (_source, v: Violation) => { seen.push(v); });
    await context.addInitScript(() => {
      document.addEventListener("securitypolicyviolation", (e) => {
        (window as unknown as { __cspViolation(v: Violation): void }).__cspViolation({
          directive: e.effectiveDirective, blocked: e.blockedURI, disposition: e.disposition, document: e.documentURI,
        });
      });
    });
    await provide(seen);
  },
});

/** The tests that use the company declare it: the checks of the header need nothing but the server. */
function useWorld(): void {
  test.beforeAll(async ({ playwright }) => {
    test.setTimeout(240_000);
    if (world) return;
    const http = await playwright.request.newContext();
    world = await createWorld(http);
    await http.dispose();
  });
}

/** Runs a flow and proves that nothing was raised in the page and nothing was reported to the API. */
async function expectNoViolation(page: Page, http: APIRequestContext, violations: Violation[], flow: () => Promise<void>): Promise<void> {
  const before = (await reportLines(http)).length;
  await flow();
  const marker = await waitForReports(page, http);
  expect(violations.filter((v) => !v.blocked.includes(marker)), "violations raised in the page").toEqual([]);
  const after = (await reportLines(http)).slice(before).filter((l) => !l.includes(marker));
  expect(after, "reports received by the API").toEqual([]);
}

/**
 * Replaces the policy of what can have one: the pages and the worker's script. For the proofs that a source is needed, and only
 * there. Other files carry the header too, and the browser does not read it. A page answered by the test instead of the server has
 * no address of its own, and without the permission Chromium refuses its calls and its reports to 127.0.0.1.
 */
async function editPolicy(context: BrowserContext, edit: (policy: string) => string): Promise<void> {
  await context.grantPermissions(["local-network-access"]);
  const ownsAPolicy = (url: URL) => url.origin === APP_ORIGIN && (!url.pathname.startsWith("/assets/") || url.pathname.includes(".worker"));
  await context.route(ownsAPolicy, async (route) => {
    const response = await route.fetch();
    const headers = response.headers();
    await route.fulfill({ response, headers: { ...headers, [POLICY_HEADER]: edit(headers[POLICY_HEADER] ?? "") } });
  });
}

const without = (directive: string, source: string) => (policy: string) => {
  const edited = policy.replace(new RegExp(`(${directive}[^;]*?) ${source.replace(/[*.]/g, "\\$&")}(?=[ ;])`), "$1");
  expect(edited, `${directive} did not have ${source}`).not.toBe(policy);
  return edited;
};

// ── Flows ────────────────────────────────────────────────────────────────────────────────────────────────────────────────

async function signIn(page: Page): Promise<void> {
  await page.goto("/login");
  await page.getByLabel("Login ou e-mail").fill(world.admin.login);
  await page.getByLabel("Senha", { exact: true }).fill(world.admin.password);
  await page.getByRole("button", { name: "Entrar", exact: true }).click();
  await expect(page).not.toHaveURL(/\/login/);
}

async function openMfaQrCode(page: Page): Promise<void> {
  await signIn(page);
  await page.goto("/perfil");
  await page.getByRole("button", { name: "Ativar verificação em duas etapas" }).click();
  await page.locator("#mfa-setup-password").fill(world.admin.password);
  await page.getByRole("button", { name: "Continuar", exact: true }).click();
  await expect(page.getByAltText("QR code para o aplicativo autenticador")).toBeVisible();
}

async function openTwinTab(page: Page, project: string): Promise<void> {
  await page.goto(`/build/projetos/${project}`);
  await page.getByRole("tab", { name: "Gêmeo digital" }).or(page.getByRole("button", { name: "Gêmeo digital" })).first().click();
}

/** IFC in a Web Worker with WebAssembly, the GLB accepted by the server, and the 3D viewer drawing it. */
async function convertIfcInTheBrowser(page: Page, context: BrowserContext, http: APIRequestContext): Promise<void> {
  const project = await createIfcProject(http);
  const wasm: string[] = [];
  context.on("response", (r) => { if (r.url().endsWith(".wasm")) wasm.push(`${r.status()} ${r.headers()["content-type"]}`); });
  await signIn(page);
  await openTwinTab(page, project);
  await page.getByRole("button", { name: "Gerar visualização 3D no navegador" }).click();
  await expect(page.getByText(/Visualização 3D gerada \(1 elementos\)/)).toBeVisible({ timeout: 60_000 });
  expect(wasm, "the wasm file was really fetched").toEqual(["200 application/wasm"]);
  await expect(page.locator("canvas").first()).toBeVisible();
}

async function openPdfOnTheFloorPlan(page: Page): Promise<void> {
  await signIn(page);
  await openTwinTab(page, world.sheetProject);
  await page.getByRole("tab", { name: "Zonas e planta" }).click();
  await page.locator("#sheet-file").setInputFiles({ name: "planta.pdf", mimeType: "application/pdf", buffer: PDF });
  await expect(page.locator('object[type="application/pdf"]')).toBeVisible();
  // the viewer's own document opens inside: give it the chance to ask for anything
  await expect.poll(() => page.frames().length, { timeout: 10_000 }).toBeGreaterThan(1);
}

async function viewPortalModel(page: Page): Promise<void> {
  await page.goto(`/${world.companySlug}/${world.portalSlug}`);
  await page.getByRole("button", { name: "Explorar a obra em 3D" }).click();
  await expect(page.getByText("Carregando modelo…")).toHaveCount(0, { timeout: 30_000 });
  await expect(page.getByRole("alert")).toHaveCount(0);
  await expect(page.locator(".mb-viewer canvas")).toBeVisible();
}

// ── The header ───────────────────────────────────────────────────────────────────────────────────────────────────────────

test.describe("what the production server sends", () => {
  const directives = (policy: string) => new Map(policy.split(";").map((d) => d.trim()).filter(Boolean).map((d) => {
    const [name, ...sources] = d.split(/\s+/);
    return [name, sources] as const;
  }));

  test("the full policy is report-only and the enforced one did not change", async ({ http }) => {
    for (const path of ["/login", "/dashboard", "/agendar/x", `/assets/${"naoexiste"}.js`]) {
      const response = await http.get(`${APP_ORIGIN}${path}`);
      const headers = response.headers();
      expect(headers["content-security-policy"], path).toBe("frame-ancestors 'none'");
      expect(headers[POLICY_HEADER], path).toBeTruthy();
      expect(headers["x-frame-options"]).toBe("DENY");
      expect(headers["x-content-type-options"]).toBe("nosniff");
      expect(headers["referrer-policy"]).toBe("strict-origin-when-cross-origin");
      expect(headers["permissions-policy"]).toBe("camera=(), microphone=(), usb=(), geolocation=(self)");
    }
  });

  test("every directive is declared, with no eval, no inline script and no open wildcard", async ({ http }) => {
    const policy = directives((await http.get(`${APP_ORIGIN}/login`)).headers()[POLICY_HEADER]);

    for (const name of ["default-src", "script-src", "style-src", "img-src", "font-src", "connect-src", "worker-src", "frame-src",
      "object-src", "media-src", "base-uri", "form-action", "frame-ancestors", "manifest-src", "report-uri"])
      expect(policy.has(name), name).toBe(true);

    expect(policy.get("default-src")).toEqual(["'none'"]);
    expect(policy.get("script-src")).toEqual(["'self'", "'wasm-unsafe-eval'"]);
    expect(policy.get("object-src")).toEqual(["blob:"]);
    expect(policy.get("frame-src")).toEqual(["blob:"]);
    expect(policy.get("worker-src")).toEqual(["'self'"]);
    expect(policy.get("frame-ancestors")).toEqual(["'none'"]);
    for (const [name, sources] of policy) {
      expect(sources, name).not.toContain("'unsafe-eval'");
      expect(sources, name).not.toContain("*");
      expect(sources, name).not.toContain("https:");
    }
    expect(policy.get("report-uri")).toEqual([`${API}/csp-reports`]);
  });

});

// ── Real flows: nothing raised, nothing reported ─────────────────────────────────────────────────────────────────────────

test.describe("no violation in the real flows", () => {
  useWorld();

  test("the sign-in page, with an error and with the form's own notice", async ({ page, http, violations }) => {
    await expectNoViolation(page, http, violations, async () => {
      await page.goto("/login?motivo=sessao-encerrada");
      await page.getByLabel("Login ou e-mail").fill("ninguem");
      await page.getByLabel("Senha", { exact: true }).fill(unique("errada"));
      await page.getByRole("button", { name: "Entrar", exact: true }).click();
      await expect(page.getByRole("alert").first()).toBeVisible();
    });
  });

  test("the dashboard and its chart", async ({ page, context, http, violations }) => {
    // The server's own answer, with revenue on one day: the chart only draws when there are sales, and this company has none.
    await context.route(`${API}/dashboard/summary`, async (route) => {
      const response = await route.fetch();
      const summary = await response.json();
      summary.salesByDay.at(-1).revenue = 100;
      await route.fulfill({ response, json: summary });
    });
    await expectNoViolation(page, http, violations, async () => {
      await signIn(page);
      await page.goto("/dashboard");
      await expect(page.locator(".recharts-surface").first()).toBeVisible();
    });
  });

  test("two-step verification shows its QR code", async ({ page, http, violations }) => {
    await expectNoViolation(page, http, violations, () => openMfaQrCode(page));
  });

  test("the public menu with a product image, and the cart looking up a CEP", async ({ page, http, violations }) => {
    await expectNoViolation(page, http, violations, async () => {
      await page.goto(`/${world.publicSlug}`);
      const image = page.locator('img[src*="orken-e2e.r2.dev"]').first();
      await expect(image).toBeVisible();
      await expect.poll(() => image.evaluate((img: HTMLImageElement) => img.naturalWidth)).toBeGreaterThan(0);
      await image.click();
      await page.getByRole("button", { name: /^Adicionar/ }).click();
      await page.getByRole("button", { name: /Ver pedido/ }).click();
      const lookup = page.waitForResponse((r) => r.url().startsWith("https://viacep.com.br/"));
      await page.getByPlaceholder("CEP *").fill("60000000");
      expect((await lookup).status()).toBe(200);
      await expect(page.getByText(/Bairro "Centro"/)).toBeVisible();
    });
  });

  test("the booking page", async ({ page, http, violations }) => {
    await expectNoViolation(page, http, violations, async () => {
      await page.goto(`/agendar/${world.publicSlug}`);
      await expect(page.getByRole("heading").first()).toBeVisible();
      await expect(page.getByText(/Servico PROD-/).first()).toBeVisible();
    });
  });

  test("a page of the Build portal, its photo and the 3D viewer with a textured GLB", async ({ page, context, http, violations }) => {
    // The viewer of a ready model only loads a GLB: WebAssembly exists for the conversion, and it does not start by itself.
    const wasm: string[] = [];
    context.on("request", (r) => { if (r.url().endsWith(".wasm")) wasm.push(r.url()); });
    await expectNoViolation(page, http, violations, () => viewPortalModel(page));
    expect(wasm).toEqual([]);
  });

  test("an IFC converted in the browser: worker, WebAssembly, GLB export, upload and viewer", async ({ page, context, http, violations }) => {
    await expectNoViolation(page, http, violations, () => convertIfcInTheBrowser(page, context, http));
  });

  test("a PDF in an <object> on the floor plan screen", async ({ page, http, violations }) => {
    await expectNoViolation(page, http, violations, () => openPdfOnTheFloorPlan(page));
  });

  test("an image of the floor plan opened from the computer", async ({ page, http, violations }) => {
    await expectNoViolation(page, http, violations, async () => {
      await signIn(page);
      await openTwinTab(page, world.sheetProject);
      await page.getByRole("tab", { name: "Zonas e planta" }).click();
      await page.locator("#sheet-file").setInputFiles({ name: "planta.png", mimeType: "image/png", buffer: PNG });
      await expect(page.getByAltText("Planta da obra")).toBeVisible();
      await expect.poll(() => page.getByAltText("Planta da obra").evaluate((img: HTMLImageElement) => img.naturalWidth)).toBeGreaterThan(0);
    });
  });

  test("a PDF made by the API, handed over through a blob: link", async ({ page, http, violations }) => {
    await expectNoViolation(page, http, violations, async () => {
      await signIn(page);
      await page.goto(`/produtos/${world.productId}`);
      const download = page.waitForEvent("download");
      await page.getByRole("button", { name: "Baixar ficha" }).click();
      expect((await download).suggestedFilename()).toBe("ficha-produto.pdf");
    });
  });

  test("a captured video and a 360 photo opened in its viewer", async ({ page, http, violations }) => {
    await expectNoViolation(page, http, violations, async () => {
      await signIn(page);
      await openTwinTab(page, world.captureProject);
      await page.getByRole("tab", { name: "Captura da realidade" }).click();
      await expect(page.locator('video[src*="orken-e2e.r2.cloudflarestorage.com"]')).toBeVisible();
      await page.getByRole("button", { name: /Abrir 360/ }).click();
      await expect(page.locator("canvas").first()).toBeVisible();
    });
  });

  test("the kitchen connects to the hub by WebSocket", async ({ page, http, violations }) => {
    await expectNoViolation(page, http, violations, async () => {
      const socket = page.waitForEvent("websocket", (ws) => ws.url().includes("/hubs/restaurant"));
      await signIn(page);
      await page.goto("/restaurante/cozinha");
      const ws = await socket;
      expect(ws.url().startsWith("ws://127.0.0.1:5196/hubs/restaurant")).toBe(true);
      await ws.waitForEvent("framereceived");
      await expect(page.getByText("Tempo real")).toBeVisible();
    });
  });

  test("a ticket printed from an order opens in a window of its own", async ({ page, context, http, violations }) => {
    await expectNoViolation(page, http, violations, async () => {
      await signIn(page);
      await page.goto(`/restaurante/comanda/${world.orderId}`);
      const popup = context.waitForEvent("page");
      await page.getByTitle("Imprimir cozinha").click();
      // A window opened with no address takes the policy of the page that opened it: this is the <style> and the style= of the ticket.
      await expect.poll(async () => (await popup).evaluate(() => document.querySelector("h1")?.textContent)).toBe("*** COZINHA ***");
    });
  });
});

// ── Teeth ────────────────────────────────────────────────────────────────────────────────────────────────────────────────

test.describe("the policy has teeth", () => {
  async function expectReported(page: Page, http: APIRequestContext, violations: Violation[], directive: string, blocked: string | RegExp, act: () => Promise<unknown>) {
    const before = (await reportLines(http)).length;
    await act();
    await expect.poll(() => violations.filter((v) => v.directive === directive && (typeof blocked === "string" ? v.blocked === blocked : blocked.test(v.blocked))).length,
      { message: `${directive} raised in the page` }).toBeGreaterThan(0);
    expect(violations.every((v) => v.disposition === "report"), "report-only: nothing is enforced").toBe(true);
    await waitForReports(page, http);
    const lines = (await reportLines(http)).slice(before).filter((l) => l.includes(directive));
    expect(lines.length, `${directive} reported to the API`).toBeGreaterThan(0);
  }

  test("an injected inline script is reported, reaches the endpoint, and still runs because nothing is enforced", async ({ page, http, violations }) => {
    await page.goto("/login");
    await expectReported(page, http, violations, "script-src-elem", "inline", async () => {
      const ran = await page.evaluate(() => {
        const script = document.createElement("script");
        script.textContent = "window.__injected = true";
        document.head.append(script);
        return (window as unknown as { __injected?: boolean }).__injected === true;
      });
      expect(ran, "report-only must not block anything").toBe(true);
    });
  });

  test("what reaches the log has no query string, fragment or token", async ({ page, http, violations }) => {
    const tracer = unique("RASTRO");
    await page.goto(`/agendar/barbearia/reserva/${tracer}?token=${tracer}#${tracer}`);
    await expectReported(page, http, violations, "script-src-elem", "inline", () => page.evaluate(() => {
      const script = document.createElement("script");
      script.textContent = "void 0";
      document.head.append(script);
    }));
    const log = (await reportLines(http)).join("\n");
    expect(log).not.toContain(tracer);
    expect(log).toContain("/agendar/*");
  });

  test("eval is reported", async ({ page, http, violations }) => {
    await page.goto("/login");
    // From a timer: the debugger protocol that runs page.evaluate lets its own code skip this check.
    await expectReported(page, http, violations, "script-src", "eval", () => page.evaluate(() => { setTimeout(() => (0, eval)("1 + 1")); }));
  });

  test("an inline event handler is reported", async ({ page, http, violations }) => {
    await page.goto("/login");
    await expectReported(page, http, violations, "script-src-attr", "inline", () => page.evaluate(() => {
      const div = document.createElement("div");
      div.setAttribute("onclick", "void 0");
      div.click();
    }));
  });

  test("a script from an unknown host is reported", async ({ page, http, violations }) => {
    await page.goto("/login");
    await expectReported(page, http, violations, "script-src-elem", /desconhecido\.invalid/, () => page.evaluate(() => {
      const script = document.createElement("script");
      script.src = "https://desconhecido.invalid/x.js";
      document.head.append(script);
    }));
  });

  test("a request to an unknown host is reported", async ({ page, http, violations }) => {
    await page.goto("/login");
    await expectReported(page, http, violations, "connect-src", /desconhecido\.invalid/, () => page.evaluate(() =>
      fetch("https://desconhecido.invalid/?dados=1").catch(() => undefined)));
  });

  test("a frame, a base address, a form to another host and a worker from a blob are reported", async ({ page, http, violations }) => {
    await page.goto("/login");
    await expectReported(page, http, violations, "frame-src", /desconhecido\.invalid/, () => page.evaluate(() => {
      const frame = document.createElement("iframe");
      frame.src = "https://desconhecido.invalid/";
      document.body.append(frame);
    }));
    await expectReported(page, http, violations, "base-uri", /desconhecido\.invalid/, () => page.evaluate(() => {
      const base = document.createElement("base");
      base.href = "https://desconhecido.invalid/";
      document.head.append(base);
      base.remove();
    }));
    await expectReported(page, http, violations, "worker-src", "blob", () => page.evaluate(() => {
      new Worker(URL.createObjectURL(new Blob(["void 0"], { type: "text/javascript" })));
    }));
    await page.goto("/login");
    await expectReported(page, http, violations, "form-action", /desconhecido\.invalid/, () => page.evaluate(() => {
      const form = document.createElement("form");
      form.action = "https://desconhecido.invalid/";
      form.target = "_blank";
      document.body.append(form);
      form.submit();
    }));
  });
});

// ── Each source is there for a reason: take it out and the flow that needs it is reported ───────────────────────────────

test.describe("each allowance is needed", () => {
  useWorld();

  /** The API received a report of this directive, for this address if one is given. The only signal that sees inside a worker. */
  async function expectFlowReported(page: Page, http: APIRequestContext, directive: string, flow: () => Promise<void>, blocked?: string) {
    const before = (await reportLines(http)).length;
    await flow();
    await waitForReports(page, http);
    const reported = (await reportLines(http)).slice(before)
      .filter((l) => l.includes(`EffectiveDirective: "${directive}"`) && (!blocked || l.includes(`BlockedOrigin: "${blocked}"`)));
    expect(reported.length, `${directive}: the flow must need this source`).toBeGreaterThan(0);
  }

  test("'wasm-unsafe-eval': the worker compiling the IFC converter", async ({ page, context, http }) => {
    await editPolicy(context, without("script-src", "'wasm-unsafe-eval'"));
    await expectFlowReported(page, http, "script-src", () => convertIfcInTheBrowser(page, context, http), "wasm-eval");
  });

  test("worker-src 'self': the worker that runs it", async ({ page, context, http }) => {
    await editPolicy(context, (policy) => policy.replace("worker-src 'self'", "worker-src 'none'"));
    await expectFlowReported(page, http, "worker-src", () => convertIfcInTheBrowser(page, context, http));
  });

  test("object-src blob:: the PDF of the floor plan", async ({ page, context, http }) => {
    await editPolicy(context, (policy) => policy.replace("object-src blob:", "object-src 'none'"));
    await expectFlowReported(page, http, "object-src", () => openPdfOnTheFloorPlan(page), "blob");
  });

  test("frame-src blob:: Chromium opens that same PDF as a frame", async ({ page, context, http }) => {
    await editPolicy(context, (policy) => policy.replace("frame-src blob:", "frame-src 'none'"));
    await expectFlowReported(page, http, "frame-src", () => openPdfOnTheFloorPlan(page), "blob");
  });

  test("connect-src blob:: the texture embedded in a GLB", async ({ page, context, http }) => {
    await editPolicy(context, without("connect-src", "blob:"));
    await expectFlowReported(page, http, "connect-src", () => viewPortalModel(page), "blob");
  });

  test("style-src 'unsafe-inline': the app shell, the toasts and the dialogs, which write <style> elements", async ({ page, context, http }) => {
    await editPolicy(context, without("style-src", "'unsafe-inline'"));
    await expectFlowReported(page, http, "style-src-elem", async () => {
      await page.goto("/login");
      await expect(page.getByLabel("Login ou e-mail")).toBeVisible();
    }, "inline");
  });

  test("media-src: the captured video", async ({ page, context, http }) => {
    await editPolicy(context, (policy) => policy.replace(/media-src [^;]*/, "media-src 'none'"));
    await expectFlowReported(page, http, "media-src", async () => {
      await signIn(page);
      await openTwinTab(page, world.captureProject);
      await page.getByRole("tab", { name: "Captura da realidade" }).click();
      await expect(page.locator("video").first()).toBeVisible();
    });
  });

  test("img-src data:: the QR code of two-step verification", async ({ page, context, http }) => {
    await editPolicy(context, without("img-src", "data:"));
    await expectFlowReported(page, http, "img-src", () => openMfaQrCode(page), "data");
  });
});
