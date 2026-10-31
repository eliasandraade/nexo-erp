// Web Worker: parses the IFC with web-ifc (WASM) off the main thread and sends back one merged mesh per element,
// already in world coordinates and named by GlobalId. three.js (GLB export) stays on the main thread, in the lazy viewer chunk.
import { IfcAPI } from "web-ifc";
import wasmUrl from "web-ifc/web-ifc.wasm?url";
import { extractElementMeshes, type ElementMesh, type IfcApiLike } from "../../../lib/ifc-convert";

export type WorkerRequest = { bytes: ArrayBuffer };
export type WorkerMessage =
  | { type: "progress"; phase: "parse" | "geometry"; done: number; total: number }
  | { type: "done"; meshes: ElementMesh[] }
  | { type: "error"; message: string };

interface WorkerScope {
  onmessage: ((e: MessageEvent<WorkerRequest>) => void) | null;
  postMessage(message: WorkerMessage, transfer?: Transferable[]): void;
}
const ctx = self as unknown as WorkerScope;

async function handle(e: MessageEvent<WorkerRequest>): Promise<void> {
  const api = new IfcAPI();
  try {
    // the hashed asset URL from Vite; single thread: no SharedArrayBuffer / COOP-COEP needed
    await api.Init((path: string) => (path.endsWith(".wasm") ? wasmUrl : path), true);
    let last = 0;
    const meshes = extractElementMeshes(api as unknown as IfcApiLike, new Uint8Array(e.data.bytes), (phase, done, total) => {
      if (phase !== "parse" && phase !== "geometry") return;
      const now = Date.now();
      if (phase === "geometry" && done < total && now - last < 150) return;
      last = now;
      ctx.postMessage({ type: "progress", phase, done, total });
    });
    const transfer: Transferable[] = [];
    for (const m of meshes) transfer.push(m.positions.buffer, m.normals.buffer, m.indices.buffer);
    ctx.postMessage({ type: "done", meshes }, transfer);
  } catch (err) {
    ctx.postMessage({ type: "error", message: err instanceof Error ? err.message : "Falha ao ler o IFC." });
  } finally {
    try { api.Dispose(); } catch { /* already gone */ }
  }
}

ctx.onmessage = (e) => { void handle(e); };
