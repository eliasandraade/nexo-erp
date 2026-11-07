import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { useNavigate } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import * as authService from "../services/authService";
import { apiClient, clearTokens, endImpersonationSession, finishPendingSignOut, isCookieSessionMode, isImpersonating, onRenewalUnavailable, onSessionEnded, onSessionReplaced, onSignedOutElsewhere, TOKEN_KEYS, type SessionEndReason } from "@/services/api-client";
import type { AuthSession, LoginInput } from "../types";

/** Where a session the client dropped on its own takes the person: each page says why (`motivo`). */
const SESSION_END_PAGE: Record<SessionEndReason, string> = {
  platform_session_ended: "/login?motivo=sessao-encerrada",
  session_ended: "/login?motivo=sessao-encerrada",
  session_revoked: "/login?motivo=sessao-revogada",
  impersonation_ended: "/acesso-encerrado",
  impersonation_left: "/acesso-encerrado?motivo=saiu",
};

interface AuthContextValue {
  session: AuthSession | null;
  /** True once the background /auth/me validation has completed. */
  isReady: boolean;
  /**
   * Cookie mode, nothing in the storage on opening the app: whether the cookie may still have a session. "pending" until a page that needs a
   * session asks (requestSessionRecovery) and the server answers. Always "done" in the normal mode, and on public pages nothing asks.
   */
  sessionRecovery: "pending" | "done";
  requestSessionRecovery: () => void;
  /**
   * Attempt login. Returns error message on failure, null on success.
   * Also returns the session type so the caller can redirect appropriately.
   */
  login: (input: LoginInput) => Promise<{ error: string | null; type: "tenant" | "platform" | null }>;
  logout: () => void;
  /**
   * Drops the local session without asking the server (it already ended it: two-step verification turned on,
   * sessions revoked…) and goes to `to` (default /login).
   */
  endLocalSession: (to?: string) => void;
  /** Switch the active store. Issues new JWT pair scoped to the given store. */
  switchStore: (storeId: string) => Promise<void>;
  /** Set session after successful email verification (auto-login). */
  setSessionFromVerify: (session: AuthSession) => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

/**
 * The entry page of an impersonation tab: it starts from no session at all (the token arrives from the platform
 * tab) — never from the administrator's own stored session, which this tab must not validate nor redirect with.
 */
const isImpersonationEntry = () => globalThis.location?.pathname === "/impersonate";

export function AuthProvider({ children }: { children: ReactNode }) {
  // Populate synchronously from storage for instant render
  const [session, setSession] = useState<AuthSession | null>(
    () => (isImpersonationEntry() ? null : authService.getCurrentSession())
  );
  const [isReady, setIsReady] = useState(false);
  const [sessionRecovery, setSessionRecovery] = useState<"pending" | "done">(() =>
    isCookieSessionMode() && !isImpersonationEntry() && !isImpersonating() && !authService.getCurrentSession() ? "pending" : "done"
  );
  const recoveryAsked = useRef(false);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  // The API client already took the person to a page that says why: the background validation must not replace it.
  const sessionEndAnnounced = useRef(false);

  // A sign-out the server did not hear goes again now, with the connection back.
  useEffect(() => { void finishPendingSignOut(); }, []);

  // Background validation: confirm the stored session is still valid
  useEffect(() => {
    const storedSession = isImpersonationEntry() ? null : authService.getCurrentSession();
    if (!storedSession) {
      setIsReady(true);
      return;
    }

    authService.validateSession().then((fresh) => {
      if (fresh) {
        setSession(fresh);
        // If platform user lands on a non-platform route, redirect them
        if (fresh.type === "platform" && !window.location.pathname.startsWith("/platform")) {
          navigate("/platform", { replace: true });
        }
      } else {
        // Token rejected by server — force logout. With optimistic render the
        // app may already be mounted, so purge any data cached during the
        // validation window before bouncing to /login.
        setSession(null);
        queryClient.clear();
        if (!sessionEndAnnounced.current) navigate("/login", { replace: true });
      }
      setIsReady(true);
    }).catch(() => {
      // Validation could not reach the server (network blip): keep the stored session — the
      // API still rejects it if it is invalid — and never leave the app stuck on "not ready".
      setIsReady(true);
    });
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const requestSessionRecovery = useCallback(() => {
    if (recoveryAsked.current) return;
    recoveryAsked.current = true;
    authService.recoverSessionFromCookie()
      .then((fresh) => { if (fresh) setSession(fresh); })
      .catch(() => undefined)   // no answer: nothing to recover from, and the login page is there
      .finally(() => setSessionRecovery("done"));
  }, []);

  const login = useCallback(async (input: LoginInput) => {
    const result = await authService.login(input);
    if (result.success === false) return { error: result.error, type: null, mfaChallenge: result.mfaChallenge } as const;
    setSession(result.session);
    return { error: null, type: result.session.type } as const;
  }, []);

  const logout = useCallback(() => {
    // In a platform access tab, "Sair" leaves the access (audited) — the administrator's own session stays.
    if (isImpersonating()) {
      apiClient.post("/auth/impersonation/end").catch(() => undefined).finally(() => endImpersonationSession("impersonation_left"));
      return;
    }
    // Server-side revocation is best effort; the local session is cleared regardless.
    void authService.logout();
    setSession(null);
    // Drop every cached query so the next user on this browser never sees the
    // previous session's data (dashboard, customers, sales, …).
    queryClient.clear();
    navigate("/login", { replace: true });
  }, [navigate, queryClient]);

  const endLocalSession = useCallback((to = "/login") => {
    clearTokens();
    setSession(null);
    queryClient.clear();
    navigate(to, { replace: true });
  }, [navigate, queryClient]);

  // The API client dropped the session on its own (revoked session, ended tenant access): leave the page.
  const leaveEndedSession = useCallback((reason: SessionEndReason) => {
    sessionEndAnnounced.current = true;
    setSession(null);
    queryClient.clear();
    navigate(SESSION_END_PAGE[reason], { replace: true });
  }, [navigate, queryClient]);

  useEffect(() => onSessionEnded(leaveEndedSession), [leaveEndedSession]);

  // The session could not be renewed right now and it stays: the screens show their own errors, and this says why, once.
  useEffect(() => onRenewalUnavailable((message) => {
    toast.warning(message, { id: "session-renewal-unavailable" });
  }), []);

  // Another tab dropped the session they share (the server ended it, or the person signed out there): without a token
  // this tab would only collect errors. An impersonation tab has its own session and stays.
  const signedIn = session !== null;
  useEffect(() => {
    if (!signedIn) return;
    // In the cookie mode no token is in the storage, and the session is what is removed with it. A cleared storage (key null) is no sign-out.
    const watched = isCookieSessionMode() ? TOKEN_KEYS.session : TOKEN_KEYS.access;
    const onStorage = (e: StorageEvent) => {
      if (e.storageArea === localStorage && e.key === watched && e.newValue === null && !isImpersonating()) {
        leaveEndedSession("session_ended");
      }
    };
    globalThis.addEventListener("storage", onStorage);
    const stopHearing = onSignedOutElsewhere(() => leaveEndedSession("session_ended"));
    return () => {
      globalThis.removeEventListener("storage", onStorage);
      stopHearing();
    };
  }, [signedIn, leaveEndedSession]);

  // Another tab signed in or moved to another store: this one follows, and what it cached belongs to the old session or store.
  useEffect(() => onSessionReplaced((sessionJson) => {
    if (isImpersonationEntry()) return;
    setSession(JSON.parse(sessionJson) as AuthSession);
    queryClient.clear();
  }), [queryClient]);

  const switchStore = useCallback(async (storeId: string): Promise<void> => {
    const fresh = await authService.switchStore(storeId);
    setSession(fresh);
    // Store-scoped data (sales, products, customers, stock) belongs to the old
    // store — clear it so the new store context refetches cleanly.
    queryClient.clear();
  }, [queryClient]);

  const setSessionFromVerify = useCallback((session: AuthSession) => {
    setSession(session);
  }, []);

  const value = useMemo(
    () => ({ session, isReady, sessionRecovery, requestSessionRecovery, login, logout, endLocalSession, switchStore, setSessionFromVerify }),
    [session, isReady, sessionRecovery, requestSessionRecovery, login, logout, endLocalSession, switchStore, setSessionFromVerify]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside AuthProvider");
  return ctx;
}
