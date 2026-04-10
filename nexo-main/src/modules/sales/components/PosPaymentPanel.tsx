import { useState, useEffect } from "react";
import { CreditCard, QrCode, Banknote, CheckCircle2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { cn } from "@/lib/utils";
import type { PaymentMethod, PaymentEntry } from "../types";
import { paymentMethodLabels } from "../types";
import { formatCurrency } from "@/lib/formatters";

interface PosPaymentPanelProps {
  total: number;
  hasOpenSession: boolean;
  cartEmpty: boolean;
  onFinalize: (payments: PaymentEntry[]) => void;
  isLoading?: boolean;
}

const paymentOptions: { method: PaymentMethod; icon: React.ElementType }[] = [
  { method: "cash", icon: Banknote },
  { method: "pix", icon: QrCode },
  { method: "card", icon: CreditCard },
];

export function PosPaymentPanel({
  total,
  hasOpenSession,
  cartEmpty,
  onFinalize,
  isLoading,
}: PosPaymentPanelProps) {
  const [method, setMethod] = useState<PaymentMethod>("cash");
  const [amountInput, setAmountInput] = useState("");

  // Auto-fill amount when total or method changes
  useEffect(() => {
    setAmountInput(total > 0 ? total.toFixed(2).replace(".", ",") : "");
  }, [total, method]);

  const parsedAmount = parseFloat(amountInput.replace(",", ".")) || 0;
  const change = method === "cash" ? Math.max(0, parsedAmount - total) : 0;
  const isSufficient = parsedAmount >= total - 0.001;

  const canFinalize =
    hasOpenSession && !cartEmpty && total > 0 && isSufficient && !isLoading;

  function handleFinalize() {
    if (!canFinalize) return;
    const payments: PaymentEntry[] = [{ method, amount: parsedAmount }];
    onFinalize(payments);
  }

  function handleKeyDown(e: React.KeyboardEvent) {
    if (e.key === "Enter" && canFinalize) {
      handleFinalize();
    }
  }

  return (
    <div className="space-y-4">
      {/* Payment method selector */}
      <div className="space-y-1.5">
        <Label className="text-xs">Forma de pagamento</Label>
        <div className="grid grid-cols-3 gap-2">
          {paymentOptions.map(({ method: m, icon: Icon }) => (
            <button
              key={m}
              type="button"
              onClick={() => setMethod(m)}
              className={cn(
                "flex flex-col items-center gap-1 rounded-lg border py-3 px-2 text-xs font-medium transition-colors",
                method === m
                  ? "border-primary bg-primary/10 text-primary"
                  : "border-border bg-background text-muted-foreground hover:bg-accent hover:text-foreground"
              )}
            >
              <Icon className="h-4 w-4" />
              {paymentMethodLabels[m]}
            </button>
          ))}
        </div>
      </div>

      {/* Amount input */}
      <div className="space-y-1.5">
        <Label htmlFor="pay-amount" className="text-xs">
          Valor recebido (R$)
        </Label>
        <Input
          id="pay-amount"
          value={amountInput}
          onChange={(e) => setAmountInput(e.target.value)}
          onKeyDown={handleKeyDown}
          inputMode="decimal"
          className="text-lg font-semibold h-10"
          placeholder="0,00"
        />
      </div>

      {/* Change (only for cash) */}
      {method === "cash" && parsedAmount > 0 && (
        <div className="flex justify-between items-center rounded-lg bg-muted px-3 py-2 text-sm">
          <span className="text-muted-foreground">Troco</span>
          <span className="font-bold text-foreground tabular-nums">
            {formatCurrency(change)}
          </span>
        </div>
      )}

      {/* Insufficient payment warning */}
      {!isSufficient && parsedAmount > 0 && total > 0 && (
        <p className="text-xs text-red-500">
          Valor insuficiente. Faltam {formatCurrency(total - parsedAmount)}.
        </p>
      )}

      {/* No open session warning */}
      {!hasOpenSession && (
        <p className="text-xs text-amber-600 dark:text-amber-400">
          Abra o caixa antes de finalizar uma venda.
        </p>
      )}

      {/* Finalize button */}
      <Button
        className="w-full h-12 text-base font-semibold"
        disabled={!canFinalize}
        onClick={handleFinalize}
      >
        {isLoading ? (
          "Processando..."
        ) : (
          <>
            <CheckCircle2 className="h-5 w-5 mr-2" />
            Finalizar venda
          </>
        )}
      </Button>
    </div>
  );
}
