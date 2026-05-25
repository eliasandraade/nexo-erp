import { useEffect } from "react";
import { CheckCircle2, Circle, ChevronRight, X } from "lucide-react";
import { Link } from "react-router-dom";
import { useProducts } from "@/modules/products/hooks/use-products";
import { useOpenSession } from "@/modules/cash/hooks/use-cash";
import { useDashboardSummary } from "@/modules/dashboard/hooks/useDashboardSummary";

interface Props {
  onDismiss: () => void;
}

interface Step {
  id:    string;
  label: string;
  hint:  string;
  href:  string;
  done:  boolean;
}

export function SetupCard({ onDismiss }: Props) {
  const { data: products = [],  isLoading: loadingProducts } = useProducts();
  const { data: cashSession,    isLoading: loadingCash }     = useOpenSession();
  const { data: summary,        isLoading: loadingSummary }  = useDashboardSummary();

  const isLoading = loadingProducts || loadingCash || loadingSummary;

  const hasProduct  = products.length > 0;
  const hasCash     = cashSession?.status === "Open" || (summary?.totalSales ?? 0) > 0;
  const hasSale     = (summary?.totalSales ?? 0) > 0;
  const allDone     = hasProduct && hasCash && hasSale;

  useEffect(() => {
    if (!isLoading && allDone) onDismiss();
  }, [isLoading, allDone, onDismiss]);

  if (!isLoading && allDone) return null;

  const steps: Step[] = [
    {
      id:    "product",
      label: "Cadastre um produto",
      hint:  "Adicione pelo menos um item para vender",
      href:  "/produtos/novo",
      done:  hasProduct,
    },
    {
      id:    "cash",
      label: "Abra o caixa",
      hint:  "Ative uma sessão de caixa antes de vender",
      href:  "/caixa",
      done:  hasCash,
    },
    {
      id:    "sale",
      label: "Faça a primeira venda",
      hint:  "Registre uma venda pelo PDV",
      href:  "/pdv",
      done:  hasSale,
    },
  ];

  const completedCount = steps.filter((s) => s.done).length;

  return (
    <div className="bg-card border border-border rounded-xl p-5 animate-fade-in">
      <div className="flex items-start justify-between gap-4 mb-4">
        <div>
          <p className="text-[13px] font-semibold text-foreground">Configure o Orken</p>
          <p className="text-[12px] text-muted-foreground mt-0.5">
            {completedCount} de {steps.length} etapas concluídas
          </p>
        </div>
        <button
          type="button"
          onClick={onDismiss}
          className="text-muted-foreground hover:text-foreground transition-colors mt-0.5 shrink-0"
          aria-label="Fechar guia de configuração"
        >
          <X className="h-4 w-4" />
        </button>
      </div>

      {/* Progress bar */}
      <div className="w-full h-1 bg-muted rounded-full mb-4 overflow-hidden">
        <div
          className="h-full bg-primary rounded-full transition-all duration-500"
          style={{ width: `${(completedCount / steps.length) * 100}%` }}
        />
      </div>

      <div className="flex flex-col sm:flex-row gap-2">
        {steps.map((step) => (
          step.done ? (
            <div
              key={step.id}
              className="flex items-center gap-2 flex-1 px-3 py-2.5 rounded-md bg-success/5 border border-success/20"
            >
              <CheckCircle2 className="h-4 w-4 text-success shrink-0" />
              <div className="min-w-0">
                <p className="text-xs font-medium text-foreground line-through text-muted-foreground">
                  {step.label}
                </p>
              </div>
            </div>
          ) : (
            <Link
              key={step.id}
              to={step.href}
              className="flex items-center gap-2 flex-1 px-3 py-2.5 rounded-md bg-muted/50 hover:bg-muted border border-border hover:border-primary/30 transition-colors group"
            >
              <Circle className="h-4 w-4 text-muted-foreground shrink-0 group-hover:text-primary transition-colors" />
              <div className="flex-1 min-w-0">
                <p className="text-xs font-medium text-foreground">{step.label}</p>
                <p className="text-[10px] text-muted-foreground leading-relaxed hidden sm:block">
                  {step.hint}
                </p>
              </div>
              <ChevronRight className="h-3.5 w-3.5 text-muted-foreground group-hover:text-primary transition-colors shrink-0" />
            </Link>
          )
        ))}
      </div>
    </div>
  );
}
