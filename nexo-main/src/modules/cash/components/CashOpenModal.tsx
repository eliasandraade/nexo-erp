import { useState } from "react";
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

interface CashOpenModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onConfirm: (openingAmount: number, operator: string) => void;
  isLoading?: boolean;
}

export function CashOpenModal({
  open,
  onOpenChange,
  onConfirm,
  isLoading,
}: CashOpenModalProps) {
  const [openingAmount, setOpeningAmount] = useState("");
  const [operator, setOperator] = useState("");

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const amount = parseFloat(openingAmount.replace(",", "."));
    if (isNaN(amount) || amount < 0 || !operator.trim()) return;
    onConfirm(amount, operator.trim());
  }

  function handleClose() {
    if (!isLoading) {
      setOpeningAmount("");
      setOperator("");
      onOpenChange(false);
    }
  }

  const isValid =
    operator.trim().length > 0 &&
    !isNaN(parseFloat(openingAmount.replace(",", "."))) &&
    parseFloat(openingAmount.replace(",", ".")) >= 0;

  return (
    <Dialog open={open} onOpenChange={handleClose}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Abrir caixa</DialogTitle>
        </DialogHeader>
        <form onSubmit={handleSubmit} className="space-y-4 pt-2">
          <div className="space-y-1.5">
            <Label htmlFor="operator">Operador</Label>
            <Input
              id="operator"
              placeholder="Nome do operador"
              value={operator}
              onChange={(e) => setOperator(e.target.value)}
              autoFocus
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="openingAmount">Valor de abertura (R$)</Label>
            <Input
              id="openingAmount"
              placeholder="0,00"
              value={openingAmount}
              onChange={(e) => setOpeningAmount(e.target.value)}
              inputMode="decimal"
            />
            <p className="text-xs text-muted-foreground">
              Informe o troco disponível no início do turno.
            </p>
          </div>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={handleClose} disabled={isLoading}>
              Cancelar
            </Button>
            <Button type="submit" disabled={!isValid || isLoading}>
              {isLoading ? "Abrindo..." : "Abrir caixa"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
