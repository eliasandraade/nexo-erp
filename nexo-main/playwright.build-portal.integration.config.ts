import { defineConfig } from "@playwright/test";
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
  webServer: {
    command: "npm run dev -- --host 127.0.0.1 --port 5195 --strictPort",
    url: "http://127.0.0.1:5195",
    env: { VITE_API_BASE_URL: "http://127.0.0.1:5194/api" },
    reuseExistingServer: false,
  },
});
