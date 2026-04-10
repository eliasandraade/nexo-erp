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
import * as authService from "../services/authService";
import type { AuthSession, LoginInput } from "../types";

interface AuthContextValue {
  session: AuthSession | null;
  /** True once the background /auth/me validation has completed. */
  isReady: boolean;
  /** Attempt login. Returns error message on failure, null on success. */
  login: (input: LoginInput) => Promise<string | null>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  // Populate synchronously from localStorage for instant render
  const [session, setSession] = useState<AuthSession | null>(
    () => authService.getCurrentSession()
  );
  const [isReady, setIsReady] = useState(false);
  const navigate = useNavigate();

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
      } else {
        // Token rejected by server — force logout
        setSession(null);
        navigate("/login", { replace: true });
      }
      setIsReady(true);
    });
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const login = useCallback(async (input: LoginInput): Promise<string | null> => {
    const result = await authService.login(input);
    if (!result.success) return result.error;
    setSession(result.session);
    return null;
  }, []);

  const logout = useCallback(() => {
    authService.logout();
    setSession(null);
    navigate("/login", { replace: true });
  }, [navigate]);

  const value = useMemo(
    () => ({ session, isReady, login, logout }),
    [session, isReady, login, logout]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside AuthProvider");
  return ctx;
}
