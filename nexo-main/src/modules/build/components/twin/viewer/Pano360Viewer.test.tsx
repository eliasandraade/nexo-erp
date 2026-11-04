import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor, cleanup } from "@testing-library/react";
import { SRGBColorSpace } from "three";
import Pano360Viewer from "./Pano360Viewer";

// Real three.js math; the renderer (WebGL), the orbit controls and the texture download are faked.
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
    render = vi.fn();
    dispose = vi.fn();
    forceContextLoss = vi.fn();
    constructor() {
      if (h.failWebgl) throw new Error("WebGL unavailable");
      h.renderers.push(this as never);
    }
  }
  class FakeTextureLoader {
    setCrossOrigin = vi.fn();
    loadAsync(url: string) { h.loadedUrls.push(url); return h.load!(url); }
  }
  return { ...three, WebGLRenderer: FakeRenderer, TextureLoader: FakeTextureLoader };
});
vi.mock("three/examples/jsm/controls/OrbitControls.js", () => ({
  OrbitControls: class {
    enableZoom = true;
    enablePan = true;
    rotateSpeed = 1;
    addEventListener = vi.fn();
    removeEventListener = vi.fn();
    dispose = vi.fn();
    constructor() { h.controls.push(this as never); }
  },
}));

describe("Pano360Viewer", () => {
  beforeEach(() => {
    h.failWebgl = false;
    h.renderers = [];
    h.controls = [];
    h.loadedUrls = [];
    vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => setTimeout(() => cb(0), 0) as unknown as number);
    vi.stubGlobal("cancelAnimationFrame", vi.fn());
  });
  afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

  it("without WebGL shows the fallback", () => {
    h.failWebgl = true;
    render(<Pano360Viewer url="u" title="Sala" />);
    expect(screen.getByTestId("viewer-fallback")).toHaveTextContent("WebGL indisponível");
  });

  it("loads the photo as an sRGB texture, locks zoom/pan on the controls and renders", async () => {
    const texture = { colorSpace: "", dispose: vi.fn() };
    h.load = async () => texture;
    render(<Pano360Viewer url="https://cdn/360.jpg" title="Sala" />);

    expect(screen.getByRole("img", { name: "Foto 360°: Sala" })).toBeInTheDocument();
    expect(screen.getByText("Carregando a foto 360°…")).toBeInTheDocument();
    expect(h.loadedUrls).toEqual(["https://cdn/360.jpg"]);
    await waitFor(() => expect(screen.queryByText("Carregando a foto 360°…")).not.toBeInTheDocument());
    expect(texture.colorSpace).toBe(SRGBColorSpace);
    expect(h.controls[0]).toMatchObject({ enableZoom: false, enablePan: false, rotateSpeed: -0.35 });
    await waitFor(() => expect(h.renderers[0].render).toHaveBeenCalled());
  });

  it("the wheel zooms by changing the field of view, clamped to 30–90°", async () => {
    h.load = async () => ({ colorSpace: "", dispose: vi.fn() });
    render(<Pano360Viewer url="u" title="Sala" />);
    const r = h.renderers[0];
    await waitFor(() => expect(r.render).toHaveBeenCalled());
    const camera = r.render.mock.calls[0][1] as { fov: number };
    expect(camera.fov).toBe(75);

    const wheel = new WheelEvent("wheel", { deltaY: 100, cancelable: true });
    r.domElement.dispatchEvent(wheel);
    expect(wheel.defaultPrevented).toBe(true);
    expect(camera.fov).toBe(80);
    r.domElement.dispatchEvent(new WheelEvent("wheel", { deltaY: 10_000 }));
    expect(camera.fov).toBe(90);
    r.domElement.dispatchEvent(new WheelEvent("wheel", { deltaY: -10_000 }));
    expect(camera.fov).toBe(30);
  });

  it("a load failure shows the 'load' fallback", async () => {
    h.load = async () => { throw new Error("404"); };
    render(<Pano360Viewer url="u" title="Sala" />);
    expect(await screen.findByTestId("viewer-fallback")).toHaveTextContent("Não foi possível carregar o arquivo de visualização");
  });

  it("unmount disposes texture, renderer and controls; a texture that arrives later is disposed", async () => {
    const texture = { colorSpace: "", dispose: vi.fn() };
    h.load = async () => texture;
    const { unmount } = render(<Pano360Viewer url="u" title="Sala" />);
    await waitFor(() => expect(screen.queryByText("Carregando a foto 360°…")).not.toBeInTheDocument());
    const r = h.renderers[0];
    unmount();
    expect(texture.dispose).toHaveBeenCalled();
    expect(r.dispose).toHaveBeenCalled();
    expect(r.forceContextLoss).toHaveBeenCalled();
    expect(h.controls[0].dispose).toHaveBeenCalled();
    expect(r.domElement.isConnected).toBe(false);

    const late = { colorSpace: "", dispose: vi.fn() };
    let resolve: (v: unknown) => void = () => {};
    h.load = () => new Promise((res) => { resolve = res; });
    const second = render(<Pano360Viewer url="u2" title="Sala" />);
    second.unmount();
    resolve(late);
    await waitFor(() => expect(late.dispose).toHaveBeenCalled());
    expect(late.colorSpace).toBe("");
  });
});
