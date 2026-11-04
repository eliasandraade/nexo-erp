// Lazy chunk: equirectangular 360° photo on the inside of a sphere (three.js).
import { useEffect, useRef, useState } from "react";
import {
  MathUtils, Mesh, MeshBasicMaterial, PerspectiveCamera, SRGBColorSpace, Scene, SphereGeometry, TextureLoader, WebGLRenderer,
  type Texture,
} from "three";
import { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";
import { ViewerFallback } from "./ViewerFallback";

export default function Pano360Viewer({ url, title }: { url: string; title: string }) {
  const hostRef = useRef<HTMLDivElement>(null);
  const [status, setStatus] = useState<"loading" | "ready" | "no-webgl" | "error">("loading");

  useEffect(() => {
    const host = hostRef.current;
    if (!host) return;
    let renderer: WebGLRenderer;
    try {
      renderer = new WebGLRenderer({ antialias: true });
    } catch {
      setStatus("no-webgl");
      return;
    }
    let disposed = false;
    let frame = 0;
    let texture: Texture | null = null;
    const scene = new Scene();
    const camera = new PerspectiveCamera(75, 1, 0.1, 1100);
    camera.position.set(0, 0, 0.01);
    const geometry = new SphereGeometry(500, 60, 40);
    geometry.scale(-1, 1, 1);                         // look at the inside of the sphere
    const material = new MeshBasicMaterial();
    scene.add(new Mesh(geometry, material));
    const controls = new OrbitControls(camera, renderer.domElement);
    controls.enableZoom = false;
    controls.enablePan = false;
    controls.rotateSpeed = -0.35;
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
    const onWheel = (e: WheelEvent) => {
      e.preventDefault();
      camera.fov = MathUtils.clamp(camera.fov + e.deltaY * 0.05, 30, 90);
      camera.updateProjectionMatrix();
      render();
    };
    renderer.domElement.addEventListener("wheel", onWheel, { passive: false });

    const loader = new TextureLoader();
    loader.setCrossOrigin("anonymous");
    loader.loadAsync(url).then((t) => {
      if (disposed) { t.dispose(); return; }
      t.colorSpace = SRGBColorSpace;
      texture = t;
      material.map = t;
      material.needsUpdate = true;
      setStatus("ready");
      render();
    }).catch(() => { if (!disposed) setStatus("error"); });

    return () => {
      disposed = true;
      if (frame) cancelAnimationFrame(frame);
      ro?.disconnect();
      renderer.domElement.removeEventListener("wheel", onWheel);
      controls.removeEventListener("change", render);
      controls.dispose();
      texture?.dispose();
      geometry.dispose();
      material.dispose();
      renderer.dispose();
      renderer.forceContextLoss();
      renderer.domElement.remove();
    };
  }, [url]);

  if (status === "no-webgl") return <ViewerFallback reason="webgl" />;
  return (
    <div className="relative h-full w-full" aria-label={`Foto 360°: ${title}`} role="img">
      <div ref={hostRef} className="absolute inset-0" />
      {status === "loading" && <div className="absolute inset-0 grid place-items-center text-sm text-muted-foreground">Carregando a foto 360°…</div>}
      {status === "error" && <ViewerFallback reason="load" />}
    </div>
  );
}
