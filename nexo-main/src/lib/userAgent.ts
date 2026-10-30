const BROWSERS: [string, string][] = [["Edg/", "Edge"], ["Chrome/", "Chrome"], ["Firefox/", "Firefox"], ["Safari/", "Safari"]];
const SYSTEMS: [string, string][] = [["Android", "Android"], ["iPhone", "iPhone/iPad"], ["iPad", "iPhone/iPad"], ["Windows", "Windows"], ["Mac OS X", "macOS"], ["Linux", "Linux"]];

/** A readable device name from the browser's user agent (best effort; first match wins). */
export function describeAgent(ua: string | null): string {
  if (!ua) return "Aparelho desconhecido";
  const browser = BROWSERS.find(([part]) => ua.includes(part))?.[1] ?? "Navegador";
  const os = SYSTEMS.find(([part]) => ua.includes(part))?.[1];
  return os ? `${browser} · ${os}` : browser;
}
