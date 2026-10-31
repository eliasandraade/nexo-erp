import { ApiError } from "@/services/api-client";
import type {
  BuildElementState, BuildManualZoneKind, BuildModelDiscipline, BuildModelDto, BuildModelVersionDto,
  BuildModelVersionStatus, BuildRealityCaptureType, BuildTwinStateDto, BuildZoneKind, BuildZonePoint, BuildZoneStatesDto,
} from "../api/build-twin.api";

// Pure rules of the "Gêmeo digital" tab (docs/build/digital-twin.md). No React, no three.js — unit-tested.

// ── States and colors ─────────────────────────────────────────────────────────

export const ELEMENT_STATES: BuildElementState[] = ["NotStarted", "InProgress", "Completed", "Delayed", "Blocked", "NeedsReview"];

export const STATE_META: Record<BuildElementState, { label: string; color: string; dot: string }> = {
  NotStarted:  { label: "Não iniciado",  color: "#9ca3af", dot: "bg-gray-400" },
  InProgress:  { label: "Em andamento",  color: "#f59e0b", dot: "bg-amber-500" },
  Completed:   { label: "Concluído",     color: "#16a34a", dot: "bg-green-600" },
  Delayed:     { label: "Atrasado",      color: "#dc2626", dot: "bg-red-600" },
  Blocked:     { label: "Bloqueado",     color: "#9333ea", dot: "bg-purple-600" },
  NeedsReview: { label: "Para revisar",  color: "#2563eb", dot: "bg-blue-600" },
};

export const UNBOUND_META = { label: "Sem vínculo", color: "#d1d5db", dot: "bg-gray-200 border border-gray-300" };

/** Color and opacity of an element/zone in the given state; unbound (null) is faint and transparent — never "0%". */
export function stateColor(state: BuildElementState | null | undefined): { color: string; opacity: number; transparent: boolean } {
  if (!state || !(state in STATE_META)) return { color: UNBOUND_META.color, opacity: 0.18, transparent: true };
  return { color: STATE_META[state].color, opacity: 1, transparent: false };
}

/** externalId → state, from the compact state of a date. */
export function stateIndex(dto: BuildTwinStateDto | null | undefined): Map<string, BuildElementState> {
  const map = new Map<string, BuildElementState>();
  if (!dto) return map;
  for (const state of ELEMENT_STATES) for (const id of dto.states?.[state] ?? []) map.set(id, state);
  return map;
}

/** zoneId → state (zones take the state of the nearest bound zone, computed by the server). */
export function zoneStateIndex(dto: BuildTwinStateDto | null | undefined): Map<string, BuildElementState> {
  const map = new Map<string, BuildElementState>();
  if (!dto) return map;
  for (const state of ELEMENT_STATES) for (const id of dto.zoneStates?.[state] ?? []) map.set(id, state);
  return map;
}

/** zoneId → state, from GET twin/zones/state (manual zones; unknown or unbound states are left out). */
export function manualZoneStateIndex(dto: BuildZoneStatesDto | null | undefined): Map<string, BuildElementState> {
  const map = new Map<string, BuildElementState>();
  for (const z of dto?.zones ?? []) if (z.state && (ELEMENT_STATES as string[]).includes(z.state)) map.set(z.zoneId, z.state);
  return map;
}

/** Legend rows: every state with its count, then the unbound ones. */
export function legendRows(dto: BuildTwinStateDto | null | undefined): Array<{ key: string; label: string; dot: string; count: number }> {
  const rows: Array<{ key: string; label: string; dot: string; count: number }> = ELEMENT_STATES.map((s) => ({ key: s, label: STATE_META[s].label, dot: STATE_META[s].dot, count: dto?.counts?.[s] ?? 0 }));
  rows.push({ key: "Unbound", label: UNBOUND_META.label, dot: UNBOUND_META.dot, count: dto?.unbound ?? 0 });
  return rows;
}

// ── Formats and limits ────────────────────────────────────────────────────────

/** Default of Build:Models:MaxUploadMb (the server's 413 is the authority). */
export const MODEL_MAX_UPLOAD_MB = 150;

/** The server's own guidance for RVT/DWG (BuildModelFormats.RvtDwgMessage). */
export const RVT_DWG_INSTRUCTIONS =
  "Arquivos .rvt/.dwg precisam ser exportados para IFC (Revit: Arquivo → Exportar → IFC; AutoCAD/Civil: exportar IFC) ou GLB. "
  + "Conversão automática exige serviço externo, não incluído.";

