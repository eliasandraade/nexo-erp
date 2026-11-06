import { ApiError, getAccessToken } from "./api-client";

/**
 * Code of the 403 of a company that is not active (suspended or cancelled): every call of its users is refused, the session is
 * not over. See TenantResolutionMiddleware and docs/security/sessoes-e-tempo-real.md.
 */
export const TENANT_SUSPENDED_CODE = "tenant_suspended";

export const isTenantSuspended = (error: unknown): boolean =>
  error instanceof ApiError && error.status === 403 && error.code === TENANT_SUSPENDED_CODE;

/**
 * Whether the access token is still good after this time. An unreadable token counts as good: only the server can judge it.
 *
 * Calls of a suspended company have to stop before the token expires. The server does not renew a token of a suspended company, and
 * the client drops the session when a renewal is refused, so a call made after that would end the session.
 */
export function accessTokenLastsMoreThan(ms: number): boolean {
  const token = getAccessToken();
  if (!token) return false;
  try {
    const payload = token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/");
    const exp = (JSON.parse(atob(payload)) as { exp?: unknown }).exp;
    return typeof exp === "number" ? exp * 1000 - Date.now() > ms : true;
  } catch {
    return true;
  }
}
