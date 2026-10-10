import { describe, expect, it } from "vitest";
import { shortcutLabel } from "./shortcutLabel";

describe("shortcutLabel", () => {
  it("shows ⌘ K on Apple platforms", () => {
    expect(shortcutLabel("MacIntel")).toBe("⌘ K");
    expect(shortcutLabel("iPad")).toBe("⌘ K");
  });

  it("shows Ctrl K everywhere else", () => {
    expect(shortcutLabel("Win32")).toBe("Ctrl K");
    expect(shortcutLabel("Linux x86_64")).toBe("Ctrl K");
    expect(shortcutLabel("")).toBe("Ctrl K");
  });
});
