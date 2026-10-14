import { useEffect, useMemo, useState } from "react";
import { Undo2 } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { formatCurrency } from "@/lib/formatters";
import type { CreateSaleReturnRequest, SaleDto, SaleReturnDto } from "../api/sales.api";

const REFUND_METHODS = [
  { value: "Cash",     label: "Dinheiro (sai do caixa)" },
  { value: "Pix",      label: "Pix" },
  { value: "Debit",    label: "Estorno no cartão de débito" },
  { value: "Credit",   label: "Estorno no cartão de crédito" },
  { value: "Transfer", label: "Transferência" },
];

interface SaleReturnDialogProps {
  open: boolean;
  sale: SaleDto;
  previousReturns: SaleReturnDto[];
  onClose: () => void;
  onConfirm: (request: CreateSaleReturnRequest) => void;
  isLoading?: boolean;
  error?: string | null;
}

/**
 * Devolução of a paid sale: pick how much of each item comes back, how the money goes back and why.
 * The estimate mirrors the backend (each line's proportional share of what was paid — discount,
 * tax, couvert and service fee included); the backend decides the final value to the cent.
 */
export function SaleReturnDialog({
  open, sale, previousReturns, onClose, onConfirm, isLoading, error,
}: SaleReturnDialogProps) {
  const [quantities, setQuantities] = useState<Record<string, string>>({});
  const [refundMethod, setRefundMethod] = useState("Cash");
  const [reason, setReason] = useState("");
  const [idempotencyKey, setIdempotencyKey] = useState(() => crypto.randomUUID());

  // A fresh key per opening: retries of the same submission reuse it, a new devolução gets a new one.
  useEffect(() => {
    if (open) {
      setQuantities({});
      setReason("");
      setRefundMethod("Cash");
      setIdempotencyKey(crypto.randomUUID());
    }
  }, [open]);

  const returnedByItem = useMemo(() => {
    const map: Record<string, number> = {};
    for (const r of previousReturns)
      for (const i of r.items) map[i.saleItemId] = (map[i.saleItemId] ?? 0) + i.quantity;
    return map;
  }, [previousReturns]);

  const factor = sale.subtotal > 0 ? sale.total / sale.subtotal : 0;
  const lines = sale.items.map((item) => {
    const remaining = Math.max(0, item.quantity - (returnedByItem[item.id] ?? 0));
    const raw = quantities[item.id] ?? "";
    const qty = Number(raw.replace(",", ".")) || 0;
    const valid = qty >= 0 && qty <= remaining;
    const estimate = valid && item.quantity > 0 ? (item.total * factor * qty) / item.quantity : 0;
    return { item, remaining, raw, qty, valid, estimate };
  });
  const selected = lines.filter((l) => l.qty > 0);
  const invalid = lines.some((l) => !l.valid);
  const estimate = Math.round(selected.reduce((s, l) => s + l.estimate, 0) * 100) / 100;
  const canSubmit = selected.length > 0 && !invalid && reason.trim().length > 0 && !isLoading;

  function submit() {
    if (!canSubmit) return;
    onConfirm({
      items: selected.map((l) => ({ saleItemId: l.item.id, quantity: l.qty })),
      refundMethod,
      reason: reason.trim(),
      idempotencyKey,
    });
  }

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v && !isLoading) onClose(); }}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Undo2 className="h-5 w-5" /> Registrar devolução
          </DialogTitle>
          <DialogDescription>
            O estoque volta para os itens devolvidos e o valor é reembolsado pela forma escolhida.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-2">
            {lines.map(({ item, remaining, raw, valid }) => (
              <div key={item.id} className="flex items-center justify-between gap-3 text-sm">
                <div className="min-w-0">
                  <p className="truncate font-medium">{item.productName}</p>
                  <p className="text-xs text-muted-foreground">
                    Disponível para devolver: {remaining.toLocaleString("pt-BR")}
                  </p>
                </div>
                <Input
                  aria-label={`Quantidade devolvida de ${item.productName}`}
                  className="w-24 text-right"
                  inputMode="decimal"
                  placeholder="0"
                  value={raw}
                  disabled={remaining <= 0 || isLoading}
                  aria-invalid={!valid}
                  onChange={(e) => setQuantities((q) => ({ ...q, [item.id]: e.target.value }))}
                />
              </div>
            ))}
            {invalid && <p className="text-xs text-destructive">A quantidade não pode passar do disponível.</p>}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="refund-method">Reembolso</Label>
            <Select value={refundMethod} onValueChange={setRefundMethod} disabled={isLoading}>
              <SelectTrigger id="refund-method"><SelectValue /></SelectTrigger>
              <SelectContent>
                {REFUND_METHODS.map((m) => <SelectItem key={m.value} value={m.value}>{m.label}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="return-reason">Motivo</Label>
            <Textarea
              id="return-reason"
              rows={2}
              maxLength={500}
              placeholder="Ex.: produto com defeito"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              disabled={isLoading}
            />
          </div>

          <div className="flex items-center justify-between rounded-md bg-muted px-3 py-2 text-sm">
            <span className="text-muted-foreground">Valor estimado do reembolso</span>
            <span className="font-semibold tabular-nums">{formatCurrency(estimate)}</span>
          </div>
          {error && <p className="text-sm font-medium text-destructive">{error}</p>}
        </div>

        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onClose} disabled={isLoading}>Voltar</Button>
          <Button onClick={submit} disabled={!canSubmit}>
            {isLoading ? "Registrando..." : "Confirmar devolução"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
