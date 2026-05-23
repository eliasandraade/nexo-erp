import { useState, useRef, type FormEvent } from "react";
import { useNavigate, Link } from "react-router-dom";
import { Eye, EyeOff, Loader2 } from "lucide-react";
import * as authService from "../services/authService";

// ─── Shared input styles (mirrors LoginPage) ──────────────────────────────────

const INPUT_BASE =
  "w-full h-12 rounded-[10px] border border-white/[0.08] bg-white/[0.04] " +
  "px-4 text-[14px] text-white placeholder:text-white/20 " +
  "outline-none transition-all duration-150 " +
  "focus:border-[#5B4DFF] focus:bg-white/[0.06] focus:shadow-[0_0_0_3px_rgba(91,77,255,0.12)] " +
  "disabled:opacity-50 disabled:cursor-not-allowed";

const LABEL =
  "block text-[11px] font-semibold uppercase tracking-[0.09em] text-slate-500 mb-1.5";

// ─── Component ────────────────────────────────────────────────────────────────

export default function RegisterPage() {
  const navigate = useNavigate();
  const formRef  = useRef<HTMLFormElement>(null);

  const [name,        setName]        = useState("");
  const [email,       setEmail]       = useState("");
  const [password,    setPassword]    = useState("");
  const [confirm,     setConfirm]     = useState("");
  const [showPass,    setShowPass]    = useState(false);
  const [showConfirm, setShowConfirm] = useState(false);
  const [error,       setError]       = useState<string | null>(null);
  const [loading,     setLoading]     = useState(false);

  function triggerShake() {
    const el = formRef.current;
    if (!el) return;
    el.classList.remove("animate-auth-shake");
    void el.offsetWidth;
    el.classList.add("animate-auth-shake");
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);

    if (!name.trim())          { setError("Informe seu nome.");                             triggerShake(); return; }
    if (!email.trim())         { setError("Informe seu e-mail.");                           triggerShake(); return; }
    if (password.length < 6)   { setError("A senha deve ter no mínimo 6 caracteres.");      triggerShake(); return; }
    if (password !== confirm)  { setError("As senhas não conferem.");                       triggerShake(); return; }

    setLoading(true);
    const result = await authService.register({ name: name.trim(), email: email.trim(), password });
    setLoading(false);

    if (result.error) {
      setError(result.error);
      triggerShake();
    } else {
      localStorage.setItem("nexo:pending_email", email.trim());
      navigate(`/check-email?email=${encodeURIComponent(email.trim())}`);
    }
  }

  return (
    <div className="animate-auth-enter">

      {/* ── Eyebrow + headline ── */}
      <div className="mb-8">
        <div className="flex items-center gap-2 mb-4">
          <div className="h-px w-5 bg-[#5B4DFF]" />
          <span className="text-[11px] font-semibold uppercase tracking-[0.1em] text-[#5B4DFF]">
            Cadastro
          </span>
        </div>
        <h1 className="font-display text-[26px] sm:text-[28px] font-bold text-white leading-[1.1] tracking-tight">
          Criar conta<br />no Orken
        </h1>
      </div>

      {/* ── Form ── */}
      <form
        ref={formRef}
        onSubmit={handleSubmit}
        noValidate
        className="space-y-4"
        onAnimationEnd={() => formRef.current?.classList.remove("animate-auth-shake")}
      >
        {/* Name */}
        <div>
          <label htmlFor="name" className={LABEL}>Seu nome</label>
          <input
            id="name"
            type="text"
            autoComplete="name"
            placeholder="João Silva"
            value={name}
            onChange={(e) => setName(e.target.value)}
            disabled={loading}
            autoFocus
            className={INPUT_BASE}
          />
        </div>

        {/* Email */}
        <div>
          <label htmlFor="email" className={LABEL}>E-mail</label>
          <input
            id="email"
            type="email"
            autoComplete="email"
            placeholder="joao@empresa.com"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            disabled={loading}
            className={INPUT_BASE}
          />
        </div>

        {/* Password */}
        <div>
          <label htmlFor="password" className={LABEL}>Senha</label>
          <div className="relative">
            <input
              id="password"
              type={showPass ? "text" : "password"}
              autoComplete="new-password"
              placeholder="Mínimo 6 caracteres"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              disabled={loading}
              className={`${INPUT_BASE} pr-11`}
            />
            <button
              type="button"
              onClick={() => setShowPass((v) => !v)}
              tabIndex={-1}
              className="absolute right-2.5 top-1/2 -translate-y-1/2 p-1.5 text-slate-600 hover:text-slate-300 transition-colors focus:outline-none"
              aria-label={showPass ? "Ocultar senha" : "Mostrar senha"}
            >
              {showPass ? <EyeOff className="h-[15px] w-[15px]" /> : <Eye className="h-[15px] w-[15px]" />}
            </button>
          </div>
        </div>

        {/* Confirm */}
        <div>
          <label htmlFor="confirm" className={LABEL}>Confirmar senha</label>
          <div className="relative">
            <input
              id="confirm"
              type={showConfirm ? "text" : "password"}
              autoComplete="new-password"
              placeholder="Repita a senha"
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
              disabled={loading}
              className={`${INPUT_BASE} pr-11`}
            />
            <button
              type="button"
              onClick={() => setShowConfirm((v) => !v)}
              tabIndex={-1}
              className="absolute right-2.5 top-1/2 -translate-y-1/2 p-1.5 text-slate-600 hover:text-slate-300 transition-colors focus:outline-none"
              aria-label={showConfirm ? "Ocultar senha" : "Mostrar senha"}
            >
              {showConfirm ? <EyeOff className="h-[15px] w-[15px]" /> : <Eye className="h-[15px] w-[15px]" />}
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
            {loading ? <Loader2 className="h-[18px] w-[18px] animate-spin" /> : "Criar conta"}
          </button>
        </div>
      </form>

      {/* ── Login link ── */}
      <div className="mt-6 pt-5 border-t border-white/[0.06]">
        <p className="text-[13px] text-slate-600">
          Já tem conta?{" "}
          <Link
            to="/login"
            className="text-slate-400 hover:text-white transition-colors font-medium focus-visible:outline-none focus-visible:text-white"
          >
            Entrar →
          </Link>
        </p>
      </div>

    </div>
  );
}
