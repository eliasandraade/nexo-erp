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
import type { AddCashMovementRequest } from "../types";

interface CashMovementModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  defaultType?: AddCashMovementRequest["movementType"];
  onConfirm: (req: AddCashMovementRequest) => void;
  isLoading?: boolean;
}

const typeOptions: { value: AddCashMovementRequest["movementType"]; label: string }[] = [
  { value: "Deposit",    label: "Suprimento" },
  { value: "Withdrawal", label: "Sangria" },
];

const titleMap: Record<AddCashMovementRequest["movementType"], string> = {
  Deposit:    "Registrar suprimento",
  Withdrawal: "Registrar sangria",
};

export function CashMovementModal({
  open,
  onOpenChange,
  defaultType = "Withdrawal",
  onConfirm,
  isLoading,
}: CashMovementModalProps) {
  const [type, setType] = useState<AddCashMovementRequest["movementType"]>(defaultType);
  const [amount, setAmount] = useState("");
  const [description, setDescription] = useState("");
  const [notes, setNotes] = useState("");

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const parsed = parseFloat(amount.replace(",", "."));
    if (isNaN(parsed) || parsed <= 0 || !description.trim()) return;
    const desc = notes.trim()
      ? `${description.trim()} — ${notes.trim()}`
      : description.trim();
    onConfirm({ movementType: type, amount: parsed, description: desc });
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

  const parsed = parseFloat(amount.replace(",", "."));
  const isValid = !isNaN(parsed) && parsed > 0 && description.trim().length > 0;

  return (
    <Dialog open={open} onOpenChange={handleClose}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{titleMap[type]}</DialogTitle>
        </DialogHeader>
        <form onSubmit={handleSubmit} className="space-y-4 pt-2">
          <div className="space-y-1.5">
            <Label>Tipo</Label>
            <Select
              value={type}
              onValueChange={(v) => setType(v as AddCashMovementRequest["movementType"])}
            >
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
