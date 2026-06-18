import { Store, UtensilsCrossed, HardHat, ConciergeBell } from "lucide-react";
import type { AuthSession } from "@/modules/auth/types";
import type { WorkspaceDef, WorkspaceId } from "./types";

/**
 * The three Orken workspaces, in display order.
 * Order here drives the selection screen and the switcher.
 */
export const WORKSPACES: WorkspaceDef[] = [
  {
    id: "store",
    moduleKey: "varejo",
    name: "Orken Store",
    shortName: "Store",
    description: "Vendas, produtos, estoque, caixa, clientes e fornecedores.",
    icon: Store,
    home: "/dashboard",
    group: "varejo",
    accent: "#5B4DFF",
  },
  {
    id: "menu",
    moduleKey: "restaurante",
    name: "Orken Menu",
    shortName: "Menu",
    description: "Mesas, comandas, delivery, cozinha e cardápio.",
    icon: UtensilsCrossed,
    home: "/dashboard",
    group: "restaurante",
    accent: "#F97316",
  },
  {
    id: "build",
    moduleKey: "build",
    name: "Orken Build",
    shortName: "Build",
    description: "Obras, etapas, despesas, diário de obra e fornecedores.",
    icon: HardHat,
    home: "/build",
    group: "build",
    accent: "#0EA5E9",
  },
  {
    id: "service",
    moduleKey: "service",
    name: "Orken Service",
    shortName: "Service",
    description: "Agenda, ordens de serviço, pacotes e pagamentos.",
    icon: ConciergeBell,
    home: "/service",
    group: "service",
    accent: "#10B981",
  },
];

/** Sidebar groups shared across every workspace (always visible). */
export const SHARED_GROUPS = ["core", "inventario", "admin"] as const;

const BY_ID: Record<WorkspaceId, WorkspaceDef> = WORKSPACES.reduce(
  (acc, w) => ({ ...acc, [w.id]: w }),
  {} as Record<WorkspaceId, WorkspaceDef>
);

export function getWorkspace(id: WorkspaceId): WorkspaceDef {
  return BY_ID[id];
}

/** Workspaces the tenant actually has an active module for, in display order. */
export function availableWorkspaces(session: Pick<AuthSession, "modules">): WorkspaceDef[] {
  return WORKSPACES.filter((w) => session.modules.includes(w.moduleKey));
}

/**
 * Infers the workspace a route belongs to, for keeping the sidebar scope honest
 * when a user deep-links into a vertical area. Shared routes (e.g. /dashboard)
 * return null — they don't belong to any single workspace.
 */
export function workspaceForPath(pathname: string): WorkspaceId | null {
  if (pathname.startsWith("/restaurante")) return "menu";
  if (pathname.startsWith("/build")) return "build";
  if (pathname.startsWith("/service")) return "service";
  if (pathname.startsWith("/pdv")) return "store";
  return null;
}
