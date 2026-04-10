import { useState } from "react";
import { Link } from "react-router-dom";
import { AlertTriangle } from "lucide-react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import { toast } from "sonner";
import { cashService } from "@/modules/cash/services/cashService";
import { posService } from "../services/posService";
import { usePosCart } from "../hooks/usePosCart";
import { PosProductSearch } from "../components/PosProductSearch";
import { PosCartTable } from "../components/PosCartTable";
import { PosTotals } from "../components/PosTotals";
import { PosDiscountInput } from "../components/PosDiscountInput";
import { PosPaymentPanel } from "../components/PosPaymentPanel";
import { PosSaleSuccessModal } from "../components/PosSaleSuccessModal";
import type { CompletedSale, PaymentEntry, ProductSearchResult } from "../types";

export default function PdvPage() {
  const cart = usePosCart();
  const queryClient = useQueryClient();
  const [completedSale, setCompletedSale] = useState<CompletedSale | null>(null);

  const { data: cashSession } = useQuery({
    queryKey: ["cash-session"],
    queryFn: () => cashService.getCurrentSession(),
    refetchInterval: 5000,
  });

  const hasOpenSession = cashSession?.status === "open";

  const saleMutation = useMutation({
    mutationFn: (payments: PaymentEntry[]) =>
      posService.completeSale(
        cart.items,
        payments,
        cart.discountTotal,
        cashSession?.operator ?? "Operador"
      ),
    onSuccess: (sale) => {
      setCompletedSale(sale);
      // Invalidate cash session (balance updated) and sales list so
      // /caixa and /vendas reflect the new sale without stale data.
      queryClient.invalidateQueries({ queryKey: ["cash-session"] });
      queryClient.invalidateQueries({ queryKey: ["cash-movements"] });
      queryClient.invalidateQueries({ queryKey: ["sales"] });
      // Dashboard widgets and reports derive from sales — refresh them too.
      queryClient.invalidateQueries({ queryKey: ["dashboard-operational"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard-top-products"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard-seller-ranking"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard-sales-chart"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard-insights"] });
    },
    onError: (err: Error) => {
      toast.error(err.message);
    },
  });

  function handleAddProduct(product: ProductSearchResult) {
    cart.addItem(product);
  }

  function handleNewSale() {
    cart.clearCart();
    setCompletedSale(null);
  }

  // No open cash session guard
  if (!hasOpenSession) {
    return (
      <div className="h-full flex items-center justify-center">
        <div className="text-center space-y-4 max-w-sm">
          <AlertTriangle className="h-12 w-12 text-amber-500 mx-auto" />
          <div>
            <h2 className="text-lg font-semibold">Caixa não está aberto</h2>
            <p className="text-sm text-muted-foreground mt-1">
              É necessário abrir o caixa antes de registrar vendas.
            </p>
          </div>
          <Button asChild>
            <Link to="/caixa">Ir para o Caixa</Link>
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div className="h-full flex gap-0 overflow-hidden">
      {/* Left: product search + cart */}
      <div className="flex flex-col flex-1 min-w-0 border-r border-border">
        {/* Product search */}
        <div className="p-4 border-b border-border">
          <PosProductSearch onAdd={handleAddProduct} />
        </div>

        {/* Cart */}
        <div className="flex-1 overflow-auto p-4">
          <PosCartTable
            items={cart.items}
            onUpdateQuantity={cart.updateQuantity}
            onRemove={cart.removeItem}
          />
        </div>

        {/* Cart footer */}
        {cart.items.length > 0 && (
          <div className="border-t border-border px-4 py-2 flex items-center justify-between text-xs text-muted-foreground">
            <span>{cart.itemCount} item(ns) no carrinho</span>
            <button
              type="button"
              className="text-red-500 hover:underline"
              onClick={cart.clearCart}
            >
              Limpar carrinho
            </button>
          </div>
        )}
      </div>

      {/* Right: totals + payment */}
      <div className="w-80 xl:w-96 flex flex-col gap-4 p-4 overflow-auto shrink-0">
        <div className="space-y-4">
          <PosTotals
            subtotal={cart.subtotal}
            discountTotal={cart.discountTotal}
            discountValue={cart.discountValue}
            discountMode={cart.discountMode}
            total={cart.total}
          />

          <PosDiscountInput
            mode={cart.discountMode}
            value={cart.discountValue}
            onModeChange={cart.setDiscountMode}
            onChange={cart.setDiscountValue}
            subtotal={cart.subtotal}
          />
        </div>

        <div className="border-t border-border pt-4">
          <PosPaymentPanel
            total={cart.total}
            hasOpenSession={hasOpenSession}
            cartEmpty={cart.items.length === 0}
            onFinalize={(payments) => saleMutation.mutate(payments)}
            isLoading={saleMutation.isPending}
          />
        </div>
      </div>

      <PosSaleSuccessModal sale={completedSale} onNewSale={handleNewSale} />
    </div>
  );
}
