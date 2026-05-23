import { useState, useMemo } from "react";
import { PageHeader } from "@/components/shared/PageHeader";
import { KpiCards } from "@/modules/dashboard/components/KpiCards";
import { SalesChart } from "@/modules/dashboard/components/SalesChart";
import { TopProducts } from "@/modules/dashboard/components/TopProducts";
import { SellerRanking } from "@/modules/dashboard/components/SellerRanking";
import { RecentInsights } from "@/modules/dashboard/components/RecentInsights";
import { StockAlerts } from "@/modules/dashboard/components/StockAlerts";
import { SetupCard } from "@/components/shared/SetupCard";
import { RestauranteBlocks } from "@/modules/dashboard/components/RestauranteBlocks";
import { useAuth } from "@/modules/auth/context/AuthContext";

function useSetupDismissed(userId: string | undefined) {
  const key = userId ? `nexo:setup-dismissed:${userId}` : null;
  const [dismissed, setDismissed] = useState(
    () => !!key && localStorage.getItem(key) === "1"
  );
  function dismiss() {
    if (key) localStorage.setItem(key, "1");
    setDismissed(true);
  }
  return { dismissed, dismiss };
}

function getGreeting(): string {
  const h = new Date().getHours();
  if (h < 12) return "Bom dia";
  if (h < 18) return "Boa tarde";
  return "Boa noite";
}

export default function DashboardPage() {
  const { session } = useAuth();
  const { dismissed, dismiss } = useSetupDismissed(session?.userId);

  const greeting = useMemo(() => getGreeting(), []);
  const firstName = session?.name?.split(" ")[0] ?? "";

  return (
    <div className="space-y-6">
      {!dismissed && <SetupCard onDismiss={dismiss} />}

      <PageHeader
        title={firstName ? `${greeting}, ${firstName}` : greeting}
        description="Visão geral da operação hoje"
      />

      <KpiCards />

      {session?.modules.includes("restaurante") && <RestauranteBlocks />}

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <div className="lg:col-span-2">
          <SalesChart />
        </div>
        <TopProducts />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <SellerRanking />
        <RecentInsights />
        <StockAlerts />
      </div>
    </div>
  );
}
