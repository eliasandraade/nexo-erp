import { useEffect, useState } from "react";
import { useSearchParams, useNavigate, Link } from "react-router-dom";
import { Loader2, CheckCircle, XCircle, Mail } from "lucide-react";
import { verifyEmail, resendVerification } from "../services/authService";
import { useAuth } from "../context/AuthContext";
import { resolvePostLogin } from "@/modules/workspace/resolvePostLogin";
import { readLastWorkspace } from "@/modules/workspace/persistence";

// ─── Shared input styles ──────────────────────────────────────────────────────

const INPUT_BASE = "auth-input";

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
        const target = resolvePostLogin(result.session, readLastWorkspace(result.session));
        setTimeout(() => navigate(target, { replace: true }), 1500);
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
    <div className=" text-center">

      {/* ── Verifying ── */}
      {status === "verifying" && (
        <div className="flex flex-col items-center gap-4">
          <Loader2 className="h-10 w-10 text-primary animate-spin" />
          <p className="text-[14px] text-muted-foreground">Verificando sua conta...</p>
        </div>
      )}

      {/* ── Success ── */}
      {status === "success" && (
        <>
          <div className="flex justify-center mb-6">
            <div className="w-16 h-16 rounded-full bg-emerald-500/10 border border-emerald-500/20 flex items-center justify-center">
              <CheckCircle className="h-7 w-7 text-success" />
            </div>
          </div>
          <h1 className="font-sans text-[26px] font-semibold text-foreground tracking-tight mb-3">
            Conta verificada!
          </h1>
          <p className="text-[13px] text-muted-foreground">Redirecionando para o sistema...</p>
        </>
      )}

      {/* ── Error ── */}
      {status === "error" && (
        <>
          {/* Icon */}
          <div className="flex justify-center mb-8">
            <div className="w-16 h-16 rounded-full bg-red-500/10 border border-red-500/20 flex items-center justify-center">
              <XCircle className="h-7 w-7 text-destructive" />
            </div>
          </div>

          {/* Heading */}
          <div className="mb-8">
            <div className="flex items-center justify-center gap-2 mb-4">

              <span className="text-[11px] font-semibold uppercase tracking-[0.1em] text-destructive">
                Link inválido
              </span>

            </div>
            <h1 className="font-sans text-[24px] font-semibold text-foreground tracking-tight mb-3">
              Link expirado ou inválido
            </h1>
            <p className="text-[13px] text-muted-foreground leading-relaxed">
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
                  className="auth-submit"
                >
                  {resendStatus === "resending"
                    ? <Loader2 className="h-[18px] w-[18px] animate-spin" />
                    : "Reenviar link"
                  }
                </button>

                {resendStatus === "error" && (
                  <p className="text-[12px] text-destructive">
                    Erro ao reenviar. Tente novamente.
                  </p>
                )}
              </>
            ) : (
              <div className="flex items-center justify-center gap-2 text-[13px] text-success py-2">
                <Mail className="h-4 w-4 shrink-0" />
                Novo link enviado. Verifique sua caixa de entrada.
              </div>
            )}
          </div>

          {/* Back to login */}
          <div className="mt-6 pt-5 border-t border-border">
            <p className="text-[13px] text-muted-foreground">
              <Link
                to="/login"
                className="text-muted-foreground hover:text-foreground transition-colors font-medium  focus-visible:text-foreground"
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
