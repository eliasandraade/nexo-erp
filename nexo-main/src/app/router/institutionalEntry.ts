/**
 * The Orken institutional entry is the portal on app.orken.com.br. On www the server already answers a visit to "/"
 * with a 307 there (Caddyfile); links inside the app that point to "/" (the sign-in header) never reach the server,
 * so the router sends them the same way. On erp.orken.com.br the root is the ERP's entry (ErpEntry).
 */
export const INSTITUTIONAL_ENTRY = "https://app.orken.com.br/";

const INSTITUTIONAL_HOSTS: ReadonlySet<string> = new Set(["www.orken.com.br"]);

/**
 * Where the root of this host goes, with its query string and fragment (as after the 307); null when the app serves
 * the path.
 */
export function institutionalEntry(hostname: string, pathname: string, search: string, hash = ""): string | null {
  if (pathname !== "/" || !INSTITUTIONAL_HOSTS.has(hostname.toLowerCase())) return null;
  return `${INSTITUTIONAL_ENTRY}${search}${hash}`;
}
