import { defineConfig } from "@playwright/test";

// "Minha Obra" against the real Orken Build API. Needs Docker (disposable PostgreSQL) and a checkout of the backend:
//   BUILD_BACKEND_ROOT=…/nexo-backend npx playwright test --config playwright.build-portal.integration.config.ts
// Both servers start fresh on every run (decisions in the suite are one-shot).
const backend = process.env.BUILD_BACKEND_ROOT;
if (!backend)
  throw new Error(
    "Set BUILD_BACKEND_ROOT to the nexo-backend folder of feat/build-complete.",
  );

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
