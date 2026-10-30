import { defineConfig } from "@playwright/test";
export default defineConfig({
  testDir: "./e2e",
  testMatch: "build-portal.spec.ts",
  workers: 1,
  timeout: 60000,
  expect: { timeout: 20000 },
  use: {
    baseURL: "http://127.0.0.1:5193",
    channel: process.platform === "win32" ? "msedge" : undefined,
    screenshot: "only-on-failure",
  },
  webServer: {
    command: "npm run dev -- --host 127.0.0.1 --port 5193",
    url: "http://127.0.0.1:5193",
    env: { VITE_BUILD_PORTAL_REVIEW: "1" },
    reuseExistingServer: false,
  },
});
