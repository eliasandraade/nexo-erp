import { useEffect, useState } from "react";
import { toast } from "sonner";
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
import type { SvcAppointmentDto, SvcAppointmentStatus } from "../api/service.api";
import { useChangeAppointmentStatus } from "../hooks/useAppointments";
import { APPOINTMENT_STATUS_LABELS } from "../lib/appointment-status";

interface AppointmentStatusDialogProps {
  open: boolean;
  onClose: () => void;
  appointment: SvcAppointmentDto | null;
  /** The target status to apply (e.g. Cancelled, NoShow). */
  target: SvcAppointmentStatus | null;
}

/** Confirms a status change that captures a reason (cancellation / no-show). */
export function AppointmentStatusDialog({ open, onClose, appointment, target }: AppointmentStatusDialogProps) {
  const change = useChangeAppointmentStatus();
  const [reason, setReason] = useState("");

  useEffect(() => { if (open) setReason(""); }, [open]);

  if (!target) return null;
  const verb = target === "Cancelled" ? "Cancelar" : "Registrar não comparecimento";

  const handleConfirm = async () => {
    if (!appointment) return;
    try {
      await change.mutateAsync({ id: appointment.id, status: target, reason: reason.trim() || null });
      toast.success(`Status alterado para "${APPOINTMENT_STATUS_LABELS[target]}".`);
      onClose();
    } catch {
      toast.error("Não foi possível alterar o status.");
    }
  };

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v && !change.isPending) onClose(); }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>{verb}</DialogTitle>
        </DialogHeader>

        <div className="space-y-2 py-1">
          <Label htmlFor="status-reason">Motivo (opcional)</Label>
          <Textarea
            id="status-reason"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            rows={3}
            maxLength={500}
            placeholder="Descreva o motivo..."
            disabled={change.isPending}
          />
        </div>

        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onClose} disabled={change.isPending}>Voltar</Button>
          <Button variant="destructive" onClick={handleConfirm} disabled={change.isPending}>
            {change.isPending ? "Aplicando..." : "Confirmar"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
