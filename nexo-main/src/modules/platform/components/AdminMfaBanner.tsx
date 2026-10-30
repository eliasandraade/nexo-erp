import { Link } from "react-router-dom";
import { ShieldAlert } from "lucide-react";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { useAdminActionsAllowed } from "../hooks/usePlatformSecurity";

/**
 * Persistent notice (no close button) while administrative acts are refused for this sign-in: the server only
 * accepts them from a super_admin signed in with two-step verification.
 */
export function AdminMfaBanner() {
  const { session } = useAuth();
  const allowed = useAdminActionsAllowed();
  if (allowed !== false) return null;
  const support = (session?.role as string | undefined) === "support";

  return (
    <div role="status" className="flex flex-wrap items-center gap-x-3 gap-y-1 border-b border-amber-500/30 bg-amber-500/10 px-4 py-2.5 text-sm text-foreground lg:px-8">
      <ShieldAlert className="h-4 w-4 shrink-0 text-amber-600" aria-hidden />
      <span className="min-w-0 flex-1">
        Ações administrativas exigem login com verificação em duas etapas.
        {support && " Seu perfil de suporte permite apenas consultar."}
      </span>
      {!support && (
        <Link to="/platform/seguranca" className="shrink-0 font-medium text-primary hover:underline">
          Configurar segurança
        </Link>
      )}
    </div>
  );
}
