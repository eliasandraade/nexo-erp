import { describe, it, expect } from "vitest";
import { isHexColor, portalPublicUrl, slugify, slugProblem, trimHyphens } from "./portal";
import { parseSite } from "./site";

describe("portal helpers", () => {
  it("slugify strips accents, spaces and invalid characters", () => {
    expect(slugify("Construtora São João  Ltda!")).toBe("construtora-sao-joao-ltda");
    expect(trimHyphens("-obra-nova-")).toBe("obra-nova");
  });
  it("slugProblem mirrors the server's length and hyphen rules", () => {
    expect(slugProblem("ab")).toMatch(/entre 3 e 60/);
    expect(slugProblem("obra-")).toMatch(/hífen/);
    expect(slugProblem("obra-nova")).toBeNull();
  });
  it("isHexColor accepts only #RRGGBB", () => {
    expect(isHexColor("#a1B2c3")).toBe(true);
    expect(isHexColor("#abc")).toBe(false);
    expect(isHexColor("a1b2c3f")).toBe(false);
  });
  it("portalPublicUrl uses the company and project slugs", () => {
    expect(portalPublicUrl("silva", "parque")).toBe("https://www.orken.com.br/silva/parque");
    expect(portalPublicUrl(null, "parque")).toBe("https://www.orken.com.br/{empresa}/parque");
  });
});

describe("parseSite", () => {
  it("accepts empty or a valid pair, rejects ranges and a lone coordinate", () => {
    expect(parseSite({ latitude: "", longitude: "", areaM2: "" })).toEqual({ value: { latitude: null, longitude: null, areaM2: null } });
    expect(parseSite({ latitude: "-23,5", longitude: "-46.6", areaM2: "120" }))
      .toEqual({ value: { latitude: -23.5, longitude: -46.6, areaM2: 120 } });
    expect(parseSite({ latitude: "10", longitude: "181", areaM2: "" })).toHaveProperty("error");
    expect(parseSite({ latitude: "10", longitude: "", areaM2: "" })).toHaveProperty("error");
    expect(parseSite({ latitude: "", longitude: "", areaM2: "-1" })).toHaveProperty("error");
  });
});
