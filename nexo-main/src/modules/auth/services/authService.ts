import {
  ApiError,
  announceSessionToOtherTabs,
  apiClient,
  beginImpersonation,
  clearTokens,
  getImpersonation,
  hasSession,
  isCookieSessionMode,
  leaveImpersonation,
  getRefreshToken,
  getStoredSession,
  renewFromCookieWithoutSession,
  setStoredSession,
  setTokens,
  withSessionLock,
} from "@/services/api-client";
import type {
  AuthSession,
  BackendLoginResponse,
  BackendRegisterResponse,
  BackendSessionDto,
  BackendSwitchStoreResponse,
  LoginInput,
  LoginResponse,
  RegisterInput,
  RegisterResult,
  VerifyEmailResult,
} from "../types";

// ── Session helpers ───────────────────────────────────────────────────────────

function toAuthSession(dto: BackendSessionDto): AuthSession {
  return {
    userId:       dto.userId,
    tenantId:     dto.tenantId,
    name:         dto.name,
    role:         dto.role as AuthSession["role"],
    login:        dto.login,
    email:        dto.email,
    modules:      dto.activeModules ?? [],
    readOnlyModules: dto.readOnlyModules ?? [],
    storeId:      dto.storeId,
    storeIds:     dto.storeIds ?? [],
    companyName:  dto.companyName ?? "",
    type:         (dto.type === "platform" ? "platform" : "tenant"),
    isNewAccount: dto.isNewAccount ?? false,
    trialEndsAt:  dto.trialEndsAt ?? undefined,
    requirePasswordChange: dto.requirePasswordChange ?? false,
  };
}

// The session lives next to the tokens: localStorage, or this tab's sessionStorage in an impersonation tab.
function persistSession(session: AuthSession): void {
  setStoredSession(JSON.stringify(session));
}

/** A new session in this tab, right after its token is stored. The other tabs hear it in the cookie mode, where nothing else tells them. */
function startSession(accessToken: string, refreshToken: string | undefined, session: AuthSession): void {
  setTokens(accessToken, refreshToken);
  persistSession(session);
  announceSessionToOtherTabs(JSON.stringify(session));
}

export function getCurrentSession(): AuthSession | null {
  try {
    const raw = getStoredSession();
    if (!raw) return null;
    return JSON.parse(raw) as AuthSession;
  } catch {
    return null;
  }
}

// ── Auth operations ───────────────────────────────────────────────────────────

export async function login(input: LoginInput): Promise<LoginResponse> {
  // signing in from a tab whose platform access ended: it becomes a normal tab again
  if (getImpersonation()?.ended) leaveImpersonation();
  try {
    const data = await apiClient.post<BackendLoginResponse | { mfaRequired: true; mfaChallenge: string }>("/auth/login", {
      login:    input.login,
      password: input.password,
    });
    if ("mfaRequired" in data) return { success: false, error: "", mfaChallenge: data.mfaChallenge };

    const session = toAuthSession(data.session);
    startSession(data.accessToken, data.refreshToken, session);

    return { success: true, session };
  } catch (err: unknown) {
    // Without an answer from the server, all there is is the browser's text ("Failed to fetch"): not for the user.
    const message =
      err instanceof ApiError ? err.message : "Erro ao conectar com o servidor.";
    if (message === "Unauthorized" || message === "Invalid login or password." || message.includes("401")) {
      return { success: false, error: "Login ou senha incorretos." };
    }
    return { success: false, error: message };
  }
}

export async function logout(): Promise<void> {
  try {
    const refreshToken = getRefreshToken();
    // In the cookie mode the token is in the cookie: only a session not yet migrated has one to send.
    const body = isCookieSessionMode() ? (refreshToken ? { refreshToken } : undefined) : { refreshToken: refreshToken ?? "" };
    apiClient.post("/auth/logout", body).catch(() => undefined);
  } finally {
    clearTokens();
  }
}

/**
 * Switches the active store. Issues a new JWT pair scoped to the given store.
 * Updates stored tokens and session on success.
 */
export async function switchStore(storeId: string): Promise<AuthSession> {
  // It spends the refresh token like a renewal, so it waits in the same line.
  return withSessionLock(async () => {
    const refreshToken = getRefreshToken() ?? "";
    const data = await apiClient.post<BackendSwitchStoreResponse>("/auth/switch-store", { storeId, refreshToken });
    const session = toAuthSession(data.session);
    startSession(data.accessToken, data.refreshToken, session);
    return session;
  });
}

/**
 * Validates the current session against the backend.
 * Called in the background on app boot. Updates stored session on success;
 * clears tokens when the server refuses them, so ProtectedRoute redirects to login.
 * Rejects when there is no verdict (no connection, server error): that says nothing about the session, and the caller keeps it.
 */
