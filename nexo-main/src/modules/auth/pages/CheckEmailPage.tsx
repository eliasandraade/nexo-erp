import { useState } from "react";
import { useSearchParams, Link } from "react-router-dom";
import { Mail, Loader2, CheckCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { resendVerification } from "../services/authService";

export default function CheckEmailPage() {
  const [params] = useSearchParams();
  const email = params.get("email") ?? "";
  const [resent, setResent] = useState(false);
  const [loading, setLoading] = useState(false);

  async function handleResend() {
    if (!email || loading) return;
    setLoading(true);
    try {
      await resendVerification(email);
      setResent(true);
    } catch {
      // silent
    }
    setLoading(false);
  }

  return (
    <div className="bg-card border border-border rounded-xl shadow-sm p-8 space-y-6 text-center">
      <div className="flex justify-center">
        <div className="w-14 h-14 rounded-full bg-primary/10 flex items-center justify-center">
          <Mail className="h-7 w-7 text-primary" />
        </div>
      </div>
      <div className="space-y-2">
        <h1 className="text-xl font-semibold text-foreground">Verifique seu e-mail</h1>
        <p className="text-sm text-muted-foreground">
          Enviamos um link de ativação para{" "}
          {email && <span className="font-medium text-foreground">{email}</span>}.
        </p>
        <p className="text-sm text-muted-foreground">
          Clique no link para ativar sua conta e entrar.
        </p>
      </div>

      {resent ? (
        <div className="flex items-center justify-center gap-2 text-sm text-green-600">
          <CheckCircle className="h-4 w-4" />
          E-mail reenviado com sucesso.
        </div>
      ) : (
        <Button
          variant="outline"
          onClick={handleResend}
          disabled={loading || !email}
          className="w-full"
        >
          {loading && <Loader2 className="h-4 w-4 mr-2 animate-spin" />}
          Reenviar e-mail
        </Button>
      )}

      <p className="text-sm text-muted-foreground">
        <Link to="/login" className="text-primary hover:underline">
          Voltar para o login
        </Link>
      </p>
    </div>
  );
}
