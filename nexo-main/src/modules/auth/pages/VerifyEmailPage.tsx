import { useEffect, useState } from "react";
import { useSearchParams, useNavigate, Link } from "react-router-dom";
import { Loader2, CheckCircle, XCircle, Mail } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { verifyEmail, resendVerification } from "../services/authService";
import { useAuth } from "../context/AuthContext";

type VerifyStatus = "verifying" | "success" | "error";
type ResendStatus = "idle" | "resending" | "resent" | "error";

export default function VerifyEmailPage() {
  const [params] = useSearchParams();
  const token = params.get("token") ?? "";
  const navigate = useNavigate();
  const { setSessionFromVerify } = useAuth();

  const [status, setStatus] = useState<VerifyStatus>("verifying");
  const [resendStatus, setResendStatus] = useState<ResendStatus>("idle");
  const [email, setEmail] = useState<string>(
    () => localStorage.getItem("nexo:pending_email") ?? ""
  );

  useEffect(() => {
    if (!token) {
      setStatus("error");
      return;
    }

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
    <div className="bg-card border border-border rounded-xl shadow-sm p-8 space-y-6 text-center">

      {/* Verifying */}
      {status === "verifying" && (
        <>
          <Loader2 className="h-10 w-10 text-primary animate-spin mx-auto" />
          <p className="text-sm text-muted-foreground">Verificando sua conta...</p>
        </>
      )}

      {/* Success */}
      {status === "success" && (
        <>
          <CheckCircle className="h-12 w-12 text-green-500 mx-auto" />
          <div className="space-y-1">
            <h1 className="text-xl font-semibold text-foreground">Conta verificada!</h1>
            <p className="text-sm text-muted-foreground">Redirecionando para o sistema...</p>
          </div>
        </>
      )}

      {/* Error */}
      {status === "error" && (
        <>
          <XCircle className="h-12 w-12 text-destructive mx-auto" />
          <div className="space-y-1">
            <h1 className="text-xl font-semibold text-foreground">Link inválido ou expirado</h1>
            <p className="text-sm text-muted-foreground">
              Este link já foi usado, expirou ou não é válido.
              <br />
              Solicite um novo link de verificação abaixo.
            </p>
          </div>

          {/* Resend section */}
          <div className="space-y-3 pt-1">
            {resendStatus !== "resent" ? (
              <>
                <div className="flex gap-2">
                  <Input
                    type="email"
                    placeholder="seu@email.com"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    disabled={resendStatus === "resending"}
                    className="flex-1"
                  />
                  <Button
                    onClick={handleResend}
                    disabled={!email.trim() || resendStatus === "resending"}
                    className="shrink-0"
                  >
                    {resendStatus === "resending" ? (
                      <Loader2 className="h-4 w-4 animate-spin" />
                    ) : (
                      "Reenviar"
                    )}
                  </Button>
                </div>
                {resendStatus === "error" && (
                  <p className="text-xs text-destructive">
                    Erro ao reenviar. Tente novamente.
                  </p>
                )}
              </>
            ) : (
              <div className="flex items-center justify-center gap-2 text-sm text-green-600 bg-green-50 border border-green-200 rounded-lg px-3 py-2">
                <Mail className="h-4 w-4 shrink-0" />
                Novo link enviado. Verifique sua caixa de entrada.
              </div>
            )}
          </div>

          <Button asChild variant="ghost" className="w-full text-muted-foreground">
            <Link to="/login">Voltar ao login</Link>
          </Button>
        </>
      )}

    </div>
  );
}
