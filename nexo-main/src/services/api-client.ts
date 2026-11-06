/**
 * API Client — thin fetch wrapper with JWT injection and transparent token refresh.
 *
 * Token storage keys are shared with authService so both modules read/write
 * the same values without circular imports.
 *
 * Two storage modes:
 *  - normal: tokens + session live in localStorage (shared by every tab of the signed-in user);
 *  - impersonation (a platform administrator acting as a tenant, tab-scoped): the tab carries the
 *    `orken:impersonation` marker in sessionStorage and then reads/writes its access token and session ONLY
 *    in sessionStorage. It never refreshes (the impersonation token has no refresh token) and never reads,
 *    overwrites or clears the administrator's own localStorage session.
 */

export const TOKEN_KEYS = {
  access:  "nexo:access_token",
  refresh: "nexo:refresh_token",
  session: "nexo:session",
} as const;

/** Tab-scoped (sessionStorage) marker of an impersonation tab. */
export const IMPERSONATION_KEY = "orken:impersonation";

const BASE_URL =
  import.meta.env.VITE_API_BASE_URL || "http://localhost:5000/api";

// ── Impersonation mode ────────────────────────────────────────────────────────

export interface ImpersonationInfo {
  /** Company the administrator is acting in. */
  tenant: string;
  /** User the administrator acts as. */
  user: string;
  /** When the impersonation token expires (ISO), when the server said. */
  expiresAt: string | null;
  /** The access ended in this tab: tokens are gone, and the tab never falls back to the admin's own session. */
  ended?: boolean;
}

export function getImpersonation(): ImpersonationInfo | null {
  try {
    const raw = sessionStorage.getItem(IMPERSONATION_KEY);
    return raw ? (JSON.parse(raw) as ImpersonationInfo) : null;
  } catch {
    return null;
  }
}

/** True in a tab opened by a platform administrator to act as a tenant (even after that access ended). */
export function isImpersonating(): boolean {
  return getImpersonation() !== null;
}

/** Where this tab keeps its access token and session. */
function tokenStore(): Storage {
  return isImpersonating() ? sessionStorage : localStorage;
}

/** Turns this tab into an impersonation tab: the token and the session stay in this tab only. */
export function beginImpersonation(accessToken: string, sessionJson: string, info: Omit<ImpersonationInfo, "ended">): void {
  sessionStorage.setItem(IMPERSONATION_KEY, JSON.stringify({ ...info, ended: false }));
  sessionStorage.setItem(TOKEN_KEYS.access, accessToken);
  sessionStorage.setItem(TOKEN_KEYS.session, sessionJson);
}

/** Leaves the (ended) impersonation mode, so this tab can go back to the administrator's own session. */
export function leaveImpersonation(): void {
  sessionStorage.removeItem(IMPERSONATION_KEY);
  sessionStorage.removeItem(TOKEN_KEYS.access);
  sessionStorage.removeItem(TOKEN_KEYS.refresh);
  sessionStorage.removeItem(TOKEN_KEYS.session);
}

// ── Session-ended notifications ───────────────────────────────────────────────

/**
 * Why the client dropped the session on its own:
 *  - platform_session_ended: the platform session was revoked/logged out (sign in again);
 *  - session_revoked: the server ended this person's sessions (role, password or access changed);
 *  - session_ended: any other session the server no longer accepts and that could not be renewed;
 *  - impersonation_ended: the tenant access of this tab expired or was ended elsewhere;
 *  - impersonation_left: the administrator left the tenant access ("Sair do acesso").
 */
export type SessionEndReason =
  | "platform_session_ended"
  | "session_revoked"
  | "session_ended"
  | "impersonation_ended"
  | "impersonation_left";

const SESSION_ENDED_EVENT = "orken:session-ended";

function emitSessionEnded(reason: SessionEndReason): void {
  globalThis.dispatchEvent(new CustomEvent<SessionEndReason>(SESSION_ENDED_EVENT, { detail: reason }));
}

/** Subscribes to the session drops above; returns the unsubscribe function. */
export function onSessionEnded(handler: (reason: SessionEndReason) => void): () => void {
  const listener = (e: Event) => handler((e as CustomEvent<SessionEndReason>).detail);
  globalThis.addEventListener(SESSION_ENDED_EVENT, listener);
  return () => globalThis.removeEventListener(SESSION_ENDED_EVENT, listener);
}

