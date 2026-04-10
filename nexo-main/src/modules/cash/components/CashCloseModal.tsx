import { useState } from "react";
import { AlertTriangle, CheckCircle2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Separator } from "@/components/ui/separator";
import { formatCurrency } from "@/lib/formatters";

interface CashCloseModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  expectedBalance: number;
  onConfirm: (countedAmount: number, notes: string) => void;
  isLoading?: boolean;
}

export function CashCloseModal({
  open,
  onOpenChange,
  expectedBalance,
  onConfirm,
  isLoading,
}: CashCloseModalProps) {
  const [countedAmount, setCountedAmount] = useState("");
  const [notes, setNotes] = useState("");

  const parsed = parseFloat(countedAmount.replace(",", "."));
  const isValidAmount = !isNaN(parsed) && parsed >= 0;
  const divergence = isValidAmount ? parsed - expectedBalance : null;

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!isValidAmount) return;
    onConfirm(parsed, notes.trim());
  }

  function handleClose() {
    if (!isLoading) {
      setCountedAmount("");
      setNotes("");
      onOpenChange(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={handleClose}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Fechar caixa</DialogTitle>
        </DialogHeader>
        <form onSubmit={handleSubmit} className="space-y-4 pt-2">
          <div className="rounded-lg bg-muted/50 border border-border p-4 space-y-2 text-sm">
            <div className="flex justify-between">
              <span className="text-muted-foreground">Saldo esperado</span>
              <span className="font-semibold">{formatCurrency(expectedBalance)}</span>
            </div>
            {isValidAmount && divergence !== null && (
              <>
                <div className="flex justify-between">
                  <span className="text-muted-foreground">Contagem física</span>
                  <span className="font-semibold">{formatCurrency(parsed)}</span>
                </div>
                <Separator />
                <div className="flex justify-between items-center">
                  <span className="text-muted-foreground">Divergência</span>
                  <div className="flex items-center gap-1.5">
                    {divergence === 0 ? (
                      <CheckCircle2 className="h-4 w-4 text-green-600" />
                    ) : (
                      <AlertTriangle className="h-4 w-4 text-red-500" />
                    )}
                    <span
                      className={`font-bold ${
                        divergence === 0
                          ? "text-green-600 dark:text-green-400"
                          : "text-red-600 dark:text-red-400"
                      }`}
                    >
                      {divergence >= 0 ? "+" : ""}
                      {formatCurrency(divergence)}
                    </span>
                  </div>
                </div>
              </>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="counted">Valor contado (R$)</Label>
            <Input
              id="counted"
              placeholder="0,00"
              value={countedAmount}
              onChange={(e) => setCountedAmount(e.target.value)}
              inputMode="decimal"
              autoFocus
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="close-notes">Observações (opcional)</Label>
            <Textarea
              id="close-notes"
              placeholder="Observações sobre o fechamento..."
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={2}
            />
          </div>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={handleClose} disabled={isLoading}>
              Cancelar
            </Button>
            <Button
              type="submit"
              variant={divergence !== null && divergence !== 0 ? "destructive" : "default"}
              disabled={!isValidAmount || isLoading}
            >
              {isLoading
                ? "Fechando..."
                : divergence !== null && divergence !== 0
                ? "Fechar com divergência"
                : "Confirmar fechamento"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
