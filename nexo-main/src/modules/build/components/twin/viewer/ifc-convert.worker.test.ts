import { describe, it, expect, vi, beforeAll, beforeEach, afterAll } from "vitest";
import { waitFor } from "@testing-library/react";
import type { ElementMesh } from "../../../lib/ifc-convert";

// The worker module registers `self.onmessage`; web-ifc and the mesh extraction are faked.
const h = vi.hoisted(() => ({
  inits: [] as Array<(path: string) => string>,
  disposed: 0,
  disposeThrows: false,
  initFails: false,
  extract: vi.fn(),
}));

vi.mock("web-ifc", () => ({
  IfcAPI: class {
    async Init(locate: (path: string) => string) {
      h.inits.push(locate);
      if (h.initFails) throw new Error("wasm não carregou");
    }
    Dispose() { h.disposed++; if (h.disposeThrows) throw new Error("gone"); }
  },
}));
vi.mock("web-ifc/web-ifc.wasm?url", () => ({ default: "/assets/web-ifc.wasm" }));
vi.mock("../../../lib/ifc-convert", () => ({ extractElementMeshes: h.extract }));

type Posted = { message: { type: string; [k: string]: unknown }; transfer?: unknown[] };

describe("ifc-convert.worker", () => {
  let posted: Posted[];
  let postSpy: ReturnType<typeof vi.spyOn>;
  const send = (bytes: ArrayBuffer) =>
    (self as unknown as { onmessage: (e: { data: { bytes: ArrayBuffer } }) => void }).onmessage({ data: { bytes } });

  beforeAll(async () => {
    postSpy = vi.spyOn(window, "postMessage").mockImplementation(((message: Posted["message"], transfer?: unknown[]) => {
      posted.push({ message, transfer });
    }) as never);
    await import("./ifc-convert.worker");
  });
  afterAll(() => { postSpy.mockRestore(); (self as unknown as { onmessage: null }).onmessage = null; });
  beforeEach(() => {
    posted = [];
    h.inits = [];
    h.disposed = 0;
    h.disposeThrows = false;
    h.initFails = false;
    h.extract.mockReset();
  });

  it("parses the bytes, throttles geometry progress and posts the meshes with their buffers as transferables", async () => {
    const mesh: ElementMesh = {
      globalId: "g", expressId: 1, positions: new Float32Array(3), normals: new Float32Array(3), indices: new Uint32Array(3),
      color: [1, 1, 1, 1],
    };
    h.extract.mockImplementation((_api, bytes: Uint8Array, onProgress: (p: string, d: number, t: number) => void) => {
      expect(Array.from(bytes)).toEqual([1, 2, 3]);
      const now = vi.spyOn(Date, "now");
      now.mockReturnValue(1000);
      onProgress("parse", 1, 1);
      now.mockReturnValue(1100);
      onProgress("geometry", 1, 10);   // < 150 ms after the parse: skipped
      now.mockReturnValue(1200);
      onProgress("geometry", 2, 10);   // 200 ms: sent
      now.mockReturnValue(1210);
      onProgress("geometry", 10, 10);  // the last one always goes
      now.mockRestore();
      onProgress("export", 0, 1);      // not a worker phase: ignored
      return [mesh];
    });

    send(new Uint8Array([1, 2, 3]).buffer);
    await waitFor(() => expect(posted.some((p) => p.message.type === "done")).toBe(true));

    expect(h.inits[0]("web-ifc.wasm")).toBe("/assets/web-ifc.wasm");
    expect(h.inits[0]("x.js")).toBe("x.js");
    expect(posted.filter((p) => p.message.type === "progress").map((p) => [p.message.phase, p.message.done])).toEqual([
      ["parse", 1], ["geometry", 2], ["geometry", 10],
    ]);
    const done = posted.find((p) => p.message.type === "done")!;
    expect(done.message.meshes).toEqual([mesh]);
    expect(done.transfer).toEqual([mesh.positions.buffer, mesh.normals.buffer, mesh.indices.buffer]);
    expect(h.disposed).toBe(1);
  });

  it("a failure is posted as an error message and web-ifc is still disposed (even if that throws)", async () => {
    h.initFails = true;
    h.disposeThrows = true;
    send(new ArrayBuffer(1));
    await waitFor(() => expect(posted).toEqual([{ message: { type: "error", message: "wasm não carregou" }, transfer: undefined }]));
    expect(h.disposed).toBe(1);

    posted = [];
    h.initFails = false;
    h.disposeThrows = false;
    h.extract.mockImplementation(() => { throw "not an Error"; });
    send(new ArrayBuffer(1));
    await waitFor(() => expect(posted[0]?.message).toEqual({ type: "error", message: "Falha ao ler o IFC." }));
  });
});