/** Ends the impersonation in this tab only (the admin's own session is untouched) and tells the app. */
export function endImpersonationSession(reason: "impersonation_ended" | "impersonation_left" = "impersonation_ended"): void {
  clearTokens();
  emitSessionEnded(reason);
}

// ── Subscription required ─────────────────────────────────────────────────────

/** Code of the 402 of a write on an expired module, or by a company without any module with access (it stays read-only). */
export const SUBSCRIPTION_REQUIRED_CODE = "subscription_required";

/**
 * Says "this area": another module of the company may be working normally. Promises no purchase on purpose: not every module
 * is for sale, and whoever tried one with no price has nothing to buy. See docs/engineering/tenant-access-enforcement.md.
 */
export const SUBSCRIPTION_REQUIRED_MESSAGE =
  "Esta área está sem assinatura ativa e ficou somente para consulta: você pode ver e exportar os dados, " +
  "mas não registrar nem alterar. Quem administra a conta pode ver as opções na tela Assinatura ou falar com o suporte Orken.";

const SUBSCRIPTION_REQUIRED_EVENT = "orken:subscription-required";

/** Subscribes to writes refused for lack of a subscription; returns the unsubscribe function. */
export function onSubscriptionRequired(handler: () => void): () => void {
  globalThis.addEventListener(SUBSCRIPTION_REQUIRED_EVENT, handler);
  return () => globalThis.removeEventListener(SUBSCRIPTION_REQUIRED_EVENT, handler);
}

/**
 * The error of a write refused for lack of a subscription, with the app told so it can show a notice.
 * Null for any other response. The session is valid: nothing here touches it.
 */
function subscriptionRequiredError(status: number, body: ErrorBody | null): ApiError | null {
  if (status !== 402 || body?.code !== SUBSCRIPTION_REQUIRED_CODE) return null;
  globalThis.dispatchEvent(new Event(SUBSCRIPTION_REQUIRED_EVENT));
  return new ApiError(402, SUBSCRIPTION_REQUIRED_MESSAGE, body.code, body.requestId);
}

// ── Token helpers ─────────────────────────────────────────────────────────────

// The storage is the source of truth (no in-memory copy): when another tab renews the shared session,
// this tab must use the new token at once.
export function getAccessToken(): string | null {
  return tokenStore().getItem(TOKEN_KEYS.access);
}

/** The refresh token of the signed-in user; never one in an impersonation tab. */
export function getRefreshToken(): string | null {
  return isImpersonating() ? null : localStorage.getItem(TOKEN_KEYS.refresh);
}

export function getStoredSession(): string | null {
  return tokenStore().getItem(TOKEN_KEYS.session);
}

export function setStoredSession(sessionJson: string): void {
  tokenStore().setItem(TOKEN_KEYS.session, sessionJson);
}

export function setTokens(access: string, refresh: string | null | undefined): void {
  if (access) tokenStore().setItem(TOKEN_KEYS.access, access);
  // an impersonation tab never keeps a refresh token
  if (refresh && !isImpersonating()) {
    localStorage.setItem(TOKEN_KEYS.refresh, refresh);
    localStorage.removeItem(RENEWAL_WAIT_KEY);
  }
}

export function clearTokens(): void {
  const impersonation = getImpersonation();
  if (impersonation) {
    // only this tab's keys: the administrator's own localStorage session stays as it is
    sessionStorage.removeItem(TOKEN_KEYS.access);
    sessionStorage.removeItem(TOKEN_KEYS.refresh);
    sessionStorage.removeItem(TOKEN_KEYS.session);
    sessionStorage.setItem(IMPERSONATION_KEY, JSON.stringify({ ...impersonation, ended: true }));
    return;
  }
  localStorage.removeItem(TOKEN_KEYS.access);
  localStorage.removeItem(TOKEN_KEYS.refresh);
  localStorage.removeItem(TOKEN_KEYS.session);
  localStorage.removeItem(RENEWAL_WAIT_KEY);
}

// ── Refresh ───────────────────────────────────────────────────────────────────

