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

// Tokens are now stored in httpOnly cookies, not accessible via JS
export function getAccessToken(): string | null {
  return null; // Not accessible
}

export function setTokens(access: string, refresh: string): void {
  // Not needed, handled by server
}

export function clearTokens(): void {
  // Not needed, handled by server
}

// ── Core request ─────────────────────────────────────────────────────────────

let refreshPromise: Promise<boolean> | null = null;
const MAX_REFRESH_ATTEMPTS = 1; // Prevent refresh loops

async function attemptRefresh(): Promise<boolean> {
  // Prevent multiple concurrent refresh attempts (race condition)
  if (refreshPromise) return refreshPromise;

  refreshPromise = (async () => {
    try {
      const res = await fetch(`${BASE_URL}/auth/refresh`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({}), // Empty body, refresh token from cookie
        credentials: "include",
      });

      if (!res.ok) {
        clearTokens();
        return false;
      }

      // Cookies updated by server
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

  const res = await fetch(url, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
    credentials: "include", // Include cookies automatically
  });

  if (res.status === 401 && !isRetry) {
    // Attempt refresh only once to prevent infinite loops
    const refreshed = await attemptRefresh();
    if (refreshed) {
      return request<T>(method, path, body, true);
    }
    // If refresh fails, treat as permanent auth failure
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

  const res = await fetch(url, {
    method: "POST",
    body: form,
    credentials: "include", // Include cookies
  });

  if (res.status === 401) {
    // Attempt refresh only once
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
