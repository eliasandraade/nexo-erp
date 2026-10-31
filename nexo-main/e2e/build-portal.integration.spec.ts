import { test, expect, type Page, type Locator } from "@playwright/test";

/**
 * "Minha Obra" against the real Orken Build API (e2e/backend-host: real controllers, services, CORS policy and a disposable
 * PostgreSQL). Nothing here intercepts the API. The only route below emulates the object store behind the signed URLs
 * (storage.test), serving the bytes the API stored and refusing signatures the host revoked — like an expired S3 link.
 * Needs a freshly started host: decisions are one-shot.
 */
type Setup = {
  company: string;
  otherCompany: string;
  password: string;
  otherPassword: string;
  otherChangeOrder: string;
};
let setup: Setup;
const api = "http://127.0.0.1:5194";
const portalApi = (company: string, project: string) =>
  `${api}/api/public/build/${company}/${project}`;
// Generated per run: a literal password in a test trips the secret scanner and teaches nothing.
const wrongPassword = () => "w" + Math.random().toString(36).slice(2, 12);

test.beforeAll(async ({ request }) => {
  const r = await request.get(api + "/__test/setup");
  expect(r.ok()).toBe(true);
  setup = await r.json();
});

const media = new Map<Page, string[]>();
test.beforeEach(async ({ page, request }) => {
  const seen: string[] = [];
  media.set(page, seen);
  await page.route("https://storage.test/**", async (route) => {
    const url = new URL(route.request().url());
    seen.push(url.pathname + url.search);
    const response = await request.get(
      api + "/__test/media" + url.pathname + url.search,
    );
    await route.fulfill({
      status: response.status(),
      body: await response.body(),
      headers: {
        "access-control-allow-origin": "*",
        "content-type":
          response.headers()["content-type"] ?? "application/octet-stream",
      },
    });
  });
});

async function login(page: Page, project = "obra-privada", password?: string) {
  await page.goto(`/${setup.company}/${project}`);
  await page.getByLabel("Senha de acesso").fill(password ?? setup.password);
  await page.getByRole("button", { name: "Acessar minha obra" }).click();
  await expect(page.getByRole("heading", { level: 1 })).toContainText(
    /teste local/,
  );
}
const noHorizontalScroll = (page: Page) =>
  page.evaluate(() => document.documentElement.scrollWidth <= innerWidth);
const formWith = (page: Page, button: string) =>
  page
    .locator("form")
    .filter({ has: page.getByRole("button", { name: button, exact: true }) });
const article = (page: Page, text: string | RegExp): Locator =>
  page.locator("article").filter({ hasText: text });

for (const width of [390, 430, 1440])
  test(`public portal at ${width}px: progress, timeline, photos, finance hidden`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 900 });
    const authorization: string[] = [];
    page.on("request", (r) => {
      if (r.url().includes("/api/public/build/"))
        authorization.push(r.headers().authorization ?? "");
    });
    await page.goto(`/${setup.company}/obra-publica`);
    await expect(page.getByRole("heading", { level: 1 })).toContainText(
      "Residência",
    );
    await expect(page.getByText("40%", { exact: true }).first()).toBeVisible();
    await expect(
      page.getByRole("heading", { name: "Fundação em andamento" }).first(),
    ).toBeVisible();
    const photo = page.getByRole("img", {
      name: "Evidência sintética de teste",
    });
    await photo.scrollIntoViewIfNeeded();
    await expect(photo).toBeVisible();
    await expect
      .poll(() => photo.evaluate((i: HTMLImageElement) => i.naturalWidth))
      .toBeGreaterThan(0);
    await expect(
      page.getByRole("heading", { name: "Investimento na sua obra" }),
    ).toHaveCount(0);
    // a public portal never serves post-construction, decisions or documents (contract v1.5)
    await expect(
      page.getByRole("heading", { name: "Cuidado que continua" }),
    ).toHaveCount(0);
    await expect(
      page.getByRole("heading", { name: "Ajustes combinados" }),
    ).toHaveCount(0);
    expect(authorization.every((h) => h === "")).toBe(true);
    expect(await noHorizontalScroll(page)).toBe(true);
    await page.screenshot({
      path: `test-results/backend-public-${width}.png`,
      fullPage: true,
    });
  });

