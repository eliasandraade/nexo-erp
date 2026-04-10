import { useState } from "react";
import { AlertTriangle, ShieldCheck } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { userService } from "@/modules/users/services/userService";

export interface CancellationConfirmPayload {
  reason: string;
  authorizedBy: string;
  authorizedByLogin: string;
}

interface SaleCancellationDialogProps {
  open: boolean;
  onClose: () => void;
  onConfirm: (payload: CancellationConfirmPayload) => void;
  mode: "full" | "item";
  itemDescription?: string;
  isLoading?: boolean;
}

export function SaleCancellationDialog({
  open,
  onClose,
  onConfirm,
  mode,
  itemDescription,
  isLoading = false,
}: SaleCancellationDialogProps) {
  const [login, setLogin] = useState("");
  const [password, setPassword] = useState("");
  const [reason, setReason] = useState("");
  const [authError, setAuthError] = useState("");

  function handleClose() {
    setLogin("");
    setPassword("");
    setReason("");
    setAuthError("");
    onClose();
  }

  function handleConfirm() {
    setAuthError("");

    if (!reason.trim()) {
      setAuthError("Informe o motivo do cancelamento.");
      return;
    }

    const result = userService.validateManagerAuthorization(login.trim(), password);
    if (!result.success) {
      setAuthError(result.error);
      return;
    }

    onConfirm({
      reason: reason.trim(),
      authorizedBy: result.user.name,
      authorizedByLogin: result.user.login,
    });

    setLogin("");
    setPassword("");
    setReason("");
    setAuthError("");
  }

  const title = mode === "full" ? "Cancelar venda" : "Cancelar item";
  const warningText =
    mode === "full"
      ? "Toda a venda será cancelada. O estoque será restituído e o caixa ajustado (pagamentos em dinheiro)."
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
              rows={2}
              disabled={isLoading}
            />
          </div>

          <div className="border rounded-md p-3 space-y-3 bg-muted/30">
            <p className="text-xs font-medium text-muted-foreground flex items-center gap-1.5">
              <ShieldCheck className="h-3.5 w-3.5" />
              Autorização gerencial obrigatória
            </p>

            <div className="space-y-1.5">
              <Label htmlFor="cancel-login">Login do gerente / diretor</Label>
              <Input
                id="cancel-login"
                placeholder="login"
                value={login}
                onChange={(e) => setLogin(e.target.value)}
                disabled={isLoading}
                autoComplete="off"
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="cancel-password">Senha</Label>
              <Input
                id="cancel-password"
                type="password"
                placeholder="••••••••"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                disabled={isLoading}
                autoComplete="new-password"
                onKeyDown={(e) => { if (e.key === "Enter") handleConfirm(); }}
              />
            </div>

            {authError && (
              <p className="text-xs text-destructive font-medium">{authError}</p>
            )}
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
