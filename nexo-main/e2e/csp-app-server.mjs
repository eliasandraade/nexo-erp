// The production server for the Content-Security-Policy suite (playwright.csp.config.ts): a production build pointed at the
// test host, served by the real Caddyfile on the image the Dockerfile uses. The policy under test is what this server sends, read
// from the response. Only the API's own addresses differ from production, through the variables the Caddyfile declares.
import { spawn, spawnSync } from "node:child_process";
import { resolve } from "node:path";

const API_ORIGIN = "http://127.0.0.1:5196";
const HUB_ORIGIN = "ws://127.0.0.1:5196";
const CONTAINER = "orken-csp-e2e";
const OUT_DIR = "dist-e2e";

const removeContainer = () => spawnSync("docker", ["rm", "-f", CONTAINER], { stdio: "ignore" });

const build = spawnSync(process.execPath, ["node_modules/vite/bin/vite.js", "build", "--outDir", OUT_DIR, "--emptyOutDir", "--logLevel", "warn"], {
  stdio: "inherit",
  env: { ...process.env, VITE_API_BASE_URL: `${API_ORIGIN}/api` },
});
if (build.status !== 0) process.exit(build.status ?? 1);

removeContainer();
const server = spawn("docker", [
  "run", "--rm", "--name", CONTAINER, "-p", "127.0.0.1:5197:8080",
  "-e", "PORT=8080", "-e", `CSP_API_ORIGIN=${API_ORIGIN}`, "-e", `CSP_HUB_ORIGIN=${HUB_ORIGIN}`,
  "-v", `${resolve("Caddyfile")}:/etc/caddy/Caddyfile:ro`, "-v", `${resolve(OUT_DIR)}:/srv:ro`,
  "caddy:2-alpine",
], { stdio: "inherit" });

for (const signal of ["SIGINT", "SIGTERM"]) {
  process.on(signal, () => {
    removeContainer();
    process.exit(0);
  });
}
server.on("exit", (code) => process.exit(code ?? 1));