test("private portal: teaser only, wrong password, then the real project", async ({
  page,
}) => {
  await page.goto(`/${setup.company}/obra-privada`);
  await expect(page.getByText("Residência · teste local")).toHaveCount(0);
  await expect(page.getByText("Fundação em andamento")).toHaveCount(0);
  await page.getByLabel("Senha de acesso").fill(wrongPassword());
  await page.getByRole("button", { name: "Acessar minha obra" }).click();
  await expect(
    page.getByText("Não foi possível acessar com essa senha."),
  ).toBeVisible();
  await page.getByLabel("Senha de acesso").fill(setup.password);
  await page.getByRole("button", { name: "Acessar minha obra" }).click();
  await expect(page.getByRole("heading", { level: 1 })).toContainText(
    "Residência",
  );
  await expect(
    page.getByRole("heading", { name: "Tudo no seu lugar" }),
  ).toBeVisible();
  await expect(
    page.getByRole("heading", { name: "Investimento na sua obra" }),
  ).toHaveCount(0);
});

test("revoked session sends the client back to the password", async ({
  page,
  request,
}) => {
  await login(page);
  await request.post(api + "/__test/sessions/revoke");
  // the next call answers 401: the password comes back and the project data is dropped
  await page.getByRole("button", { name: "Explorar a obra em 3D" }).click();
  await expect(
    page.getByText("Seu acesso expirou. Informe a senha novamente."),
  ).toBeVisible();
  await expect(page.getByText("Residência · teste local")).toHaveCount(0);
  await expect(page.getByLabel("Senha de acesso")).toBeVisible();
});

test("session expired on the server: an action answers 401 and nothing is shown as done", async ({
  page,
  request,
}) => {
  await login(page);
  await request.post(api + "/__test/sessions/expire");
  await page
    .getByRole("button", { name: "Escolher Porcelanato 90x90" })
    .click();
  await page
    .getByRole("button", { name: "Confirmar escolha e impactos" })
    .click();
  await expect(
    page.getByText("Seu acesso expirou. Informe a senha novamente."),
  ).toBeVisible();
  await expect(page.getByText("Escolha enviada")).toHaveCount(0);
  // the choice was not recorded: after logging in again it is still open
  await page.getByLabel("Senha de acesso").fill(setup.password);
  await page.getByRole("button", { name: "Acessar minha obra" }).click();
  await expect(
    page.getByRole("button", { name: "Escolher Porcelanato 90x90" }),
  ).toBeVisible();
});

test("session lifetime ends in the browser: the portal locks without a server round-trip", async ({
  page,
}) => {
  await page.clock.install();
  await login(page);
  await page.clock.fastForward(48 * 60 * 60 * 1000);
  await page.evaluate(() => window.dispatchEvent(new Event("focus")));
  await expect(
    page.getByText("Seu acesso expirou. Informe a senha novamente."),
  ).toBeVisible();
  await expect(page.getByText("Residência · teste local")).toHaveCount(0);
});

test("change order: schedule impact, cost hidden without finance, the signer's name is required by the server", async ({
  page,
}) => {
  await login(page);
  const order = article(page, "Ajuste de revestimento");
  await expect(order).toContainText("Impacto no prazo: 2 dias");
  // financialSummary is off: the API does not send the value and the portal does not invent one
  await expect(order).not.toContainText("Impacto no contrato");
  const form = order.locator("form");
  await form.getByLabel("Seu nome completo").fill("   ");
  await form.locator("select[name=decision]").selectOption("approve");
  await form.getByRole("checkbox").check();
  await form
    .getByRole("button", { name: "Registrar decisão do aditivo" })
    .click();
  await expect(order.getByRole("alert")).toBeVisible();
  await expect(order).toContainText("Aguardando decisão");
  await form.getByLabel("Seu nome completo").fill("Cliente de teste");
  await form.getByRole("checkbox").check();
  await form
    .getByRole("button", { name: "Registrar decisão do aditivo" })
    .click();
  await expect(order).toContainText("Aditivo 1 · Aprovado");
  await expect(order.locator("form")).toHaveCount(0);
  await expect(order.getByRole("status")).toHaveText("Decisão registrada.");
});

