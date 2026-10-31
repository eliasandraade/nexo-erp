// Lazy viewer chunk only (imports three.js). Orchestrates IFC → GLB in the browser:
//   1. web-ifc parses the IFC inside a Web Worker (the heavy part — the page stays responsive);
//   2. the merged meshes come back as transferable buffers and three's GLTFExporter writes a binary GLB, one node per
//      element named with its GlobalId.
// Without Worker support (very old browsers) the same pipeline runs on the main thread behind the blocking progress dialog.
import { BufferAttribute, BufferGeometry, Color, Group, Mesh, MeshStandardMaterial, Scene } from "three";
import { GLTFExporter } from "three/examples/jsm/exporters/GLTFExporter.js";
import { extractElementMeshes, type ConvertProgress, type ElementMesh, type IfcApiLike } from "../../../lib/ifc-convert";
import type { WorkerMessage } from "./ifc-convert.worker";

async function parseInWorker(bytes: ArrayBuffer, onProgress?: ConvertProgress): Promise<ElementMesh[]> {
  const worker = new Worker(new URL("./ifc-convert.worker.ts", import.meta.url), { type: "module" });
  try {
    return await new Promise<ElementMesh[]>((resolve, reject) => {
      worker.onmessage = (e: MessageEvent<WorkerMessage>) => {
        const msg = e.data;
        if (msg.type === "progress") onProgress?.(msg.phase, msg.done, msg.total);
        else if (msg.type === "done") resolve(msg.meshes);
        else reject(new Error(msg.message));
      };
      worker.onerror = (e) => reject(new Error(e.message || "Falha ao ler o IFC."));
      worker.postMessage({ bytes }, [bytes]);
    });
  } finally {
    worker.terminate();
  }
}

async function parseOnMainThread(bytes: ArrayBuffer, onProgress?: ConvertProgress): Promise<ElementMesh[]> {
  const [{ IfcAPI }, { default: wasmUrl }] = await Promise.all([import("web-ifc"), import("web-ifc/web-ifc.wasm?url")]);
  const api = new IfcAPI();
  await api.Init((path: string) => (path.endsWith(".wasm") ? wasmUrl : path), true);
  try {
    return extractElementMeshes(api as unknown as IfcApiLike, new Uint8Array(bytes), onProgress);
  } finally {
    api.Dispose();
  }
}

/** Binary GLB: one mesh node per element, node name = GlobalId (what PUT …/viewer matches against the version). */
export async function meshesToGlb(meshes: ElementMesh[]): Promise<ArrayBuffer> {
  const scene = new Scene();
  const root = new Group();
  root.name = "ifc";
  scene.add(root);
  const materials = new Map<string, MeshStandardMaterial>();
  const geometries: BufferGeometry[] = [];
  try {
    for (const m of meshes) {
      const geometry = new BufferGeometry();
      geometry.setAttribute("position", new BufferAttribute(m.positions, 3));
      geometry.setAttribute("normal", new BufferAttribute(m.normals, 3));
      geometry.setIndex(new BufferAttribute(m.indices, 1));
      geometries.push(geometry);
      const key = m.color.map((c) => c.toFixed(2)).join(",");
      let material = materials.get(key);
      if (!material) {
        material = new MeshStandardMaterial({ color: new Color(m.color[0], m.color[1], m.color[2]),
          transparent: m.color[3] < 1, opacity: m.color[3] });
        materials.set(key, material);
      }
      const mesh = new Mesh(geometry, material);
      mesh.name = m.globalId;
      root.add(mesh);
    }
    const result = await new GLTFExporter().parseAsync(scene, { binary: true });
    if (!(result instanceof ArrayBuffer)) throw new Error("O exportador não gerou um GLB binário.");
    return result;
  } finally {
    geometries.forEach((g) => g.dispose());
    materials.forEach((m) => m.dispose());
  }
}

/** The whole pipeline for one local .ifc file → GLB bytes and the element count. */
export async function convertIfcToGlb(file: File, onProgress?: ConvertProgress): Promise<{ glb: ArrayBuffer; elements: number }> {
  const bytes = await file.arrayBuffer();
  const meshes = typeof Worker === "undefined"
    ? await parseOnMainThread(bytes, onProgress)
    : await parseInWorker(bytes, onProgress);
  if (meshes.length === 0) throw new Error("Nenhum elemento com geometria e GlobalId foi encontrado neste IFC.");
  onProgress?.("export", 0, 1);
  const glb = await meshesToGlb(meshes);
  onProgress?.("export", 1, 1);
  return { glb, elements: meshes.length };
}
