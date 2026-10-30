import { test, expect, type Page } from "@playwright/test";
let setup: { company: string; password: string };
const api = "http://127.0.0.1:5194";
test.beforeEach(async ({ page, request }) => {
  await page.route("https://storage.test/**", async (route) => {
    const url = new URL(route.request().url());
    const response = await request.get(api + "/__test/media" + url.pathname);
    await route.fulfill({
      response,
      headers: {
        "access-control-allow-origin": "*",
        "content-type": response.headers()["content-type"] ?? "image/png",
      },
    });
  });
});
test.beforeAll(async ({ request }) => {
  const r = await request.get(api + "/__test/setup");
  expect(r.ok()).toBe(true);
  setup = await r.json();
});
const login = async (page: Page, project = "obra-privada") => {
  await page.goto(`/${setup.company}/${project}`);
  await page.getByLabel("Senha de acesso").fill(setup.password);
  await page.getByRole("button", { name: "Acessar minha obra" }).click();
  await expect(page.getByRole("heading", { level: 1 })).toContainText(
    /teste local/,
  );
};
for (const width of [390, 1440])
  test(`real public portal, timeline, hidden finance at ${width}px`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 900 });
    const headers: string[] = [];
    page.on("request", (r) => {
      if (r.url().includes("/api/public/build/"))
        headers.push(r.headers().authorization ?? "");
    });
    await page.goto(`/${setup.company}/obra-publica`);
    await expect(page.getByRole("heading", { level: 1 })).toContainText(
      "Residência",
    );
    await expect(page.getByText("40%", { exact: true }).first()).toBeVisible();
    await expect(
      page.getByRole("heading", { name: "Fundação em andamento" }),
    ).toBeVisible();
    await expect(
      page.getByRole("heading", { name: "Investimento na sua obra" }),
    ).toHaveCount(0);
    await expect(
      page.getByRole("heading", { name: "Cuidado que continua" }),
    ).toHaveCount(0);
    expect(headers.every((h) => h === "")).toBe(true);
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
    ).toBe(true);
    await page.screenshot({
      path: `test-results/backend-public-${width}.png`,
      fullPage: true,
    });
  });
