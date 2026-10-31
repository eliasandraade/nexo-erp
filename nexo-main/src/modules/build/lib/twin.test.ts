import { describe, it, expect } from "vitest";
import { ApiError } from "@/services/api-client";
import type { BuildModelDto, BuildModelVersionDto, BuildTwinStateDto } from "../api/build-twin.api";
import {
  BUILD_3D_DISABLED_MESSAGE, FORMAT_MATRIX, MAX_OUTLINE_POINTS, RVT_DWG_INSTRUCTIONS, STATE_META, VERSION_POLL_MS, addDays,
  captureLimitBytes, daysBetween, legendRows, needsBrowserConversion, normalizePoint, primaryVersion, stackBoxes, stateColor,
  stateIndex, twinErrorMessage, validateOutline, versionPollInterval, zoneStateIndex, zoneStatesToday,
} from "./twin";

const state: BuildTwinStateDto = {
  date: "2026-10-01",
  counts: { Completed: 2, InProgress: 1, Delayed: 1 },
  states: { Completed: ["a", "b"], InProgress: ["c"], Delayed: ["d"] },
  zoneStates: { InProgress: ["z1"] },
  unbound: 3,
  total: 7,
};

describe("state colors", () => {
  it("maps each state to its color and unbound to a faint transparent neutral", () => {
    expect(stateColor("NotStarted").color).toBe("#9ca3af");     // gray
    expect(stateColor("InProgress").color).toBe("#f59e0b");     // amber
    expect(stateColor("Completed").color).toBe("#16a34a");      // green
    expect(stateColor("Delayed").color).toBe("#dc2626");        // red
    expect(stateColor("Blocked").color).toBe("#9333ea");        // purple
    expect(stateColor("NeedsReview").color).toBe("#2563eb");    // blue
    expect(stateColor("Completed")).toMatchObject({ opacity: 1, transparent: false });
    const unbound = stateColor(null);
    expect(unbound.transparent).toBe(true);
    expect(unbound.opacity).toBeLessThan(0.5);
    expect(Object.keys(STATE_META)).toHaveLength(6);
  });

  it("indexes external ids and zone ids by state, and the legend counts every state plus the unbound", () => {
    const idx = stateIndex(state);
    expect(idx.get("a")).toBe("Completed");
    expect(idx.get("d")).toBe("Delayed");
    expect(idx.has("zzz")).toBe(false);
    expect(zoneStateIndex(state).get("z1")).toBe("InProgress");
    const rows = legendRows(state);
    expect(rows.find((r) => r.key === "Completed")?.count).toBe(2);
    expect(rows.find((r) => r.key === "Blocked")?.count).toBe(0);
    expect(rows.at(-1)).toMatchObject({ key: "Unbound", count: 3 });
  });
});

describe("formats and errors", () => {
  it("the onboarding matrix carries the server's RVT/DWG export instructions", () => {
    const rvt = FORMAT_MATRIX.find((r) => r.formats.includes("RVT"));
    expect(rvt?.result).toBe(RVT_DWG_INSTRUCTIONS);
    expect(RVT_DWG_INSTRUCTIONS).toContain("Revit: Arquivo → Exportar → IFC");
  });

  it("403 build_3d_disabled → the support message; 422 keeps the server's sentence", () => {
    expect(twinErrorMessage(new ApiError(403, "x", "build_3d_disabled", undefined, undefined, "desligado"))).toBe(BUILD_3D_DISABLED_MESSAGE);
    expect(twinErrorMessage(new ApiError(422, "x", "model_format_compressed", undefined, undefined, "Descompacte."))).toBe("Descompacte.");
    expect(twinErrorMessage(new ApiError(422, "x", "viewer_mismatch", undefined, undefined, "Só 10%."))).toMatch(/^Só 10%\. Confira se o IFC/);
  });

  it("capture limits per type (drone: image 25 MB, video 200 MB)", () => {
    const MB = 1024 * 1024;
    expect(captureLimitBytes("Photo", { type: "image/jpeg", name: "a.jpg" })).toBe(25 * MB);
    expect(captureLimitBytes("Video", { type: "video/mp4", name: "a.mp4" })).toBe(200 * MB);
    expect(captureLimitBytes("Drone", { type: "video/mp4", name: "a.mp4" })).toBe(200 * MB);
    expect(captureLimitBytes("Drone", { type: "image/png", name: "a.png" })).toBe(25 * MB);
    expect(captureLimitBytes("PointCloud", { type: "", name: "scan.las" })).toBe(500 * MB);
  });
});

describe("processing poll", () => {
  it("polls every 3 s while queued/processing and stops on Ready, Failed or Rejected", () => {
    expect(versionPollInterval("Queued")).toBe(VERSION_POLL_MS);
    expect(versionPollInterval("Processing")).toBe(3000);
    expect(versionPollInterval("Ready")).toBe(false);
    expect(versionPollInterval("Failed")).toBe(false);
    expect(versionPollInterval("Rejected")).toBe(false);
  });

  it("browser conversion only for an IFC version Ready without viewer", () => {
    expect(needsBrowserConversion({ sourceFormat: "Ifc", status: "Ready", hasViewer: false })).toBe(true);
    expect(needsBrowserConversion({ sourceFormat: "Ifc", status: "Ready", hasViewer: true })).toBe(false);
    expect(needsBrowserConversion({ sourceFormat: "Glb", status: "Ready", hasViewer: false })).toBe(false);
    expect(needsBrowserConversion({ sourceFormat: "Ifc", status: "Processing", hasViewer: false })).toBe(false);
  });
});

