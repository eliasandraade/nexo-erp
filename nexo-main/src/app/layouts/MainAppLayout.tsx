import { useState } from "react";
import { Outlet } from "react-router-dom";
import { Menu } from "lucide-react";
import { AppSidebar, SidebarContent } from "@/components/shared/AppSidebar";
import { AppHeader } from "@/components/shared/AppHeader";
import { Sheet, SheetContent, SheetTitle, SheetDescription, SheetTrigger } from "@/components/ui/sheet";
export function MainAppLayout() {
  const [open, setOpen] = useState(false);
  return <div className="flex min-h-dvh w-full bg-background">
    <a href="#main-content" className="skip-link">Pular para o conteúdo</a>
    <AppSidebar />
    <div className="flex min-w-0 flex-1 flex-col">
      <div className="sticky top-0 z-30 flex items-center border-b border-border bg-card">
        <Sheet open={open} onOpenChange={setOpen}>
          <SheetTrigger asChild><button className="ml-3 flex h-11 w-11 shrink-0 items-center justify-center rounded hover:bg-muted lg:hidden" aria-label="Abrir menu"><Menu className="h-5 w-5" /></button></SheetTrigger>
          <SheetContent side="left" className="w-72 p-0">
            <SheetTitle className="sr-only">Navegação</SheetTitle>
            <SheetDescription className="sr-only">Área de trabalho e páginas disponíveis.</SheetDescription>
            <SidebarContent onNav={() => setOpen(false)} />
          </SheetContent>
        </Sheet>
        <div className="min-w-0 flex-1"><AppHeader /></div>
      </div>
      <main id="main-content" tabIndex={-1} className="min-w-0 flex-1 p-4 sm:p-6 lg:p-8">
        <div className="mx-auto w-full max-w-[1600px]"><Outlet /></div>
      </main>
    </div>
  </div>;
}
