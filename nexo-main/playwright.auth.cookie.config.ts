/** The same suite and servers as playwright.auth.config.ts, on the bundle of the cookie mode. `npm run test:e2e:cookie`. */
process.env.AUTH_E2E_SESSION_MODE = "cookie";

const { default: config } = await import("./playwright.auth.config");

export default config;
