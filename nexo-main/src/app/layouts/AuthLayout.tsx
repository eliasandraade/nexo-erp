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
          <img
            src="/orken_lightmode.png"
            alt="Orken"
            className="h-8 w-auto object-contain"
          />
        </div>

        <div className="space-y-3">
          <p className="text-3xl font-semibold text-foreground leading-snug">
            Um sistema.
            <br />
            Seus negócios.
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
          <div className="flex items-center justify-center mb-2">
            <img
              src="/orken_lightmode.png"
              alt="Orken"
              className="h-8 w-auto object-contain"
            />
          </div>
          <p className="text-sm text-muted-foreground">
            Um sistema. Seus negócios.
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