test("change order decided on another device: the stale screen gets 409, explained, and the record refreshes", async ({
  page,
  request,
}) => {
  await login(page);
  const order = article(page, "Troca de louças");
  await expect(order.locator("form")).toBeVisible();
  // someone else in the family decides first, from another device (real API, its own session)
  const other = await request.post(
    `${portalApi(setup.company, "obra-privada")}/session`,
    { data: { password: setup.password } },
  );
  const token = (await other.json()).token as string;
  const orders = await request.get(
    `${portalApi(setup.company, "obra-privada")}/change-orders`,
    { headers: { "X-Build-Portal-Session": token } },
  );
  const id = (
    (await orders.json()).items as { id: string; title: string }[]
  ).find((o) => o.title === "Troca de louças")!.id;
  const first = await request.post(
    `${portalApi(setup.company, "obra-privada")}/change-orders/${id}/decision`,
    {
      headers: { "X-Build-Portal-Session": token },
      data: { decision: "reject", signerName: "Outra pessoa" },
    },
  );
  expect(first.status()).toBe(200);
  // this screen still shows the form: its decision is refused, never shown as done
  const form = order.locator("form");
  await form.getByLabel("Seu nome completo").fill("Cliente de teste");
  await form.locator("select[name=decision]").selectOption("approve");
  await form.getByRole("checkbox").check();
  await form
    .getByRole("button", { name: "Registrar decisão do aditivo" })
    .click();
  await expect(order.getByRole("alert")).toContainText(
    "O registro foi atualizado com a situação atual.",
  );
  await expect(order).toContainText("Recusado");
  await expect(order.locator("form")).toHaveCount(0);
  await expect(order).not.toContainText("Decisão registrada.");
});

test("choice: sent once, confirmed by the company, its change order decided by the client", async ({
  page,
  request,
}) => {
  await login(page);
  await page
    .getByRole("button", { name: "Escolher Porcelanato 90x90" })
    .click();
  const dialog = page.getByRole("dialog");
  await expect(dialog).toContainText("15 dias de fornecimento");
  await dialog
    .getByRole("button", { name: "Confirmar escolha e impactos" })
    .click();
  await expect(
    page.getByText("Enviada, aguardando a construtora"),
  ).toBeVisible();
  // sent ≠ confirmed, and it cannot be sent again
  await expect(page.getByRole("button", { name: /^Escolher / })).toHaveCount(0);
  const confirmed = await request.post(api + "/__test/selection/confirm");
  expect((await confirmed.json()).changeOrder).toBe(true);
  await page.reload();
  await page.getByLabel("Senha de acesso").fill(setup.password);
  await page.getByRole("button", { name: "Acessar minha obra" }).click();
  await expect(page.getByText("Confirmada pela construtora")).toBeVisible();
  const generated = page.locator("article").filter({ hasText: "Aditivo 3" });
  // the backend keeps the option's lead time apart from the schedule: shown as recorded, nothing recomputed
  await expect(generated).toContainText("Prazo de entrega da opção: 15 dia(s)");
  await expect(generated).toContainText("Impacto no prazo: Sem alteração");
  const form = generated.locator("form");
  await form.getByLabel("Seu nome completo").fill("Cliente de teste");
  await form.locator("select[name=decision]").selectOption("approve");
  await form.getByRole("checkbox").check();
  await form
    .getByRole("button", { name: "Registrar decisão do aditivo" })
    .click();
  await expect(generated).toContainText("Aprovado");
  await page.screenshot({
    path: "test-results/backend-decisions.png",
    fullPage: true,
  });
});

test("handover: accepted with the signer's name, final state and history", async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 900 });
  await login(page, "imovel-entregue");
  await expect(page.getByText("Meu Imóvel", { exact: true })).toBeVisible();
  const accept = formWith(page, "Registrar aceite da entrega");
  await accept.getByLabel("Seu nome completo").fill("Cliente de teste");
  await accept.getByRole("checkbox").check();
  await accept
    .getByRole("button", { name: "Registrar aceite da entrega" })
    .click();
  await expect(page.getByText(/Aceite registrado em/)).toBeVisible();
  await expect(
    page.getByText("Aceite registrado.", { exact: true }),
  ).toBeVisible();
  await expect(formWith(page, "Registrar aceite da entrega")).toHaveCount(0);
  // the acceptance is the server's: it survives a new session
  await page.reload();
  await page.getByLabel("Senha de acesso").fill(setup.password);
  await page.getByRole("button", { name: "Acessar minha obra" }).click();
  await expect(page.getByText(/Aceite registrado em/)).toBeVisible();
  expect(await noHorizontalScroll(page)).toBe(true);
});

