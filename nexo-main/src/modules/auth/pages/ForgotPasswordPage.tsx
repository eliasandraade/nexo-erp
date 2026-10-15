import { useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { CheckCircle, Loader2, Mail } from "lucide-react";
import { requestPasswordReset } from "../services/authService";

/** Asks for a reset link. Says the same thing whether or not the address has an account. */
export default function ForgotPasswordPage() {
  const [email, setEmail] = useState("");
  const [sent, setSent] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!email.trim() || loading) return;
    setError(null);
    setLoading(true);
    try {
      await requestPasswordReset(email.trim());
      setSent(true);
    } catch {
      setError("Não foi possível enviar agora. Tente de novo em alguns minutos.");
    }
    setLoading(false);
  }

  return (
    <div>
      <div className="mb-8">
        <span className="text-[11px] font-semibold uppercase tracking-[0.1em] text-primary">Recuperar acesso</span>
        <h1 className="mt-4 font-sans text-[26px] sm:text-[28px] font-semibold text-foreground leading-[1.1] tracking-tight">
          Esqueci a senha
        </h1>
        <p className="mt-3 text-[13px] text-muted-foreground leading-relaxed">
          Informe o e-mail da sua conta. Se ele estiver cadastrado, enviamos um link para criar uma nova senha.
        </p>
      </div>

      {sent ? (
        <output className="block space-y-6">
          <div className="flex items-start gap-2 text-[13px] text-success">
            <CheckCircle className="mt-0.5 h-4 w-4 shrink-0" />
            <span>Se houver uma conta com <strong>{email.trim()}</strong>, o link chega em instantes. Ele vale por 1 hora. Confira também o spam.</span>
          </div>
          <Link to="/login" className="text-[13px] font-medium text-primary hover:underline">Voltar para o login</Link>
        </output>
      ) : (
        <form onSubmit={handleSubmit} noValidate className="space-y-4">
          <div>
            <label htmlFor="email" className="mb-1.5 block text-[11px] font-semibold uppercase tracking-[0.09em] text-muted-foreground">E-mail</label>
            <div className="relative">
              <Mail className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
              <input id="email" type="email" autoComplete="email" value={email}
                onChange={(e) => setEmail(e.target.value)} className="auth-input pl-9" placeholder="voce@empresa.com.br" />
            </div>
          </div>
          {error && <p role="alert" className="text-[13px] text-destructive">{error}</p>}
          <button type="submit" disabled={!email.trim() || loading} className="auth-submit inline-flex w-full items-center justify-center gap-2">
            {loading && <Loader2 className="h-4 w-4 animate-spin" />} Enviar link
          </button>
          <Link to="/login" className="block text-center text-[13px] text-muted-foreground hover:text-foreground">Voltar para o login</Link>
        </form>
      )}
    </div>
  );
}
