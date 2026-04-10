import { Navigate, Outlet } from "react-router-dom";
import { useAuth } from "@/modules/auth/context/AuthContext";

/**
 * Layout for unauthenticated pages (/login).
 * Redirects to /dashboard if a session already exists,
 * so users who are already logged in never see the login page again.
 */
export function AuthLayout() {
  const { session } = useAuth();

  if (session) {
    return <Navigate to="/dashboard" replace />;
  }

  return (
    <div className="flex min-h-screen bg-background">
      {/* Left panel — branding */}
      <div className="hidden lg:flex lg:w-1/2 xl:w-2/5 bg-card border-r border-border flex-col justify-between p-12">
        <div>
          <div className="flex items-center gap-2">
            <div className="w-8 h-8 rounded-lg bg-primary flex items-center justify-center">
              <span className="text-xs font-bold text-primary-foreground">N</span>
            </div>
            <span className="text-xl font-bold text-foreground tracking-tight">
              NEXO
            </span>
          </div>
        </div>

        <div className="space-y-3">
          <p className="text-3xl font-semibold text-foreground leading-snug">
            Gestão inteligente
            <br />
            para empresas reais.
          </p>
          <p className="text-sm text-muted-foreground">
            Controle operacional centralizado para pequenas e médias empresas.
          </p>
        </div>

        <p className="text-xs text-muted-foreground">
          © {new Date().getFullYear()} Andrade Systems. Todos os direitos reservados.
        </p>
      </div>

      {/* Right panel — form */}
      <div className="flex flex-1 flex-col items-center justify-center px-6 py-12">
        {/* Mobile branding */}
        <div className="lg:hidden mb-8 text-center">
          <div className="flex items-center justify-center gap-2 mb-1">
            <div className="w-8 h-8 rounded-lg bg-primary flex items-center justify-center">
              <span className="text-xs font-bold text-primary-foreground">N</span>
            </div>
            <span className="text-xl font-bold text-foreground tracking-tight">
              NEXO
            </span>
          </div>
          <p className="text-sm text-muted-foreground">
            Gestão inteligente para empresas reais.
          </p>
        </div>

        <div className="w-full max-w-sm">
          <div className="mb-6">
            <h2 className="text-xl font-semibold text-foreground">
              Acesse sua conta
            </h2>
            <p className="text-sm text-muted-foreground mt-1">
              Entre com suas credenciais para continuar.
            </p>
          </div>

          <Outlet />
        </div>

        <p className="mt-8 text-xs text-muted-foreground lg:hidden">
          © {new Date().getFullYear()} Andrade Systems
        </p>
      </div>
    </div>
  );
}
