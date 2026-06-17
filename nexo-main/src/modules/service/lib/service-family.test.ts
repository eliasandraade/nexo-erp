import { describe, it, expect } from "vitest";
import { SERVICE_FAMILY_KEYS, isServiceFamilyKey, hasServiceModule } from "./service-family";

describe("service-family", () => {
  it("lists the nine v1 service verticals", () => {
    expect(SERVICE_FAMILY_KEYS).toHaveLength(9);
  });

  it.each([
    "clinica-medica",
    "salao-beleza",
    "pet-shop",
    "oficina-mecanica",
    "nutricionista",
    "personal-trainer",
    "autoescola",
    "escola-idiomas",
    "programador-autonomo",
  ])("recognizes %s as a service-family key", (key) => {
    expect(isServiceFamilyKey(key)).toBe(true);
  });

  it.each(["build", "varejo", "restaurante", "imobiliaria", "pousada-hotel", ""])(
    "rejects %s as a service-family key",
    (key) => {
      expect(isServiceFamilyKey(key)).toBe(false);
    },
  );

  it("hasServiceModule is true when any active module is a service-family key", () => {
    expect(hasServiceModule(["varejo", "salao-beleza"])).toBe(true);
  });

  it("hasServiceModule is false without any service-family key", () => {
    expect(hasServiceModule(["varejo", "build"])).toBe(false);
  });

  it("hasServiceModule tolerates undefined and empty input", () => {
    expect(hasServiceModule(undefined)).toBe(false);
    expect(hasServiceModule([])).toBe(false);
  });
});
