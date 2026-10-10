import { type CSSProperties, type ReactNode } from "react";
import { type PortalTheme, themeVars } from "../lib/portal-theme";

/**
 * Wraps the whole public portal: applies the vertical's theme as CSS custom properties (fully
 * isolated from the admin app tokens), uses native fonts, and sets the
 * base surface. A store brand color (PR16) overrides the accent without changing the theme.
 */
export function PortalThemeRoot({
  theme, brandColor, children,
}: { theme: PortalTheme; brandColor?: string | null; children: ReactNode }) {

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
