import { useEffect, useState } from "react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";

const REASON_MIN = 10;
const REASON_MAX = 300;

/** The reason goes to the audit log; the server takes 10 to 300 characters (trimmed). */
function isValidImpersonationReason(reason: string): boolean {
  const length = reason.trim().length;
  return length >= REASON_MIN && length <= REASON_MAX;
}

interface ImpersonationReasonDialogProps {
  open: boolean;
  tenantName: string;
  /** Name of the user the administrator will act as, when known. */
  targetName?: string;
  pending?: boolean;
  /**
   * Called synchronously from the click (the caller opens the new tab right away, before any await, so the
   * browser does not block it as a pop-up).
   */
  onConfirm: (reason: string) => void;
  onCancel: () => void;
}

export function ImpersonationReasonDialog({
  open, tenantName, targetName, pending = false, onConfirm, onCancel,
}: Readonly<ImpersonationReasonDialogProps>) {
  const [reason, setReason] = useState("");
  const [touched, setTouched] = useState(false);

  useEffect(() => {
    if (open) { setReason(""); setTouched(false); }
  }, [open]);

  const length = reason.trim().length;
  const valid = isValidImpersonationReason(reason);
  const showError = touched && !valid;

  return (
    <Dialog open={open} onOpenChange={v => { if (!v) onCancel(); }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Entrar como cliente</DialogTitle>
          <DialogDescription>
            {`Você vai acessar a conta de "${tenantName}"${targetName ? ` como ${targetName} (Diretoria)` : ""} por até 30 minutos, `}
            em uma nova aba. O acesso e o motivo ficam registrados na auditoria.
          </DialogDescription>
        </DialogHeader>
        <form
          id="impersonation-reason-form"
          onSubmit={e => {
            e.preventDefault();
            setTouched(true);
            if (valid && !pending) onConfirm(reason.trim());
          }}
          className="space-y-1.5"
        >
          <Label htmlFor="impersonation-reason">Motivo do acesso</Label>
          <Textarea
            id="impersonation-reason"
            rows={3}
            maxLength={REASON_MAX}
            value={reason}
            onChange={e => setReason(e.target.value)}
            onBlur={() => setTouched(true)}
            aria-invalid={showError}
            aria-describedby="impersonation-reason-help"
            placeholder="Ex.: chamado #123 — cliente não consegue emitir a nota fiscal"
          />
          <div id="impersonation-reason-help" className="flex justify-between gap-2 text-xs">
            <span className={showError ? "text-destructive" : "text-muted-foreground"}>
              {showError ? `Escreva de ${REASON_MIN} a ${REASON_MAX} caracteres.` : `Mínimo de ${REASON_MIN} caracteres.`}
            </span>
            <span className="tabular-nums text-muted-foreground" aria-live="polite">{length}/{REASON_MAX}</span>
          </div>
        </form>
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={onCancel}>Cancelar</Button>
          <Button type="submit" form="impersonation-reason-form" disabled={!valid || pending}>
            {pending ? "Entrando…" : "Entrar como cliente"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
