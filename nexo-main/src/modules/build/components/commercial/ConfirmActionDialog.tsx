import { useEffect, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";

interface Props {
  open: boolean;
  title: string;
  description?: ReactNode;
  confirmLabel: string;
  destructive?: boolean;
  /** Shows an optional text field (comment / reason) and passes it to onConfirm. */
  textLabel?: string;
  pending?: boolean;
  onConfirm: (text: string | undefined) => void;
  onClose: () => void;
}

/** Confirmation for irreversible commercial actions, with an optional comment / reason. */
export function ConfirmActionDialog({
  open, title, description, confirmLabel, destructive, textLabel, pending, onConfirm, onClose,
}: Props) {
  const [text, setText] = useState("");
  useEffect(() => { if (open) setText(""); }, [open]);

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          {description && <DialogDescription asChild><div className="space-y-1 text-sm text-muted-foreground">{description}</div></DialogDescription>}
        </DialogHeader>
        {textLabel && (
          <div className="space-y-1">
            <Label htmlFor="confirm-action-text" className="text-xs">{textLabel}</Label>
            <Textarea id="confirm-action-text" value={text} onChange={(e) => setText(e.target.value)} rows={3} />
          </div>
        )}
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Voltar</Button>
          <Button variant={destructive ? "destructive" : "default"} disabled={pending}
            onClick={() => onConfirm(text.trim() || undefined)}>
            {confirmLabel}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
