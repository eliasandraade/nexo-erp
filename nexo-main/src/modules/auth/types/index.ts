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
  /** Active store ID from JWT storeId claim */
  storeId?: string;
  /** All store IDs accessible to this user */
  storeIds: string[];
  /** Company display name (TradeName ?? CompanyName from tenant) */
  companyName: string;
  /** "tenant" for regular users, "platform" for NexoERP superusers */
  type: "tenant" | "platform";
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
  storeId?: string;
  storeIds?: string[];
  companyName?: string;
  type?: string;
}

export interface BackendSwitchStoreResponse {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
  session: BackendSessionDto;
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
