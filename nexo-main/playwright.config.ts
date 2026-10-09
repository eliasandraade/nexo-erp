import { defineConfig, devices } from "@playwright/test";

/**
 * Browser end-to-end tests (e2e/). They need the API and the frontend running — they are NOT part
 * of `npm test` (Vitest only runs src/**). Run with `npm run test:e2e`.
 *   API_URL  (default http://localhost:5000)
 *   APP_URL  (default http://localhost:8080 — the Vite dev server)
 */
export default defineConfig({
  testDir: "./e2e",
  timeout: 30_000,
  retries: 0,
  use: {
    baseURL: process.env.APP_URL || "http://localhost:8080",
    trace: "retain-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
