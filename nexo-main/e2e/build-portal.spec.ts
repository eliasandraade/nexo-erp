import { test, expect } from "@playwright/test";
for (const width of [360, 390, 430, 768, 1440])
  test("public review at " + width, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/__review/minha-obra");
    await expect(
      page.getByRole("heading", { name: "Residência de revisão" }),
    ).toBeVisible();
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
    ).toBe(true);
    await expect(
      page.getByRole("heading", { name: "Investimento na sua obra" }),
    ).toHaveCount(0);
    await page.screenshot({
      path: "test-results/minha-obra-" + width + ".png",
      fullPage: true,
    });
  });
test("private wrong password then access", async ({ page }) => {
  await page.goto("/__review/minha-obra?scenario=private");
  await expect(page.getByText("Residência de revisão")).toHaveCount(0);
  await page.getByLabel("Senha de acesso").fill("bad");
  await page.getByRole("button", { name: "Acessar minha obra" }).click();
  await expect(
    page.getByText("Não foi possível acessar com essa senha."),
  ).toBeVisible();
  await page.getByLabel("Senha de acesso").fill("review-only");
  await page.getByRole("button", { name: "Acessar minha obra" }).click();
  await expect(
    page.getByRole("heading", { name: "Residência de revisão" }),
  ).toBeVisible();
});
test("decision requires impact confirmation", async ({ page }) => {
  await page.goto("/__review/minha-obra");
  await page.getByRole("button", { name: "Escolher Opção de teste B" }).click();
  await expect(page.getByRole("dialog")).toContainText("3 dias adicionais");
  await page
    .getByRole("button", { name: "Confirmar escolha e impactos" })
    .click();
  await expect(page.getByRole("status")).toContainText("Escolha enviada");
});
test("handover and assistance", async ({ page }) => {
  await page.goto("/__review/minha-obra?scenario=handover");
  await expect(page.getByText("Bem-vindo ao seu imóvel.")).toBeVisible();
  await page.getByLabel("Como podemos ajudar?").fill("Descrição de teste");
  await page.getByLabel("Disponibilidade").fill("Manhã");
  await page.getByRole("button", { name: "Solicitar atendimento" }).click();
  await expect(page.getByRole("status")).toContainText("Solicitação recebida");
});
test("model failure retains photos and stage fallback", async ({ page }) => {
  await page.route("https://fixtures.invalid/**", (route) => route.abort());
  await page.goto("/__review/minha-obra?scenario=model");
  await page.getByRole("button", { name: "Explorar a obra em 3D" }).click();
  await expect(page.getByRole("alert")).toContainText(/modelo|dispositivo/);
  await expect(
    page.getByRole("heading", { name: "O caminho da sua obra" }),
  ).toBeVisible();
  await page.getByLabel("Registro no tempo").selectOption("past");
  await page.getByText("Elementos e estados da construção").click();
  await expect(page.getByText("Não iniciado", { exact: false })).toBeVisible();
});
test("custom production route handles HTTP failure without fixtures", async ({ page }) => {
  await page.route("**/api/public/build/solutie/shoppingeusebio", route => route.fulfill({ status: 503, contentType: "application/json", body: JSON.stringify({ code: "unavailable", error: "Unavailable" }) }));
  await page.goto("/solutie/shoppingeusebio");
  await expect(
    page.getByText("O acompanhamento está indisponível neste momento."),
  ).toBeVisible();
  await expect(page.getByText("Residência de revisão")).toHaveCount(0);
});

test("structured model and historical snapshot", async ({ page }) => {
  const bytes = Buffer.from(
    new Float32Array([0, 0, 0, 2, 0, 0, 0, 2, 0]).buffer,
  );
  const gltf = {
    asset: { version: "2.0" },
    scene: 0,
    scenes: [{ nodes: [0] }],
    nodes: [{ name: "Wall", mesh: 0 }],
    meshes: [{ primitives: [{ attributes: { POSITION: 0 }, material: 0 }] }],
    materials: [
      {
        doubleSided: true,
        pbrMetallicRoughness: { baseColorFactor: [0.5, 0.6, 0.5, 1] },
      },
    ],
    buffers: [
      {
        uri: "data:application/octet-stream;base64," + bytes.toString("base64"),
        byteLength: 36,
      },
    ],
    bufferViews: [{ buffer: 0, byteOffset: 0, byteLength: 36 }],
    accessors: [
      {
        bufferView: 0,
        componentType: 5126,
        count: 3,
        type: "VEC3",
        min: [0, 0, 0],
        max: [2, 2, 0],
      },
    ],
  };
  let loads = 0;
  await page.route("https://fixtures.invalid/model.glb", (r) => {
    loads++;
    return r.fulfill({
      contentType: "model/gltf+json",
      body: JSON.stringify(gltf),
      headers: { "access-control-allow-origin": "*" },
    });
  });
  await page.goto("/__review/minha-obra?scenario=model");
  await page.getByRole("button", { name: "Explorar a obra em 3D" }).click();
  await expect(page.getByText("Carregando modelo…")).toHaveCount(0);
  await expect(page.locator(".mb-viewer canvas")).toBeVisible();
  await page.getByLabel("Registro no tempo").selectOption("past");
  await page.getByRole("button", { name: "Agora", exact: true }).click();
  await page.getByText("Elementos e estados da construção").click();
  await expect(page.getByText("Não iniciado", { exact: false })).toBeVisible();
  expect(loads).toBe(1);
  await page.screenshot({
    path: "test-results/minha-obra-3d.png",
    fullPage: true,
  });
});