test("assistance: only allowed photos, protocol, evidence with progress, status and rating once", async ({
  page,
  request,
}) => {
  await page.setViewportSize({ width: 390, height: 900 });
  await login(page, "imovel-entregue");
  const open = formWith(page, "Solicitar atendimento");
  // a file that is not a photo never leaves the browser
  await open.getByLabel("Categoria").selectOption({ index: 0 });
  await open.getByLabel("Título do chamado").fill("Revisar janela");
  await open
    .getByLabel("Como podemos ajudar?")
    .fill("A janela da sala precisa de ajuste. Registro de teste.");
  await open.getByLabel("Fotos (opcional)").setInputFiles({
    name: "planta.pdf",
    mimeType: "application/pdf",
    buffer: Buffer.from("%PDF-1.4"),
  });
  await open
    .getByRole("button", { name: "Solicitar atendimento", exact: true })
    .click();
  await expect(open.getByRole("alert")).toContainText(
    "Envie fotos JPEG, PNG ou WebP.",
  );
  await expect(page.getByRole("heading", { name: /^Chamado \d+/ })).toHaveCount(
    0,
  );
  // a real photo goes with the ticket
  await open.getByLabel("Fotos (opcional)").setInputFiles({
    name: "janela.png",
    mimeType: "image/png",
    buffer: Buffer.from(
      "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN1sAAAAASUVORK5CYII=",
      "base64",
    ),
  });
  await open
    .getByRole("button", { name: "Solicitar atendimento", exact: true })
    .click();
  const ticket = page.locator("article").filter({
    has: page.getByRole("heading", { name: /Chamado \d+: Revisar janela/ }),
  });
  await expect(ticket).toBeVisible();
  await expect(page.getByText(/Chamado aberto\./)).toBeVisible();
  await expect(
    ticket.getByRole("img", { name: /Evidência 1 do chamado/ }),
  ).toBeVisible();

  // the server checks the content: a text file named .png is refused (422), nothing is shown as sent
  await ticket.getByLabel("Adicionar fotos ao chamado").setInputFiles({
    name: "falsa.png",
    mimeType: "image/png",
    buffer: Buffer.from("isto não é uma imagem"),
  });
  await ticket
    .getByRole("button", { name: "Enviar fotos", exact: true })
    .click();
  await expect(ticket.getByRole("alert")).toBeVisible();
  await expect(
    ticket.getByRole("img", { name: /Evidência 2 do chamado/ }),
  ).toHaveCount(0);
  // too large for the contract (8 MB): refused before upload, as the API would answer 413
  await ticket.getByLabel("Adicionar fotos ao chamado").setInputFiles({
    name: "grande.png",
    mimeType: "image/png",
    buffer: Buffer.alloc(8 * 1024 * 1024 + 1),
  });
  await ticket
    .getByRole("button", { name: "Enviar fotos", exact: true })
    .click();
  await expect(ticket.getByRole("alert")).toContainText("até 8 MB");
  // a valid evidence photo
  await ticket.getByLabel("Adicionar fotos ao chamado").setInputFiles({
    name: "detalhe.png",
    mimeType: "image/png",
    buffer: Buffer.from(
      "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN1sAAAAASUVORK5CYII=",
      "base64",
    ),
  });
  await ticket
    .getByRole("button", { name: "Enviar fotos", exact: true })
    .click();
  await expect(ticket.getByRole("status")).toHaveText(
    "Fotos recebidas pela construtora.",
  );
  await expect(
    ticket.getByRole("img", { name: /Evidência 2 do chamado/ }),
  ).toBeVisible();

  // the company resolves; the client rates once
  await request.post(api + "/__test/assistance/resolve");
  await page.reload();
  await page.getByLabel("Senha de acesso").fill(setup.password);
  await page.getByRole("button", { name: "Acessar minha obra" }).click();
  const resolved = page.locator("article").filter({
    has: page.getByRole("heading", { name: /Revisar janela/ }),
  });
  await expect(resolved).toContainText("Resolvido");
  await resolved.getByText("Histórico do atendimento").click();
  await expect(resolved.locator("details")).toContainText("Em análise");
  await expect(resolved).toContainText("Atendimento:");
  await resolved.getByLabel("Avalie o atendimento").selectOption("5");
  await resolved.getByRole("button", { name: "Enviar avaliação" }).click();
  await expect(resolved.getByText("Sua avaliação: 5 de 5")).toBeVisible();
  await expect(
    resolved.getByRole("button", { name: "Enviar avaliação" }),
  ).toHaveCount(0);
  expect(await noHorizontalScroll(page)).toBe(true);
  await page.screenshot({
    path: "test-results/backend-handover-390.png",
    fullPage: true,
  });
});

