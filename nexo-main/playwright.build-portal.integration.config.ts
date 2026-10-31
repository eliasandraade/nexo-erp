import { defineConfig } from "@playwright/test";
import { existsSync } from "node:fs";
import { resolve } from "node:path";

// "Minha Obra" against the real Orken Build API. Needs Docker (disposable PostgreSQL); run from nexo-main:
//   npx playwright test --config playwright.build-portal.integration.config.ts
// The backend is the repository's ../nexo-backend (BUILD_BACKEND_ROOT points elsewhere if needed).
// Both servers start fresh on every run (decisions in the suite are one-shot).
const backend = resolve(process.env.BUILD_BACKEND_ROOT ?? "../nexo-backend");
if (!existsSync(resolve(backend, "src/Nexo.Api")))
  throw new Error(`No Orken backend at ${backend}; set BUILD_BACKEND_ROOT to the nexo-backend folder.`);

export default defineConfig({
  testDir: "./e2e",
  testMatch: "build-portal.integration.spec.ts",
  workers: 1,
  timeout: 90000,
  expect: { timeout: 20000 },
  use: {
    baseURL: "http://127.0.0.1:5195",
    channel: process.platform === "win32" ? "msedge" : undefined,
    screenshot: "only-on-failure",
  },
  webServer: [
    {
      command: `dotnet run --project e2e/backend-host/PortalReviewHost.csproj -p:BuildBackendRoot="${backend}"`,
      url: "http://127.0.0.1:5194/__test/setup",
      env: { BUILD_BACKEND_ROOT: backend },
      timeout: 600000,
      reuseExistingServer: false,
      stdout: "ignore",
      stderr: "pipe",
    },
    {
      command: "npm run dev -- --host 127.0.0.1 --port 5195 --strictPort",
      url: "http://127.0.0.1:5195",
      env: { VITE_API_BASE_URL: "http://127.0.0.1:5194/api" },
      reuseExistingServer: false,
    },
  ],
});
