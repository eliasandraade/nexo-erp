import type { UserRole } from "@/modules/users/types";

/**
 * Active session stored in localStorage after successful login.
 * Derived from the backend SessionDto returned on login or /auth/me.
 */
export interface AuthSession {
  userId: string;
  tenantId: string;
  name: string;
  role: UserRole;
  login: string;
  email: string;
  /** Module keys granted to this tenant (e.g. "retail", "restaurant") */
  modules: string[];
  /** Optional display label — not in backend, kept for UI compatibility */
  store?: string;
}

export interface LoginInput {
  login: string;
  password: string;
}

// ── Backend DTO shapes (camelCase after JSON.parse) ──────────────────────────

export interface BackendSessionDto {
  userId: string;
  tenantId: string;
  name: string;
  role: string;
  login: string;
  email: string;
  activeModules: string[];
}

export interface BackendLoginResponse {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
  session: BackendSessionDto;
}

export interface BackendRefreshResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
}

// ── Internal result types ────────────────────────────────────────────────────

export interface LoginResult {
  success: true;
  session: AuthSession;
}

export interface LoginError {
  success: false;
  error: string;
}

export type LoginResponse = LoginResult | LoginError;