/**
 * Only a refusal from the server ends a session: a 401, or a 409 after the wait for the pair of the tab that won the race.
 * Everything else says nothing about the session (no connection, 429, 5xx, a proxy's answer): that is "unavailable", the tokens
 * stay, and the call that needed the renewal fails with a temporary error.
 */
type RefreshOutcome = "ok" | "refused" | "platform_ended" | "unavailable";

/** Code of the temporary error of a call whose renewal got no verdict. */
export const SESSION_RENEWAL_UNAVAILABLE_CODE = "session_renewal_unavailable";

/**
 * Until when no tab sends a renewal, shared through localStorage: every tab of the browser renews the same session, and without
 * this each one, and each call of each one, would ask again. It holds no credential, and it survives a reload on purpose.
 */
const RENEWAL_WAIT_KEY = "nexo:renewal_wait";

interface RenewalWait {
  until: number;
  /** What the last attempt got: the status of the answer, 0 without one. */
  status: number;
  attempts: number;
}

/** Whatever a server or a clock says, a session is never held back for longer than this. */
const RENEWAL_WAIT_MAX_MS = 3_600_000;
const RENEWAL_BACKOFF_FIRST_MS = 2000;
const RENEWAL_BACKOFF_MAX_MS = 60_000;
const RENEWAL_SPREAD_MAX_MS = 5000;

function readRenewalWait(): RenewalWait | null {
  try {
    const wait = JSON.parse(localStorage.getItem(RENEWAL_WAIT_KEY) ?? "null") as Partial<RenewalWait> | null;
    if (typeof wait?.until !== "number" || typeof wait.status !== "number" || typeof wait.attempts !== "number") return null;
    return wait.until - Date.now() > RENEWAL_WAIT_MAX_MS ? null : (wait as RenewalWait);
  } catch {
    return null; // unreadable: the same as no wait
  }
}

/** The Retry-After of the answer in milliseconds (seconds or a date), when there is one. */
function retryAfterMs(res: Response): number | null {
  const value = res.headers.get("Retry-After");
  if (!value) return null;
  const seconds = Number(value);
  if (Number.isFinite(seconds)) return Math.max(0, seconds * 1000);
  const date = Date.parse(value);
  return Number.isNaN(date) ? null : Math.max(0, date - Date.now());
}

/**
 * The next attempt waits what the server asked for or, when it did not say, twice as long as the last one. A little on top
 * spreads the terminals of a store, which all got the same answer at the same moment.
 */
function noteRenewalUnavailable(status: number, retryAfter: number | null): void {
  const attempts = (readRenewalWait()?.attempts ?? 0) + 1;
  const asked = retryAfter ?? Math.min(RENEWAL_BACKOFF_MAX_MS, RENEWAL_BACKOFF_FIRST_MS * 2 ** (attempts - 1));
  const spread = Math.random() * Math.min(asked * 0.2, RENEWAL_SPREAD_MAX_MS);
  const until = Date.now() + Math.min(RENEWAL_WAIT_MAX_MS, asked + spread);
  try {
    localStorage.setItem(RENEWAL_WAIT_KEY, JSON.stringify({ until, status, attempts } satisfies RenewalWait));
  } catch {
    // storage refused: the wait is lost, and that is no reason to fail the call any other way
  }
}

function timeLeftInWords(ms: number): string {
  const seconds = Math.ceil(ms / 1000);
  if (seconds > 90) return `em cerca de ${Math.ceil(seconds / 60)} minutos`;
  return seconds > 5 ? `em ${seconds} segundos` : "em instantes";
}

const RENEWAL_UNAVAILABLE_EVENT = "orken:session-renewal-unavailable";

/** Subscribes to the renewals that got no verdict, with the text for the person; returns the unsubscribe function. */
export function onRenewalUnavailable(handler: (message: string) => void): () => void {
  const listener = (e: Event) => handler((e as CustomEvent<string>).detail);
  globalThis.addEventListener(RENEWAL_UNAVAILABLE_EVENT, listener);
  return () => globalThis.removeEventListener(RENEWAL_UNAVAILABLE_EVENT, listener);
}

