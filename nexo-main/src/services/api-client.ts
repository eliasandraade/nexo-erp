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
  // TEMP DEBUG — remove after diagnosing 401s
  console.log('[nexo-auth]', method, path, {
    hasToken: !!token,
    tokenPrefix: token ? token.substring(0, 20) + '...' : null,
    lsAccess: localStorage.getItem(TOKEN_KEYS.access)?.substring(0, 20),
    lsRefresh: !!localStorage.getItem(TOKEN_KEYS.refresh),
  });
  if (token) headers["Authorization"] = `Bearer ${token}`;

  const res = await fetch(url, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });

  if (res.status === 401 && !isRetry) {
    const refreshed = await attemptRefresh();
    if (refreshed) {
      return request<T>(method, path, body, true);
    }
    clearTokens();
    throw new ApiError(401, "Unauthorized");
  }

  if (!res.ok) {
    let message = res.statusText;
    try {
      const err = await res.json();
      if (Array.isArray(err?.details) && err.details.length > 0) {
        message = err.details.join(" | ");
      } else {
        message = err?.error ?? err?.message ?? message;
      }
    } catch {
      // ignore parse failure
    }
    throw new ApiError(res.status, message);
  }

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
    throw new ApiError(401, "Unauthorized");
  }

  if (!res.ok) {
    let message = res.statusText;
    try {
      const err = await res.json();
      if (Array.isArray(err?.details) && err.details.length > 0) {
        message = err.details.join(" | ");
      } else {
        message = err?.error ?? err?.message ?? message;
      }
    } catch {
      // ignore parse failure
    }
    throw new ApiError(res.status, message);
  }

  if (res.status === 204) return undefined as unknown as T;

  return res.json() as Promise<T>;
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
    message: string
  ) {
    super(message);
    this.name = "ApiError";
  }
}
