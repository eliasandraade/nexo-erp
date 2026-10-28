import { describe, it, expect } from "vitest";
import { legacyMenuTarget } from "./AppRouter";

describe("legacyMenuTarget", () => {
  it("keeps a normal slug as a same-origin path", () => {
    expect(legacyMenuTarget("cantina-da-ana")).toBe("/cantina-da-ana");
  });

  it("never turns a crafted slug into a redirect to another origin", () => {
    expect(legacyMenuTarget("\\example.com")).toBe("/example.com");
    expect(legacyMenuTarget("/example.com")).toBe("/example.com");
    expect(legacyMenuTarget("\\\\example.com")).toBe("/example.com");
    expect(legacyMenuTarget(undefined)).toBe("/");
  });
});