/** The error of a call that could not renew the session. Never a 401: everything that reads a 401 takes it for a refusal. */
function renewalUnavailableError(): ApiError {
  const wait = readRenewalWait();
  const left = Math.max(0, (wait?.until ?? 0) - Date.now());
  const status = wait?.status ?? 0;
  const cause = status === 0 ? "Sem conexão com o servidor para renovar sua sessão."
    : status === 429 ? "Muitas renovações de sessão em pouco tempo."
    : "O servidor não conseguiu renovar sua sessão agora.";
  const message = `${cause} Sua sessão continua ativa: tente de novo ${timeLeftInWords(left)}.`;
  globalThis.dispatchEvent(new CustomEvent<string>(RENEWAL_UNAVAILABLE_EVENT, { detail: message }));
  return new ApiError(status, message, SESSION_RENEWAL_UNAVAILABLE_CODE, undefined, undefined, undefined, Math.ceil(left / 1000));
}

/** Refresh tokens of platform administrators start with "p." (rotated on every refresh). */
const isPlatformRefreshToken = (token: string) => token.startsWith("p.");

/** How long a tab that lost the refresh race waits for the winner tab to store the new pair. */
export const ROTATION_WAIT_MS = 3000;

const sleep = (ms: number) => new Promise<void>(resolve => setTimeout(resolve, ms));

async function waitForRotatedToken(sent: string): Promise<boolean> {
  const deadline = Date.now() + ROTATION_WAIT_MS;
  for (;;) {
    const current = localStorage.getItem(TOKEN_KEYS.refresh);
    if (current && current !== sent) return true;
    if (Date.now() >= deadline) return false;
    await sleep(100);
  }
}

interface ErrorBody {
  error?: string;
  message?: string;
  code?: string;
  requestId?: string;
  details?: unknown[];
}

async function readErrorBody(res: Response): Promise<ErrorBody | null> {
  try {
    return (await res.json()) as ErrorBody;
  } catch {
    return null; // empty or non-JSON body
  }
}

let refreshPromise: Promise<RefreshOutcome> | null = null;

async function attemptRefresh(): Promise<RefreshOutcome> {
  if (isImpersonating()) return "refused";   // the impersonation token is never renewed
  if (refreshPromise) return refreshPromise;

  // checked before the shared promise exists: an IIFE that returns synchronously would run its `finally` before the
  // assignment below and leave a settled "refused" promise cached, so no later 401 would ever refresh again
  const sent = localStorage.getItem(TOKEN_KEYS.refresh);
  if (!sent) return "refused";
  const wait = readRenewalWait();
  if (wait && wait.until > Date.now()) return "unavailable";

  refreshPromise = (async (): Promise<RefreshOutcome> => {
    try {
      const res = await fetch(`${BASE_URL}/auth/refresh`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ refreshToken: sent }),
      });

      if (res.ok) {
        const data = await res.json();
        if (data.accessToken) setTokens(data.accessToken, data.refreshToken ?? "");
        localStorage.removeItem(RENEWAL_WAIT_KEY);
        return "ok";
      }

      if (res.status !== 401 && res.status !== 409) {
        noteRenewalUnavailable(res.status, retryAfterMs(res));
        return "unavailable";
      }
      const err = await readErrorBody(res);
      // Another tab renewed the same session a moment ago and stores the new pair in localStorage: use it.
      if (res.status === 409 && err?.code === "refresh_already_rotated" && await waitForRotatedToken(sent)) return "ok";
      clearTokens();
      return isPlatformRefreshToken(sent) ? "platform_ended" : "refused";
    } catch (error) {
      // No answer says nothing about the session, so the tokens stay.
      console.error("Token refresh failed:", error);
      noteRenewalUnavailable(0, null);
      return "unavailable";
    } finally {
      refreshPromise = null;
    }
  })();

  return refreshPromise;
}

/** A token with less than this left is renewed first: a connection opened with it would be closed right after, at its expiry. */
const TOKEN_MIN_LIFETIME_MS = 30_000;

/** The expiry the token says it has, in milliseconds. Null when it cannot be read: only the server can judge it then. */
function tokenExpiry(token: string): number | null {
  try {
    const payload = token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/");
    const exp = (JSON.parse(atob(payload)) as { exp?: unknown }).exp;
    return typeof exp === "number" ? exp * 1000 : null;
  } catch {
    return null;
  }
}

