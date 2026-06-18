import type { LucideIcon } from "lucide-react";
import type { RouteGroup } from "@/app/router/routes";

/** Stable identifier for a product workspace (the "área de trabalho"). */
export type WorkspaceId = "store" | "menu" | "build" | "service";

/**
 * A product workspace = the lens a tenant operates through.
 * Maps 1:1 to a backend module entitlement key (session.modules):
 *   store → "varejo" · menu → "restaurante" · build → "build" · service → "service"
 */
export interface WorkspaceDef {
  id: WorkspaceId;
  /** Backend module key as it appears in session.modules. */
  moduleKey: string;
  /** Full product name, e.g. "Orken Store". */
  name: string;
  /** Short label for chips/switcher, e.g. "Store". */
  shortName: string;
  /** One-line description for the selection cards. */
  description: string;
  icon: LucideIcon;
  /** Route a user lands on when entering this workspace. */
  home: string;
  /** The vertical sidebar group this workspace owns. */
  group: RouteGroup;
  /** Accent color (hex) used on the selection card. */
  accent: string;
}
