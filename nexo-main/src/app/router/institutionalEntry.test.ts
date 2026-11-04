import { describe, it, expect } from "vitest";
import { INSTITUTIONAL_ENTRY, institutionalEntry } from "./institutionalEntry";

describe("institutionalEntry", () => {
  it("sends the root of www to the Orken portal, with the query string", () => {
    expect(institutionalEntry("www.orken.com.br", "/", "")).toBe("https://app.orken.com.br/");
    expect(institutionalEntry("www.orken.com.br", "/", "?utm_source=jornal&b=1")).toBe(
      "https://app.orken.com.br/?utm_source=jornal&b=1",
    );
    expect(institutionalEntry("WWW.Orken.com.br", "/", "")).toBe(INSTITUTIONAL_ENTRY);
    expect(institutionalEntry("www.orken.com.br", "/", "?a=1", "#produtos")).toBe("https://app.orken.com.br/?a=1#produtos");
  });

  it("keeps every client path on www", () => {
    for (const path of [
      "/solutie/shoppingeusebio",
      "/cantina-da-ana",
      "/menu/cantina-da-ana",
      "/agendar/barbearia",
      "/agendar/barbearia/reserva/abc123",
      "/rastrear/abc123",
      "/login",
      "/redefinir-senha",
      "/verify-email",
    ]) {
      expect(institutionalEntry("www.orken.com.br", path, "?token=x")).toBeNull();
    }
  });

  it("leaves the root of erp and of any other host alone", () => {
    expect(institutionalEntry("erp.orken.com.br", "/", "")).toBeNull();
    expect(institutionalEntry("app.orken.com.br", "/", "")).toBeNull();
    expect(institutionalEntry("localhost", "/", "")).toBeNull();
  });
});
