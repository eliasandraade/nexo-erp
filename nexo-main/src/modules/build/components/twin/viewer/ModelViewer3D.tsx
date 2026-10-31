// Lazy chunk (React.lazy) — the only place three.js is imported for the model viewer, so the main bundle never carries it.
import { useEffect, useRef, useState } from "react";
import {
  AmbientLight, Box3, Color, DirectionalLight, Material, Mesh, MeshStandardMaterial, Object3D, PerspectiveCamera, Raycaster,
  Scene, Vector2, Vector3, WebGLRenderer,
} from "three";
import { GLTFLoader, type GLTF } from "three/examples/jsm/loaders/GLTFLoader.js";
import { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";
import type { BuildElementState, BuildModelSourceFormat } from "../../../api/build-twin.api";
import { ELEMENT_STATES, stateColor } from "../../../lib/twin";
import { isClick, viewerNodeIds, type ViewerNode } from "../../../lib/twin-viewer";
import { ViewerFallback } from "./ViewerFallback";

export interface ModelViewer3DProps {
  url: string;
  sourceFormat: BuildModelSourceFormat;
  /** externalId → state on the selected date; missing = unbound (neutral). */
  states: Map<string, BuildElementState>;
  selectedId: string | null;
  onSelect: (externalId: string | null) => void;
}

interface SceneRefs {
  renderer: WebGLRenderer;
  scene: Scene;
  camera: PerspectiveCamera;
  controls: OrbitControls;
  meshes: Array<{ mesh: Mesh; id: string }>;
  materials: Map<string, MeshStandardMaterial>;
  render: () => void;
}

const SELECTED = "__selected";

function makeMaterials(): Map<string, MeshStandardMaterial> {
  const map = new Map<string, MeshStandardMaterial>();
  for (const key of [...ELEMENT_STATES, "unbound"] as const) {
    const c = stateColor(key === "unbound" ? null : key);
    map.set(key, new MeshStandardMaterial({ color: new Color(c.color), transparent: c.transparent, opacity: c.opacity,
      depthWrite: !c.transparent, roughness: 0.8, metalness: 0 }));
  }
  map.set(SELECTED, new MeshStandardMaterial({ color: new Color("#0ea5e9"), emissive: new Color("#0369a1"), roughness: 0.5 }));
  return map;
}

function disposeObject(root: Object3D) {
  root.traverse((o) => {
    const mesh = o as Mesh;
    if (mesh.isMesh) {
      mesh.geometry?.dispose();
      const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
      mats.forEach((m: Material | undefined) => m?.dispose());
    }
  });
}

/** Mesh nodes with their element ids (the server's naming rules), from the loader's node associations. */
function collectMeshes(gltf: GLTF, sourceFormat: BuildModelSourceFormat): Array<{ mesh: Mesh; id: string }> {
  const parser = gltf.parser;
  const jsonNodes = (parser.json.nodes ?? []) as Array<{ name?: string; mesh?: number; children?: number[] }>;
  const parentOf = new Map<number, number>();
  jsonNodes.forEach((n, i) => n.children?.forEach((c) => parentOf.set(c, i)));
  const nodes: ViewerNode[] = jsonNodes.map((n, i) => ({ index: i, name: n.name ?? null, hasMesh: n.mesh !== undefined,
    parent: parentOf.get(i) ?? null }));
  const ids = viewerNodeIds(nodes, sourceFormat);

  const out: Array<{ mesh: Mesh; id: string }> = [];
  gltf.scene.traverse((o) => {
    const mesh = o as Mesh;
    if (!mesh.isMesh) return;
    let cur: Object3D | null = mesh;
    for (let guard = 0; cur && guard < 64; guard++) {
      const nodeIndex = parser.associations.get(cur)?.nodes;
      if (nodeIndex !== undefined) {
        const id = ids.get(nodeIndex);
        if (id) out.push({ mesh, id });
        return;
      }
      cur = cur.parent;
    }
  });
  return out;
}

/** GLB viewer: orbit, colors by state, click selects an element. Renders on demand; disposes everything on unmount. */
export default function ModelViewer3D({ url, sourceFormat, states, selectedId, onSelect }: ModelViewer3DProps) {
  const hostRef = useRef<HTMLDivElement>(null);
  const refs = useRef<SceneRefs | null>(null);
  const onSelectRef = useRef(onSelect);
  onSelectRef.current = onSelect;
  const [status, setStatus] = useState<"loading" | "ready" | "no-webgl" | "error">("loading");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const host = hostRef.current;
    if (!host) return;
    let renderer: WebGLRenderer;
    try {
      renderer = new WebGLRenderer({ antialias: true, alpha: true });
    } catch {
      setStatus("no-webgl");
      return;
    }
    let disposed = false;
    let frame = 0;
    const scene = new Scene();
    const camera = new PerspectiveCamera(45, 1, 0.1, 10_000);
    const controls = new OrbitControls(camera, renderer.domElement);
    const materials = makeMaterials();
    scene.add(new AmbientLight(0xffffff, 0.9));
    const sun = new DirectionalLight(0xffffff, 1.6);
    sun.position.set(1, 2, 1.5);
    scene.add(sun);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    renderer.domElement.style.display = "block";
    renderer.domElement.style.touchAction = "none";
    host.appendChild(renderer.domElement);

    const render = () => {
      if (frame || disposed) return;
      frame = requestAnimationFrame(() => { frame = 0; renderer.render(scene, camera); });
    };
    const resize = () => {
      const w = host.clientWidth || 300, h = host.clientHeight || 300;
      renderer.setSize(w, h, false);
      camera.aspect = w / h;
      camera.updateProjectionMatrix();
      render();
    };
    const ro = typeof ResizeObserver === "undefined" ? null : new ResizeObserver(resize);
    ro?.observe(host);
    resize();
    controls.addEventListener("change", render);

    // click (not drag) → raycast → element
    const down = { x: 0, y: 0 };
    const raycaster = new Raycaster();
    const onDown = (e: PointerEvent) => { down.x = e.clientX; down.y = e.clientY; };
    const onUp = (e: PointerEvent) => {
      const r = refs.current;
      if (!r || !isClick(down, { x: e.clientX, y: e.clientY })) return;
      const rect = renderer.domElement.getBoundingClientRect();
      const ndc = new Vector2(((e.clientX - rect.left) / rect.width) * 2 - 1, -((e.clientY - rect.top) / rect.height) * 2 + 1);
      raycaster.setFromCamera(ndc, camera);
      const hit = raycaster.intersectObjects(r.meshes.map((m) => m.mesh), false)[0];
      onSelectRef.current(hit ? r.meshes.find((m) => m.mesh === hit.object)?.id ?? null : null);
    };
    renderer.domElement.addEventListener("pointerdown", onDown);
    renderer.domElement.addEventListener("pointerup", onUp);

    const loader = new GLTFLoader();
    loader.setCrossOrigin("anonymous");
    loader.loadAsync(url).then((gltf) => {
      if (disposed) { disposeObject(gltf.scene); return; }
      const meshes = collectMeshes(gltf, sourceFormat);
      // the file's own materials are replaced by the shared state materials
      for (const { mesh } of meshes) {
        const own = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
        own.forEach((m) => m.dispose());
        mesh.material = materials.get("unbound")!;
      }
      scene.add(gltf.scene);
      const box = new Box3().setFromObject(gltf.scene);
      const size = box.getSize(new Vector3()).length() || 10;
      const center = box.getCenter(new Vector3());
      camera.near = size / 1000;
      camera.far = size * 20;
      camera.position.copy(center).add(new Vector3(size * 0.7, size * 0.55, size * 0.7));
      camera.updateProjectionMatrix();
      controls.target.copy(center);
      controls.update();
      refs.current = { renderer, scene, camera, controls, meshes, materials, render };
      setStatus("ready");
    }).catch((e: unknown) => {
      if (disposed) return;
      setError(e instanceof Error ? e.message : String(e));
      setStatus("error");
    });

    return () => {
      disposed = true;
      if (frame) cancelAnimationFrame(frame);
      ro?.disconnect();
      renderer.domElement.removeEventListener("pointerdown", onDown);
      renderer.domElement.removeEventListener("pointerup", onUp);
      controls.removeEventListener("change", render);
      controls.dispose();
      // geometries of the model; the shared state materials are disposed once below
      scene.traverse((o) => { const m = o as Mesh; if (m.isMesh) m.geometry?.dispose(); });
      materials.forEach((m) => m.dispose());
      scene.clear();
      renderer.dispose();
      renderer.forceContextLoss();
      renderer.domElement.remove();
      refs.current = null;
    };
  }, [url, sourceFormat]);

  // recolor on state/selection change (no reload)
  useEffect(() => {
    const r = refs.current;
    if (!r || status !== "ready") return;
    for (const { mesh, id } of r.meshes) {
      mesh.material = id === selectedId ? r.materials.get(SELECTED)! : r.materials.get(states.get(id) ?? "unbound")!;
    }
    r.render();
  }, [states, selectedId, status]);

  if (status === "no-webgl") return <ViewerFallback reason="webgl" />;
  return (
    <div className="relative h-full w-full">
      <div ref={hostRef} className="absolute inset-0" data-testid="model-viewer-canvas" />
      {status === "loading" && (
        <div className="absolute inset-0 grid place-items-center text-sm text-muted-foreground">Carregando o modelo 3D…</div>
      )}
      {status === "error" && <ViewerFallback reason="load" detail={error} />}
    </div>
  );
}
