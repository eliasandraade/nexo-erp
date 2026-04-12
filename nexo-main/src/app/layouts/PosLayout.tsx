import { Outlet, useNavigate } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import { Button } from "@/components/ui/button";

export function PosLayout() {
  const navigate = useNavigate();

  return (
    <div className="h-screen flex flex-col bg-background overflow-hidden">
      {/* Minimal top bar */}
      <header className="flex items-center justify-between px-4 py-2 border-b border-border bg-sidebar shrink-0">
        <div className="flex items-center gap-3">
          <img src="/orken_darkmode.png" alt="Orken" className="h-5 w-auto object-contain" />
          <span className="text-xs text-sidebar-muted">PDV — Ponto de Venda</span>
        </div>
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/dashboard")}
          className="text-sidebar-foreground hover:bg-sidebar-accent/50 text-xs"
        >
          <ArrowLeft className="h-3.5 w-3.5 mr-1.5" />
          Voltar ao sistema
        </Button>
      </header>

      {/* Main POS content */}
      <main className="flex-1 overflow-hidden">
        <Outlet />
      </main>
    </div>
  );
}
