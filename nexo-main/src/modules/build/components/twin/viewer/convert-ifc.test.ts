import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { BufferGeometry, Group, Mesh, type Scene } from "three";
import type { ElementMesh } from "../../../lib/ifc-convert";
import { convertIfcToGlb, meshesToGlb } from "./convert-ifc";

// The worker, the GLB exporter and web-ifc are faked: these tests cover the orchestration around them.
const h = vi.hoisted(() => ({
  exported: [] as Array<{ scene: unknown; options: unknown }>,
  exportResult: null as unknown,
  workers: [] as unknown[],
  script: null as null | ((w: { emit: (data: unknown) => void; fail: (message?: string) => void }) => void),
  ifcInits: [] as Array<(path: string) => string>,
  ifcDisposed: 0,
  extract: vi.fn(),
}));

vi.mock("three/examples/jsm/exporters/GLTFExporter.js", () => ({
  GLTFExporter: class {
    async parseAsync(scene: unknown, options: unknown) { h.exported.push({ scene, options }); return h.exportResult; }
  },
}));
vi.mock("web-ifc", () => ({
  IfcAPI: class {
    async Init(locate: (path: string) => string) { h.ifcInits.push(locate); }
    Dispose() { h.ifcDisposed++; }
  },
}));
vi.mock("web-ifc/web-ifc.wasm?url", () => ({ default: "/assets/web-ifc.wasm" }));
vi.mock("../../../lib/ifc-convert", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../../lib/ifc-convert")>()),
  extractElementMeshes: h.extract,
}));

class FakeWorker {
  onmessage: ((e: { data: unknown }) => void) | null = null;
  onerror: ((e: { message: string }) => void) | null = null;
  posted: Array<{ data: unknown; transfer: unknown }> = [];
  terminate = vi.fn();
  constructor(public url: URL, public options: unknown) { h.workers.push(this); }
  postMessage(data: unknown, transfer: unknown) {
    this.posted.push({ data, transfer });
    queueMicrotask(() => h.script?.({
      emit: (d) => this.onmessage?.({ data: d }),
      fail: (message = "") => this.onerror?.({ message }),
    }));
  }
}

const mesh = (globalId: string, color: [number, number, number, number]): ElementMesh => ({
  globalId, expressId: 1,
  positions: new Float32Array([0, 0, 0, 1, 0, 0, 0, 1, 0]),
  normals: new Float32Array([0, 0, 1, 0, 0, 1, 0, 0, 1]),
  indices: new Uint32Array([0, 1, 2]),
  color,
});

const ifcFile = () => {
  const bytes = new Uint8Array([73, 83, 79]).buffer;
  return Object.assign(new File(["ISO"], "casa.ifc"), { arrayBuffer: async () => bytes }) as File;
};

