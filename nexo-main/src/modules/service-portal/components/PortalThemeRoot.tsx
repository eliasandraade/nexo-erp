import { useEffect, type CSSProperties, type ReactNode } from "react";
import { type PortalTheme, themeVars } from "../lib/portal-theme";

const FONT_ID = "orken-portal-fonts";
const FONT_HREF =
  "https://fonts.googleapis.com/css2?" +
  "family=Fraunces:opsz,wght@9..144,400;9..144,500;9..144,600;9..144,700&" +
  "family=Manrope:wght@400;500;600;700;800&display=swap";

function ensureFonts() {
  if (typeof document === "undefined" || document.getElementById(FONT_ID)) return;
  const pre1 = document.createElement("link");
  pre1.rel = "preconnect"; pre1.href = "https://fonts.googleapis.com";
  const pre2 = document.createElement("link");
  pre2.rel = "preconnect"; pre2.href = "https://fonts.gstatic.com"; pre2.crossOrigin = "anonymous";
  const link = document.createElement("link");
  link.id = FONT_ID; link.rel = "stylesheet"; link.href = FONT_HREF;
  document.head.append(pre1, pre2, link);
}

/**
 * Wraps the whole public portal: applies the vertical's theme as CSS custom properties (fully
 * isolated from the admin app's dark tokens), loads the display/body fonts once, and sets the
 * base surface. A store brand color (PR16) overrides the accent without changing the theme.
 */
export function PortalThemeRoot({
  theme, brandColor, children,
}: { theme: PortalTheme; brandColor?: string | null; children: ReactNode }) {
  useEffect(() => { ensureFonts(); }, []);

  const style = {
    ...themeVars(theme, brandColor),
    backgroundColor: "var(--p-bg)",
    color: "var(--p-ink)",
    fontFamily: "var(--p-body)",
  } as CSSProperties;

  return (
    <div style={style} className="min-h-screen w-full antialiased [text-rendering:optimizeLegibility]">
      {children}
    </div>
  );
}