test("real private teaser, wrong password and session revocation", async ({
  page,
  request,
}) => {
  await page.goto(`/${setup.company}/obra-privada`);
  await expect(page.getByText("Residência · teste local")).toHaveCount(0);
  await page.screenshot({
    path: "test-results/backend-private.png",
    fullPage: true,
  });
  await page.getByLabel("Senha de acesso").fill("incorrect-test-password");
  await page.getByRole("button", { name: "Acessar minha obra" }).click();
  await expect(
    page.getByText("Não foi possível acessar com essa senha."),
  ).toBeVisible();
  await page.getByLabel("Senha de acesso").fill(setup.password);
  await page.getByRole("button", { name: "Acessar minha obra" }).click();
  await expect(page.getByRole("heading", { level: 1 })).toContainText(
    "Residência",
  );
  await request.post(api + "/__test/expire");
  await page.getByRole("button", { name: "Explorar a obra em 3D" }).click();
  await expect(
    page.getByText("Seu acesso expirou. Informe a senha novamente."),
  ).toBeVisible();
  await expect(page.getByText("Residência · teste local")).toHaveCount(0);
  await page.screenshot({
    path: "test-results/backend-expired.png",
    fullPage: true,
  });
});
test("real choices and change order decisions are confirmed by the server", async ({
  page,
}) => {
  await login(page);
  await page.getByRole("button", { name: "Escolher Cerâmica padrão" }).click();
  await page
    .getByRole("button", { name: "Confirmar escolha e impactos" })
    .click();
  await expect(
    page.getByText("Enviada, aguardando a construtora"),
  ).toBeVisible();
  const form = page
    .locator("form")
    .filter({
      has: page.getByRole("button", { name: "Registrar decisão do aditivo" }),
    });
  await form.getByLabel("Seu nome completo").fill("Cliente de teste");
  await form
    .getByRole("combobox", { name: "Decisão", exact: true })
    .selectOption("approve");
  await form.getByRole("checkbox").check();
  await form
    .getByRole("button", { name: "Registrar decisão do aditivo" })
    .click();
  await expect(page.getByText("Aditivo 1 · Aprovado")).toBeVisible();
  await page.screenshot({
    path: "test-results/backend-decisions.png",
    fullPage: true,
  });
});
test("real handover acceptance and assistance request", async ({
  page,
  request,
}) => {
  await page.setViewportSize({ width: 390, height: 900 });
  await login(page, "imovel-entregue");
  const accept = page
    .locator("form")
    .filter({
      has: page.getByRole("button", { name: "Registrar aceite da entrega" }),
    });
  await accept.getByLabel("Seu nome completo").fill("Cliente de teste");
  await accept.getByRole("checkbox").check();
  await accept
    .getByRole("button", { name: "Registrar aceite da entrega" })
    .click();
  await expect(page.getByText(/Aceite registrado em/)).toBeVisible();
  const form = page
    .locator("form")
    .filter({
      has: page.getByRole("button", {
        name: "Solicitar atendimento",
        exact: true,
      }),
    });
  await form.getByLabel("Categoria").selectOption({ label: "Outros" });
  await form.getByLabel("Título do chamado").fill("Revisar janela");
  await form
    .getByLabel("Como podemos ajudar?")
    .fill("A janela da sala precisa de ajuste. Registro de teste.");
  await form
    .getByRole("button", { name: "Solicitar atendimento", exact: true })
    .click();
  await expect(
    page.getByRole("heading", { name: /Chamado 1: Revisar janela/ }),
  ).toBeVisible();
  await page
    .getByLabel("Adicionar fotos ao chamado")
    .setInputFiles({
      name: "evidence.png",
      mimeType: "image/png",
      buffer: Buffer.from(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN1sAAAAASUVORK5CYII=",
        "base64",
      ),
    });
  await page.getByRole("button", { name: "Enviar fotos", exact: true }).click();
  await expect(
    page.getByRole("img", { name: "Evidência 1 do chamado 1" }),
  ).toBeVisible();
  await request.post(api + "/__test/resolve");
  await page.reload();
  await page.getByLabel("Senha de acesso").fill(setup.password);
  await page.getByRole("button", { name: "Acessar minha obra" }).click();
  await page.getByLabel("Avalie o atendimento").selectOption("5");
  await page.getByRole("button", { name: "Enviar avaliação" }).click();
  await expect(page.getByText("Sua avaliação: 5 de 5")).toBeVisible();
  await page.screenshot({
    path: "test-results/backend-handover-390.png",
    fullPage: true,
  });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
});
test("real twin, date snapshot and ETag revalidation", async ({ page }) => {
  await login(page);
  const states: string[] = [];
  page.on("request", (r) => {
    if (r.url().includes("/twin/state?"))
      states.push(r.headers()["if-none-match"] ?? "");
  });
  await page.getByRole("button", { name: "Explorar a obra em 3D" }).click();
  await expect(
    page.getByText("Sala · medidas de teste:", { exact: false }),
  ).toBeAttached();
  await expect(
    page.getByText(/Representação simplificada/).first(),
  ).toBeVisible();
  const date = page.getByLabel("Consultar data da obra");
  const today = await date.inputValue();
  await date.fill("2026-02-01");
  await expect(page.getByText("Consultando estado nesta data…")).toHaveCount(0);
  await date.fill(today);
  await expect.poll(() => states.some(Boolean)).toBe(true);
  await page.screenshot({
    path: "test-results/backend-twin.png",
    fullPage: true,
  });
});
test("real twin preserves accessible fallback without WebGL", async ({
  page,
}) => {
  await page.addInitScript(() => {
    HTMLCanvasElement.prototype.getContext = (() =>
      null) as typeof HTMLCanvasElement.prototype.getContext;
  });
  await login(page);
  await page.getByRole("button", { name: "Explorar a obra em 3D" }).click();
  await expect(page.getByRole("alert")).toContainText("dispositivo");
  await expect(
    page.getByRole("heading", { name: "O caminho da sua obra" }),
  ).toBeVisible();
  await page.screenshot({
    path: "test-results/backend-webgl-fallback.png",
    fullPage: true,
  });
});
test("real signed media is renewed before expiry", async ({ page }) => {
  await page.clock.install();
  await page.goto(`/${setup.company}/obra-publica`);
  const image = page.getByRole("img", { name: "Evidência sintética de teste" });
  await expect(image).toBeVisible();
  const before = await image.getAttribute("src");
  const refreshed = page.waitForResponse(
    (r) => r.url().endsWith("/photos?take=24") && r.status() === 200,
  );
  await page.clock.fastForward(15 * 60 * 1000);
  await refreshed;
  await expect.poll(() => image.getAttribute("src")).not.toBe(before);
});
test("real password rate limit follows Retry-After", async ({
  page,
  request,
}) => {
  const endpoint = `${api}/api/public/build/${setup.company}/obra-privada/session`;
  let status = 0;
  for (let i = 0; i < 23 && status !== 429; i++) {
    const r = await request.post(endpoint, {
      data: { password: "wrong-for-rate-test" },
    });
    status = r.status();
  }
  expect(status).toBe(429);
  await page.goto(`/${setup.company}/obra-privada`);
  await page.getByLabel("Senha de acesso").fill(setup.password);
  await page.getByRole("button", { name: "Acessar minha obra" }).click();
  await expect(page.getByText(/Tente novamente em \d+ segundos/)).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Tentar novamente" }),
  ).toBeDisabled();
  await page.screenshot({
    path: "test-results/backend-rate-limit.png",
    fullPage: true,
  });
});
