import { useState } from "react";
import { useSearchParams, Link } from "react-router-dom";
import { Mail, CheckCircle, Loader2 } from "lucide-react";
import { resendVerification } from "../services/authService";

export default function CheckEmailPage() {
  const [params] = useSearchParams();
  const email    = params.get("email") ?? "";

  const [resent,  setResent]  = useState(false);
  const [loading, setLoading] = useState(false);

  async function handleResend() {
    if (!email || loading) return;
    setLoading(true);
    try { await resendVerification(email); setResent(true); } catch { /* silent */ }
    setLoading(false);
  }

  return (
    <div className="animate-auth-enter text-center">

      {/* Icon */}
      <div className="flex justify-center mb-8">
        <div className="w-16 h-16 rounded-full bg-[#5B4DFF]/10 border border-[#5B4DFF]/20 flex items-center justify-center">
          <Mail className="h-7 w-7 text-[#5B4DFF]" />
        </div>
      </div>

      {/* Heading */}
      <div className="mb-8">
        <div className="flex items-center justify-center gap-2 mb-4">
          <div className="h-px w-5 bg-[#5B4DFF]" />
          <span className="text-[11px] font-semibold uppercase tracking-[0.1em] text-[#5B4DFF]">
            Verificação
          </span>
          <div className="h-px w-5 bg-[#5B4DFF]" />
        </div>
        <h1 className="font-display text-[26px] sm:text-[28px] font-bold text-white leading-tight tracking-tight mb-3">
          Verifique seu e-mail
        </h1>
        <p className="text-[13px] text-slate-500 leading-relaxed">
          Enviamos um link de ativação para{" "}
          {email && <span className="text-slate-300 font-medium">{email}</span>}.
          <br />Clique no link para ativar sua conta.
        </p>
      </div>

      {/* Resend / success */}
      {resent ? (
        <div className="flex items-center justify-center gap-2 text-[13px] text-emerald-400 mb-6">
          <CheckCircle className="h-4 w-4 shrink-0" />
          E-mail reenviado com sucesso.
        </div>
      ) : (
        <button
          onClick={handleResend}
          disabled={loading || !email}
          className="
            w-full h-[52px] rounded-[10px]
            border border-white/[0.08] bg-white/[0.04]
            text-white text-[14px] font-semibold
            flex items-center justify-center gap-2
            transition-all duration-150
            hover:bg-white/[0.07] hover:border-white/[0.15]
            disabled:opacity-50 disabled:cursor-not-allowed
            focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#5B4DFF]/50
          "
        >
          {loading ? <Loader2 className="h-[18px] w-[18px] animate-spin" /> : "Reenviar e-mail"}
        </button>
      )}

      {/* Back to login */}
      <div className="mt-6 pt-5 border-t border-white/[0.06]">
        <p className="text-[13px] text-slate-600">
          <Link
            to="/login"
            className="text-slate-400 hover:text-white transition-colors font-medium focus-visible:outline-none focus-visible:text-white"
          >
            ← Voltar para o login
          </Link>
        </p>
      </div>

    </div>
  );
}
