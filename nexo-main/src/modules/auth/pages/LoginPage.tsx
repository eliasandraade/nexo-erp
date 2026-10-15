import { useRef, useState, type FormEvent } from "react";
import { useNavigate, Link, useSearchParams } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import { Eye, EyeOff, Loader2 } from "lucide-react";
import { useAuth } from "../context/AuthContext";
import { resolvePostLogin } from "@/modules/workspace/resolvePostLogin";
import { readLastWorkspace } from "@/modules/workspace/persistence";
import { getCurrentSession, verifyMfa } from "../services/authService";
import { ApiError } from "@/services/api-client";
import { DASHBOARD_SUMMARY_KEY } from "@/modules/dashboard/hooks/useDashboardSummary";
import { getDashboardSummary } from "@/modules/dashboard/api/dashboard.api";

// ─── Shared input styles ──────────────────────────────────────────────────────

const INPUT_BASE = "auth-input";

const LABEL = "auth-label";

// ─── Component ────────────────────────────────────────────────────────────────

export default function LoginPage() {
  const { login, setSessionFromVerify }  = useAuth();
  const [challenge, setChallenge] = useState<string | null>(null);
  const [code, setCode] = useState("");
  const [useRecovery, setUseRecovery] = useState(false);
  const navigate   = useNavigate();
  const [params]   = useSearchParams();
  const notice = ({
    "senha-redefinida": "Senha alterada. Entre com a nova senha.",
    "senha": "Senha alterada. Por segurança, entre de novo.",
    "sessoes": "Você saiu de todos os dispositivos. Entre de novo.",
    "mfa": "Verificação em duas etapas ativada. Entre de novo com a senha e o código do app.",
  } as Record<string, string>)[params.get("motivo") ?? ""];
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
    const { error: err, type, mfaChallenge } = await login({ login: loginField.trim(), password }) as
      { error: string | null; type: string | null; mfaChallenge?: string };
    setLoading(false);

    if (mfaChallenge) {
      setChallenge(mfaChallenge);
      setCode("");
      return;
    }
    if (err) {
      setError(err);
      focusField();
    } else if (type === "platform") {
      navigate("/platform", { replace: true });
    } else {
      goHome();
    }
  }

  async function handleCode(e: FormEvent) {
    e.preventDefault();
    if (!challenge || !code.trim()) return;
    setError(null);
    setLoading(true);
    try {
      const session = await verifyMfa(challenge, useRecovery ? null : code, useRecovery ? code : null);
      setSessionFromVerify(session);
      goHome();
    } catch (err) {
      setLoading(false);
      if (err instanceof ApiError && err.code === "invalid_challenge") { setChallenge(null); setPassword(""); setCode(""); setUseRecovery(false); }
      setError(err instanceof ApiError ? err.message : "Não foi possível conferir o código.");
    }
  }

  function goHome() {
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

      {notice && (
        <output className="mb-6 block rounded-md border border-success/30 bg-success/5 px-3 py-2 text-[13px] text-success">{notice}</output>
      )}

      {challenge ? (
        <form onSubmit={handleCode} noValidate className="space-y-4">
          <div>
            <label htmlFor="mfa-code" className="mb-1.5 block text-[11px] font-semibold uppercase tracking-[0.09em] text-muted-foreground">
              {useRecovery ? "Código de recuperação" : "Código do aplicativo autenticador"}
            </label>
            <input id="mfa-code" value={code} onChange={(e) => setCode(e.target.value)} className="auth-input tracking-widest"
              inputMode={useRecovery ? "text" : "numeric"} autoComplete="one-time-code" placeholder={useRecovery ? "xxxxx-xxxxx" : "000000"}
              aria-describedby={error ? "mfa-error" : undefined} />
            <p className="mt-1 text-[11px] text-muted-foreground">
              {useRecovery ? "Cada código de recuperação vale uma vez." : "Abra o app (Google Authenticator, Microsoft Authenticator…) e digite os 6 números."}
            </p>
          </div>
          {error && <p id="mfa-error" role="alert" className="text-[13px] text-destructive">{error}</p>}
          <button type="submit" disabled={loading || !code.trim()} className="auth-submit">
            {loading ? <Loader2 className="h-[18px] w-[18px] animate-spin" /> : "Confirmar"}
          </button>
          <div className="flex justify-between text-[12px]">
            <button type="button" className="text-muted-foreground hover:text-foreground" onClick={() => { setUseRecovery(!useRecovery); setCode(""); setError(null); }}>
              {useRecovery ? "Usar o código do aplicativo" : "Usar um código de recuperação"}
            </button>
            <button type="button" className="text-muted-foreground hover:text-foreground" onClick={() => { setChallenge(null); setError(null); setCode(""); setUseRecovery(false); }}>
              Voltar
            </button>
          </div>
        </form>
      ) : (<>
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
            <Link to="/esqueci-senha" className="text-xs text-muted-foreground hover:text-foreground hover:underline">Esqueci a senha</Link>
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

      </>)}

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
