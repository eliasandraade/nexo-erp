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
import { Textarea } from "@/components/ui/textarea";

interface CashOpenModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onConfirm: (openingBalance: number, notes?: string) => void;
  isLoading?: boolean;
}

export function CashOpenModal({
  open,
  onOpenChange,
  onConfirm,
  isLoading,
}: CashOpenModalProps) {
  const [openingBalance, setOpeningBalance] = useState("");
  const [notes, setNotes] = useState("");

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const amount = parseFloat(openingBalance.replace(",", "."));
    if (isNaN(amount) || amount < 0) return;
    onConfirm(amount, notes.trim() || undefined);
  }

  function handleClose() {
    if (!isLoading) {
      setOpeningBalance("");
      setNotes("");
      onOpenChange(false);
    }
  }

  const parsed = parseFloat(openingBalance.replace(",", "."));
  const isValid = !isNaN(parsed) && parsed >= 0;

  return (
    <Dialog open={open} onOpenChange={handleClose}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Abrir caixa</DialogTitle>
        </DialogHeader>
        <form onSubmit={handleSubmit} className="space-y-4 pt-2">
          <div className="space-y-1.5">
            <Label htmlFor="openingBalance">Valor de abertura (R$)</Label>
            <Input
              id="openingBalance"
              placeholder="0,00"
              value={openingBalance}
              onChange={(e) => setOpeningBalance(e.target.value)}
              inputMode="decimal"
              autoFocus
            />
            <p className="text-xs text-muted-foreground">
              Informe o troco disponível no início do turno.
            </p>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="open-notes">Observações (opcional)</Label>
            <Textarea
              id="open-notes"
              placeholder="Observações sobre a abertura..."
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={2}
            />
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
