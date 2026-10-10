import { useState } from "react";
import { useSearchParams, Link } from "react-router-dom";
import { Mail, CheckCircle, Loader2 } from "lucide-react";
import { resendVerification } from "../services/authService";

export default function CheckEmailPage() {
  const [params] = useSearchParams();
  const email    = params.get("email") ?? "";

  const [error, setError] = useState(false);
  const [resent,  setResent]  = useState(false);
  const [loading, setLoading] = useState(false);

  async function handleResend() {
    if (!email || loading) return;
    setError(false);
    setLoading(true);
    try { await resendVerification(email); setResent(true); } catch { setError(true); }
    setLoading(false);
  }

  return (
    <div className=" text-center">

      {/* Icon */}
      <div className="flex justify-center mb-8">
        <div className="w-16 h-16 rounded-full bg-primary/10 border border-primary/20 flex items-center justify-center">
          <Mail className="h-7 w-7 text-primary" />
        </div>
      </div>

      {/* Heading */}
      <div className="mb-8">
        <div className="flex items-center justify-center gap-2 mb-4">

          <span className="text-[11px] font-semibold uppercase tracking-[0.1em] text-primary">
            Verificação
          </span>

        </div>
        <h1 className="font-sans text-[26px] sm:text-[28px] font-semibold text-foreground leading-tight tracking-tight mb-3">
          Verifique seu e-mail
        </h1>
        <p className="text-[13px] text-muted-foreground leading-relaxed">
          Enviamos um link de ativação para{" "}
          {email && <span className="text-muted-foreground font-medium">{email}</span>}.
          <br />Clique no link para ativar sua conta.
        </p>
      </div>

      {/* Resend / success */}
      {resent ? (
        <div className="flex items-center justify-center gap-2 text-[13px] text-success mb-6">
          <CheckCircle className="h-4 w-4 shrink-0" />
          E-mail reenviado com sucesso.
        </div>
      ) : (
        <button
          onClick={handleResend}
          disabled={loading || !email}
          className="auth-submit"
        >
          {loading ? <Loader2 className="h-[18px] w-[18px] animate-spin" /> : "Reenviar e-mail"}
        </button>
      )}

      {error && <p role="alert" className="mt-3 text-sm text-destructive">Não foi possível reenviar. Tente novamente.</p>}
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

    </div>
  );
}