test("signed URLs: renewed before expiry and, if one fails, transparently re-requested once", async ({
  page,
  request,
}) => {
  await page.clock.install();
  await page.goto(`/${setup.company}/obra-publica`);
  const image = page.getByRole("img", { name: "Evidência sintética de teste" });
  await image.scrollIntoViewIfNeeded();
  await expect(image).toBeVisible();
  const before = await image.getAttribute("src");
  // timer: the bootstrap is fetched again before the URLs' 15 minutes
  await page.clock.fastForward(15 * 60 * 1000);
  await expect.poll(() => image.getAttribute("src")).not.toBe(before);
  const renewed = await image.getAttribute("src");

  // expiry in the middle of the session: the stored link answers 403, the portal asks the API once and recovers
  await request.post(api + "/__test/media/expire");
  await page.clock.fastForward(2 * 60 * 1000);
  const bootstraps: string[] = [];
  page.on("request", (r) => {
    if (r.url().endsWith("/photos?take=24")) bootstraps.push(r.url());
  });
  await image.evaluate((img: HTMLImageElement) => {
    img.src = img.src + "&reload=1";
  });
  await expect.poll(() => image.getAttribute("src")).not.toContain("reload=1");
  expect(await image.getAttribute("src")).not.toBe(renewed);
  await expect
    .poll(() => image.evaluate((i: HTMLImageElement) => i.naturalWidth))
    .toBeGreaterThan(0);
  // no loop: one renewal for the failure
  await page.waitForTimeout(500);
  expect(bootstraps.length).toBe(1);
});

test("documents: a stale link is renewed instead of opened", async ({
  page,
}) => {
  await page.clock.install();
  await login(page);
  const link = page.getByRole("link", { name: "Abrir documento ↗" });
  await expect(link).toHaveAttribute("href", /signature=review-/);
  const before = await link.getAttribute("href");
  // the page slept: the link's signature is past its window when the client clicks
  await page.clock.setSystemTime(Date.now() + 20 * 60 * 1000);
  await link.click();
  await expect(page.getByText(/O link do arquivo foi renovado/)).toBeVisible();
  await expect.poll(() => link.getAttribute("href")).not.toBe(before);
});

