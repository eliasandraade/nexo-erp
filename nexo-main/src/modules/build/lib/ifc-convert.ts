// IFC → viewer GLB in the browser (ViewerSource ConvertedBrowser). This file holds the pure, testable part of the
// pipeline: read every element's placed geometries from web-ifc, bake the world transforms, merge per element and name
// each one with its IFC GlobalId (the server matches node names to the version's elements — 422 viewer_mismatch below 50%).
// It imports nothing at runtime: web-ifc runs in a Web Worker (ifc-convert.worker.ts), three.js only in the lazy viewer chunk.

/** The slice of web-ifc's IfcAPI the pipeline uses (mocked in the tests). */
export interface IfcApiLike {
  OpenModel(data: Uint8Array, settings?: Record<string, unknown>): number;
  CloseModel(modelID: number): void;
  StreamAllMeshes(modelID: number, cb: (mesh: IfcFlatMeshLike, index: number, total: number) => void): void;
  GetGeometry(modelID: number, geometryExpressID: number): IfcGeometryLike;
  GetVertexArray(ptr: number, size: number): Float32Array;
  GetIndexArray(ptr: number, size: number): Uint32Array;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any -- web-ifc's own signature
  GetLine(modelID: number, expressID: number): any;
}

export interface IfcFlatMeshLike {
  expressID: number;
  geometries: { size(): number; get(i: number): IfcPlacedGeometryLike };
}

export interface IfcPlacedGeometryLike {
  color: { x: number; y: number; z: number; w: number };
  geometryExpressID: number;
  flatTransformation: number[];
}

export interface IfcGeometryLike {
  GetVertexData(): number;
  GetVertexDataSize(): number;
  GetIndexData(): number;
  GetIndexDataSize(): number;
  delete(): void;
}

/** One element, merged: world-space positions/normals, triangle indices, its first color (RGBA 0..1). */
export interface ElementMesh {
  globalId:  string;
  expressId: number;
  positions: Float32Array;
  normals:   Float32Array;
  indices:   Uint32Array;
  color:     [number, number, number, number];
}

export type ConvertProgress = (phase: "parse" | "geometry" | "export" | "upload", done: number, total: number) => void;

/** The element's IFC GlobalId (GetLine(...).GlobalId.value), or null when it has none. */
export function globalIdOf(api: Pick<IfcApiLike, "GetLine">, modelID: number, expressID: number): string | null {
  try {
    const line = api.GetLine(modelID, expressID);
    const value = line?.GlobalId?.value;
    return typeof value === "string" && value.trim() ? value.trim() : null;
  } catch {
    return null;
  }
}

/**
 * Applies a column-major 4×4 matrix (web-ifc's flatTransformation) to interleaved vertex data [x,y,z,nx,ny,nz]*,
 * returning world positions and normals.
 */
export function transformVertices(vertexData: Float32Array, m: number[]): { positions: Float32Array; normals: Float32Array } {
  const count = Math.floor(vertexData.length / 6);
  const positions = new Float32Array(count * 3);
  const normals = new Float32Array(count * 3);
  for (let i = 0; i < count; i++) {
    const x = vertexData[i * 6], y = vertexData[i * 6 + 1], z = vertexData[i * 6 + 2];
    const nx = vertexData[i * 6 + 3], ny = vertexData[i * 6 + 4], nz = vertexData[i * 6 + 5];
    positions[i * 3]     = m[0] * x + m[4] * y + m[8] * z + m[12];
    positions[i * 3 + 1] = m[1] * x + m[5] * y + m[9] * z + m[13];
    positions[i * 3 + 2] = m[2] * x + m[6] * y + m[10] * z + m[14];
    // rotation part only (IFC placements are rigid, no non-uniform scale)
    const tx = m[0] * nx + m[4] * ny + m[8] * nz;
    const ty = m[1] * nx + m[5] * ny + m[9] * nz;
    const tz = m[2] * nx + m[6] * ny + m[10] * nz;
    const len = Math.hypot(tx, ty, tz) || 1;
    normals[i * 3] = tx / len; normals[i * 3 + 1] = ty / len; normals[i * 3 + 2] = tz / len;
  }
  return { positions, normals };
}

/** Concatenates parts into one buffer set (indices offset by the vertices before them). */
export function mergeParts(parts: Array<{ positions: Float32Array; normals: Float32Array; indices: Uint32Array }>) {
  const vertexCount = parts.reduce((n, p) => n + p.positions.length / 3, 0);
  const indexCount = parts.reduce((n, p) => n + p.indices.length, 0);
  const positions = new Float32Array(vertexCount * 3);
  const normals = new Float32Array(vertexCount * 3);
  const indices = new Uint32Array(indexCount);
  let v = 0, k = 0;
  for (const p of parts) {
    positions.set(p.positions, v * 3);
    normals.set(p.normals, v * 3);
    for (let i = 0; i < p.indices.length; i++) indices[k + i] = p.indices[i] + v;
    v += p.positions.length / 3;
    k += p.indices.length;
  }
  return { positions, normals, indices };
}

