import type { AuthSession } from "@/modules/auth/types";
import type { WorkspaceId } from "./types";

const VALID_IDS: WorkspaceId[] = ["store", "menu", "build"];

/**
 * Per-tenant, per-user key for the last chosen workspace.
 * Scoped so two accounts (or tenants) on the same browser never collide.
 */
function lastWorkspaceKey(session: Pick<AuthSession, "tenantId" | "userId">): string {
  return `orken:last-module:${session.tenantId}:${session.userId}`;
}

export function readLastWorkspace(
  session: Pick<AuthSession, "tenantId" | "userId">
): WorkspaceId | null {
  try {
    const raw = localStorage.getItem(lastWorkspaceKey(session));
    return raw && (VALID_IDS as string[]).includes(raw) ? (raw as WorkspaceId) : null;
  } catch {
    return null;
  }
}

export function writeLastWorkspace(
  session: Pick<AuthSession, "tenantId" | "userId">,
  id: WorkspaceId
): void {
  try {
    localStorage.setItem(lastWorkspaceKey(session), id);
  } catch {
    /* storage unavailable (private mode) — non-fatal */
  }
}

export function clearLastWorkspace(
  session: Pick<AuthSession, "tenantId" | "userId">
): void {
  try {
    localStorage.removeItem(lastWorkspaceKey(session));
  } catch {
    /* non-fatal */
  }
}