test("digital twin: real GLB, modes, element index filters, clipping, selection, date snapshots with ETag, GLB fetched once", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await login(page, "obra-3d");
  const stateRequests: { url: string; ifNoneMatch: string }[] = [];
  const stateStatus: number[] = [];
  page.on("request", (r) => {
    if (r.url().includes("/twin/state?"))
      stateRequests.push({
        url: r.url(),
        ifNoneMatch: r.headers()["if-none-match"] ?? "",
      });
  });
  page.on("response", (r) => {
    if (r.url().includes("/twin/state?")) stateStatus.push(r.status());
  });
  await page.getByRole("button", { name: "Explorar a obra em 3D" }).click();
  await expect(page.getByText("Carregando modelo…")).toHaveCount(0);
  await expect(page.getByRole("alert")).toHaveCount(0);
  const viewer = page.locator(".mb-viewer");
  // the viewer's own controls (the photo gallery and the assistance form have selects with similar labels)
  const twin = page.locator("section").filter({ has: viewer });
  const control = (name: string) =>
    twin
      .locator("label")
      .filter({ hasText: new RegExp(`^${name}`) })
      .locator("select");
  await expect(viewer.locator("canvas")).toBeVisible();
  const glb = () => media.get(page)!.filter((u) => u.includes(".glb")).length;
  expect(glb()).toBe(1);

  // metadata comes from the API's element index, never from node names
  const stage = control("Etapa");
  await expect(stage.locator("option")).toHaveText([
    "Todas",
    "Fundação",
    "Alvenaria",
  ]);
  const category = control("Categoria");
  await expect(category.locator("option")).toHaveText([
    "Todas",
    "Lajes e pisos",
    "Paredes",
  ]);
  await expect(control("Pavimento").locator("option")).toHaveText([
    "Todos",
    "Pavimento térreo",
    "Pavimento superior",
  ]);

  // modes change what is drawn
  const shot = () => viewer.screenshot();
  const compare = await shot();
  await page.getByRole("button", { name: "Projeto", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "Projeto", exact: true }),
  ).toHaveAttribute("aria-pressed", "true");
  const project = await shot();
  expect(project.equals(compare)).toBe(false);
  await page.getByRole("button", { name: "Agora", exact: true }).click();
  expect((await shot()).equals(project)).toBe(false);
  await page.getByRole("button", { name: "Comparar", exact: true }).click();

  // filters and clipping
  await category.selectOption("Lajes e pisos");
  const slabs = await shot();
  await category.selectOption("");
  await stage.selectOption("Alvenaria");
  expect((await shot()).equals(slabs)).toBe(false);
  await stage.selectOption("");
  const elements = page
    .locator("details")
    .filter({ hasText: "Elementos e estados da construção" });
  await elements.locator("summary").click();
  await control("Pavimento").selectOption("Pavimento superior");
  await expect(elements.locator("li")).toHaveCount(2);
  await control("Pavimento").selectOption("");
  await expect(elements.locator("li")).toHaveCount(6);
  const whole = await shot();
  await page.getByLabel(/Corte horizontal/).fill("60");
  await expect(page.getByText("Corte horizontal: 60%")).toBeVisible();
  expect((await shot()).equals(whole)).toBe(false);
  await page.getByLabel(/Corte horizontal/).fill("0");

  // selection: today Alvenaria is running, Fundação is done
  await elements.getByRole("button", { name: "Paredes · DE-T01" }).click();
  await expect(
    page.getByRole("status").filter({ hasText: "Paredes · DE-T01" }),
  ).toContainText("Em execução · Alvenaria");
  await elements
    .getByRole("button", { name: "Lajes e pisos · JE-T01" })
    .click();
  await expect(
    page.getByRole("status").filter({ hasText: "Lajes e pisos · JE-T01" }),
  ).toContainText("Concluído · Fundação");

  // the client's date: the state on that day, from the API, without downloading the model again
  const date = page.getByLabel("Consultar data da obra");
  const today = await date.inputValue();
  await date.fill("2026-03-01");
  await expect(page.getByText("Consultando estado nesta data…")).toHaveCount(0);
  await expect(
    page.getByRole("status").filter({ hasText: "Lajes e pisos · JE-T01" }),
  ).toContainText("Concluído");
  await elements.getByRole("button", { name: "Paredes · DE-T01" }).click();
  await expect(
    page.getByRole("status").filter({ hasText: "Paredes · DE-T01" }),
  ).toContainText("Não iniciado");
  await date.fill("2026-01-20");
  await expect(
    page.getByRole("status").filter({ hasText: "Paredes · DE-T01" }),
  ).toContainText("Não iniciado");
  await date.fill("2026-03-01");
  await date.fill(today);
  await expect(
    page.getByRole("status").filter({ hasText: "Paredes · DE-T01" }),
  ).toContainText("Em execução");
  // ETag: dates seen before are revalidated with If-None-Match and come back 304
  await expect
    .poll(() => stateRequests.some((r) => r.ifNoneMatch !== ""))
    .toBe(true);
  await expect.poll(() => stateStatus.includes(304)).toBe(true);
  expect(glb()).toBe(1);

  // picking on the canvas selects the element under the pointer
  await page.getByRole("button", { name: "Visão geral" }).click();
  await viewer.scrollIntoViewIfNeeded();
  const box = (await viewer.boundingBox())!;
  const selected = page
    .getByRole("status")
    .filter({ hasText: /^(Paredes|Lajes e pisos) · .+ · / });
  await page.mouse.click(box.x + 4, box.y + 4); // empty corner: clears the selection
  await expect(selected).toHaveCount(0);
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
  await expect(selected).toHaveCount(1);
  await page.screenshot({
    path: "test-results/backend-twin.png",
    fullPage: true,
  });
});

