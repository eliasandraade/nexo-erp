import { lazy, Suspense, useState, useMemo } from "react";
import { PageHeader } from "@/components/shared/PageHeader";
import { Skeleton } from "@/components/ui/skeleton";
import { KpiCards } from "@/modules/dashboard/components/KpiCards";
import { TopProducts } from "@/modules/dashboard/components/TopProducts";

// Lazy: recharts + d3 (the ~374KB vendor-charts chunk) is the single biggest
// dependency. Loading it lazily keeps it off the dashboard's first-paint path —
// the KPIs render immediately while the chart streams in behind a skeleton.
const SalesChart = lazy(() =>
  import("@/modules/dashboard/components/SalesChart").then((m) => ({ default: m.SalesChart }))
);
import { SellerRanking } from "@/modules/dashboard/components/SellerRanking";
import { OperationPriorities } from "@/modules/dashboard/components/OperationPriorities";
import { useDashboardSummary } from "../hooks/useDashboardSummary";
import { ErrorState } from "@/components/shared/ErrorState";
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

  const summary = useDashboardSummary();
  const greeting = useMemo(() => getGreeting(), []);
  const firstName = session?.name?.split(" ")[0] ?? "";

  return (
    <div className="space-y-6">
      {!dismissed && <SetupCard onDismiss={dismiss} />}

      <PageHeader
        title={firstName ? `${greeting}, ${firstName}` : greeting}
        description="Vendas, caixa e estoque. Confira o que pede atenção."
      />

      {summary.isError ? <ErrorState title="Não foi possível atualizar a operação" description="Não é possível confirmar vendas, caixa ou estoque agora." onRetry={() => void summary.refetch()} /> : <>
      <OperationPriorities />
      <KpiCards />

      {session?.modules.includes("restaurante") && <RestauranteBlocks />}

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <div className="lg:col-span-2">
          <Suspense fallback={<Skeleton className="h-[360px] rounded-md" />}>
            <SalesChart />
          </Suspense>
        </div>
        <TopProducts />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <SellerRanking />

        <StockAlerts />
      </div>
      </>}
    </div>
  );
}