export const FORMAT_MATRIX: Array<{ formats: string; result: string; tone: "best" | "ok" | "export" | "assisted" | "none" }> = [
  { formats: "IFC (IFC2X3 / IFC4)", result: "Recomendado: elementos, pavimentos e quantidades do projeto BIM.", tone: "best" },
  { formats: "GLB / glTF", result: "Modelo 3D pronto para visualizar; cada objeto vira um elemento.", tone: "ok" },
  { formats: "RVT / DWG", result: RVT_DWG_INSTRUCTIONS, tone: "export" },
  { formats: "PDF, imagem (PNG/JPEG/WebP), DXF", result: "Mapeamento assistido: você desenha as zonas sobre a planta (2D).", tone: "assisted" },
  { formats: "Sem projeto", result: "Representação simplificada: caixas com as medidas que você digitar, sempre rotuladas.", tone: "none" },
];

export const MODEL_ACCEPT = ".ifc,.glb,.gltf,.dxf,.pdf,.png,.jpg,.jpeg,.webp,.rvt,.dwg";

/** Size limits of reality capture (Build:RealityCapture:Max*Mb defaults). */
export const CAPTURE_LIMITS_MB = { image: 25, video: 200, pointCloud: 500 } as const;

export const CAPTURE_TYPES: Record<BuildRealityCaptureType, { label: string; accept: string; limit: string }> = {
  Photo:      { label: "Foto",               accept: "image/jpeg,image/png,image/webp", limit: "imagem até 25 MB" },
  Photo360:   { label: "Foto 360°",          accept: "image/jpeg,image/png,image/webp", limit: "imagem equirretangular até 25 MB" },
  Video:      { label: "Vídeo",              accept: "video/mp4,video/quicktime,video/webm", limit: "vídeo até 200 MB" },
  Drone:      { label: "Drone",              accept: "image/jpeg,image/png,image/webp,video/mp4,video/quicktime,video/webm",
                limit: "imagem até 25 MB ou vídeo até 200 MB" },
  PointCloud: { label: "Nuvem de pontos",    accept: ".las,.e57", limit: ".las/.e57 até 500 MB (guardada sem processamento)" },
};

const MB = 1024 * 1024;

/** The limit that applies to this file for this capture type, in bytes. */
export function captureLimitBytes(type: BuildRealityCaptureType, file: Pick<File, "type" | "name">): number {
  if (type === "PointCloud") return CAPTURE_LIMITS_MB.pointCloud * MB;
  if (type === "Video") return CAPTURE_LIMITS_MB.video * MB;
  if (type === "Drone") return file.type.startsWith("video/") || /\.(mp4|mov|webm)$/i.test(file.name)
    ? CAPTURE_LIMITS_MB.video * MB : CAPTURE_LIMITS_MB.image * MB;
  return CAPTURE_LIMITS_MB.image * MB;
}

