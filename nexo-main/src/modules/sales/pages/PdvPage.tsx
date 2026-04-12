import { useState } from "react";
import { Link } from "react-router-dom";
import { AlertTriangle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { toast } from "sonner";
import { useOpenSession } from "@/modules/cash/hooks/use-cash";
import { usePosCart } from "../hooks/usePosCart";
import { useCompleteSale } from "../hooks/use-pos-sale";
import { PosProductSearch } from "../components/PosProductSearch";
import { PosCartTable } from "../components/PosCartTable";
import { PosTotals } from "../components/PosTotals";
import { PosDiscountInput } from "../components/PosDiscountInput";
import { PosPaymentPanel } from "../components/PosPaymentPanel";
import { PosSaleSuccessModal } from "../components/PosSaleSuccessModal";
import type { CompletedSale, PaymentEntry, ProductSearchResult } from "../types";
import type { SaleDto } from "../api/sales.api";

/** Maps a confirmed SaleDto + cart snapshot into the CompletedSale shape
 *  that PosSaleSuccessModal expects — without touching the modal's contract. */
function toCompletedSale(
  dto: SaleDto,
  cartItems: ReturnType<typeof usePosCart>["items"],
  payments: PaymentEntry[],
  subtotal: number,
  discountTotal: number,
): CompletedSale {
  const cashPayment = payments.find((p) => p.method === "cash");
  const change      = cashPayment ? Math.max(0, cashPayment.amount - dto.total) : 0;

  return {
    id:           `#${dto.number}`,
    timestamp:    dto.confirmedAt ?? dto.createdAt,
    operator:     dto.soldByName,
    status:       "completed",
    items:        cartItems,
    subtotal,
    discountTotal,
    total:        dto.total,
    payments,
    change,
  };
}

export default function PdvPage() {
  const cart        = usePosCart();
  const completeSale = useCompleteSale();
  const [completedSale, setCompletedSale] = useState<CompletedSale | null>(null);

  // Real cash session from the backend (same query as CaixaPage)
  const { data: cashSession } = useOpenSession();
  const hasOpenSession = cashSession?.status === "Open";

  function handleAddProduct(product: ProductSearchResult) {
    cart.addItem(product);
  }

  function handleFinalize(payments: PaymentEntry[]) {
    if (!cashSession?.id) return;

    // Snapshot cart before clearing so the success modal can display it
    const snapshotItems       = [...cart.items];
    const snapshotSubtotal    = cart.subtotal;
    const snapshotDiscountTotal = cart.discountTotal;

    completeSale.mutate(
      {
        items:          snapshotItems,
        payments,
        discountAmount: snapshotDiscountTotal,
        cashSessionId:  cashSession.id,
      },
      {
        onSuccess: (dto) => {
          setCompletedSale(
            toCompletedSale(dto, snapshotItems, payments, snapshotSubtotal, snapshotDiscountTotal)
          );
        },
        onError: (err: Error) => {
          toast.error(err.message ?? "Erro ao finalizar venda. Tente novamente.");
        },
      }
    );
  }

  function handleNewSale() {
    cart.clearCart();
    setCompletedSale(null);
  }

  // Guard: cash session must be open
  if (!hasOpenSession) {
    return (
      <div className="h-full flex items-center justify-center">
        <div className="text-center space-y-4 max-w-sm">
          <AlertTriangle className="h-12 w-12 text-amber-500 mx-auto" />
          <div>
            <h2 className="text-lg font-semibold">Caixa não está aberto</h2>
            <p className="text-sm text-muted-foreground mt-1">
              Abra o caixa antes de registrar vendas.
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
              onClick={() => {
                if (window.confirm("Limpar todos os itens do carrinho?")) {
                  cart.clearCart();
                }
              }}
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
            onFinalize={handleFinalize}
            isLoading={completeSale.isPending}
          />
        </div>
      </div>

      <PosSaleSuccessModal sale={completedSale} onNewSale={handleNewSale} />
    </div>
  );
}
