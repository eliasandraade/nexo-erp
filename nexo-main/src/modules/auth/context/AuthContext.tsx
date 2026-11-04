import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  startTransition,
  type ReactNode,
} from "react";
import { useNavigate } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import * as authService from "../services/authService";
import { apiClient, clearTokens, endImpersonationSession, isImpersonating, onSessionEnded } from "@/services/api-client";
import type { AuthSession, LoginInput } from "../types";

interface AuthContextValue {
  session: AuthSession | null;
  /** True once the background /auth/me validation has completed. */
  isReady: boolean;
  /**
   * Attempt login. Returns error message on failure, null on success.
   * Also returns the session type so the caller can redirect appropriately.
   */
  login: (input: LoginInput) => Promise<{ error: string | null; type: "tenant" | "platform" | null }>;
  /**
   * Signs out and goes to `to` (the login page by default; a reason can ride in its query). In a platform access
   * tab it leaves the access instead.
   */
  logout: (to?: string) => void;
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
  const navigate = useNavigate();
  const queryClient = useQueryClient();

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
          void navigate("/platform", { replace: true });
        }
      } else {
        // Token rejected by server — force logout. With optimistic render the
        // app may already be mounted, so purge any data cached during the
        // validation window before bouncing to /login.
        setSession(null);
        queryClient.clear();
        void navigate("/login", { replace: true });
      }
      setIsReady(true);
    }).catch(() => {
      // Validation could not reach the server (network blip): keep the stored session — the
      // API still rejects it if it is invalid — and never leave the app stuck on "not ready".
      setIsReady(true);
    });
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const login = useCallback(async (input: LoginInput) => {
    const result = await authService.login(input);
    if (result.success === false) return { error: result.error, type: null, mfaChallenge: result.mfaChallenge } as const;
    setSession(result.session);
    return { error: null, type: result.session.type } as const;
  }, []);

  const logout = useCallback((to = "/login") => {
    // In a platform access tab, "Sair" leaves the access (audited) — the administrator's own session stays.
    if (isImpersonating()) {
      apiClient.post("/auth/impersonation/end").catch(() => undefined).finally(() => endImpersonationSession("impersonation_left"));
      return;
    }
    // Server-side revocation is best effort; the local session is cleared regardless.
    void authService.logout();
    // Go first, then drop the session in a transition: React Router 7 navigates inside one, so both land in the
    // same render and the guard of the page being left never redirects to a bare /login (losing `to`'s reason).
    void navigate(to, { replace: true });
    startTransition(() => setSession(null));
    // Drop every cached query so the next user on this browser never sees the
    // previous session's data (dashboard, customers, sales, …).
    queryClient.clear();
  }, [navigate, queryClient]);

  const endLocalSession = useCallback((to = "/login") => {
    clearTokens();
    // Same order as logout: navigate first, drop the session in a transition, so `to`'s reason survives the guard.
    void navigate(to, { replace: true });
    startTransition(() => setSession(null));
    queryClient.clear();
  }, [navigate, queryClient]);

  // The API client dropped the session on its own (revoked platform session, ended tenant access): leave the page.
  useEffect(() => onSessionEnded((reason) => {
    setSession(null);
    queryClient.clear();
    if (reason === "platform_session_ended") void navigate("/login?motivo=sessao-encerrada", { replace: true });
    else void navigate(`/acesso-encerrado${reason === "impersonation_left" ? "?motivo=saiu" : ""}`, { replace: true });
  }), [navigate, queryClient]);

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
    () => ({ session, isReady, login, logout, endLocalSession, switchStore, setSessionFromVerify }),
    [session, isReady, login, logout, endLocalSession, switchStore, setSessionFromVerify]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside AuthProvider");
  return ctx;
}
