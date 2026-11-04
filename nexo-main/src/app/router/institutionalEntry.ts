/**
 * The Orken institutional entry is the portal on app.orken.com.br. On www the server already answers a visit to "/"
 * with a 307 there (Caddyfile); links inside the app that point to "/" (the sign-in header, the order tracking page)
 * never reach the server, so the router sends them the same way. erp.orken.com.br keeps its own root.
 */
export const INSTITUTIONAL_ENTRY = "https://app.orken.com.br/";

const INSTITUTIONAL_HOSTS: ReadonlySet<string> = new Set(["www.orken.com.br"]);

/** Where the root of this host goes, with its query string; null when the app serves the path. */
export function institutionalEntry(hostname: string, pathname: string, search: string): string | null {
  if (pathname !== "/" || !INSTITUTIONAL_HOSTS.has(hostname.toLowerCase())) return null;
  return `${INSTITUTIONAL_ENTRY}${search}`;
}
