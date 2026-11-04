import { describe, it, expect } from "vitest";
import { isClick, viewerNodeIds } from "./twin-viewer";

describe("viewer node ids", () => {
  it("GLB/glTF: unique names, else node:{index} (the server's rule)", () => {
    const ids = viewerNodeIds([
      { index: 0, name: "Grupo", hasMesh: false, parent: null },
      { index: 1, name: "Parede", hasMesh: true, parent: 0 },
      { index: 2, name: "Porta", hasMesh: true, parent: 0 },
      { index: 3, name: "Porta", hasMesh: true, parent: 0 },
      { index: 4, name: null, hasMesh: true, parent: 0 },
      { index: 5, name: "node:9", hasMesh: true, parent: 0 },
    ], "Glb");
    expect([...ids.entries()]).toEqual([[1, "Parede"], [2, "node:2"], [3, "node:3"], [4, "node:4"], [5, "node:5"]]);
  });

  it("IFC viewer: the GlobalId name, or the nearest named ancestor for unnamed primitives", () => {
    const ids = viewerNodeIds([
      { index: 0, name: "2O2Fr$t4X7Zf8NOew3FLOH", hasMesh: false, parent: null },
      { index: 1, name: null, hasMesh: true, parent: 0 },
      { index: 2, name: "1hOSvn6df7F8_7GcBWlRGQ", hasMesh: true, parent: null },
    ], "Ifc");
    expect(ids.get(1)).toBe("2O2Fr$t4X7Zf8NOew3FLOH");
    expect(ids.get(2)).toBe("1hOSvn6df7F8_7GcBWlRGQ");
  });

  it("a short pointer move is a click, a drag is not", () => {
    expect(isClick({ x: 0, y: 0 }, { x: 3, y: 3 })).toBe(true);
    expect(isClick({ x: 0, y: 0 }, { x: 20, y: 0 })).toBe(false);
  });
});
