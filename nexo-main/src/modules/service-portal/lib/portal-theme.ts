/**
 * Adaptive per-vertical theming for the public booking portal. Each Service preset maps to a
 * distinct premium look (palette + type + mood) so a clínica feels serene, a salão elegant, a
 * pet shop friendly — one engine, nine personalities. Everything is expressed as CSS custom
 * properties applied on the portal root, fully isolated from the admin app's dark tokens.
 *
 * A store-provided brand color (PR16 branding) can override the accent without losing the theme.
 */

export type ThemeMood =
  | "serene" | "fresh" | "energetic" | "industrial" | "tech" | "elegant" | "friendly" | "scholarly";

export interface PortalTheme {
  key:        string;
  /** Display font stack (loaded by ThemeProvider). */
  display:    string;
  /** Body font stack. */
  body:       string;
  mood:       ThemeMood;
  /** Core palette — tinted neutrals, never pure black/white. */
  bg:         string;
  bgSoft:     string;
  surface:    string;
  line:       string;
  ink:        string;
  muted:      string;
  accent:     string;
  accentInk:  string;
  accentSoft: string;
  /** Hero wash. */
  heroFrom:   string;
  heroTo:     string;
  /** Corner rounding scale (px). */
  radius:     number;
}

const FRAUNCES = "'Fraunces', 'Georgia', serif";
const MANROPE = "'Manrope', 'Segoe UI', sans-serif";

const THEMES: Record<string, PortalTheme> = {
  clinica: {
    key: "clinica", display: FRAUNCES, body: MANROPE, mood: "serene",
    bg: "#f3f7f9", bgSoft: "#eaf1f4", surface: "#fbfdfe", line: "#dce8ec",
    ink: "#132834", muted: "#577284", accent: "#0e8a8f", accentInk: "#f4fffe", accentSoft: "#dff4f3",
    heroFrom: "#e6f3f4", heroTo: "#f6fbfb", radius: 20,
  },
  nutri: {
    key: "nutri", display: FRAUNCES, body: MANROPE, mood: "fresh",
    bg: "#f3f9f3", bgSoft: "#e9f3e8", surface: "#fbfdfa", line: "#dcebd9",
    ink: "#1c3322", muted: "#5b7561", accent: "#3f9d52", accentInk: "#f6fff7", accentSoft: "#e2f3e3",
    heroFrom: "#e8f5e6", heroTo: "#f6fbf4", radius: 22,
  },
  salao: {
    key: "salao", display: FRAUNCES, body: MANROPE, mood: "elegant",
    bg: "#f8f4f2", bgSoft: "#efe7e2", surface: "#fdfaf8", line: "#e9ddd6",
    ink: "#2a1f1c", muted: "#7a6760", accent: "#a8743f", accentInk: "#fffaf4", accentSoft: "#f1e4d6",
    heroFrom: "#f3e8e1", heroTo: "#faf4ef", radius: 18,
  },
  pet: {
    key: "pet", display: MANROPE, body: MANROPE, mood: "friendly",
    bg: "#fef6f2", bgSoft: "#fde9e0", surface: "#fffbf9", line: "#fadccf",
    ink: "#3a241c", muted: "#8a6a5d", accent: "#f06b4a", accentInk: "#fff6f3", accentSoft: "#ffe2d8",
    heroFrom: "#ffe7da", heroTo: "#fff5ef", radius: 26,
  },
  personal: {
    key: "personal", display: MANROPE, body: MANROPE, mood: "energetic",
    bg: "#f6f5f3", bgSoft: "#eceae6", surface: "#fcfbfa", line: "#e2ded7",
    ink: "#1c1a17", muted: "#6c655c", accent: "#e2592a", accentInk: "#fff6f2", accentSoft: "#fbe3d7",
    heroFrom: "#efe9e3", heroTo: "#faf7f3", radius: 16,
  },
  oficina: {
    key: "oficina", display: MANROPE, body: MANROPE, mood: "industrial",
    bg: "#f1f3f5", bgSoft: "#e4e8ec", surface: "#fafbfc", line: "#d7dee4",
    ink: "#18222b", muted: "#566571", accent: "#c2620c", accentInk: "#fff8f0", accentSoft: "#f6e3cd",
    heroFrom: "#e7ecf0", heroTo: "#f6f8fa", radius: 12,
  },
  tech: {
    key: "tech", display: MANROPE, body: MANROPE, mood: "tech",
    bg: "#f5f6fb", bgSoft: "#eaecf6", surface: "#fbfbfe", line: "#dfe2f0",
    ink: "#1a1b34", muted: "#5d6190", accent: "#5145e0", accentInk: "#f6f5ff", accentSoft: "#e6e4fb",
    heroFrom: "#e9eaf8", heroTo: "#f6f7fc", radius: 18,
  },
  escola: {
    key: "escola", display: FRAUNCES, body: MANROPE, mood: "scholarly",
    bg: "#f7f5f0", bgSoft: "#efeadf", surface: "#fdfbf7", line: "#e8e0d2",
    ink: "#241f17", muted: "#766a56", accent: "#b5532a", accentInk: "#fff7f3", accentSoft: "#f4e2d6",
    heroFrom: "#f1eadd", heroTo: "#faf6ef", radius: 18,
  },
  default: {
    key: "default", display: FRAUNCES, body: MANROPE, mood: "serene",
    bg: "#f5f6f9", bgSoft: "#eceef3", surface: "#fbfcfe", line: "#e0e3ea",
    ink: "#171a21", muted: "#5b6373", accent: "#4f46e5", accentInk: "#f6f6ff", accentSoft: "#e6e5fb",
    heroFrom: "#eceef6", heroTo: "#f7f8fb", radius: 18,
  },
};

