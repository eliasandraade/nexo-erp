import { useEffect, useState, type FormEvent } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { Eye, EyeOff, Loader2 } from "lucide-react";
import { ApiError } from "@/services/api-client";
import { resetPassword } from "../services/authService";

const MIN = 6;

/** New password from the e-mailed link (one use, 1 h). Every session ends — the user signs in again. */
export default function ResetPasswordPage() {
  const [params] = useSearchParams();
  const [token] = useState(() => params.get("token") ?? "");
  const navigate = useNavigate();
  // keep the token out of the address bar / history once read
  useEffect(() => {
    if (token) window.history.replaceState(null, "", "/redefinir-senha");
  }, [token]);
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [show, setShow] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const mismatch = confirm.length > 0 && confirm !== password;
  const valid = password.length >= MIN && confirm === password;

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!valid || loading) return;
    setError(null);
    setLoading(true);
    try {
      await resetPassword(token, password);
      navigate("/login?motivo=senha-redefinida", { replace: true });
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Não foi possível alterar a senha agora.");
      setLoading(false);
    }
  }

  if (!token) {
    return (
      <div role="alert" className="space-y-4">
        <h1 className="font-sans text-[26px] font-semibold text-foreground">Link incompleto</h1>
        <p className="text-[13px] text-muted-foreground">Abra o link exatamente como chegou no e-mail, ou peça um novo.</p>
        <Link to="/esqueci-senha" className="text-[13px] font-medium text-primary hover:underline">Pedir um novo link</Link>
      </div>
    );
  }

  return (
    <div>
      <div className="mb-8">
        <span className="text-[11px] font-semibold uppercase tracking-[0.1em] text-primary">Recuperar acesso</span>
        <h1 className="mt-4 font-sans text-[26px] sm:text-[28px] font-semibold text-foreground leading-[1.1] tracking-tight">
          Criar nova senha
        </h1>
        <p className="mt-3 text-[13px] text-muted-foreground">Depois de salvar, você entra de novo em todos os aparelhos.</p>
      </div>
      <form onSubmit={handleSubmit} noValidate className="space-y-4">
        <div>
          <label htmlFor="new-password" className="mb-1.5 block text-[11px] font-semibold uppercase tracking-[0.09em] text-muted-foreground">Nova senha</label>
          <div className="relative">
            <input id="new-password" type={show ? "text" : "password"} autoComplete="new-password"
              value={password} onChange={(e) => setPassword(e.target.value)} className="auth-input pr-10" />
            <button type="button" onClick={() => setShow(!show)} aria-label={show ? "Ocultar senha" : "Mostrar senha"}
              className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground">
              {show ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
            </button>
          </div>
          <p className="mt-1 text-[11px] text-muted-foreground">Mínimo de {MIN} caracteres.</p>
        </div>
        <div>
          <label htmlFor="confirm-password" className="mb-1.5 block text-[11px] font-semibold uppercase tracking-[0.09em] text-muted-foreground">Confirme a nova senha</label>
          <input id="confirm-password" type={show ? "text" : "password"} autoComplete="new-password"
            value={confirm} onChange={(e) => setConfirm(e.target.value)} className="auth-input" aria-invalid={mismatch} />
          {mismatch && <p className="mt-1 text-[11px] text-destructive">As senhas não coincidem.</p>}
        </div>
        {error && (
          <p role="alert" className="text-[13px] text-destructive">
            {error} <Link to="/esqueci-senha" className="font-medium underline">Pedir um novo link</Link>
          </p>
        )}
        <button type="submit" disabled={!valid || loading} className="auth-submit inline-flex w-full items-center justify-center gap-2">
          {loading && <Loader2 className="h-4 w-4 animate-spin" />} Salvar nova senha
        </button>
      </form>
    </div>
  );
}
