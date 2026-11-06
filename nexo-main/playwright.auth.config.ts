import { defineConfig, devices } from "@playwright/test";
import { resolve } from "node:path";

/**
 * Authentication end to end, against the real API: e2e/auth-host runs it on a disposable PostgreSQL (needs Docker and the
 * .NET 8 SDK) and the app runs from a production build. `npm run test:e2e` starts both. Nothing here takes a URL from the
 * environment: the suite only ever talks to these two local servers.
 */
const API_ORIGIN = "http://127.0.0.1:5196";
const APP_ORIGIN = "http://127.0.0.1:5197";

export default defineConfig({
  testDir: "./e2e",
  testMatch: "auth.e2e.spec.ts",
  timeout: 60_000,
  expect: { timeout: 15_000 },
  retries: 0,
  // Every test creates its own accounts and has its own client address for the rate limiter: nothing is shared.
  fullyParallel: true,
  workers: 2,
  forbidOnly: !!process.env.CI,
  reporter: [["list"], ["html", { open: "never" }]],
  use: {
    baseURL: APP_ORIGIN,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    // The browser cannot resolve any other host: no test can reach production or staging, whatever a page asks for.
    launchOptions: { args: ["--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE 127.0.0.1"] },
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: [
    {
      command: "dotnet run --project e2e/auth-host/AuthE2EHost.csproj",
      url: `${API_ORIGIN}/__test/setup`,
      env: { AUTH_E2E_BACKEND_ROOT: resolve("../nexo-backend") },
      timeout: 600_000,
      reuseExistingServer: !process.env.CI,
      stdout: "ignore",
      stderr: "pipe",
    },
    {
      // A build of its own, pointed at the host: the tracked dist/ is left alone. Production bundle, not the dev server: it
      // is what ships, and a page opens fast enough for every test to start from a new browser context.
      command: "npx vite build --outDir dist-e2e --emptyOutDir --logLevel warn && npx vite preview --outDir dist-e2e --host 127.0.0.1 --port 5197 --strictPort",
      url: APP_ORIGIN,
      env: { VITE_API_BASE_URL: `${API_ORIGIN}/api` },
      timeout: 300_000,
      reuseExistingServer: !process.env.CI,
    },
  ],
});
