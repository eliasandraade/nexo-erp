import { useState } from "react";
import { Outlet, NavLink, Link } from "react-router-dom";
import { Menu, LogOut } from "lucide-react";
import { Brand } from "@/components/shared/Brand";
import { Sheet, SheetContent, SheetTitle, SheetDescription, SheetTrigger } from "@/components/ui/sheet";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { cn } from "@/lib/utils";

const groups = [
  { label: "Plataforma", links: [["/platform", "Visão geral"], ["/platform/tenants", "Empresas"], ["/platform/trial", "Períodos de teste"], ["/platform/activity", "Atividade"], ["/platform/flags", "Recursos"], ["/platform/system", "Sistema"]] },
  { label: "Operações de IA", links: [["/platform/ai", "Visão geral de IA"], ["/platform/ai/playground", "Testar interpretação"], ["/platform/ai/providers", "Provedores"], ["/platform/ai/telemetry", "Telemetria"], ["/platform/ai/costs", "Custos"], ["/platform/ai/prompts", "Instruções"]] },
];
function Navigation({ onNavigate }: { onNavigate?: () => void }) {
  const { session, logout } = useAuth();
  return <div className="flex h-full flex-col bg-sidebar">
    <Link to="/platform" className="px-5 py-6" onClick={onNavigate}><Brand /></Link>
    <p className="mx-4 mb-6 border-b border-border pb-4 text-xs font-medium text-muted-foreground">Administração da plataforma</p>
    <nav aria-label="Plataforma" className="flex-1 space-y-6 overflow-auto px-3">
      {groups.map(group => <div key={group.label}><p className="mb-2 px-3 text-xs font-medium text-muted-foreground">{group.label}</p>
        {group.links.map(([to, label]) => <NavLink key={to} to={to} end={to === "/platform" || to === "/platform/ai"} onClick={onNavigate} className={({isActive}) => cn("block rounded px-3 py-2 text-sm", isActive ? "bg-sidebar-accent font-semibold text-sidebar-accent-foreground" : "text-sidebar-foreground hover:bg-muted")}>{label}</NavLink>)}
      </div>)}
    </nav>
    <div className="mt-6 border-t border-border p-4"><p className="truncate text-sm font-medium">{session?.name || session?.email}</p><p className="mt-1 text-xs text-muted-foreground">Administrador da plataforma</p><button onClick={logout} className="mt-3 flex min-h-10 items-center gap-2 text-sm text-muted-foreground"><LogOut className="h-4 w-4" />Sair</button></div>
  </div>;
}
export function PlatformLayout() {
  const [open, setOpen] = useState(false);
  return <div className="flex min-h-dvh bg-background">
    <a href="#platform-content" className="skip-link">Pular para o conteúdo</a>
    <aside className="sticky top-0 hidden h-dvh w-60 shrink-0 border-r border-border lg:block"><Navigation /></aside>
    <div className="min-w-0 flex-1">
      <header className="sticky top-0 z-30 flex h-16 items-center gap-4 border-b border-border bg-card px-4 lg:px-8">
        <Sheet open={open} onOpenChange={setOpen}><SheetTrigger asChild><button className="flex h-11 w-11 items-center justify-center lg:hidden" aria-label="Abrir menu"><Menu className="h-5 w-5" /></button></SheetTrigger>
          <SheetContent side="left" className="w-72 p-0"><SheetTitle className="sr-only">Navegação da plataforma</SheetTitle><SheetDescription className="sr-only">Empresas e ferramentas administrativas.</SheetDescription><Navigation onNavigate={() => setOpen(false)} /></SheetContent>
        </Sheet>
        <span className="text-sm font-medium">Plataforma</span><span className="text-xs text-muted-foreground">Administração global</span>
      </header>
      <main id="platform-content" tabIndex={-1} className="min-w-0"><Outlet /></main>
    </div>
  </div>;
}
