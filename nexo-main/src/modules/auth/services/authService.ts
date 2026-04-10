import {
  apiClient,
  clearTokens,
  setTokens,
  TOKEN_KEYS,
} from "@/services/api-client";
import type {
  AuthSession,
  BackendLoginResponse,
  BackendSessionDto,
  LoginInput,
  LoginResponse,
} from "../types";

// ── Session helpers ───────────────────────────────────────────────────────────

function toAuthSession(dto: BackendSessionDto): AuthSession {
  return {
    userId:   dto.userId,
    tenantId: dto.tenantId,
    name:     dto.name,
    role:     dto.role as AuthSession["role"],
    login:    dto.login,
    email:    dto.email,
    modules:  dto.activeModules ?? [],
  };
}

function persistSession(session: AuthSession): void {
  localStorage.setItem(TOKEN_KEYS.session, JSON.stringify(session));
}

export function getCurrentSession(): AuthSession | null {
  try {
    const raw = localStorage.getItem(TOKEN_KEYS.session);
    if (!raw) return null;
    return JSON.parse(raw) as AuthSession;
  } catch {
    return null;
  }
}

// ── Auth operations ───────────────────────────────────────────────────────────

export async function login(input: LoginInput): Promise<LoginResponse> {
  try {
    const data = await apiClient.post<BackendLoginResponse>("/auth/login", {
      login:    input.login,
      password: input.password,
    });

    const session = toAuthSession(data.session);
    setTokens(data.accessToken, data.refreshToken);
    persistSession(session);

    return { success: true, session };
  } catch (err: unknown) {
    const message =
      err instanceof Error ? err.message : "Erro ao conectar com o servidor.";
    if (message === "Unauthorized" || message.includes("401")) {
      return { success: false, error: "Login ou senha incorretos." };
    }
    return { success: false, error: message };
  }
}

export async function logout(): Promise<void> {
  try {
    const refreshToken = localStorage.getItem(TOKEN_KEYS.refresh);
    if (refreshToken) {
      // fire-and-forget — don't block the UI
      apiClient.post("/auth/logout", { refreshToken }).catch(() => undefined);
    }
  } finally {
    clearTokens();
  }
}

/**
 * Validates the current session against the backend.
 * Called in the background on app boot. Updates stored session on success;
 * clears tokens on failure so ProtectedRoute redirects to login.
 */
export async function validateSession(): Promise<AuthSession | null> {
  try {
    const dto = await apiClient.get<BackendSessionDto>("/auth/me");
    const session = toAuthSession(dto);
    persistSession(session);
    return session;
  } catch {
    clearTokens();
    return null;
  }
}
