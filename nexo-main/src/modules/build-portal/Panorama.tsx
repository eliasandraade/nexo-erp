import { useEffect, useRef, useState } from "react";
import * as THREE from "three";
import { OrbitControls } from "three/addons/controls/OrbitControls.js";
import type { Portal } from "./types";
import { safeUrl, date } from "./types";
export default function Panorama({
  captures,
}: {
  captures: NonNullable<Portal["captures"]>;
}) {
  const [id, setId] = useState(captures[0]?.id);
  const [error, setError] = useState(false);
  const host = useRef<HTMLDivElement>(null);
  const capture = captures.find((c) => c.id === id);
  useEffect(() => {
    if (!host.current || !capture) return;
    const target = host.current;
    let alive = true;
    let renderer: THREE.WebGLRenderer | undefined;
    let controls: OrbitControls | undefined;
    let geometry: THREE.SphereGeometry | undefined;
    let material: THREE.MeshBasicMaterial | undefined;
    let texture: THREE.Texture | undefined;
    let observer: ResizeObserver | undefined;
    setError(false);
    try {
      if (!safeUrl(capture.url)) throw Error();
      renderer = new THREE.WebGLRenderer({ antialias: false });
      renderer.setPixelRatio(Math.min(devicePixelRatio, 1.5));
      target.append(renderer.domElement);
      const scene = new THREE.Scene();
      const camera = new THREE.PerspectiveCamera(70, 1, 0.1, 100);
      camera.position.set(0.01, 0, 0);
      controls = new OrbitControls(camera, renderer.domElement);
      controls.enablePan = false;
      controls.enableZoom = false;
      const render = () => {
        if (alive) renderer!.render(scene, camera);
      };
      controls.addEventListener("change", render);
      geometry = new THREE.SphereGeometry(10, 48, 24);
      geometry.scale(-1, 1, 1);
      material = new THREE.MeshBasicMaterial();
      scene.add(new THREE.Mesh(geometry, material));
      const resize = () => {
        renderer!.setSize(target.clientWidth, target.clientHeight);
        camera.aspect = target.clientWidth / Math.max(1, target.clientHeight);
        camera.updateProjectionMatrix();
        render();
      };
      observer = new ResizeObserver(resize);
      observer.observe(target);
      resize();
      texture = new THREE.TextureLoader().load(
        capture.url,
        (t) => {
          if (!alive) {
            t.dispose();
            return;
          }
          t.colorSpace = THREE.SRGBColorSpace;
          material!.map = t;
          material!.needsUpdate = true;
          render();
        },
        undefined,
        () => {
          if (alive) setError(true);
        },
      );
    } catch {
      setError(true);
    }
    return () => {
      alive = false;
      observer?.disconnect();
      controls?.dispose();
      geometry?.dispose();
      material?.dispose();
      texture?.dispose();
      renderer?.dispose();
      renderer?.domElement.remove();
    };
  }, [capture]);
  return (
    <div>
      <label>
        Local e data da visita
        <select value={id} onChange={(e) => setId(e.target.value)}>
          {captures.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name} · {c.floor} · {date(c.date)}
            </option>
          ))}
        </select>
      </label>
      {error ? (
        <p role="alert">
          A visita não pôde ser aberta. Consulte as fotos da obra.
        </p>
      ) : (
        <p>
          Arraste para olhar ao redor. Os locais conectados também estão
          disponíveis como botões abaixo.
        </p>
      )}
      <div className="mb-viewer" ref={host} hidden={error} />
      <div className="mb-controls">
        {capture?.hotspots
          .filter((h) => captures.some((c) => c.id === h.targetId))
          .map((h) => (
            <button key={h.targetId} onClick={() => setId(h.targetId)}>
              {h.name}
            </button>
          ))}
      </div>
    </div>
  );
}
