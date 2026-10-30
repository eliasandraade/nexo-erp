import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor, cleanup } from "@testing-library/react";
import { BoxGeometry, Group, Mesh, MeshStandardMaterial } from "three";
import type { BuildElementState } from "../../../api/build-twin.api";
import { stateColor } from "../../../lib/twin";
import ModelViewer3D from "./ModelViewer3D";

// three's math (scene, camera, raycaster, materials) runs for real in jsdom; only what needs a GPU or the network is faked:
// the WebGL renderer, the orbit controls (DOM pointer plumbing) and the GLTF loader (returns a scene built here).
const h = vi.hoisted(() => ({
  failWebgl: false,
  renderers: [] as Array<Record<string, ReturnType<typeof vi.fn>> & { domElement: HTMLCanvasElement }>,
  controls: [] as Array<Record<string, unknown>>,
  load: null as null | ((url: string) => Promise<unknown>),
  loadedUrls: [] as string[],
}));

vi.mock("three", async (importOriginal) => {
  const three = await importOriginal<typeof import("three")>();
  class FakeRenderer {
    domElement = document.createElement("canvas");
    setPixelRatio = vi.fn();
    setSize = vi.fn();
    // like the real renderer, a frame refreshes the world matrices (the raycaster reads the camera's)
    render = vi.fn((scene: { updateMatrixWorld: () => void }, camera: { updateMatrixWorld: () => void }) => {
      scene.updateMatrixWorld();
      camera.updateMatrixWorld();
    });
    dispose = vi.fn();
    forceContextLoss = vi.fn();
    constructor() {
      if (h.failWebgl) throw new Error("WebGL unavailable");
      h.renderers.push(this as never);
    }
  }
  return { ...three, WebGLRenderer: FakeRenderer };
});
vi.mock("three/examples/jsm/controls/OrbitControls.js", async () => {
  const { Vector3 } = await vi.importActual<typeof import("three")>("three");
  class FakeControls {
    target = new Vector3();
    listeners = new Map<string, () => void>();
    addEventListener = vi.fn((t: string, f: () => void) => this.listeners.set(t, f));
    removeEventListener = vi.fn();
    // like the real controls, update() points the camera at the target
    update = vi.fn(() => this.camera.lookAt(this.target));
    dispose = vi.fn();
    constructor(private camera: { lookAt: (v: unknown) => void }) { h.controls.push(this as never); }
  }
  return { OrbitControls: FakeControls };
});
vi.mock("three/examples/jsm/loaders/GLTFLoader.js", () => ({
  GLTFLoader: class {
    setCrossOrigin = vi.fn();
    loadAsync(url: string) { h.loadedUrls.push(url); return h.load!(url); }
  },
}));

/** A GLTF-like result: node 0 "GUID-A" (mesh), node 1 "GUID-B" (group) whose unnamed child node 2 has a mesh. */
function makeGltf() {
  const ownA = new MeshStandardMaterial();
  const ownB = new MeshStandardMaterial();
  const meshA = new Mesh(new BoxGeometry(2, 2, 2), ownA);
  const groupB = new Group();
  const meshB = new Mesh(new BoxGeometry(1, 1, 1), ownB);
  meshB.position.set(0.5, 0, 0); // inside the big box: the scene is centered on the origin
  groupB.add(meshB);
  const scene = new Group();
  scene.add(meshA, groupB);
  const associations = new Map<object, { nodes?: number }>([[meshA, { nodes: 0 }], [groupB, { nodes: 1 }], [meshB, { nodes: 2 }]]);
  const gltf = {
    scene,
    parser: { json: { nodes: [{ name: "GUID-A", mesh: 0 }, { name: "GUID-B", children: [2] }, { mesh: 1 }] }, associations },
  };
  return { gltf, meshA, meshB, ownA, ownB };
}

const hex = (m: Mesh) => `#${(m.material as MeshStandardMaterial).color.getHexString()}`;
const states = (entries: Array<[string, BuildElementState]>) => new Map(entries);

