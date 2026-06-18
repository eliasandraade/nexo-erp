import { describe, it, expect } from "vitest";
import {
  SERVICE_MODULE_KEY,
  hasServiceModule,
  isValidPresetKey,
  SERVICE_PRESET_OPTIONS,
} from "./service-family";

describe("service-family (single-module model)", () => {
  it("the commercial module key is 'service'", () => {
    expect(SERVICE_MODULE_KEY).toBe("service");
  });

  it("hasServiceModule is true only when 'service' is present", () => {
    expect(hasServiceModule(["service"])).toBe(true);
    expect(hasServiceModule(["varejo", "service"])).toBe(true);
  });

  it("hasServiceModule is false without the 'service' module (a preset key is NOT the module)", () => {
    expect(hasServiceModule(["varejo", "build"])).toBe(false);
    expect(hasServiceModule(["salao-beleza"])).toBe(false);
    expect(hasServiceModule([])).toBe(false);
    expect(hasServiceModule(undefined)).toBe(false);
  });

  it("offers the nine internal presets for onboarding", () => {
    expect(SERVICE_PRESET_OPTIONS).toHaveLength(9);
    expect(SERVICE_PRESET_OPTIONS.every((o) => o.key && o.label)).toBe(true);
  });

  it.each([
    "clinica-medica", "salao-beleza", "pet-shop", "oficina-mecanica", "nutricionista",
    "personal-trainer", "autoescola", "escola-idiomas", "programador-autonomo",
  ])("isValidPresetKey is true for preset %s", (key) => {
    expect(isValidPresetKey(key)).toBe(true);
  });

  it.each(["service", "varejo", "build", ""])(
    "isValidPresetKey is false for non-preset %s",
    (key) => {
      expect(isValidPresetKey(key)).toBe(false);
    },
  );
});