export async function validateSession(): Promise<AuthSession | null> {
  try {
    const dto = await apiClient.get<BackendSessionDto>("/auth/me");
    // Signed out while the answer was on its way: it confirms a session that no longer exists.
    if (!hasSession()) return null;
    const session = toAuthSession(dto);
    persistSession(session);
    return session;
  } catch (err) {
    if (!(err instanceof ApiError) || err.status !== 401) throw err;
    clearTokens();
    return null;
  }
}

/**
 * Cookie mode, nothing in the storage on opening a page that needs a session: the storage may have been cleared while the cookie lives. Then
 * the session comes back with no sign-in. After a sign-out, or once the server said there is none, this asks nothing.
 */
export async function recoverSessionFromCookie(): Promise<AuthSession | null> {
  if (!(await renewFromCookieWithoutSession())) return null;
  const session = await validateSession();
  if (session) announceSessionToOtherTabs(JSON.stringify(session));
  return session;
}

/** What the platform tab hands to the impersonation tab (never through any storage). */
export interface ImpersonationGrant {
  accessToken: string;
  /** When the access ends (ISO). */
  expiresAt: string | null;
  session: BackendSessionDto;
}

/**
 * Starts the tenant session of an impersonation tab: token and session in this tab's sessionStorage only,
 * never in localStorage (the administrator's own session there stays untouched).
 */
export function startImpersonationSession(grant: ImpersonationGrant): AuthSession {
  const session = toAuthSession(grant.session);
  beginImpersonation(grant.accessToken, JSON.stringify(session), {
    tenant: session.companyName,
    user: session.name,
    expiresAt: grant.expiresAt,
  });
  return session;
}

// ── Registration / Email verification ────────────────────────────────────────

export async function register(input: RegisterInput): Promise<RegisterResult> {
  try {
    await apiClient.post<BackendRegisterResponse>("/auth/register", {
      name:     input.name,
      email:    input.email,
      password: input.password,
    });
    return { success: true };
  } catch (err: unknown) {
    const message = err instanceof Error ? err.message : "Erro ao criar conta.";
    if (message.includes("email_already_registered") || message.includes("409")) {
      return { success: false, error: "Este e-mail já está cadastrado." };
    }
    return { success: false, error: message };
  }
}

export async function verifyEmail(token: string): Promise<VerifyEmailResult> {
  try {
    const data = await apiClient.get<BackendLoginResponse>(
      `/auth/verify-email?token=${encodeURIComponent(token)}`
    );
    const session = toAuthSession(data.session);
    startSession(data.accessToken, data.refreshToken, session);
    if (session.isNewAccount) {
      localStorage.setItem(`nexo:onboarding:${session.userId}`, "true");
    }
    return { success: true, session };
  } catch {
    return { success: false, error: "Link inválido ou expirado." };
  }
}

export async function resendVerification(email: string): Promise<void> {
  await apiClient.post("/auth/resend-verification", { email });
}

// ── Account recovery & sessions (PR 63) ─────────────────────────────────────────

/** Always resolves the same way, whether or not the address has an account. */
export async function requestPasswordReset(email: string): Promise<void> {
  await apiClient.post("/auth/forgot-password", { email });
}

export async function resetPassword(token: string, newPassword: string): Promise<void> {
  await apiClient.post("/auth/reset-password", { token, newPassword });
}

export interface UserSessionDto {
  id: string;
  startedAt: string;
  lastUsedAt: string;
  ipAddress: string | null;
  userAgent: string | null;
}

export const fetchMySessions = () => apiClient.get<UserSessionDto[]>("/auth/sessions");

/** Ends every session, this one included. */
export async function revokeAllSessions(): Promise<void> {
  await apiClient.post("/auth/sessions/revoke-all", { confirm: true });
}

// ── Two-step verification (PR 64b) ──────────────────────────────────────────────

/** Second step of the sign-in. Resolves to the session (and stores the tokens) like login does. */
export async function verifyMfa(challenge: string, code: string | null, recoveryCode: string | null = null): Promise<AuthSession> {
  const data = await apiClient.post<BackendLoginResponse>("/auth/mfa/verify", { challenge, code, recoveryCode });
  const session = toAuthSession(data.session);
  startSession(data.accessToken, data.refreshToken, session);
  return session;
}

export interface MfaStatus { enabled: boolean; setupPending: boolean; recoveryCodesLeft: number }
export const fetchMfaStatus = () => apiClient.get<MfaStatus>("/auth/mfa");
export const startMfaSetup = (password: string) => apiClient.post<{ secret: string; otpAuthUri: string }>("/auth/mfa/setup", { password });
export const enableMfa = (code: string) => apiClient.post<{ recoveryCodes: string[] }>("/auth/mfa/enable", { code });
/** The code field takes either the app code or a recovery code (the server tells them apart). */
export const disableMfa = (password: string, code: string) =>
  apiClient.post<void>("/auth/mfa/disable", { password, code });
