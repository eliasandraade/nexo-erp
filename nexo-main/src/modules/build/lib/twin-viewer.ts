import type { BuildModelSourceFormat } from "../api/build-twin.api";

/** A glTF node as the viewer sees it after loading: its index in the file, original name, mesh flag and parent. */
export interface ViewerNode { index: number; name: string | null; hasMesh: boolean; parent: number | null }

/**
 * Element external id of each mesh node, with the server's rules:
 * - IFC version (viewer converted from the IFC): node name = GlobalId; an unnamed mesh takes its nearest named ancestor;
 * - GLB/glTF version: the node name when it is unique among the mesh nodes (≤ 100 chars, not "node:…"), else "node:{index}".
 */
export function viewerNodeIds(nodes: ViewerNode[], sourceFormat: BuildModelSourceFormat): Map<number, string> {
  const byIndex = new Map(nodes.map((n) => [n.index, n]));
  const ids = new Map<number, string>();
  const meshes = nodes.filter((n) => n.hasMesh);

  if (sourceFormat === "Ifc") {
    for (const n of meshes) {
      let cur: ViewerNode | undefined = n;
      for (let guard = 0; cur && guard < 64; guard++) {
        if (cur.name?.trim()) { ids.set(n.index, cur.name.trim()); break; }
        cur = cur.parent == null ? undefined : byIndex.get(cur.parent);
      }
    }
    return ids;
  }

  const count = new Map<string, number>();
  for (const n of meshes) if (n.name != null) count.set(n.name, (count.get(n.name) ?? 0) + 1);
  for (const n of meshes) {
    const nm = n.name;
    ids.set(n.index, nm != null && count.get(nm) === 1 && !nm.startsWith("node:") && nm.length <= 100 ? nm : `node:${n.index}`);
  }
  return ids;
}

/** A pointer that moved less than this (px) between down and up is a click (select), not an orbit drag. */
export const CLICK_TOLERANCE_PX = 5;

export function isClick(down: { x: number; y: number }, up: { x: number; y: number }): boolean {
  return Math.hypot(up.x - down.x, up.y - down.y) <= CLICK_TOLERANCE_PX;
}