describe("ModelViewer3D", () => {
  let rafId = 0;
  beforeEach(() => {
    h.failWebgl = false;
    h.renderers = [];
    h.controls = [];
    h.loadedUrls = [];
    vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => { const id = ++rafId; setTimeout(() => cb(0), 0); return id; });
    vi.stubGlobal("cancelAnimationFrame", vi.fn());
  });
  afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks(); });

  it("without WebGL shows the friendly fallback", () => {
    h.failWebgl = true;
    render(<ModelViewer3D url="u" sourceFormat="Ifc" states={new Map()} selectedId={null} onSelect={vi.fn()} />);
    expect(screen.getByTestId("viewer-fallback")).toHaveTextContent("WebGL indisponível");
  });

  it("loads the GLB, replaces the file materials and colors the elements by state, then by selection", async () => {
    const m = makeGltf();
    const ownDispose = vi.spyOn(m.ownA, "dispose");
    h.load = async () => m.gltf;
    const { rerender } = render(<ModelViewer3D url="https://cdn/v.glb" sourceFormat="Ifc"
      states={states([["GUID-A", "Completed"]])} selectedId={null} onSelect={vi.fn()} />);

    expect(screen.getByText("Carregando o modelo 3D…")).toBeInTheDocument();
    expect(h.loadedUrls).toEqual(["https://cdn/v.glb"]);
    await waitFor(() => expect(screen.queryByText("Carregando o modelo 3D…")).not.toBeInTheDocument());
    expect(screen.getByTestId("model-viewer-canvas").querySelector("canvas")).toBe(h.renderers[0].domElement);
    expect(ownDispose).toHaveBeenCalled();
    // GUID-A completed; the unnamed mesh inherits GUID-B (IFC rule) and is unbound
    expect(hex(m.meshA)).toBe(stateColor("Completed").color);
    expect(hex(m.meshB)).toBe(stateColor(null).color);
    expect(h.controls[0].update).toHaveBeenCalled();
    await waitFor(() => expect(h.renderers[0].render).toHaveBeenCalled());

    rerender(<ModelViewer3D url="https://cdn/v.glb" sourceFormat="Ifc"
      states={states([["GUID-A", "Delayed"], ["GUID-B", "InProgress"]])} selectedId="GUID-A" onSelect={vi.fn()} />);
    expect(hex(m.meshA)).toBe("#0ea5e9");
    expect(hex(m.meshB)).toBe(stateColor("InProgress").color);
  });

  it("a click (not a drag) on an element selects it; a click on the void clears", async () => {
    const m = makeGltf();
    h.load = async () => m.gltf;
    const onSelect = vi.fn();
    render(<ModelViewer3D url="u" sourceFormat="Ifc" states={new Map()} selectedId={null} onSelect={onSelect} />);
    await waitFor(() => expect(screen.queryByText("Carregando o modelo 3D…")).not.toBeInTheDocument());
    // wait for the frame drawn after the model was placed
    await waitFor(() => expect(h.renderers[0].render).toHaveBeenCalled());

    const canvas = h.renderers[0].domElement;
    vi.spyOn(canvas, "getBoundingClientRect").mockReturnValue({ left: 0, top: 0, width: 300, height: 300 } as DOMRect);
    // the camera looks at the model's center: near the middle of the canvas (off the box's diagonal edge) hits GUID-A
    // jsdom has no PointerEvent: a MouseEvent of the pointer type carries the coordinates
    const pointer = (type: string, x: number, y: number) => canvas.dispatchEvent(new MouseEvent(type, { clientX: x, clientY: y }));
    pointer("pointerdown", 140, 160);
    pointer("pointerup", 141, 160);
    expect(onSelect).toHaveBeenLastCalledWith("GUID-A");
    // a drag is an orbit, not a selection
    pointer("pointerdown", 150, 150);
    pointer("pointerup", 200, 150);
    expect(onSelect).toHaveBeenCalledTimes(1);
    // the top-left corner hits nothing
    pointer("pointerdown", 0, 0);
    pointer("pointerup", 0, 0);
    expect(onSelect).toHaveBeenLastCalledWith(null);
  });

  it("GLB naming: unique node names become the ids (GUID-B is a group, its unnamed mesh falls back to node:2)", async () => {
    const m = makeGltf();
    h.load = async () => m.gltf;
    render(<ModelViewer3D url="u" sourceFormat="Glb" states={states([["node:2", "Blocked"], ["GUID-A", "NotStarted"]])}
      selectedId={null} onSelect={vi.fn()} />);
    await waitFor(() => expect(hex(m.meshB)).toBe(stateColor("Blocked").color));
    expect(hex(m.meshA)).toBe(stateColor("NotStarted").color);
  });

  it("a load failure shows the 'load' fallback with the reason", async () => {
    h.load = async () => { throw new Error("403 expired"); };
    render(<ModelViewer3D url="u" sourceFormat="Ifc" states={new Map()} selectedId={null} onSelect={vi.fn()} />);
    expect(await screen.findByTestId("viewer-fallback")).toHaveTextContent("Não foi possível carregar o arquivo de visualização");
    expect(screen.getByText("403 expired")).toBeInTheDocument();
  });

  it("unmount disposes the renderer, the controls and the materials and removes the canvas", async () => {
    const m = makeGltf();
    h.load = async () => m.gltf;
    const geoDispose = vi.spyOn(m.meshA.geometry, "dispose");
    const { unmount } = render(<ModelViewer3D url="u" sourceFormat="Ifc" states={new Map()} selectedId={null} onSelect={vi.fn()} />);
    await waitFor(() => expect(screen.queryByText("Carregando o modelo 3D…")).not.toBeInTheDocument());
    const r = h.renderers[0];
    const sharedMaterial = m.meshA.material as MeshStandardMaterial;
    const matDispose = vi.spyOn(sharedMaterial, "dispose");
    // a controls change asks for a frame
    (h.controls[0].listeners as Map<string, () => void>).get("change")!();

    unmount();
    expect(r.dispose).toHaveBeenCalled();
    expect(r.forceContextLoss).toHaveBeenCalled();
    expect(h.controls[0].dispose).toHaveBeenCalled();
    expect(geoDispose).toHaveBeenCalled();
    expect(matDispose).toHaveBeenCalled();
    expect(r.domElement.isConnected).toBe(false);
  });

  it("a model that arrives after unmount is disposed and never shown", async () => {
    const m = makeGltf();
    let resolve: (v: unknown) => void = () => {};
    h.load = () => new Promise((r) => { resolve = r; });
    const geoDispose = vi.spyOn(m.meshB.geometry, "dispose");
    const { unmount } = render(<ModelViewer3D url="u" sourceFormat="Ifc" states={new Map()} selectedId={null} onSelect={vi.fn()} />);
    unmount();
    resolve(m.gltf);
    await waitFor(() => expect(geoDispose).toHaveBeenCalled());
  });
});