/**
 * The access token for a connection that stays open. The server closes it when its token expires, and a screen that only listens
 * to the connection makes no other request that would renew the token: the renewal has to happen here. Uses the same single
 * renewal as the requests. Null means the session is over (the app was already told): there is nothing to connect with.
 *
 * Without a verdict from the server (no connection, 429, 5xx) the session is not over: the token goes as it is, the attempt fails
 * like any other during a network failure, and a later one tries again, after the wait the renewal is under.
 */
export async function getFreshAccessToken(): Promise<string | null> {
  const token = getAccessToken();
  if (!token) return null;
  const expiry = tokenExpiry(token);
  if (isImpersonating() || expiry === null || expiry - Date.now() > TOKEN_MIN_LIFETIME_MS) return token;

  const outcome = await attemptRefresh();
  if (outcome === "ok") return getAccessToken();
  if (getAccessToken()) return token;
  emitSessionEnded(outcome === "platform_ended" ? "platform_session_ended" : "session_ended");
  return null;
}

/**
 * A 401 on an authenticated call. True when the request may be sent again (once) with a renewed token. False when the local
 * session is already dropped (and the app told, when it must leave the page). Throws the temporary error when the renewal got no
 * verdict: the session stays, and only this call fails. A revoked token is a verdict in itself, so that one still ends here.
 */
async function recoverFromUnauthorized(usedToken: string | null, code: string | undefined, isRetry: boolean): Promise<boolean> {
  if (isImpersonating()) {
    endImpersonationSession("impersonation_ended");
    return false;
  }
  if (code === "platform_session_ended") {
    clearTokens();
    emitSessionEnded("platform_session_ended");
    return false;
  }
  // Tokens already gone means the page dropped them on purpose (recovery codes on screen, signing out): a late answer
  // to a call it sent before that ends nothing. A call that never had a token had no session to end.
  const sessionInUse = usedToken !== null && getAccessToken() === usedToken;
  if (!isRetry) {
    // another tab may have renewed the shared session already
    const current = getAccessToken();
    if (current && current !== usedToken) return true;
    const outcome = await attemptRefresh();
    if (outcome === "ok") return true;
    if (outcome === "platform_ended") {
      emitSessionEnded("platform_session_ended");
      return false;
    }
    if (outcome === "unavailable" && code !== "session_revoked") throw renewalUnavailableError();
  }
  clearTokens();
  if (sessionInUse) emitSessionEnded(code === "session_revoked" ? "session_revoked" : "session_ended");
  return false;
}

// ── Core request ─────────────────────────────────────────────────────────────

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

  if (res.status === 401) {
    // keep the server's explanation (wrong code, unverified e-mail, blocked account, session ended…)
    const err = await readErrorBody(res);
    const message = err?.error ?? err?.message ?? "Unauthorized";
    // sign-in calls answer 401 for wrong credentials/codes: no token refresh (and no session drop) for those
    const anonymousAuth = path.startsWith("/auth/login") || path.startsWith("/auth/mfa/verify");
    if (!anonymousAuth && await recoverFromUnauthorized(token, err?.code, isRetry)) {
      return request<T>(method, path, body, true);
    }
    throw new ApiError(401, message, err?.code);
  }

  if (!res.ok) {
    let message = res.statusText;
    let details: string[] | undefined;
    const err = await readErrorBody(res);
    const refused = subscriptionRequiredError(res.status, err);
    if (refused) throw refused;
    if (Array.isArray(err?.details) && err.details.length > 0) {
      details = err.details.map(String);
      message = details.join(" | ");
    } else {
      message = err?.error ?? err?.message ?? message;
    }
    const serverMessage = err?.error ?? err?.message ?? undefined;
    const requestId = err?.requestId;
    if (res.status === 429 && !message) message = "Muitas tentativas. Aguarde alguns minutos.";
    // an unexpected error carries the id of its log lines: show it, so support can find them
    if (res.status >= 500 && requestId) message = `${message} (código: ${requestId})`;
    throw new ApiError(res.status, message, err?.code, requestId, details, serverMessage);
  }

  if (res.status === 204) return undefined as unknown as T;

  return res.json() as Promise<T>;
}

