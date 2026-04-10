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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import type { CashMovementInput } from "../types";

interface CashMovementModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  defaultType?: "reinforcement" | "withdrawal" | "adjustment";
  onConfirm: (input: CashMovementInput) => void;
  isLoading?: boolean;
}

const typeOptions: { value: CashMovementInput["type"]; label: string }[] = [
  { value: "reinforcement", label: "Suprimento" },
  { value: "withdrawal", label: "Sangria" },
  { value: "adjustment", label: "Ajuste" },
];

export function CashMovementModal({
  open,
  onOpenChange,
  defaultType = "withdrawal",
  onConfirm,
  isLoading,
}: CashMovementModalProps) {
  const [type, setType] = useState<CashMovementInput["type"]>(defaultType);
  const [amount, setAmount] = useState("");
  const [description, setDescription] = useState("");
  const [notes, setNotes] = useState("");

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const parsedAmount = parseFloat(amount.replace(",", "."));
    if (isNaN(parsedAmount) || parsedAmount <= 0 || !description.trim()) return;
    onConfirm({ type, amount: parsedAmount, description: description.trim(), notes: notes.trim() || undefined });
  }

  function handleClose() {
    if (!isLoading) {
      setAmount("");
      setDescription("");
      setNotes("");
      setType(defaultType);
      onOpenChange(false);
    }
  }

  const parsedAmount = parseFloat(amount.replace(",", "."));
  const isValid = !isNaN(parsedAmount) && parsedAmount > 0 && description.trim().length > 0;

  const titleMap: Record<CashMovementInput["type"], string> = {
    reinforcement: "Registrar suprimento",
    withdrawal: "Registrar sangria",
    adjustment: "Registrar ajuste",
  };

  return (
    <Dialog open={open} onOpenChange={handleClose}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{titleMap[type]}</DialogTitle>
        </DialogHeader>
        <form onSubmit={handleSubmit} className="space-y-4 pt-2">
          <div className="space-y-1.5">
            <Label>Tipo</Label>
            <Select value={type} onValueChange={(v) => setType(v as CashMovementInput["type"])}>
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {typeOptions.map((o) => (
                  <SelectItem key={o.value} value={o.value}>
                    {o.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="mov-amount">Valor (R$)</Label>
            <Input
              id="mov-amount"
              placeholder="0,00"
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
              inputMode="decimal"
              autoFocus
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="mov-desc">Descrição</Label>
            <Input
              id="mov-desc"
              placeholder="Motivo da movimentação"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="mov-notes">Observações (opcional)</Label>
            <Textarea
              id="mov-notes"
              placeholder="Informações adicionais..."
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
              {isLoading ? "Registrando..." : "Confirmar"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
