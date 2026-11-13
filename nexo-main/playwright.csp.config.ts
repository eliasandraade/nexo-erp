import { defineConfig, devices } from "@playwright/test";
import { resolve } from "node:path";

/**
 * The Content-Security-Policy of the app under a real browser (docs/security/csp.md): the production bundle, served by the
 * real Caddyfile on the image the Dockerfile uses, against the real API on a disposable PostgreSQL (e2e/auth-host).
 * `npm run test:e2e:csp` starts both; it needs Docker and the .NET 8 SDK. Nothing here takes a URL from the environment: the suite
 * only ever talks to these two local servers. The policy under test is what this server sends, read from the response, and only
 * the API's own addresses differ from production, through the variables the Caddyfile declares.
 */
const API_ORIGIN = "http://127.0.0.1:5196";
const HUB_ORIGIN = "ws://127.0.0.1:5196";
const APP_ORIGIN = "http://127.0.0.1:5197";
const CONTAINER = "orken-csp-e2e";
const OUT_DIR = "dist-e2e";

export default defineConfig({
  testDir: "./e2e",
  testMatch: "csp.e2e.spec.ts",
  timeout: 120_000,
  expect: { timeout: 30_000 },
  retries: 0,
  // Reports are read from the API's log, and a test owns what arrives while it runs: one at a time.
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  reporter: [["list"], ["html", { open: "never" }]],
  use: {
    baseURL: APP_ORIGIN,
    // No DOM snapshots: with requests intercepted the browser has no cache, and a snapshot of each step refetches every image.
    trace: { mode: "retain-on-failure", snapshots: false, screenshots: false },
    screenshot: "only-on-failure",
    // The full browser, not the headless shell: only it has the PDF viewer that an <object> opens.
    channel: "chromium",
    launchOptions: {
      args: [
        // The browser cannot resolve any other host: no test can reach production or staging, whatever a page asks for.
        "--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE 127.0.0.1",
        // WebGL in software, on a machine with no GPU: the 3D viewers must really draw.
        "--use-angle=swiftshader",
        "--enable-unsafe-swiftshader",
        "--ignore-gpu-blocklist",
      ],
    },
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"], channel: "chromium" } }],
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
      // A build of its own, pointed at the test host. Removes a container that a run stopped from outside may have left.
      command: [
        `(docker rm -f ${CONTAINER} || echo nothing to remove)`,
        `npx vite build --outDir ${OUT_DIR} --emptyOutDir --logLevel warn`,
        `docker run --rm --name ${CONTAINER} -p 127.0.0.1:5197:8080 -e PORT=8080 -e CSP_API_ORIGIN=${API_ORIGIN} -e CSP_HUB_ORIGIN=${HUB_ORIGIN} ` +
          `-v "${resolve("Caddyfile")}:/etc/caddy/Caddyfile:ro" -v "${resolve(OUT_DIR)}:/srv:ro" caddy:2-alpine`,
      ].join(" && "),
      env: { VITE_API_BASE_URL: `${API_ORIGIN}/api` },
      url: APP_ORIGIN,
      timeout: 300_000,
      reuseExistingServer: !process.env.CI,
    },
  ],
});
