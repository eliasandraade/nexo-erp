import { Link, Navigate, Outlet } from "react-router-dom";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { Brand } from "@/components/shared/Brand";
export function AuthLayout() {
  const { session } = useAuth();
  if (session) return <Navigate to="/dashboard" replace />;
  return <div className="flex min-h-dvh flex-col bg-background">
    <header className="flex items-center justify-between border-b border-border px-6 py-6 sm:px-10"><Link to="/" aria-label="ORKEN, início"><Brand /></Link><span className="text-xs text-muted-foreground">Acesso à sua empresa</span></header>
    <main className="mx-auto grid w-full max-w-5xl flex-1 items-center gap-12 px-6 py-12 md:grid-cols-2 md:gap-20">
      <div className="hidden self-stretch border-r border-border pr-12 md:flex md:flex-col md:justify-center">
        <p className="mb-5 text-xs font-medium uppercase tracking-widest text-muted-foreground">Seu ponto de trabalho</p>
        <h2 className="text-4xl font-semibold leading-tight tracking-tight">A operação continua por aqui.</h2>
        <p className="mt-6 max-w-sm text-base leading-relaxed text-muted-foreground">Acesse os registros, as pessoas e as tarefas da sua empresa. Cada equipe encontra as áreas liberadas para seu trabalho.</p>
        <p className="mt-12 border-t border-border pt-5 text-xs text-muted-foreground">ORKEN · Andrade Systems</p>
      </div>
      <div className="mx-auto w-full max-w-sm"><Outlet /></div>
    </main>
    <footer className="px-6 py-6 text-center text-xs text-muted-foreground">Precisa de acesso? Fale com o administrador da sua empresa.</footer>
  </div>;
}
