import { defineConfig, devices } from "@playwright/test";
import { resolve } from "node:path";

/**
 * Authentication end to end, against the real API: e2e/auth-host runs it on a disposable PostgreSQL (needs Docker and the
 * .NET 8 SDK) and the app runs from a production build. `npm run test:e2e` starts both. Nothing here takes a URL from the
 * environment: the suite only ever talks to these two local servers.
 *
 * AUTH_E2E_SESSION_MODE=cookie runs the same suite against the bundle built with VITE_SESSION_MODE=cookie (`npm run test:e2e:cookie`). Each
 * bundle has its own port and folder, so a server kept alive between runs can never be taken for the other mode, and the first test checks
 * which mode it is really talking to. http://127.0.0.1 is a trustworthy origin for Chromium: it stores a __Host- cookie from it, measured in
 * the suite. 5196 and 5197/5198 are other origins of the same site, the shape of erp.orken.com.br and api.erp.orken.com.br.
 */
const COOKIE_MODE = process.env.AUTH_E2E_SESSION_MODE === "cookie";
const API_ORIGIN = "http://127.0.0.1:5196";
const APP_PORT = COOKIE_MODE ? 5198 : 5197;
const APP_ORIGIN = `http://127.0.0.1:${APP_PORT}`;
const BUILD_DIR = COOKIE_MODE ? "dist-e2e-cookie" : "dist-e2e";

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
    // The browser cannot resolve any other host: no test can reach production or staging, whatever a page asks for. The names of
    // .test are reserved and never leave this machine: the tests of the shape of production use them, on the host's second listener.
    launchOptions: { args: ["--host-resolver-rules=MAP *.orken.test 127.0.0.1, MAP * ~NOTFOUND, EXCLUDE 127.0.0.1"] },
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
      command: `npx vite build --outDir ${BUILD_DIR} --emptyOutDir --logLevel warn && npx vite preview --outDir ${BUILD_DIR} --host 127.0.0.1 --port ${APP_PORT} --strictPort`,
      url: APP_ORIGIN,
      env: { VITE_API_BASE_URL: `${API_ORIGIN}/api`, VITE_SESSION_MODE: COOKIE_MODE ? "cookie" : "" },
      timeout: 300_000,
      reuseExistingServer: !process.env.CI,
    },
  ],
});