const PRESET_THEME: Record<string, string> = {
  "clinica-medica": "clinica",
  "nutricionista": "nutri",
  "salao-beleza": "salao",
  "pet-shop": "pet",
  "personal-trainer": "personal",
  "oficina-mecanica": "oficina",
  "programador-autonomo": "tech",
  "autoescola": "tech",
  "escola-idiomas": "escola",
};

export function getPortalTheme(presetKey: string | undefined | null): PortalTheme {
  const themeKey = (presetKey && PRESET_THEME[presetKey]) || "default";
  return THEMES[themeKey] ?? THEMES.default;
}

/** Maps a theme (+ optional store brand color override) to the CSS custom properties the portal uses. */
export function themeVars(theme: PortalTheme, brandColor?: string | null): Record<string, string> {
  const accent = isHexColor(brandColor) ? brandColor! : theme.accent;
  return {
    "--p-bg": theme.bg,
    "--p-bg-soft": theme.bgSoft,
    "--p-surface": theme.surface,
    "--p-line": theme.line,
    "--p-ink": theme.ink,
    "--p-muted": theme.muted,
    "--p-accent": accent,
    "--p-accent-ink": theme.accentInk,
    "--p-accent-soft": isHexColor(brandColor) ? hexTint(brandColor!, 0.12) : theme.accentSoft,
    "--p-hero-from": theme.heroFrom,
    "--p-hero-to": theme.heroTo,
    "--p-radius": `${theme.radius}px`,
    "--p-display": theme.display,
    "--p-body": theme.body,
  };
}

export function isHexColor(v: string | null | undefined): v is string {
  return typeof v === "string" && /^#([0-9a-fA-F]{6})$/.test(v.trim());
}

/** Mixes a hex color toward white by `amount` (0..1) for soft accent tints. */
export function hexTint(hex: string, amount: number): string {
  const c = hex.replace("#", "");
  const r = parseInt(c.slice(0, 2), 16);
  const g = parseInt(c.slice(2, 4), 16);
  const b = parseInt(c.slice(4, 6), 16);
  const mix = (ch: number) => Math.round(ch + (255 - ch) * (1 - amount));
  const to2 = (n: number) => n.toString(16).padStart(2, "0");
  return `#${to2(mix(r))}${to2(mix(g))}${to2(mix(b))}`;
}
