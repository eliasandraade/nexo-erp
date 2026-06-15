import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { useNavigate } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import * as authService from "../services/authService";
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
  logout: () => void;
  /** Switch the active store. Issues new JWT pair scoped to the given store. */
  switchStore: (storeId: string) => Promise<void>;
  /** Set session after successful email verification (auto-login). */
  setSessionFromVerify: (session: AuthSession) => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  // Populate synchronously from localStorage for instant render
  const [session, setSession] = useState<AuthSession | null>(
    () => authService.getCurrentSession()
  );
  const [isReady, setIsReady] = useState(false);
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  // Background validation: confirm the stored session is still valid
  useEffect(() => {
    const storedSession = authService.getCurrentSession();
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
        navigate("/login", { replace: true });
      }
      setIsReady(true);
    });
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const login = useCallback(async (input: LoginInput) => {
    const result = await authService.login(input);
    if (!result.success) return { error: result.error, type: null } as const;
    setSession(result.session);
    return { error: null, type: result.session.type } as const;
  }, []);

  const logout = useCallback(() => {
    authService.logout();
    setSession(null);
    // Drop every cached query so the next user on this browser never sees the
    // previous session's data (dashboard, customers, sales, …).
    queryClient.clear();
    navigate("/login", { replace: true });
  }, [navigate, queryClient]);

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
    () => ({ session, isReady, login, logout, switchStore, setSessionFromVerify }),
    [session, isReady, login, logout, switchStore, setSessionFromVerify]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside AuthProvider");
  return ctx;
}
