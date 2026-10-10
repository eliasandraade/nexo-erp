import { useRef, useState, type FormEvent } from "react";
import { useNavigate, Link } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import { Eye, EyeOff, Loader2 } from "lucide-react";
import { useAuth } from "../context/AuthContext";
import { resolvePostLogin } from "@/modules/workspace/resolvePostLogin";
import { readLastWorkspace } from "@/modules/workspace/persistence";
import { getCurrentSession } from "../services/authService";
import { DASHBOARD_SUMMARY_KEY } from "@/modules/dashboard/hooks/useDashboardSummary";
import { getDashboardSummary } from "@/modules/dashboard/api/dashboard.api";

// ─── Shared input styles ──────────────────────────────────────────────────────

const INPUT_BASE = "auth-input";

const LABEL = "auth-label";

// ─── Component ────────────────────────────────────────────────────────────────

export default function LoginPage() {
  const { login }  = useAuth();
  const navigate   = useNavigate();
  const queryClient = useQueryClient();
  const formRef    = useRef<HTMLFormElement>(null);

  const [loginField,    setLoginField]    = useState("");
  const [password,      setPassword]      = useState("");
  const [showPassword,  setShowPassword]  = useState(false);
  const [error,         setError]         = useState<string | null>(null);
  const [loading,       setLoading]       = useState(false);

  // Return focus to the field that needs attention.
  function focusField(id = "login") { formRef.current?.querySelector<HTMLInputElement>(`#${id}`)?.focus(); }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);

    if (!loginField.trim()) {
      setError("Informe o login ou e-mail.");
      focusField();
      return;
    }
    if (!password) {
      setError("Informe a senha.");
      focusField("password");
      return;
    }

    setLoading(true);
    const { error: err, type } = await login({ login: loginField.trim(), password });
    setLoading(false);

    if (err) {
      setError(err);
      focusField();
    } else if (type === "platform") {
      navigate("/platform", { replace: true });
    } else {
      const session = getCurrentSession();
      const target  = session
        ? resolvePostLogin(session, readLastWorkspace(session))
        : "/dashboard";
      // Warm the dashboard cache in parallel with the route transition + chunk
      // download, so the first data is already in flight (or done) by the time
      // DashboardPage mounts. Only when the user actually lands on /dashboard.
      if (target === "/dashboard") {
        void queryClient.prefetchQuery({
          queryKey: DASHBOARD_SUMMARY_KEY,
          queryFn:  getDashboardSummary,
          staleTime: 60_000,
        });
      }
      navigate(target, { replace: true });
    }
  }

  return (
    <div className="">

      {/* ── Eyebrow + headline ── */}
      <div className="mb-8">
        <div className="flex items-center gap-2 mb-4">

          <span className="text-[11px] font-semibold uppercase tracking-[0.1em] text-primary">
            Identificação
          </span>
        </div>
        <h1 className="font-sans text-[26px] sm:text-[28px] font-semibold text-foreground leading-[1.1] tracking-tight">
          Entrar no ORKEN
        </h1>
      </div>

      {/* ── Form ── */}
      <form
        ref={formRef}
        onSubmit={handleSubmit}
        noValidate
        className="space-y-4"
      >
        {/* Login */}
        <div>
          <label htmlFor="login" className={LABEL}>
            Login ou e-mail
          </label>
          <input
            id="login"
            type="text"
            inputMode="email"
            autoComplete="username"
            placeholder="usuario ou email"
            value={loginField}
            onChange={(e) => setLoginField(e.target.value)}
            disabled={loading}
            autoFocus
            className={INPUT_BASE}
          />
        </div>

        {/* Password */}
        <div>
          <div className="flex items-center justify-between mb-1.5">
            <label
              htmlFor="password"
              className="text-[11px] font-semibold uppercase tracking-[0.09em] text-muted-foreground"
            >
              Senha
            </label>
            <span className="text-xs text-muted-foreground">Esqueceu? Fale com o administrador.</span>
          </div>
          <div className="relative">
            <input
              id="password"
              type={showPassword ? "text" : "password"}
              autoComplete="current-password"
              placeholder="••••••••"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              disabled={loading}
              className={`${INPUT_BASE} pr-11`}
            />
            <button
              type="button"
              onClick={() => setShowPassword((v) => !v)}
              className="absolute right-2.5 top-1/2 -translate-y-1/2 p-1.5 text-muted-foreground hover:text-muted-foreground transition-colors "
              aria-label={showPassword ? "Ocultar senha" : "Mostrar senha"}
            >
              {showPassword
                ? <EyeOff className="h-[15px] w-[15px]" />
                : <Eye    className="h-[15px] w-[15px]" />
              }
            </button>
          </div>
        </div>

        {/* Error */}
        {error && (
          <p className="text-[13px] text-destructive leading-snug pt-0.5" role="alert">
            {error}
          </p>
        )}

        {/* Submit */}
        <div className="pt-1.5">
          <button
            type="submit"
            disabled={loading}
            className="auth-submit"
          >
            {loading
              ? <Loader2 className="h-[18px] w-[18px] animate-spin" />
              : "Entrar"
            }
          </button>
        </div>
      </form>

      {/* ── Register link ── */}
      <div className="mt-6 pt-5 border-t border-border">
        <p className="text-[13px] text-muted-foreground">
          Ainda não tem conta?{" "}
          <Link
            to="/register"
            className="text-muted-foreground hover:text-foreground transition-colors font-medium  focus-visible:text-foreground"
          >
            Criar acesso →
          </Link>
        </p>
      </div>

    </div>
  );
}