describe("convertIfcToGlb", () => {
  beforeEach(() => {
    h.exported = [];
    h.exportResult = new ArrayBuffer(16);
    h.workers = [];
    h.ifcInits = [];
    h.ifcDisposed = 0;
    h.extract.mockReset();
    vi.stubGlobal("Worker", FakeWorker);
  });
  afterEach(() => vi.unstubAllGlobals());

  it("parses in a worker (bytes transferred), forwards its progress and exports one named node per element", async () => {
    const a = mesh("0aBcDeFgHiJkLmNoPqRsT1", [0.5, 0.5, 0.5, 1]);
    const b = mesh("0aBcDeFgHiJkLmNoPqRsT2", [0.5, 0.5, 0.5, 1]);
    const glass = mesh("0aBcDeFgHiJkLmNoPqRsT3", [0.2, 0.4, 0.9, 0.3]);
    h.script = ({ emit }) => {
      emit({ type: "progress", phase: "parse", done: 1, total: 1 });
      emit({ type: "progress", phase: "geometry", done: 2, total: 3 });
      emit({ type: "done", meshes: [a, b, glass] });
    };
    const progress = vi.fn();

    const out = await convertIfcToGlb(ifcFile(), progress);
    expect(out).toEqual({ glb: h.exportResult, elements: 3 });

    const worker = h.workers[0] as FakeWorker;
    expect(new URL(String(worker.url)).pathname).toMatch(/\/ifc-convert\.worker\.ts$/);
    expect(worker.options).toEqual({ type: "module" });
    const { data, transfer } = worker.posted[0] as { data: { bytes: ArrayBuffer }; transfer: ArrayBuffer[] };
    expect(transfer).toEqual([data.bytes]);
    expect(worker.terminate).toHaveBeenCalled();
    expect(progress.mock.calls).toEqual([
      ["parse", 1, 1], ["geometry", 2, 3], ["export", 0, 1], ["export", 1, 1],
    ]);

    // the exported scene: binary, root "ifc", nodes named by GlobalId, the same color shares one material
    const { scene, options } = h.exported[0] as { scene: Scene; options: unknown };
    expect(options).toEqual({ binary: true });
    const root = scene.children[0] as Group;
    expect(root.name).toBe("ifc");
    const meshes = root.children as Mesh[];
    expect(meshes.map((m) => m.name)).toEqual([a.globalId, b.globalId, glass.globalId]);
    expect(meshes[0].material).toBe(meshes[1].material);
    expect(meshes[2].material).toMatchObject({ transparent: true, opacity: 0.3 });
    expect(meshes[0].geometry.getIndex()?.count).toBe(3);
  });

  it("the worker's error message rejects the conversion and the worker is terminated", async () => {
    h.script = ({ emit }) => emit({ type: "error", message: "IFC inválido" });
    await expect(convertIfcToGlb(ifcFile())).rejects.toThrow("IFC inválido");
    expect((h.workers[0] as FakeWorker).terminate).toHaveBeenCalled();
  });

  it("a worker crash rejects with its message, or a default one", async () => {
    h.script = ({ fail }) => fail("out of memory");
    await expect(convertIfcToGlb(ifcFile())).rejects.toThrow("out of memory");
    h.script = ({ fail }) => fail();
    await expect(convertIfcToGlb(ifcFile())).rejects.toThrow("Falha ao ler o IFC.");
  });

  it("an IFC without elements with geometry is refused before exporting", async () => {
    h.script = ({ emit }) => emit({ type: "done", meshes: [] });
    await expect(convertIfcToGlb(ifcFile())).rejects.toThrow("Nenhum elemento com geometria e GlobalId foi encontrado neste IFC.");
    expect(h.exported).toHaveLength(0);
  });

  it("without Web Workers parses on the main thread with web-ifc (local wasm) and disposes it", async () => {
    vi.stubGlobal("Worker", undefined);
    h.extract.mockImplementation((_api, bytes: Uint8Array, onProgress?: (p: string, d: number, t: number) => void) => {
      expect(Array.from(bytes)).toEqual([73, 83, 79]);
      onProgress?.("geometry", 1, 1);
      return [mesh("0aBcDeFgHiJkLmNoPqRsT1", [1, 0, 0, 1])];
    });
    const progress = vi.fn();
    await expect(convertIfcToGlb(ifcFile(), progress)).resolves.toMatchObject({ elements: 1 });
    expect(h.workers).toHaveLength(0);
    expect(h.ifcInits[0]("web-ifc.wasm")).toBe("/assets/web-ifc.wasm");
    expect(h.ifcInits[0]("other.js")).toBe("other.js");
    expect(h.ifcDisposed).toBe(1);
    expect(progress).toHaveBeenCalledWith("geometry", 1, 1);
  });
});

describe("meshesToGlb", () => {
  beforeEach(() => { h.exported = []; });

  it("a non-binary exporter result is an error, and the geometries are disposed anyway", async () => {
    h.exportResult = { json: true };
    const dispose = vi.spyOn(BufferGeometry.prototype, "dispose");
    await expect(meshesToGlb([mesh("0aBcDeFgHiJkLmNoPqRsT1", [1, 1, 1, 1])])).rejects.toThrow("O exportador não gerou um GLB binário.");
    expect(dispose).toHaveBeenCalled();
    dispose.mockRestore();
  });
});