describe("zone outlines (assisted mapping)", () => {
  const tri = [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 0.5, y: 1 }];
  it("needs 3 to 200 points, all within 0..1", () => {
    expect(validateOutline(tri)).toBeNull();
    expect(validateOutline(tri.slice(0, 2))).toMatch(/3 pontos/);
    const many = Array.from({ length: MAX_OUTLINE_POINTS + 1 }, (_, i) => ({ x: (i % 10) / 10, y: 0.5 }));
    expect(validateOutline(many)).toMatch(/200 pontos/);
    expect(validateOutline(many.slice(0, 200))).toBeNull();
    expect(validateOutline([...tri, { x: 1.2, y: 0.5 }])).toMatch(/0 a 1/);
    expect(validateOutline([...tri, { x: -0.01, y: 0.5 }])).toMatch(/0 a 1/);
  });

  it("normalizes clicks to 0..1 (clamped, 4 decimals)", () => {
    expect(normalizePoint(50, 25, 200, 100)).toEqual({ x: 0.25, y: 0.25 });
    expect(normalizePoint(-5, 150, 200, 100)).toEqual({ x: 0, y: 1 });
    expect(normalizePoint(1, 1, 3, 3)).toEqual({ x: 0.3333, y: 0.3333 });
  });
});

describe("time and zones", () => {
  it("counts days and moves along the slider", () => {
    expect(daysBetween("2026-01-30", "2026-03-01")).toBe(30);
    expect(addDays("2026-01-30", 30)).toBe("2026-03-01");
    expect(daysBetween("2026-05-01", "2026-04-01")).toBe(0);
  });

  it("stacks boxes by elevation and leaves zones without measures out", () => {
    const zones = [
      { id: "2", order: 0, simplifiedBox: { width: 10, depth: 10, height: 3, elevation: 3 } },
      { id: "x", order: 0, simplifiedBox: null },
      { id: "1", order: 0, simplifiedBox: { width: 10, depth: 10, height: 3, elevation: 0 } },
    ];
    expect(stackBoxes(zones).map((z) => z.id)).toEqual(["1", "2"]);
  });

  it("today's zone state from the nearest bound zone (no model to ask)", () => {
    const zones = [{ id: "floor", parentZoneId: null }, { id: "room", parentZoneId: "floor" }, { id: "free", parentZoneId: null }];
    const bindings = [{ scope: "Zone", zoneId: "floor", stageId: "s1" }];
    const stages = [{ id: "s1", progressPercent: 40, status: "InProgress", plannedEndDate: "2099-01-01" }];
    const map = zoneStatesToday(zones, bindings, stages, "2026-10-30");
    expect(map.get("floor")).toBe("InProgress");
    expect(map.get("room")).toBe("InProgress");          // inherits from the parent
    expect(map.has("free")).toBe(false);                  // unbound stays neutral
    const late = zoneStatesToday(zones, bindings, [{ ...stages[0], plannedEndDate: "2026-10-01" }], "2026-10-30");
    expect(late.get("floor")).toBe("Delayed");
    const done = zoneStatesToday(zones, bindings, [{ ...stages[0], progressPercent: 100 }], "2026-10-30");
    expect(done.get("floor")).toBe("Completed");
  });
});

describe("primary version", () => {
  const v = (id: string, over: Partial<BuildModelVersionDto> = {}): BuildModelVersionDto => ({
    id, modelId: "m", number: 1, sourceFormat: "Ifc", sourceFileName: "a.ifc", sizeBytes: 1, sha256: null, geometrySource: "NativeBim",
    viewerSource: "None", hasViewer: false, status: "Ready", statusMessage: null, elementCount: 1, storeyCount: 1, schema: null,
    layers: null, processedAt: null, createdAt: "2026-01-01", ...over,
  });
  const m = (id: string, version: BuildModelVersionDto, over: Partial<BuildModelDto> = {}): BuildModelDto => ({
    id, projectId: "p", name: id, discipline: "Estrutura", currentVersionId: version.id, versions: [version], createdAt: "2026-01-01", ...over,
  });

  it("prefers 3D with viewer, then Arquitetura, and ignores versions that are not Ready", () => {
    const sheet = m("sheet", v("v-sheet", { sourceFormat: "Pdf", geometrySource: "AssistedMapping" }), { discipline: "Arquitetura" });
    const struct = m("struct", v("v-struct", { hasViewer: true }));
    const arch = m("arch", v("v-arch", { hasViewer: true }), { discipline: "Arquitetura", createdAt: "2026-02-01" });
    const queued = m("queued", v("v-q", { status: "Queued", hasViewer: true }), { discipline: "Arquitetura" });
    expect(primaryVersion([sheet, struct, arch, queued])?.id).toBe("v-arch");
    expect(primaryVersion([sheet])?.id).toBe("v-sheet");
    expect(primaryVersion([queued])).toBeNull();
  });
});
