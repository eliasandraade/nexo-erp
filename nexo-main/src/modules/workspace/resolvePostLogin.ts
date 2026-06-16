import type { AuthSession } from "@/modules/auth/types";
import type { UserRole } from "@/modules/users/types";
import { homeRoute } from "@/modules/auth/hooks/useRoleAccess";
import { availableWorkspaces } from "./config";
import type { WorkspaceId } from "./types";

/** Route shown when a user must pick (or has no) workspace. */
export const WORKSPACE_SELECT_ROUTE = "/workspaces";

const MANAGEMENT: UserRole[] = ["diretoria", "gerente"];

export function isManagementRole(role: UserRole): boolean {
  return MANAGEMENT.includes(role);
}

/**
 * Pure decision for where to send a user right after authentication.
 *
 * Operational roles (vendedor, cozinha, estoquista) keep their role-based home —
 * they don't choose workspaces. Workspace selection is a management concept.
 *
 * Management:
 *   - persisted workspace still available  → that workspace's home
 *   - exactly one workspace available      → that workspace's home (Caso 1)
 *   - two or more, nothing persisted       → the selection screen (Caso 2)
 *   - none available                       → the selection screen (Caso 3 state)
 *
 * `lastWorkspace` is passed in (not read here) to keep this pure and testable.
 */
export function resolvePostLogin(
  session: AuthSession,
  lastWorkspace: WorkspaceId | null
): string {
  if (session.type === "platform") return "/platform";

  if (!isManagementRole(session.role)) return homeRoute(session);

  const available = availableWorkspaces(session);
  if (available.length === 0) return WORKSPACE_SELECT_ROUTE;

  if (lastWorkspace) {
    const match = available.find((w) => w.id === lastWorkspace);
    if (match) return match.home;
  }

  if (available.length === 1) return available[0].home;

  return WORKSPACE_SELECT_ROUTE;
}
