import { describe, it, expect } from "vitest";
import { getPortalTheme, themeVars, isHexColor, hexTint } from "./portal-theme";

describe("getPortalTheme", () => {
  it.each([
    ["clinica-medica", "clinica"],
    ["nutricionista", "nutri"],
    ["salao-beleza", "salao"],
    ["pet-shop", "pet"],
    ["personal-trainer", "personal"],
    ["oficina-mecanica", "oficina"],
    ["programador-autonomo", "tech"],
    ["autoescola", "tech"],
    ["escola-idiomas", "escola"],
  ])("maps preset %s to theme %s", (preset, themeKey) => {
    expect(getPortalTheme(preset).key).toBe(themeKey);
  });

  it.each([null, undefined, "", "unknown-vertical"])("falls back to default for %s", (preset) => {
    expect(getPortalTheme(preset as string | null).key).toBe("default");
  });

  it("every theme defines core tokens and fonts", () => {
    const t = getPortalTheme("salao-beleza");
    expect(t.accent).toMatch(/^#[0-9a-f]{6}$/i);
    expect(t.display).toContain("Fraunces");
    expect(t.body).toContain("Manrope");
  });
});

describe("themeVars", () => {
  it("exposes the theme accent by default", () => {
    const t = getPortalTheme("clinica-medica");
    expect(themeVars(t)["--p-accent"]).toBe(t.accent);
    expect(themeVars(t)["--p-radius"]).toBe(`${t.radius}px`);
  });

  it("a valid store brand color overrides the accent", () => {
    const t = getPortalTheme("pet-shop");
    expect(themeVars(t, "#FF8800")["--p-accent"]).toBe("#FF8800");
  });

  it("an invalid brand color is ignored (keeps the theme accent)", () => {
    const t = getPortalTheme("pet-shop");
    expect(themeVars(t, "orange")["--p-accent"]).toBe(t.accent);
    expect(themeVars(t, "#fff")["--p-accent"]).toBe(t.accent);
  });
});

describe("isHexColor / hexTint", () => {
  it.each([["#ff0000", true], ["#AABBCC", true], ["#fff", false], ["red", false], ["", false]])(
    "isHexColor(%s) = %s", (v, expected) => expect(isHexColor(v)).toBe(expected));

  it("hexTint lightens toward white", () => {
    expect(hexTint("#000000", 0)).toBe("#ffffff");
    expect(hexTint("#000000", 1)).toBe("#000000");
    expect(hexTint("#3366cc", 0.5)).toMatch(/^#[0-9a-f]{6}$/);
  });
});
