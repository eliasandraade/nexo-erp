import { describe, it, expect, vi } from "vitest";
import {
  buildViewerForm, extractElementMeshes, globalIdOf, mergeParts, overallPercent, transformVertices, uploadConvertedViewer,
  type IfcApiLike,
} from "./ifc-convert";

// web-ifc is never loaded in jsdom: the pipeline takes an IfcAPI-shaped object, mocked here.
vi.mock("web-ifc", () => ({ IfcAPI: vi.fn() }));

const IDENTITY = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
const TRANSLATE_X10 = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 10, 0, 0, 1];

/** One triangle, interleaved [x,y,z,nx,ny,nz]. */
const TRIANGLE = new Float32Array([0, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 1]);

function fakeApi(meshes: Array<{ expressID: number; geometries: Array<{ geometryExpressID: number; flatTransformation: number[] }> }>,
  globalIds: Record<number, string | undefined>): IfcApiLike & { closed: number[] } {
  const closed: number[] = [];
  return {
    closed,
    OpenModel: vi.fn(() => 7),
    CloseModel: vi.fn((id: number) => { closed.push(id); }),
    StreamAllMeshes: vi.fn((_id, cb) => meshes.forEach((m, i) => cb({
      expressID: m.expressID,
      geometries: { size: () => m.geometries.length,
        get: (k: number) => ({ color: { x: 0.5, y: 0.5, z: 0.5, w: 1 }, ...m.geometries[k] }) },
    }, i, meshes.length))),
    GetGeometry: vi.fn(() => ({ GetVertexData: () => 1, GetVertexDataSize: () => 18, GetIndexData: () => 2, GetIndexDataSize: () => 3, delete: vi.fn() })),
    GetVertexArray: vi.fn(() => TRIANGLE),
    GetIndexArray: vi.fn(() => new Uint32Array([0, 1, 2])),
    GetLine: vi.fn((_m: number, expressID: number) => (globalIds[expressID] ? { GlobalId: { value: globalIds[expressID] } } : {})),
  };
}

describe("IFC → GLB pipeline (pure parts)", () => {
  it("names each element with its GlobalId, merges its placed geometries and skips elements without GlobalId", () => {
    const api = fakeApi([
      { expressID: 11, geometries: [{ geometryExpressID: 100, flatTransformation: IDENTITY }, { geometryExpressID: 101, flatTransformation: TRANSLATE_X10 }] },
      { expressID: 12, geometries: [{ geometryExpressID: 102, flatTransformation: IDENTITY }] },
      { expressID: 13, geometries: [{ geometryExpressID: 103, flatTransformation: IDENTITY }] },
    ], { 11: "2O2Fr$t4X7Zf8NOew3FLOH", 12: "1hOSvn6df7F8_7GcBWlRGQ" });
    const progress = vi.fn();
    const meshes = extractElementMeshes(api, new Uint8Array([1, 2, 3]), progress);

    expect(meshes.map((m) => m.globalId)).toEqual(["2O2Fr$t4X7Zf8NOew3FLOH", "1hOSvn6df7F8_7GcBWlRGQ"]);
    const wall = meshes[0];
    expect(wall.positions.length).toBe(6 * 3);                    // two triangles merged
    expect(Array.from(wall.indices)).toEqual([0, 1, 2, 3, 4, 5]);  // second part offset by 3 vertices
    expect(wall.positions[9]).toBe(10);                            // world transform baked (x + 10)
    expect(api.closed).toEqual([7]);                               // model always closed
    expect(progress).toHaveBeenCalledWith("geometry", 3, 3);
  });

  it("reads the GlobalId from GetLine and tolerates lines without it", () => {
    const api = { GetLine: vi.fn((_m: number, id: number) => (id === 1 ? { GlobalId: { value: " abc " } } : null)) };
    expect(globalIdOf(api, 0, 1)).toBe("abc");
    expect(globalIdOf(api, 0, 2)).toBeNull();
    expect(globalIdOf({ GetLine: () => { throw new Error("x"); } }, 0, 3)).toBeNull();
  });

  it("transforms vertices with a column-major matrix and keeps normals unit length", () => {
    const { positions, normals } = transformVertices(TRIANGLE, TRANSLATE_X10);
    expect(Array.from(positions.slice(0, 3))).toEqual([10, 0, 0]);
    expect(Math.hypot(normals[0], normals[1], normals[2])).toBeCloseTo(1);
    const merged = mergeParts([
      { positions: new Float32Array(9), normals: new Float32Array(9), indices: new Uint32Array([0, 1, 2]) },
      { positions: new Float32Array(9), normals: new Float32Array(9), indices: new Uint32Array([0, 2, 1]) },
    ]);
    expect(Array.from(merged.indices)).toEqual([0, 1, 2, 3, 5, 4]);
  });

  it("uploads the GLB as multipart 'file' (viewer.glb, model/gltf-binary) to the version, reporting the upload phase", async () => {
    const upload = vi.fn(async (_id: string, _form: FormData, onProgress?: (l: number, t: number) => void) => {
      onProgress?.(50, 100);
      return { id: "v1", hasViewer: true };
    });
    const progress = vi.fn();
    const glb = new Uint8Array([0x67, 0x6c, 0x54, 0x46]).buffer;
    const res = await uploadConvertedViewer("v1", glb, upload, progress);

    expect(res).toEqual({ id: "v1", hasViewer: true });
    expect(upload).toHaveBeenCalledTimes(1);
    const [versionId, form] = upload.mock.calls[0];
    expect(versionId).toBe("v1");
    const file = form.get("file") as File;
    expect(file).toBeInstanceOf(Blob);
    expect(file.name).toBe("viewer.glb");
    expect(file.type).toBe("model/gltf-binary");
    expect(file.size).toBe(4);
    expect(progress).toHaveBeenCalledWith("upload", 50, 100);
    expect(buildViewerForm(new Blob(["x"]), "a.glb").get("file")).toBeInstanceOf(Blob);
  });

  it("overall progress grows across the phases", () => {
    expect(overallPercent("parse", 0, 1)).toBe(0);
    expect(overallPercent("geometry", 1, 2)).toBe(40);
    expect(overallPercent("upload", 1, 1)).toBe(100);
  });
});
