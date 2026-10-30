import type { BuildProjectDetailsDto } from "../api/build.api";
import type { BuildManualZoneKind, BulkBuildBindingRequest, SaveBuildZoneRequest } from "../api/build-twin.api";
import { parseDecimal } from "./format";

// Form → request mapping of the "Gêmeo digital" tab (pure, unit-tested through the components).

export const SLIDER_DEBOUNCE_MS = 300;

/** Request of the multi-select "Vincular à etapa": the chosen element ids of the version. */
export function selectionBindRequest(stageId: string, versionId: string, ids: Iterable<string>): BulkBuildBindingRequest {
  return { stageId, modelVersionId: versionId, externalIds: [...ids], replaceExisting: true };
}

/** Request of "Vincular por categoria e zona": at least one of them. */
export function filterBindRequest(stageId: string, versionId: string, category: string, zoneId: string): BulkBuildBindingRequest {
  return { stageId, modelVersionId: versionId, category: category || null, zoneId: zoneId || null, replaceExisting: true };
}

/** First day of the slider: the obra's start, else the earliest planned stage start, else the version's upload. */
export function timelineStart(project: Pick<BuildProjectDetailsDto, "startDate" | "stages" | "createdAt">, fallback: string): string {
  if (project.startDate) return project.startDate.slice(0, 10);
  const planned = project.stages.map((s) => s.plannedStartDate).filter((d): d is string => !!d).sort((a, b) => a.localeCompare(b));
  if (planned[0]) return planned[0].slice(0, 10);
  return (project.createdAt || fallback).slice(0, 10);
}

export interface ZoneForm {
  id?: string;
  kind: BuildManualZoneKind;
  name: string;
  order: string;
  parentZoneId: string;
  withBox: boolean;
  width: string; depth: string; height: string; elevation: string;
}

export const EMPTY_ZONE_FORM: ZoneForm = { kind: "Floor", name: "", order: "0", parentZoneId: "", withBox: false, width: "", depth: "", height: "", elevation: "0" };

/** Zone form → request; a string with the problem when the measures are not valid. */
export function zoneRequest(f: ZoneForm): SaveBuildZoneRequest | string {
  if (!f.name.trim()) return "Informe o nome da zona.";
  const order = Number.parseInt(f.order || "0", 10);
  const req: SaveBuildZoneRequest = { kind: f.kind, name: f.name.trim(), order: Number.isFinite(order) ? order : 0,
    parentZoneId: f.parentZoneId || null, simplifiedBox: null };
  if (f.withBox) {
    const [w, d, h, e] = [f.width, f.depth, f.height, f.elevation || "0"].map(parseDecimal);
    if ([w, d, h].some((v) => v == null || Number.isNaN(v) || v <= 0) || e == null || Number.isNaN(e))
      return "Informe largura, profundidade e altura maiores que zero (em metros).";
    req.simplifiedBox = { width: w!, depth: d!, height: h!, elevation: e };
  }
  return req;
}