test("digital twin without WebGL keeps an accessible path", async ({
  page,
}) => {
  await page.addInitScript(() => {
    HTMLCanvasElement.prototype.getContext = (() =>
      null) as typeof HTMLCanvasElement.prototype.getContext;
  });
  await login(page, "obra-3d");
  await page.getByRole("button", { name: "Explorar a obra em 3D" }).click();
  await expect(page.getByRole("alert")).toContainText("dispositivo");
  await page.getByText("Elementos e estados da construção").click();
  await expect(
    page.getByRole("button", { name: "Paredes · DE-T01" }),
  ).toBeVisible();
  await expect(
    page.getByRole("heading", { name: "O caminho da sua obra" }),
  ).toBeVisible();
  await page.screenshot({
    path: "test-results/backend-webgl-fallback.png",
    fullPage: true,
  });
});

test("reality capture: a site photo the company made visible, with its stage", async ({
  page,
}) => {
  await login(page, "obra-3d");
  const capture = page.getByRole("img", { name: "Fachada registrada" });
  await capture.scrollIntoViewIfNeeded();
  await expect(capture).toBeVisible();
  await page.getByLabel("Filtrar por etapa").selectOption("Alvenaria");
  await expect(capture).toBeVisible();
});

test("lockout: 429 with Retry-After counts down and blocks retries", async ({
  page,
  request,
}) => {
  const endpoint = `${portalApi(setup.company, "obra-bloqueio")}/session`;
  let status = 0;
  let retryAfter = "";
  for (let i = 0; i < 25 && status !== 429; i++) {
    const r = await request.post(endpoint, {
      data: { password: wrongPassword() },
    });
    status = r.status();
    retryAfter = r.headers()["retry-after"] ?? "";
  }
  expect(status).toBe(429);
  expect(Number(retryAfter)).toBeGreaterThan(0);
  await page.goto(`/${setup.company}/obra-bloqueio`);
  await page.getByLabel("Senha de acesso").fill(setup.password);
  await page.getByRole("button", { name: "Acessar minha obra" }).click();
  await expect(page.getByText(/Tente novamente em \d+ segundos/)).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Tentar novamente" }),
  ).toBeDisabled();
  // while locked the password form is not offered at all
  await expect(page.getByLabel("Senha de acesso")).toHaveCount(0);
  await page.screenshot({
    path: "test-results/backend-rate-limit.png",
    fullPage: true,
  });
});

test("isolation: a session, a password and an id of one company never open another's portal", async ({
  page,
  request,
}) => {
  const mine = await request.post(
    `${portalApi(setup.company, "obra-privada")}/session`,
    { data: { password: setup.password } },
  );
  const token = (await mine.json()).token as string;
  const theirs = portalApi(setup.otherCompany, "obra-privada");
  // same project slug, other company: my session is not theirs
  expect(
    (
      await request.get(theirs, {
        headers: { "X-Build-Portal-Session": token },
      })
    ).status(),
  ).toBe(401);
  // my password does not open their portal
  expect(
    (
      await request.post(`${theirs}/session`, {
        data: { password: setup.password },
      })
    ).status(),
  ).toBe(401);
  // their change order id is unknown to my portal (identical 404)
  const decided = await request.post(
    `${portalApi(setup.company, "obra-privada")}/change-orders/${setup.otherChangeOrder}/decision`,
    {
      headers: { "X-Build-Portal-Session": token },
      data: { decision: "approve", signerName: "Cliente de teste" },
    },
  );
  expect(decided.status()).toBe(404);
  // and in the browser the other company's portal shows only its own teaser
  await page.goto(`/${setup.otherCompany}/obra-privada`);
  await page.getByLabel("Senha de acesso").fill(setup.otherPassword);
  await page.getByRole("button", { name: "Acessar minha obra" }).click();
  await expect(page.getByRole("heading", { level: 1 })).toContainText(
    "Obra de outra empresa",
  );
  await expect(page.getByText("Ajuste de revestimento")).toHaveCount(0);
  await expect(page.getByText("Aditivo de outra empresa")).toBeVisible();
  // an unknown company or project looks exactly like any other unknown one
  await page.goto(`/${setup.company}/nao-existe`);
  await expect(
    page.getByText("Não encontramos este acompanhamento."),
  ).toBeVisible();
});