async function requestForm<T>(path: string, form: FormData, isRetry = false): Promise<T> {
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
    const err = await readErrorBody(res);
    if (await recoverFromUnauthorized(token, err?.code, isRetry)) {
      return requestForm<T>(path, form, true);
    }
    throw new ApiError(401, err?.error ?? err?.message ?? "Unauthorized", err?.code);
  }

  if (!res.ok) {
    let message = res.statusText;
    const err = await readErrorBody(res);
    const refused = subscriptionRequiredError(res.status, err);
    if (refused) throw refused;
    if (Array.isArray(err?.details) && err.details.length > 0) {
      message = err.details.join(" | ");
    } else {
      message = err?.error ?? err?.message ?? message;
    }
    throw new ApiError(res.status, message, err?.code);
  }

  if (res.status === 204) return undefined as unknown as T;

  return res.json() as Promise<T>;
}

/** Upload progress: bytes sent of the total (total 0 when the browser cannot tell). */
export type UploadProgressHandler = (loaded: number, total: number) => void;

function parseJson(text: string): ErrorBody | null {
  if (!text) return null;
  try {
    return JSON.parse(text) as ErrorBody;
  } catch {
    return null;
  }
}

function sendXhr(method: "POST" | "PUT", url: string, form: FormData, token: string | null,
  onProgress?: UploadProgressHandler): Promise<{ status: number; statusText: string; text: string }> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open(method, url);
    if (token) xhr.setRequestHeader("Authorization", `Bearer ${token}`);
    if (onProgress) xhr.upload.onprogress = (e) => onProgress(e.loaded, e.lengthComputable ? e.total : 0);
    xhr.onload = () => resolve({ status: xhr.status, statusText: xhr.statusText, text: xhr.responseText });
    xhr.onerror = () => reject(new ApiError(0, "Falha de conexão ao enviar o arquivo."));
    xhr.onabort = () => reject(new ApiError(0, "Envio cancelado."));
    xhr.send(form);
  });
}

/**
 * Multipart upload through XMLHttpRequest — fetch has no upload progress. Same token handling (one renewal on 401)
 * and the same ApiError shape as the JSON calls.
 */
async function uploadForm<T>(method: "POST" | "PUT", path: string, form: FormData, onProgress?: UploadProgressHandler,
  isRetry = false): Promise<T> {
  const token = getAccessToken();
  const res = await sendXhr(method, `${BASE_URL}${path}`, form, token, onProgress);
  const body = parseJson(res.text);

  if (res.status === 401) {
    if (await recoverFromUnauthorized(token, body?.code, isRetry)) return uploadForm<T>(method, path, form, onProgress, true);
    throw new ApiError(401, body?.error ?? body?.message ?? "Unauthorized", body?.code);
  }
  if (res.status < 200 || res.status >= 300) {
    const refused = subscriptionRequiredError(res.status, body);
    if (refused) throw refused;
    const details = Array.isArray(body?.details) && body.details.length > 0 ? body.details.map(String) : undefined;
    const serverMessage = body?.error ?? body?.message ?? undefined;
    let message = details ? details.join(" | ") : serverMessage ?? res.statusText;
    if (res.status >= 500 && body?.requestId) message = `${message} (código: ${body.requestId})`;
    throw new ApiError(res.status, message, body?.code, body?.requestId, details, serverMessage);
  }
  if (res.status === 204 || !res.text) return undefined as unknown as T;
  return JSON.parse(res.text) as T;
}

// ── Public client ─────────────────────────────────────────────────────────────

export const apiClient = {
  /** Multipart POST/PUT with upload progress (large files: models, reality capture). */
  upload:   <T>(method: "POST" | "PUT", path: string, form: FormData, onProgress?: UploadProgressHandler) =>
    uploadForm<T>(method, path, form, onProgress),
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
    /** The body's { details } list, when it sends one (e.g. the reason an AI provider failed). */
    public readonly details?: string[],
    /** The body's own { error } text, kept apart from `message` (which prefers `details`). */
    public readonly serverMessage?: string,
    /** When it makes sense to try again, in seconds, when known. */
    public readonly retryAfterSeconds?: number
  ) {
    super(message);
    this.name = "ApiError";
  }
}
