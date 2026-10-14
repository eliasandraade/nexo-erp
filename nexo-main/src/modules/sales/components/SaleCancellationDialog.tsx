import { useState } from "react";
import { AlertTriangle } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Alert, AlertDescription } from "@/components/ui/alert";

export interface CancellationConfirmPayload {
  reason: string;
}

interface SaleCancellationDialogProps {
  open: boolean;
  onClose: () => void;
  onConfirm: (payload: CancellationConfirmPayload) => void;
  mode: "full" | "item";
  itemDescription?: string;
  isLoading?: boolean;
}

/**
 * Cancellation of a sale that is not paid yet (a prazo still open). The screen is management-only
 * and the API enforces the role, so the dialog asks for the reason — kept with the sale and in the
 * audit trail. A paid sale is undone with a devolução, not here.
 */
export function SaleCancellationDialog({
  open,
  onClose,
  onConfirm,
  mode,
  itemDescription,
  isLoading = false,
}: SaleCancellationDialogProps) {
  const [reason, setReason] = useState("");
  const [error, setError] = useState("");

  function handleClose() {
    setReason("");
    setError("");
    onClose();
  }

  function handleConfirm() {
    if (!reason.trim()) {
      setError("Informe o motivo do cancelamento.");
      return;
    }
    onConfirm({ reason: reason.trim() });
    setReason("");
    setError("");
  }

  const title = mode === "full" ? "Cancelar venda" : "Cancelar item";
  const warningText =
    mode === "full"
      ? "A venda será cancelada: o estoque volta, o dinheiro sai do caixa e os valores a prazo são cancelados."
      : `O item "${itemDescription}" será cancelado. O estoque do produto será restituído.`;

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v) handleClose(); }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <AlertTriangle className="h-5 w-5 text-destructive" />
            {title}
          </DialogTitle>
        </DialogHeader>

        <div className="space-y-4 py-1">
          <Alert variant="destructive">
            <AlertDescription className="text-sm">{warningText}</AlertDescription>
          </Alert>

          <div className="space-y-1.5">
            <Label htmlFor="cancel-reason">Motivo do cancelamento</Label>
            <Textarea
              id="cancel-reason"
              placeholder="Descreva o motivo..."
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              rows={3}
              maxLength={500}
              disabled={isLoading}
            />
            {error && <p className="text-xs font-medium text-destructive">{error}</p>}
          </div>
        </div>

        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={handleClose} disabled={isLoading}>
            Voltar
          </Button>
          <Button variant="destructive" onClick={handleConfirm} disabled={isLoading}>
            {isLoading ? "Cancelando..." : "Confirmar cancelamento"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
