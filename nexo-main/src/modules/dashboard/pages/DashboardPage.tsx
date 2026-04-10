import { PageHeader } from "@/components/shared/PageHeader";
import { KpiCards } from "@/modules/dashboard/components/KpiCards";
import { SalesChart } from "@/modules/dashboard/components/SalesChart";
import { TopProducts } from "@/modules/dashboard/components/TopProducts";
import { SellerRanking } from "@/modules/dashboard/components/SellerRanking";
import { RecentInsights } from "@/modules/dashboard/components/RecentInsights";
import { StockAlerts } from "@/modules/dashboard/components/StockAlerts";

export default function DashboardPage() {
  return (
    <div className="space-y-6">
      <PageHeader
        title="Dashboard"
        description="Visão geral do seu negócio hoje"
      />
      <KpiCards />
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
