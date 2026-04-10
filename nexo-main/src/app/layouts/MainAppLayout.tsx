import { Outlet } from "react-router-dom";
import { AppSidebar } from "@/components/shared/AppSidebar";
import { AppHeader } from "@/components/shared/AppHeader";

export function MainAppLayout() {
  return (
    <div className="flex min-h-screen w-full bg-background">
      <AppSidebar />
      <div className="flex-1 flex flex-col min-w-0">
        <AppHeader />
        <main className="flex-1 p-6 overflow-auto">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
