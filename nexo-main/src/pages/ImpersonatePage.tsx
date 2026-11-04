import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { startImpersonationSession } from "@/modules/auth/services/authService";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { resolvePostLogin } from "@/modules/workspace/resolvePostLogin";
import { readLastWorkspace } from "@/modules/workspace/persistence";
import { HandoffError, requestImpersonation } from "@/services/impersonation-handoff";

/**
 * Landing page of a platform administrator's access to a tenant (opened in a new tab as `/impersonate?k=<nonce>`).
 * It asks the platform tab for the token of its nonce over a BroadcastChannel — nothing is passed through
 * localStorage — and keeps the token and the session in THIS tab only (sessionStorage, impersonation mode).
 */
export default function ImpersonatePage() {
  const navigate = useNavigate();
  const { setSessionFromVerify } = useAuth();
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const nonce = new URLSearchParams(window.location.search).get("k");
    if (!nonce) {
      setError("Este link de acesso não é válido. Abra o acesso de novo pela plataforma.");
      return;
    }
    const abort = new AbortController();
    requestImpersonation(nonce, { signal: abort.signal })
      .then(grant => {
        const session = startImpersonationSession(grant);
        setSessionFromVerify(session);
        navigate(resolvePostLogin(session, readLastWorkspace(session)), { replace: true });
      })
      .catch((err: unknown) => {
        if (abort.signal.aborted) return;
        setError(
          err instanceof HandoffError && err.reason === "failed"
            ? err.message
            : "O acesso não chegou a esta aba. Volte à plataforma e tente de novo."
        );
      });
    return () => abort.abort();
  }, [navigate, setSessionFromVerify]);

  if (error) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background p-4">
        <div role="alert" className="max-w-sm text-center space-y-3">
          <h1 className="text-lg font-semibold text-foreground">Não foi possível entrar na empresa</h1>
          <p className="text-sm text-muted-foreground">{error}</p>
          {/* full load: this tab then reads the administrator's own session again */}
          <a href="/platform" className="inline-block text-sm font-medium text-primary hover:underline">
            Voltar à plataforma
          </a>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-background">
      <div className="text-center space-y-3">
        <div className="w-8 h-8 border-2 border-primary border-t-transparent rounded-full animate-spin mx-auto" />
        <p className="text-sm text-muted-foreground">Entrando na empresa…</p>
      </div>
    </div>
  );
}