/**
 * Reads every mesh of the IFC with web-ifc: world transforms baked, merged per element, named by GlobalId.
 * Elements without a GlobalId (rare: geometry outside an IfcProduct) are skipped — they could not be matched anyway.
 */
export function extractElementMeshes(api: IfcApiLike, bytes: Uint8Array, onProgress?: ConvertProgress): ElementMesh[] {
  onProgress?.("parse", 0, 1);
  const modelID = api.OpenModel(bytes, { COORDINATE_TO_ORIGIN: true });
  onProgress?.("parse", 1, 1);
  const byElement = new Map<number, { color: [number, number, number, number]; parts: Array<{ positions: Float32Array; normals: Float32Array; indices: Uint32Array }> }>();
  try {
    api.StreamAllMeshes(modelID, (mesh, index, total) => {
      const placed = mesh.geometries;
      for (let i = 0; i < placed.size(); i++) {
        const pg = placed.get(i);
        const geometry = api.GetGeometry(modelID, pg.geometryExpressID);
        try {
          const vertexData = api.GetVertexArray(geometry.GetVertexData(), geometry.GetVertexDataSize());
          const indexData = api.GetIndexArray(geometry.GetIndexData(), geometry.GetIndexDataSize());
          if (vertexData.length === 0 || indexData.length === 0) continue;
          const { positions, normals } = transformVertices(vertexData, pg.flatTransformation);
          let entry = byElement.get(mesh.expressID);
          if (!entry) {
            entry = { color: [pg.color.x, pg.color.y, pg.color.z, pg.color.w], parts: [] };
            byElement.set(mesh.expressID, entry);
          }
          entry.parts.push({ positions, normals, indices: Uint32Array.from(indexData) });
        } finally {
          geometry.delete();
        }
      }
      onProgress?.("geometry", index + 1, total);
    });

    const result: ElementMesh[] = [];
    for (const [expressId, entry] of byElement) {
      const globalId = globalIdOf(api, modelID, expressId);
      if (!globalId) continue;
      result.push({ globalId, expressId, color: entry.color, ...mergeParts(entry.parts) });
    }
    return result;
  } finally {
    api.CloseModel(modelID);
  }
}

/** Multipart body of PUT /models/versions/{id}/viewer: the binary GLB in the field "file". */
export function buildViewerForm(glb: ArrayBuffer | Blob, fileName = "viewer.glb"): FormData {
  const blob = glb instanceof Blob ? glb : new Blob([glb], { type: "model/gltf-binary" });
  const form = new FormData();
  form.append("file", blob, fileName);
  return form;
}

/** Quick local sanity check before uploading (the server re-checks): the share of names that are GlobalIds. */
export function namedShare(meshes: Pick<ElementMesh, "globalId">[]): number {
  if (meshes.length === 0) return 0;
  return meshes.filter((m) => /^[0-9A-Za-z_$]{22}$/.test(m.globalId)).length / meshes.length;
}

/** Sends the converted GLB to PUT …/viewer with the right multipart body; progress is reported as the "upload" phase. */
export function uploadConvertedViewer<T>(
  versionId: string,
  glb: ArrayBuffer,
  upload: (versionId: string, form: FormData, onProgress?: (loaded: number, total: number) => void) => Promise<T>,
  onProgress?: ConvertProgress,
): Promise<T> {
  return upload(versionId, buildViewerForm(glb), (loaded, total) => onProgress?.("upload", loaded, total));
}

/** SHA-256 (hex) of a local file — to confirm the user picked the same IFC as the version. null when unavailable. */
export async function sha256Hex(file: Blob): Promise<string | null> {
  if (!globalThis.crypto?.subtle) return null;
  return sha256HexOfBytes(await file.arrayBuffer());
}

/** SHA-256 (hex) of bytes already in memory, or null without WebCrypto. */
export async function sha256HexOfBytes(bytes: ArrayBuffer): Promise<string | null> {
  const subtle = globalThis.crypto?.subtle;
  if (!subtle) return null;
  const digest = await subtle.digest("SHA-256", bytes);
  return Array.from(new Uint8Array(digest), (b) => b.toString(16).padStart(2, "0")).join("");
}

/** Overall percentage of the conversion from the phase progress (parse 10%, geometry 60%, export 10%, upload 20%). */
export function overallPercent(phase: "parse" | "geometry" | "export" | "upload", done: number, total: number): number {
  const f = total > 0 ? Math.min(1, done / total) : 0;
  const base = { parse: 0, geometry: 10, export: 70, upload: 80 }[phase];
  const width = { parse: 10, geometry: 60, export: 10, upload: 20 }[phase];
  return Math.round(base + width * f);
}
