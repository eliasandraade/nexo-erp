import { useEffect, useState } from "react";
import { useSearchParams, useNavigate, Link } from "react-router-dom";
import { Loader2, CheckCircle, XCircle, Mail } from "lucide-react";
import { verifyEmail, resendVerification } from "../services/authService";
import { useAuth } from "../context/AuthContext";

// ─── Shared input styles ──────────────────────────────────────────────────────

const INPUT_BASE =
  "w-full h-12 rounded-[10px] border border-white/[0.08] bg-white/[0.04] " +
  "px-4 text-[14px] text-white placeholder:text-white/20 " +
  "outline-none transition-all duration-150 " +
  "focus:border-[#5B4DFF] focus:bg-white/[0.06] focus:shadow-[0_0_0_3px_rgba(91,77,255,0.12)] " +
  "disabled:opacity-50 disabled:cursor-not-allowed";

// ─── Types ────────────────────────────────────────────────────────────────────

type VerifyStatus = "verifying" | "success" | "error";
type ResendStatus = "idle" | "resending" | "resent" | "error";

// ─── Component ────────────────────────────────────────────────────────────────

export default function VerifyEmailPage() {
  const [params]   = useSearchParams();
  const token      = params.get("token") ?? "";
  const navigate   = useNavigate();
  const { setSessionFromVerify } = useAuth();

  const [status,       setStatus]       = useState<VerifyStatus>("verifying");
  const [resendStatus, setResendStatus] = useState<ResendStatus>("idle");
  const [email,        setEmail]        = useState<string>(
    () => localStorage.getItem("nexo:pending_email") ?? ""
  );

  useEffect(() => {
    if (!token) { setStatus("error"); return; }

    verifyEmail(token).then((result) => {
      if (result.success && result.session) {
        localStorage.removeItem("nexo:pending_email");
        setSessionFromVerify(result.session);
        setStatus("success");
        setTimeout(() => navigate("/dashboard", { replace: true }), 1500);
      } else {
        setStatus("error");
      }
    });
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function handleResend() {
    const target = email.trim();
    if (!target || resendStatus === "resending") return;
    setResendStatus("resending");
    try {
      await resendVerification(target);
      localStorage.setItem("nexo:pending_email", target);
      setResendStatus("resent");
    } catch {
      setResendStatus("error");
    }
  }

  return (
    <div className="animate-auth-enter text-center">

      {/* ── Verifying ── */}
      {status === "verifying" && (
        <div className="flex flex-col items-center gap-4">
          <Loader2 className="h-10 w-10 text-[#5B4DFF] animate-spin" />
          <p className="text-[14px] text-slate-400">Verificando sua conta...</p>
        </div>
      )}

      {/* ── Success ── */}
      {status === "success" && (
        <>
          <div className="flex justify-center mb-6">
            <div className="w-16 h-16 rounded-full bg-emerald-500/10 border border-emerald-500/20 flex items-center justify-center">
              <CheckCircle className="h-7 w-7 text-emerald-400" />
            </div>
          </div>
          <h1 className="font-display text-[26px] font-bold text-white tracking-tight mb-3">
            Conta verificada!
          </h1>
          <p className="text-[13px] text-slate-500">Redirecionando para o sistema...</p>
        </>
      )}

      {/* ── Error ── */}
      {status === "error" && (
        <>
          {/* Icon */}
          <div className="flex justify-center mb-8">
            <div className="w-16 h-16 rounded-full bg-red-500/10 border border-red-500/20 flex items-center justify-center">
              <XCircle className="h-7 w-7 text-red-400" />
            </div>
          </div>

          {/* Heading */}
          <div className="mb-8">
            <div className="flex items-center justify-center gap-2 mb-4">
              <div className="h-px w-5 bg-red-500/60" />
              <span className="text-[11px] font-semibold uppercase tracking-[0.1em] text-red-400">
                Link inválido
              </span>
              <div className="h-px w-5 bg-red-500/60" />
            </div>
            <h1 className="font-display text-[24px] font-bold text-white tracking-tight mb-3">
              Link expirado ou inválido
            </h1>
            <p className="text-[13px] text-slate-500 leading-relaxed">
              Este link já foi usado ou expirou.<br />
              Solicite um novo link abaixo.
            </p>
          </div>

          {/* Resend */}
          <div className="space-y-3">
            {resendStatus !== "resent" ? (
              <>
                <input
                  type="email"
                  placeholder="seu@email.com"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  disabled={resendStatus === "resending"}
                  className={INPUT_BASE}
                />

                <button
                  onClick={handleResend}
                  disabled={!email.trim() || resendStatus === "resending"}
                  className="
                    w-full h-[52px] rounded-[10px]
                    bg-[#5B4DFF] hover:bg-[#4338CA] active:scale-[0.985]
                    text-white text-[14px] font-semibold
                    flex items-center justify-center
                    transition-all duration-150
                    disabled:opacity-60 disabled:cursor-not-allowed
                    focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#5B4DFF]/50
                  "
                >
                  {resendStatus === "resending"
                    ? <Loader2 className="h-[18px] w-[18px] animate-spin" />
                    : "Reenviar link"
                  }
                </button>

                {resendStatus === "error" && (
                  <p className="text-[12px] text-red-400">
                    Erro ao reenviar. Tente novamente.
                  </p>
                )}
              </>
            ) : (
              <div className="flex items-center justify-center gap-2 text-[13px] text-emerald-400 py-2">
                <Mail className="h-4 w-4 shrink-0" />
                Novo link enviado. Verifique sua caixa de entrada.
              </div>
            )}
          </div>

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
        </>
      )}

    </div>
  );
}
