import { describe, it, expect } from "vitest";
import { comparePtBr } from "./collation";

describe("comparePtBr", () => {
  it("keeps accented names next to their base letter", () => {
    const names = ["Aeroporto", "Água Fria", "Aldeota", "Siqueira", "São Gerardo", "Sapiranga", "Washington Soares"];
    expect([...names].sort(comparePtBr)).toEqual([
      "Aeroporto", "Água Fria", "Aldeota", "São Gerardo", "Sapiranga", "Siqueira", "Washington Soares",
    ]);
  });

  it("does not put lowercase names after every uppercase one", () => {
    expect(["Zeca", "ana", "Bruno"].sort(comparePtBr)).toEqual(["ana", "Bruno", "Zeca"]);
  });
});
