/**
 * API Client — thin fetch wrapper with JWT injection and transparent token refresh.
 *
 * Token storage keys are shared with authService so both modules read/write
 * the same values without circular imports.
 */

export const TOKEN_KEYS = {
  access:  "nexo:access_token",
  refresh: "nexo:refresh_token",
  session: "nexo:session",
} as const;

const BASE_URL =
  import.meta.env.VITE_API_BASE_URL || "http://localhost:5000/api";

// ── Token helpers ─────────────────────────────────────────────────────────────

// Keep access token in memory to survive the tab session without localStorage XSS risk.
// localStorage is the fallback so a hard reload doesn't immediately log the user out.
let _accessToken: string | null = null;

export function getAccessToken(): string | null {
  return _accessToken ?? localStorage.getItem(TOKEN_KEYS.access);
}

export function setTokens(access: string, refresh: string): void {
  if (access) {
    _accessToken = access;
    localStorage.setItem(TOKEN_KEYS.access, access);
  }
  if (refresh) {
    localStorage.setItem(TOKEN_KEYS.refresh, refresh);
  }
}

export function clearTokens(): void {
  _accessToken = null;
  localStorage.removeItem(TOKEN_KEYS.access);
  localStorage.removeItem(TOKEN_KEYS.refresh);
  localStorage.removeItem(TOKEN_KEYS.session);
}

// ── Core request ─────────────────────────────────────────────────────────────

let refreshPromise: Promise<boolean> | null = null;

async function attemptRefresh(): Promise<boolean> {
  if (refreshPromise) return refreshPromise;

  refreshPromise = (async () => {
    try {
      const refreshToken = localStorage.getItem(TOKEN_KEYS.refresh);
      if (!refreshToken) return false;

      const res = await fetch(`${BASE_URL}/auth/refresh`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ refreshToken }),
      });

      if (!res.ok) {
        clearTokens();
        return false;
      }

      const data = await res.json();
      if (data.accessToken) {
        setTokens(data.accessToken, data.refreshToken ?? "");
      }
      return true;
    } catch (error) {
      console.error("Token refresh failed:", error);
      clearTokens();
      return false;
    } finally {
      refreshPromise = null;
    }
  })();

  return refreshPromise;
}

async function request<T>(
  method: string,
  path: string,
  body?: unknown,
  isRetry = false
): Promise<T> {
  const url = `${BASE_URL}${path}`;

  const headers: Record<string, string> = {
    "Content-Type": "application/json",
  };

  const token = getAccessToken();
  if (token) headers["Authorization"] = `Bearer ${token}`;

  const res = await fetch(url, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });

  // sign-in calls answer 401 for wrong credentials/codes: no token refresh for those
  const anonymousAuth = path.startsWith("/auth/login") || path.startsWith("/auth/mfa/verify");
  if (res.status === 401 && !isRetry && !anonymousAuth) {
    const refreshed = await attemptRefresh();
    if (refreshed) {
      return request<T>(method, path, body, true);
    }
    clearTokens();
    // keep the server's explanation (wrong code, unverified e-mail, blocked account…)
    let unauthorized = "Unauthorized";
    let reason: string | undefined;
    try {
      const err = await res.json();
      unauthorized = err?.error ?? err?.message ?? unauthorized;
      reason = err?.code;
    } catch {
      // empty or non-JSON body
    }
    throw new ApiError(401, unauthorized, reason);
  }

  if (res.status === 401) {
    let message = "Unauthorized";
    let reason: string | undefined;
    try {
      const err = await res.json();
      message = err?.error ?? err?.message ?? message;
      reason = err?.code;
    } catch {
      // empty or non-JSON body
    }
    if (!anonymousAuth) clearTokens();
    throw new ApiError(401, message, reason);
  }

  if (!res.ok) throw await errorOf(res);

  if (res.status === 204) return undefined as unknown as T;

  return res.json() as Promise<T>;
}

async function requestForm<T>(path: string, form: FormData): Promise<T> {
  const url = `${BASE_URL}${path}`;

  const headers: Record<string, string> = {};
  const token = getAccessToken();
  if (token) headers["Authorization"] = `Bearer ${token}`;

  const res = await fetch(url, {
    method: "POST",
    headers,
    body: form,
  });

  if (res.status === 401) {
    const refreshed = await attemptRefresh();
    if (refreshed) {
      return requestForm<T>(path, form);
    }
    clearTokens();
    // keep the server's explanation (wrong code, unverified e-mail, blocked account…)
    let unauthorized = "Unauthorized";
    try {
      const err = await res.json();
      unauthorized = err?.error ?? err?.message ?? unauthorized;
    } catch {
      // empty or non-JSON body
    }
    throw new ApiError(401, unauthorized);
  }

  if (!res.ok) throw await errorOf(res);

  if (res.status === 204) return undefined as unknown as T;

  return res.json() as Promise<T>;
}

/**
 * The error of a failed response. The server's explanation ({ error }) is kept and its details (reasons, rules,
 * fields) are added after it — never replacing it: "refused (code): rule A, rule B" loses nothing. A bare
 * validation failure shows only its details. 5xx carry the request id, for support.
 */
export async function errorOf(res: Response): Promise<ApiError> {
  let message = res.statusText;
  let code: string | undefined;
  let requestId: string | undefined;
  let details: string[] = [];
  try {
    const err = await res.json();
    details = Array.isArray(err?.details) ? err.details.filter((d: unknown): d is string => typeof d === "string") : [];
    const explanation: string | undefined = err?.error ?? err?.message;
    const generic = !explanation || explanation === "Validation failed.";
    const extra = generic ? details : details.filter((d) => !explanation.includes(d));
    message = generic
      ? (details.join(" | ") || explanation || message)
      : extra.length > 0 ? `${explanation} ${extra.join(" | ")}` : explanation;
    code = err?.code ?? undefined;
    requestId = err?.requestId;
  } catch {
    // empty or non-JSON body
  }
  if (res.status === 429 && !message) message = "Muitas tentativas. Aguarde alguns minutos.";
  // an unexpected error carries the id of its log lines: show it, so support can find them
  if (res.status >= 500 && requestId) message = `${message} (código: ${requestId})`;
  return new ApiError(res.status, message, code, requestId, details);
}

// ── Public client ─────────────────────────────────────────────────────────────

export const apiClient = {
  get:      <T>(path: string)                   => request<T>("GET",    path),
  post:     <T>(path: string, body?: unknown)   => request<T>("POST",   path, body),
  put:      <T>(path: string, body: unknown)    => request<T>("PUT",    path, body),
  patch:    <T>(path: string, body: unknown)    => request<T>("PATCH",  path, body),
  delete:   <T>(path: string)                   => request<T>("DELETE", path),
  postForm: <T>(path: string, form: FormData)   => requestForm<T>(path, form),
};

// ── Error type ────────────────────────────────────────────────────────────────

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    /** Machine-readable reason from the server body ({ code }), when it sends one. */
    public readonly code?: string,
    /** Id of the request in the server logs (X-Request-Id), for support. */
    public readonly requestId?: string,
    /** The server's itemised reasons (refused rules, coverage findings, invalid fields), when it sends them. */
    public readonly details: string[] = []
  ) {
    super(message);
    this.name = "ApiError";
  }
}
