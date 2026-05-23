import { useRef, useState, type FormEvent } from "react";
import { useNavigate, Link } from "react-router-dom";
import { Eye, EyeOff, Loader2 } from "lucide-react";
import { useAuth } from "../context/AuthContext";
import { homeRoute } from "../hooks/useRoleAccess";
import { getCurrentSession } from "../services/authService";

// ─── Shared input styles ──────────────────────────────────────────────────────

const INPUT_BASE =
  "w-full h-12 rounded-[10px] border border-white/[0.08] bg-white/[0.04] " +
  "px-4 text-[14px] text-white placeholder:text-white/20 " +
  "outline-none transition-all duration-150 " +
  "focus:border-[#5B4DFF] focus:bg-white/[0.06] focus:shadow-[0_0_0_3px_rgba(91,77,255,0.12)] " +
  "disabled:opacity-50 disabled:cursor-not-allowed";

const LABEL =
  "block text-[11px] font-semibold uppercase tracking-[0.09em] text-slate-500 mb-1.5";

// ─── Component ────────────────────────────────────────────────────────────────

export default function LoginPage() {
  const { login }  = useAuth();
  const navigate   = useNavigate();
  const formRef    = useRef<HTMLFormElement>(null);

  const [loginField,    setLoginField]    = useState("");
  const [password,      setPassword]      = useState("");
  const [showPassword,  setShowPassword]  = useState(false);
  const [error,         setError]         = useState<string | null>(null);
  const [loading,       setLoading]       = useState(false);

  // Imperatively add/remove shake class so it can repeat on every error
  function triggerShake() {
    const el = formRef.current;
    if (!el) return;
    el.classList.remove("animate-auth-shake");
    // Reading offsetWidth forces the browser to reflow, which resets the
    // animation so it replays even if the class was already present.
    void el.offsetWidth;
    el.classList.add("animate-auth-shake");
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);

    if (!loginField.trim()) {
      setError("Informe o login ou e-mail.");
      triggerShake();
      return;
    }
    if (!password) {
      setError("Informe a senha.");
      triggerShake();
      return;
    }

    setLoading(true);
    const { error: err, type } = await login({ login: loginField.trim(), password });
    setLoading(false);

    if (err) {
      setError(err);
      triggerShake();
    } else if (type === "platform") {
      navigate("/platform", { replace: true });
    } else {
      const session = getCurrentSession();
      navigate(session ? homeRoute(session) : "/dashboard", { replace: true });
    }
  }

  return (
    <div className="animate-auth-enter">

      {/* ── Eyebrow + headline ── */}
      <div className="mb-8">
        <div className="flex items-center gap-2 mb-4">
          <div className="h-px w-5 bg-[#5B4DFF]" />
          <span className="text-[11px] font-semibold uppercase tracking-[0.1em] text-[#5B4DFF]">
            Identificação
          </span>
        </div>
        <h1 className="font-display text-[26px] sm:text-[28px] font-bold text-white leading-[1.1] tracking-tight">
          Acesso ao<br />centro operacional
        </h1>
      </div>

      {/* ── Form ── */}
      <form
        ref={formRef}
        onSubmit={handleSubmit}
        noValidate
        className="space-y-4"
        onAnimationEnd={() =>
          formRef.current?.classList.remove("animate-auth-shake")
        }
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
              className="text-[11px] font-semibold uppercase tracking-[0.09em] text-slate-500"
            >
              Senha
            </label>
            <Link
              to="/forgot-password"
              className="text-[11px] text-slate-600 hover:text-slate-400 transition-colors py-1.5 -my-1.5 px-1 -mr-1 focus-visible:outline-none focus-visible:text-slate-300"
            >
              Esqueceu?
            </Link>
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
              className="absolute right-2.5 top-1/2 -translate-y-1/2 p-1.5 text-slate-600 hover:text-slate-300 transition-colors focus:outline-none"
              tabIndex={-1}
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
          <p className="text-[13px] text-red-400 leading-snug pt-0.5" role="alert">
            {error}
          </p>
        )}

        {/* Submit */}
        <div className="pt-1.5">
          <button
            type="submit"
            disabled={loading}
            className="
              w-full h-[52px] rounded-[10px]
              bg-[#5B4DFF] hover:bg-[#4338CA] active:scale-[0.985]
              text-white text-[14px] font-semibold
              flex items-center justify-center
              transition-all duration-150
              disabled:opacity-60 disabled:cursor-not-allowed
              focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#5B4DFF]/50 focus-visible:ring-offset-2 focus-visible:ring-offset-[#0B1020]
            "
          >
            {loading
              ? <Loader2 className="h-[18px] w-[18px] animate-spin" />
              : "Entrar"
            }
          </button>
        </div>
      </form>

      {/* ── Register link ── */}
      <div className="mt-6 pt-5 border-t border-white/[0.06]">
        <p className="text-[13px] text-slate-600">
          Novo no Orken?{" "}
          <Link
            to="/register"
            className="text-slate-400 hover:text-white transition-colors font-medium focus-visible:outline-none focus-visible:text-white"
          >
            Criar acesso →
          </Link>
        </p>
      </div>

    </div>
  );
}