export function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`;
  if (n < MB) return `${(n / 1024).toFixed(0)} KB`;
  if (n < 1024 * MB) return `${(n / MB).toLocaleString("pt-BR", { maximumFractionDigits: 1 })} MB`;
  return `${(n / (1024 * MB)).toLocaleString("pt-BR", { maximumFractionDigits: 2 })} GB`;
}

// ── Labels ────────────────────────────────────────────────────────────────────

export const DISCIPLINES: Array<{ value: BuildModelDiscipline; label: string }> = [
  { value: "Arquitetura", label: "Arquitetura" },
  { value: "Estrutura",   label: "Estrutura" },
  { value: "Hidraulica",  label: "Hidráulica" },
  { value: "Eletrica",    label: "Elétrica" },
  { value: "Outro",       label: "Outro" },
];

export const ZONE_KINDS: Record<BuildZoneKind, string> = {
  Site: "Terreno", Building: "Edifício", Storey: "Pavimento", Space: "Ambiente",
  Floor: "Pavimento", Area: "Área", Room: "Cômodo", Facade: "Fachada", Roof: "Cobertura", External: "Área externa",
};

export const MANUAL_ZONE_KINDS: BuildManualZoneKind[] = ["Floor", "Area", "Room", "Facade", "Roof", "External"];

export const VERSION_STATUS: Record<BuildModelVersionStatus, { label: string; tone: "gray" | "blue" | "green" | "red" | "amber" }> = {
  Uploaded:   { label: "Enviado",        tone: "gray" },
  Queued:     { label: "Na fila",        tone: "blue" },
  Processing: { label: "Processando",    tone: "blue" },
  Ready:      { label: "Pronto",         tone: "green" },
  Failed:     { label: "Falhou",         tone: "red" },
  Rejected:   { label: "Recusado",       tone: "amber" },
};

export const GEOMETRY_LABEL: Record<string, string> = {
  NativeBim: "Modelo BIM (IFC)",
  Converted3D: "Modelo 3D (GLB/glTF)",
  AssistedMapping: "Mapeamento sobre a planta (2D)",
  SimplifiedRepresentation: "Representação simplificada",
  None: "Sem modelo",
};

export const SIMPLIFIED_LABEL = "Representação simplificada — não corresponde ao projeto arquitetônico";

// ── Processing poll ───────────────────────────────────────────────────────────

export const VERSION_POLL_MS = 3000;

/** While the server still works on the version, poll every 3 s; stop on Ready / Failed / Rejected. */
export function versionPollInterval(status: BuildModelVersionStatus | undefined): number | false {
  return status === "Queued" || status === "Processing" || status === "Uploaded" ? VERSION_POLL_MS : false;
}

/** Can the browser build the viewer GLB from this IFC version? */
export function needsBrowserConversion(v: Pick<BuildModelVersionDto, "sourceFormat" | "status" | "hasViewer">): boolean {
  return v.sourceFormat === "Ifc" && v.status === "Ready" && !v.hasViewer;
}

export function isSheetVersion(v: Pick<BuildModelVersionDto, "sourceFormat" | "status">): boolean {
  return (v.sourceFormat === "Pdf" || v.sourceFormat === "Image" || v.sourceFormat === "Dxf") && v.status === "Ready";
}

/**
 * The version the obra shows (the server's "versão principal"): current Ready versions, 3D first, with viewer first,
 * Arquitetura first, then the oldest model.
 */
export function primaryVersion(models: BuildModelDto[]): BuildModelVersionDto | null {
  const candidates = models.flatMap((m) => {
    const v = m.versions.find((x) => x.id === m.currentVersionId && x.status === "Ready");
    return v ? [{ m, v }] : [];
  });
  const is3D = (v: BuildModelVersionDto) => v.geometrySource === "NativeBim" || v.geometrySource === "Converted3D";
  candidates.sort((a, b) =>
    Number(is3D(b.v)) - Number(is3D(a.v))
    || Number(b.v.hasViewer) - Number(a.v.hasViewer)
    || Number(a.m.discipline !== "Arquitetura") - Number(b.m.discipline !== "Arquitetura")
    || a.m.createdAt.localeCompare(b.m.createdAt));
  return candidates[0]?.v ?? null;
}

// ── Errors ────────────────────────────────────────────────────────────────────

export const BUILD_3D_DISABLED_CODE = "build_3d_disabled";
export const BUILD_3D_DISABLED_MESSAGE = "O 3D não está habilitado para sua empresa. Fale com o suporte Orken.";

/** Message for a failed model/viewer/capture upload: 403 3D off, 413, 422 (the server's own sentence), 503. */
export function twinErrorMessage(e: unknown, fallback = "Não foi possível enviar o arquivo."): string {
  if (e instanceof ApiError) {
    if (e.status === 403 && e.code === BUILD_3D_DISABLED_CODE) return BUILD_3D_DISABLED_MESSAGE;
    if (e.status === 413) return e.serverMessage ?? "Arquivo grande demais para o limite configurado.";
    if (e.status === 503) return "Armazenamento de arquivos indisponível no momento. Tente novamente mais tarde.";
    if (e.status === 422 && e.code === "viewer_mismatch")
      return `${e.serverMessage ?? "O arquivo gerado não corresponde a esta versão."} Confira se o IFC escolhido é o mesmo desta versão.`;
    if (e.status === 0) return e.message;
    return e.serverMessage ?? e.message ?? fallback;
  }
  return e instanceof Error && e.message ? e.message : fallback;
}

// ── Polygons (assisted mapping) ───────────────────────────────────────────────

export const MAX_OUTLINE_POINTS = 200;

/** null when the outline can be saved (3–200 points, every coordinate 0..1); otherwise the reason. */
export function validateOutline(points: BuildZonePoint[]): string | null {
  if (points.length < 3) return "Desenhe pelo menos 3 pontos.";
  if (points.length > MAX_OUTLINE_POINTS) return `No máximo ${MAX_OUTLINE_POINTS} pontos por zona.`;
  const bad = points.some((p) => !Number.isFinite(p.x) || !Number.isFinite(p.y) || p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1);
  return bad ? "Os pontos precisam estar dentro da planta (0 a 1)." : null;
}

/** A click inside a box → normalized point (0..1, 4 decimals, clamped). */
export function normalizePoint(px: number, py: number, width: number, height: number): BuildZonePoint {
  const clamp = (v: number) => Math.min(1, Math.max(0, v));
  const round = (v: number) => Math.round(v * 10000) / 10000;
  return { x: round(clamp(width > 0 ? px / width : 0)), y: round(clamp(height > 0 ? py / height : 0)) };
}

// ── Time slider ───────────────────────────────────────────────────────────────

const DAY_MS = 86_400_000;

function parseDay(d: string): number {
  const [y, m, day] = d.slice(0, 10).split("-").map(Number);
  return Date.UTC(y, m - 1, day);
}

function formatDay(ms: number): string {
  const d = new Date(ms);
  return `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, "0")}-${String(d.getUTCDate()).padStart(2, "0")}`;
}

/** Days from start to end (≥ 0). */
export function daysBetween(start: string, end: string): number {
  return Math.max(0, Math.round((parseDay(end) - parseDay(start)) / DAY_MS));
}

/** yyyy-MM-dd `offset` days after start. */
export function addDays(start: string, offset: number): string {
  return formatDay(parseDay(start) + offset * DAY_MS);
}

/** Simplified boxes stacked by elevation (then order): bottom first. */
export function stackBoxes<T extends { simplifiedBox: { elevation: number } | null; order: number }>(zones: T[]): T[] {
  return zones.filter((z) => z.simplifiedBox)
    .sort((a, b) => a.simplifiedBox!.elevation - b.simplifiedBox!.elevation || a.order - b.order);
}

/**
 * Today's state of manual zones when the obra has no model version to ask the server (simplified representation only):
 * the nearest zone (itself or an ancestor) bound to a stage; several stages → the latest planned end. Same reading of the
 * stage as the server's engine for today: 100% → Completed, blocked → Blocked, planned end passed → Delayed, % > 0 →
 * InProgress, else NotStarted. Unbound zones are absent (neutral).
 */
export function zoneStatesToday(
  zones: Array<{ id: string; parentZoneId: string | null }>,
  bindings: Array<{ scope: string; zoneId: string | null; stageId: string }>,
  stages: Array<{ id: string; progressPercent: number; status: string; plannedEndDate: string | null }>,
  today: string,
): Map<string, BuildElementState> {
  const parent = new Map(zones.map((z) => [z.id, z.parentZoneId]));
  const stageById = new Map(stages.map((s) => [s.id, s]));
  const bound = new Map<string, string[]>();
  for (const b of bindings) if (b.scope === "Zone" && b.zoneId) bound.set(b.zoneId, [...(bound.get(b.zoneId) ?? []), b.stageId]);

  const stageState = (s: { progressPercent: number; status: string; plannedEndDate: string | null }): BuildElementState => {
    if (s.progressPercent >= 100) return "Completed";
    if (s.status === "Blocked") return "Blocked";
    if (s.plannedEndDate && s.plannedEndDate.slice(0, 10) < today) return "Delayed";
    return s.progressPercent > 0 ? "InProgress" : "NotStarted";
  };

  const out = new Map<string, BuildElementState>();
  for (const z of zones) {
    let cur: string | null = z.id;
    for (let guard = 0; cur && guard < 64; guard++) {
      const ids = (bound.get(cur) ?? []).map((id) => stageById.get(id)).filter((s): s is NonNullable<typeof s> => !!s);
      if (ids.length) {
        ids.sort((a, b) => (b.plannedEndDate ?? "").localeCompare(a.plannedEndDate ?? ""));
        out.set(z.id, stageState(ids[0]));
        break;
      }
      cur = parent.get(cur) ?? null;
    }
  }
  return out;
}
