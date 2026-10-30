import { useEffect, useRef, useState } from "react";
import * as THREE from "three";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";
import { OrbitControls } from "three/addons/controls/OrbitControls.js";
import {
  type Phase,
  type TwinModel,
  type TwinSnapshot,
  date,
  safeUrl,
} from "./types";

const labels: Record<Phase, string> = {
  waiting: "Não iniciado",
  active: "Em execução",
  complete: "Concluído",
  delayed: "Atenção ao prazo",
  blocked: "Bloqueado",
  review: "Requer revisão",
  unknown: "Sem vínculo confirmado",
};
export default function TwinViewer({
  model,
  loadState,
  refresh,
}: {
  model: TwinModel;
  loadState?: (date: string, signal: AbortSignal) => Promise<TwinSnapshot>;
  refresh?: () => Promise<void>;
}) {
  const refreshRef = useRef(refresh);
  refreshRef.current = refresh;
  const modelRef = useRef(model);
  modelRef.current = model;
  const [historical, setHistorical] = useState<TwinSnapshot>();
  const [dateBusy, setDateBusy] = useState(false);
  const [dateError, setDateError] = useState("");
  const [requestedDate, setRequestedDate] = useState(model.maxDate ?? "");
  const renewAttempt = useRef(0);
  const historyRef = useRef<TwinSnapshot>();
  historyRef.current = historical;
  const container = useRef<HTMLDivElement>(null);
  const reset = useRef<() => void>();
  const [category, setCategory] = useState("");
  const [stage, setStage] = useState("");
  const [cut, setCut] = useState(0);
  const [mode, setMode] = useState<"project" | "now" | "compare">("compare");
  const [snapshot, setSnapshot] = useState("");
  const [floor, setFloor] = useState("");
  const [selected, setSelected] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const apply = useRef<() => void>();
  const current = useRef({ mode, snapshot, floor, stage, cut, category });
  current.current = { mode, snapshot, floor, stage, cut, category };
  const states =
    historical?.states ??
    model.snapshots.find((s) => s.id === snapshot)?.states;
  useEffect(() => {
    if (!loadState || !requestedDate) return;
    const controller = new AbortController();
    setDateBusy(true);
    setDateError("");
    void loadState(requestedDate, controller.signal)
      .then((value) => {
        if (!controller.signal.aborted) setHistorical(value);
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setHistorical({
            id: requestedDate,
            date: requestedDate,
            states: {},
            zoneStates: {},
          });
          setDateError(
            "O registro desta data está indisponível. Tente outra data ou atualize o acompanhamento.",
          );
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) setDateBusy(false);
      });
    return () => controller.abort();
  }, [loadState, requestedDate]);
  const stateOf = (id: string, fallback: Phase) =>
    states ? states[id] : fallback;
  const selection = model.elements.find((e) => e.id === selected);
  useEffect(() => {
    const host = container.current;
    if (!host) return;
    let alive = true;
    let renderer: THREE.WebGLRenderer | undefined;
    let controls: OrbitControls | undefined;
    let root: THREE.Group | undefined;
    let observer: ResizeObserver | undefined;
    let scene: THREE.Scene | undefined;
    const dispose = () => {
      root?.traverse((object) => {
        if (object instanceof THREE.Mesh) {
          object.geometry.dispose();
          for (const material of [
            ...(object.userData.originalMaterials ?? []),
            ...(object.userData.portalMaterials ?? []),
            ...(Array.isArray(object.material)
              ? object.material
              : [object.material]),
          ]) {
            Object.values(material).forEach((value) => {
              if (value instanceof THREE.Texture) value.dispose();
            });
            material.dispose();
          }
        }
      });
    };
    setError("");
    setLoading(true);
    try {
      if (model.geometrySource === "AssistedMapping") {
        setLoading(false);
        return;
      }
      if (
        !safeUrl(model.url) &&
        model.geometrySource !== "SimplifiedRepresentation"
      )
        throw Error("Modelo indisponível");
      renderer = new THREE.WebGLRenderer({ antialias: false, alpha: true });
      renderer.localClippingEnabled = true;
      renderer.setPixelRatio(Math.min(window.devicePixelRatio, 1.5));
      host.append(renderer.domElement);
      scene = new THREE.Scene();
      const camera = new THREE.PerspectiveCamera(45, 1, 0.01, 100000);
      scene.add(new THREE.HemisphereLight(0xf5f5e9, 0x617269, 3));
      controls = new OrbitControls(camera, renderer.domElement);
      controls.enableDamping = false;
      const render = () => {
        if (alive && renderer && scene) renderer.render(scene, camera);
      };
      controls.addEventListener("change", render);
      const resize = () => {
        if (!renderer) return;
        renderer.setSize(host.clientWidth, host.clientHeight);
        camera.aspect = host.clientWidth / Math.max(1, host.clientHeight);
        camera.updateProjectionMatrix();
        render();
      };
      observer = new ResizeObserver(resize);
      observer.observe(host);
      resize();
      const lost = (event: Event) => {
        event.preventDefault();
        setError(
          "Seu dispositivo interrompeu o 3D. Continue pelas etapas e fotos.",
        );
      };
      renderer.domElement.addEventListener("webglcontextlost", lost);
      const ray = new THREE.Raycaster();
      const pointer = new THREE.Vector2();
      let down = [0, 0];
      renderer.domElement.addEventListener("pointerdown", (e) => {
        down = [e.clientX, e.clientY];
      });
      renderer.domElement.addEventListener("pointerup", (e) => {
        if (Math.hypot(e.clientX - down[0], e.clientY - down[1]) > 6 || !root)
          return;
        const bounds = host.getBoundingClientRect();
        pointer.set(
          ((e.clientX - bounds.left) / bounds.width) * 2 - 1,
          (-(e.clientY - bounds.top) / bounds.height) * 2 + 1,
        );
        ray.setFromCamera(pointer, camera);
        const hit = ray
          .intersectObject(root, true)
          .find((h) => h.object.visible);
        setSelected(hit?.object.userData.portalId ?? "");
      });
      const acceptRoot = (group: THREE.Group) => {
        root = group;
        if (!alive) {
          dispose();
          return;
        }
        root.traverse((object) => {
          if (!(object instanceof THREE.Mesh)) return;
          const materials = Array.isArray(object.material)
            ? object.material
            : [object.material];
          object.userData.originalMaterials = materials;
          object.userData.portalMaterials = materials.map((m) => m.clone());
        });
        apply.current = () => {
          const config = current.current;
          const model = modelRef.current;
          const historical =
            historyRef.current?.states ??
            model.snapshots.find((s) => s.id === config.snapshot)?.states;
          const bounds = new THREE.Box3().setFromObject(root!);
          const clipping = new THREE.Plane(
            new THREE.Vector3(0, -1, 0),
            bounds.max.y - ((bounds.max.y - bounds.min.y) * config.cut) / 100,
          );
          root!.traverse((object) => {
            if (!(object instanceof THREE.Mesh)) return;
            let ancestor: THREE.Object3D | null = object;
            let element = model.elements.find((e) => e.node === object.name);
            let inFloor = !config.floor;
            while (ancestor) {
              element ??= model.elements.find((e) => e.node === ancestor!.name);
              if (ancestor.name === config.floor) inFloor = true;
              ancestor = ancestor.parent;
            }
            const zone = model.zones?.find(
              (z) => z.id === object.name || z.node === object.name,
            );
            object.userData.portalId = element?.id;
            const status = element
              ? historical
                ? historical[element.id]
                : element.status
              : zone
                ? (historyRef.current?.zoneStates ?? model.zoneStates)?.[
                    zone.id
                  ]
                : undefined;
            object.visible =
              (inFloor || element?.floor === config.floor) &&
              (!config.stage || element?.stage === config.stage) &&
              (!config.category || element?.category === config.category) &&
              (config.mode !== "now" ||
                status === "complete" ||
                status === "active");
            const originals = object.userData
              .originalMaterials as THREE.Material[];
            const styled = object.userData.portalMaterials as THREE.Material[];
            styled.forEach((material, index) => {
              material.copy(originals[index]);
              if (config.mode !== "project") {
                if ("color" in material)
                  (material.color as THREE.Color).set(
                    status === "complete"
                      ? 0x34594d
                      : status === "active"
                        ? 0x3d7566
                        : status === "delayed"
                          ? 0xa4712e
                          : status === "blocked"
                            ? 0x925b50
                            : status === "review"
                              ? 0x795b8f
                              : 0x9aa59e,
                  );
                material.transparent =
                  status === undefined || status === "waiting";
                material.opacity = material.transparent ? 0.18 : 1;
                material.depthWrite = !material.transparent;
              }
              material.clippingPlanes = config.cut > 0 ? [clipping] : [];
              material.needsUpdate = true;
            });
            object.material = styled.length === 1 ? styled[0] : styled;
          });
          render();
        };
        scene!.add(root);
        const box = new THREE.Box3().setFromObject(root);
        const size = box.getSize(new THREE.Vector3()).length() || 10;
        const center = box.getCenter(new THREE.Vector3());
        reset.current = () => {
          camera.position
            .copy(center)
            .add(new THREE.Vector3(size * 0.8, size * 0.6, size * 0.8));
          controls!.target.copy(center);
          camera.near = size / 1000;
          camera.far = size * 100;
          camera.updateProjectionMatrix();
          controls!.update();
          render();
        };
        reset.current();
        apply.current();
        setLoading(false);
      };
      if (model.geometrySource === "SimplifiedRepresentation" && !model.url) {
        const group = new THREE.Group();
        let offset = 0;
        for (const zone of modelRef.current.zones ?? []) {
          const b = zone.box;
          if (
            !b ||
            ![b.width, b.height, b.depth, b.elevation].every(Number.isFinite) ||
            Math.min(b.width, b.height, b.depth) <= 0
          )
            continue;
          const mesh = new THREE.Mesh(
            new THREE.BoxGeometry(b.width, b.height, b.depth),
            new THREE.MeshStandardMaterial({ color: 0x9aa59e }),
          );
          mesh.name = zone.id;
          mesh.position.set(
            offset + b.width / 2,
            b.elevation + b.height / 2,
            0,
          );
          offset += b.width + 1;
          group.add(mesh);
        }
        acceptRoot(group);
      } else
        new GLTFLoader().load(
          model.url!,
          (gltf) => acceptRoot(gltf.scene),
          undefined,
          () => {
            if (!alive) return;
            setLoading(false);
            setError(
              "O modelo 3D não pôde ser carregado. As fotos e etapas continuam disponíveis.",
            );
            if (
              refreshRef.current &&
              Date.now() - renewAttempt.current > 60000
            ) {
              renewAttempt.current = Date.now();
              void refreshRef.current();
            }
          },
        );
    } catch {
      setLoading(false);
      setError(
        "A visualização 3D não está disponível neste dispositivo. Acompanhe pelas fotos e etapas.",
      );
    }
    return () => {
      alive = false;
      observer?.disconnect();
      controls?.dispose();
      dispose();
      renderer?.dispose();
      renderer?.domElement.remove();
      reset.current = undefined;
      apply.current = undefined;
    };
  }, [model.url, model.geometrySource]);
  useEffect(() => {
    apply.current?.();
  }, [mode, snapshot, floor, stage, cut, category, historical, model]);
  return (
    <div>
      {model.geometrySource === "SimplifiedRepresentation" && (
        <p>
          Representação simplificada. Volumes separados para consulta das
          medidas; a disposição não representa a posição real dos ambientes.
        </p>
      )}
      {model.geometrySource === "AssistedMapping" && (
        <p>Mapeamento em planta. Consulte as zonas e os estados abaixo.</p>
      )}
      {loadState && model.minDate && model.maxDate && (
        <label>
          Consultar data da obra
          <input
            type="date"
            min={model.minDate}
            max={model.maxDate}
            value={requestedDate}
            onChange={(e) => setRequestedDate(e.target.value)}
          />
        </label>
      )}
      {loadState && model.minDate && model.maxDate && (
        <label>
          Percorrer evolução por data
          <input
            type="range"
            min={Date.parse(model.minDate)}
            max={Date.parse(model.maxDate)}
            step={86400000}
            defaultValue={Date.parse(model.maxDate)}
            onPointerUp={(e) =>
              setRequestedDate(
                new Date(Number(e.currentTarget.value))
                  .toISOString()
                  .slice(0, 10),
              )
            }
            onKeyUp={(e) =>
              setRequestedDate(
                new Date(Number(e.currentTarget.value))
                  .toISOString()
                  .slice(0, 10),
              )
            }
          />
        </label>
      )}
      {dateBusy && <p role="status">Consultando estado nesta data…</p>}
      {dateError && <p role="alert">{dateError}</p>}
      <div className="mb-controls">
        {(["project", "now", "compare"] as const).map((value, index) => (
          <button
            key={value}
            aria-pressed={mode === value}
            onClick={() => setMode(value)}
          >
            {["Projeto", "Agora", "Comparar"][index]}
          </button>
        ))}
        <button onClick={() => reset.current?.()}>Visão geral</button>
        <button
          onClick={() => {
            void container.current
              ?.requestFullscreen()
              .catch(() =>
                setError("A tela cheia não está disponível neste navegador."),
              );
          }}
        >
          Tela cheia
        </button>
      </div>
      {!loadState && model.snapshots.length > 0 && (
        <label>
          Percorrer evolução
          <input
            type="range"
            min="0"
            max={model.snapshots.length}
            value={
              snapshot
                ? model.snapshots.findIndex((s) => s.id === snapshot)
                : model.snapshots.length
            }
            onChange={(e) =>
              setSnapshot(model.snapshots[Number(e.target.value)]?.id ?? "")
            }
            aria-valuetext={
              snapshot
                ? date(
                    model.snapshots.find((s) => s.id === snapshot)?.date ??
                      null,
                  )
                : "Hoje, último estado publicado"
            }
          />
        </label>
      )}
      {!loadState && (
        <label>
          Registro no tempo
          <select
            value={snapshot}
            onChange={(e) => setSnapshot(e.target.value)}
          >
            <option value="">Hoje, último estado publicado</option>
            {model.snapshots.map((s) => (
              <option key={s.id} value={s.id}>
                {date(s.date)}
              </option>
            ))}
          </select>
        </label>
      )}
      <label>
        Pavimento
        <select value={floor} onChange={(e) => setFloor(e.target.value)}>
          <option value="">Todos</option>
          {[...new Set(model.elements.map((e) => e.floor).filter(Boolean))].map(
            (f) => (
              <option key={f}>{f}</option>
            ),
          )}
          {model.zones
            ?.filter((z) => z.node)
            .map((z) => (
              <option key={z.id} value={z.node}>
                {z.name}
              </option>
            ))}
        </select>
      </label>
      {model.elements.some((e) => e.stage) && (
        <label>
          Etapa
          <select value={stage} onChange={(e) => setStage(e.target.value)}>
            <option value="">Todas</option>
            {[...new Set(model.elements.map((e) => e.stage))].map((value) => (
              <option key={value}>{value}</option>
            ))}
          </select>
        </label>
      )}
      <label>
        Corte horizontal: {cut === 0 ? "desativado" : cut + "%"}
        <input
          type="range"
          min="0"
          max="95"
          value={cut}
          onChange={(e) => setCut(Number(e.target.value))}
        />
      </label>
      {model.elements.some((e) => e.category) && (
        <label>
          Categoria
          <select
            value={category}
            onChange={(e) => setCategory(e.target.value)}
          >
            <option value="">Todas</option>
            {[
              ...new Set(model.elements.map((e) => e.category).filter(Boolean)),
            ].map((value) => (
              <option key={value}>{value}</option>
            ))}
          </select>
        </label>
      )}
      {selection?.photos?.length ? (
        <div className="mb-gallery">
          {selection.photos.map((photo) => (
            <a
              key={photo.id}
              href={safeUrl(photo.url)}
              target="_blank"
              rel="noreferrer"
            >
              <img
                src={safeUrl(photo.thumbnail) || safeUrl(photo.url)}
                alt={photo.caption}
                loading="lazy"
              />
              {photo.caption}
            </a>
          ))}
        </div>
      ) : null}
      {selection?.updateId && (
        <a href={"#update-" + selection.updateId}>Ver atualização</a>
      )}
      {error ? (
        <p role="alert">{error}</p>
      ) : (
        <>
          <p>
            Arraste para girar. Use dois dedos para aproximar ou deslocar. A
            lista abaixo oferece as mesmas informações sem gestos.
          </p>
          {loading && <p role="status">Carregando modelo…</p>}
        </>
      )}
      <div
        ref={container}
        className="mb-viewer"
        role="img"
        aria-label="Modelo da obra, informações equivalentes na lista de elementos abaixo"
        hidden={!!error}
      />
      {selection && (
        <p role="status">
          {selection.name} ·{" "}
          {stateOf(selection.id, selection.status)
            ? labels[stateOf(selection.id, selection.status)!]
            : "Sem registro nesta data"}{" "}
          · {selection.stage}
        </p>
      )}
      {!!model.zones?.length && (
        <details open={model.geometrySource === "AssistedMapping"}>
          <summary>Zonas da obra</summary>
          <ul>
            {model.zones.map((z) => (
              <li key={z.id}>
                {z.name}:{" "}
                {
                  labels[
                    (historical?.zoneStates ?? model.zoneStates)?.[z.id] ??
                      "unknown"
                  ]
                }
                {z.box &&
                  ` · ${z.box.width} × ${z.box.depth} × ${z.box.height} m`}
              </li>
            ))}
          </ul>
        </details>
      )}
      {model.unbound !== undefined && (
        <p>
          {model.unbound} elementos sem vínculo de execução, exibidos de forma
          neutra.
        </p>
      )}
      <details>
        <summary>Elementos e estados da construção</summary>
        <ul>
          {model.elements
            .filter((e) => !floor || e.floor === floor)
            .map((e) => (
              <li key={e.id}>
                <button onClick={() => setSelected(e.id)}>{e.name}</button>{" "}
                {stateOf(e.id, e.status)
                  ? labels[stateOf(e.id, e.status)!]
                  : "Sem registro nesta data"}{" "}
                · {e.stage}
                {e.completedAt && !snapshot && ` · ${date(e.completedAt)}`}
              </li>
            ))}
        </ul>
      </details>
    </div>
  );
}
