import { useEffect, useState } from "react";
import { toast } from "sonner";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { ApiError } from "@/services/api-client";
import type { CreatePaymentRequest, SvcPaymentMethod } from "../api/service.api";
import { useCreatePayment } from "../hooks/usePayments";
import { PAYMENT_METHODS, PAYMENT_METHOD_LABELS } from "../lib/payment";

interface PaymentDialogProps {
  open: boolean;
  onClose: () => void;
  /** The payment target — exactly one of order / customer-package (enforced by the backend). */
  target: { kind: "order" | "customer-package"; id: string };
  /** Pre-fills the amount with the remaining balance. */
  suggestedAmount?: number;
}

function pad(n: number) { return String(n).padStart(2, "0"); }
function todayInput() {
  const d = new Date();
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

export function PaymentDialog({ open, onClose, target, suggestedAmount }: PaymentDialogProps) {
  const create = useCreatePayment();

  const [amount, setAmount] = useState("");
  const [method, setMethod] = useState<SvcPaymentMethod>("Pix");
  const [date, setDate] = useState(todayInput());
  const [reference, setReference] = useState("");
  const [notes, setNotes] = useState("");

  useEffect(() => {
    if (!open) return;
    setAmount(suggestedAmount && suggestedAmount > 0 ? suggestedAmount.toFixed(2) : "");
    setMethod("Pix");
    setDate(todayInput());
    setReference("");
    setNotes("");
  }, [open, suggestedAmount]);

  const handleSave = async () => {
    const value = Number(amount);
    if (!Number.isFinite(value) || value <= 0) { toast.error("Informe um valor maior que zero."); return; }

    const now = new Date();
    const paidAt = new Date(`${date}T${pad(now.getHours())}:${pad(now.getMinutes())}`).toISOString();

    const body: CreatePaymentRequest = {
      amount: value,
      method,
      paidAt,
      orderId: target.kind === "order" ? target.id : null,
      customerPackageId: target.kind === "customer-package" ? target.id : null,
      externalReference: reference.trim() || null,
      notes: notes.trim() || null,
    };

    try {
      await create.mutateAsync(body);
      toast.success("Pagamento registrado.");
      onClose();
    } catch (e) {
      toast.error(e instanceof ApiError ? e.message : "Não foi possível registrar o pagamento.");
    }
  };

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v && !create.isPending) onClose(); }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Registrar pagamento</DialogTitle>
        </DialogHeader>

        <div className="space-y-3 py-1">
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label htmlFor="pay-amount">Valor (R$) *</Label>
              <Input id="pay-amount" type="number" min={0} step="0.01" value={amount}
                onChange={(e) => setAmount(e.target.value)} disabled={create.isPending} autoFocus />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="pay-date">Data *</Label>
              <Input id="pay-date" type="date" value={date} onChange={(e) => setDate(e.target.value)} disabled={create.isPending} />
            </div>
          </div>

          <div className="space-y-1.5">
            <Label>Forma de pagamento</Label>
            <Select value={method} onValueChange={(v) => setMethod(v as SvcPaymentMethod)} disabled={create.isPending}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                {PAYMENT_METHODS.map((m) => <SelectItem key={m} value={m}>{PAYMENT_METHOD_LABELS[m]}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="pay-ref">Referência (opcional)</Label>
            <Input id="pay-ref" value={reference} onChange={(e) => setReference(e.target.value)}
              maxLength={200} disabled={create.isPending} placeholder="Nº da transação, comprovante..." />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="pay-notes">Observações</Label>
            <Textarea id="pay-notes" value={notes} rows={2} onChange={(e) => setNotes(e.target.value)}
              maxLength={2000} disabled={create.isPending} />
          </div>
        </div>

        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onClose} disabled={create.isPending}>Cancelar</Button>
          <Button onClick={handleSave} disabled={create.isPending}>
            {create.isPending ? "Registrando..." : "Registrar"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
