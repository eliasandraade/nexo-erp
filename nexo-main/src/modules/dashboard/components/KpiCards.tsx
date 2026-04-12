import { DollarSign, TrendingUp, Receipt, AlertTriangle } from "lucide-react";
import { useQuery } from "@tanstack/react-query";
import { Skeleton } from "@/components/ui/skeleton";
import { useMemo } from "react";
import { listSales } from "@/modules/sales/api/sales.api";
import { useStockItems } from "@/modules/inventory/hooks/use-stock";
import { useProducts } from "@/modules/products/hooks/use-products";
import { deriveStockStatus } from "@/modules/inventory/types";
import { formatCurrency } from "@/lib/formatters";

interface KpiDef {
  label: string;
  value: string;
  sub: string;
  subType: "positive" | "warning";
  icon: React.ElementType;
  iconBg: string;
  iconColor: string;
}

export function KpiCards() {
  const { data: sales = [], isLoading: loadingSales } = useQuery({
    queryKey: ["sales"],
    queryFn:  listSales,
  });

  const { data: stockItems = [], isLoading: loadingStock } = useStockItems();
  const { data: products = [],   isLoading: loadingProducts } = useProducts();

  const isLoading = loadingSales || loadingStock || loadingProducts;

  /** Aggregate sales KPIs from real SaleDto list */
  const operational = useMemo(() => {
    const activeSales    = sales.filter((s) => s.status !== "Cancelled");
    const cancelledCount = sales.filter((s) => s.status === "Cancelled").length;
    const totalRevenue   = activeSales.reduce((acc, s) => acc + s.total, 0);
    const averageTicket  = activeSales.length > 0
      ? Math.round((totalRevenue / activeSales.length) * 100) / 100
      : 0;

    return {
      totalSales:    sales.length,
      totalRevenue:  Math.round(totalRevenue * 100) / 100,
      averageTicket,
      cancelledCount,
    };
  }, [sales]);

  /** Count items below minimum stock using the same enrichment as EstoquePage */
  const alertCount = useMemo(() => {
    return stockItems.filter((s) => {
      const product  = products.find((p) => p.id === s.productId);
      const minStock = product?.minStockQuantity ?? null;
      const status   = deriveStockStatus(s.availableQuantity, minStock);
      return status === "low" || status === "zero";
    }).length;
  }, [stockItems, products]);

  if (isLoading) {
    return (
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} className="h-24 rounded-lg" />
        ))}
      </div>
    );
  }

  const kpis: KpiDef[] = [
    {
      label:     "Faturamento",
      value:     formatCurrency(operational.totalRevenue),
      sub:       `${operational.totalSales} venda(s) no período`,
      subType:   "positive",
      icon:      DollarSign,
      iconBg:    "bg-secondary/10",
      iconColor: "text-secondary",
    },
    {
      label:     "Ticket médio",
      value:     formatCurrency(operational.averageTicket),
      sub:       "por venda ativa",
      subType:   "positive",
      icon:      TrendingUp,
      iconBg:    "bg-success/10",
      iconColor: "text-success",
    },
    {
      label:     "Vendas registradas",
      value:     String(operational.totalSales),
      sub:       `${operational.cancelledCount} cancelada(s)`,
      subType:   operational.cancelledCount > 0 ? "warning" : "positive",
      icon:      Receipt,
      iconBg:    "bg-primary/10",
      iconColor: "text-primary",
    },
    {
      label:     "Itens em alerta",
      value:     String(alertCount),
      sub:       alertCount > 0 ? "requerem atenção" : "estoque normal",
      subType:   alertCount > 0 ? "warning" : "positive",
      icon:      AlertTriangle,
      iconBg:    "bg-warning/10",
      iconColor: "text-warning",
    },
  ];

  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
      {kpis.map((kpi, i) => (
        <div
          key={kpi.label}
          className="bg-card rounded-lg border border-border p-5 shadow-sm animate-fade-in"
          style={{ animationDelay: `${i * 75}ms` }}
        >
          <div className="flex items-start justify-between">
            <div>
              <p className="text-xs font-medium text-muted-foreground uppercase tracking-wide">
                {kpi.label}
              </p>
              <p className="text-2xl font-bold text-foreground mt-1">{kpi.value}</p>
            </div>
            <div className={`w-9 h-9 rounded-lg ${kpi.iconBg} flex items-center justify-center`}>
              <kpi.icon className={`h-4 w-4 ${kpi.iconColor}`} />
            </div>
          </div>
          <p
            className={`text-xs mt-3 font-medium ${
              kpi.subType === "warning" ? "text-warning" : "text-success"
            }`}
          >
            {kpi.sub}
          </p>
        </div>
      ))}
    </div>
  );
}
