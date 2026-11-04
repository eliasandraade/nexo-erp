import { apiClient } from "@/services/api-client";
import type { PlatformMfaSetup, PlatformSecurity } from "../types";

// Two-step verification and sessions of the signed-in platform administrator.
// Errors: 422 { error, code } — invalid_password, invalid_code, mfa_already_enabled, mfa_setup_missing.

export const fetchPlatformSecurity = () => apiClient.get<PlatformSecurity>("/platform/auth/security");

export const startPlatformMfaSetup = (password: string) =>
  apiClient.post<PlatformMfaSetup>("/platform/auth/mfa/setup", { password });

/** Answers the recovery codes once — and ends every session, this one included (sign in again with the code). */
export const enablePlatformMfa = (code: string) =>
  apiClient.post<{ recoveryCodes: string[] }>("/platform/auth/mfa/enable", { code });

/** Also ends every session. Takes the app code or a recovery code. */
export const disablePlatformMfa = (input: { password: string; code?: string; recoveryCode?: string }) =>
  apiClient.post<void>("/platform/auth/mfa/disable", input);

export const revokePlatformSession = (id: string) => apiClient.delete<void>(`/platform/auth/sessions/${id}`);

/** Ends every session of the administrator, this one included. */
export const revokeAllPlatformSessions = () => apiClient.post<void>("/platform/auth/sessions/revoke-all");
