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
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

export const RESET_PASSWORD_MIN = 8;

interface ResetPasswordDialogProps {
  /** The user whose password is reset; null closes the dialog. */
  user: { id: string; name: string } | null;
  pending?: boolean;
  onConfirm: (newPassword: string) => void;
  onCancel: () => void;
}

/** Confirms a password reset by the platform: at least 8 characters, typed twice. */
export function ResetPasswordDialog({ user, pending = false, onConfirm, onCancel }: Readonly<ResetPasswordDialogProps>) {
  const [password, setPassword] = useState("");
  const [repeat, setRepeat] = useState("");

  useEffect(() => {
    setPassword("");
    setRepeat("");
  }, [user?.id]);

  const tooShort = password.length > 0 && password.length < RESET_PASSWORD_MIN;
  const mismatch = repeat.length > 0 && repeat !== password;
  const valid = password.length >= RESET_PASSWORD_MIN && repeat === password;

  return (
    <Dialog open={user !== null} onOpenChange={v => { if (!v) onCancel(); }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Redefinir senha</DialogTitle>
          <DialogDescription>
            {`A senha de ${user?.name ?? "este usuário"} será trocada agora. `}
            Ela vale só para o próximo login: o usuário será obrigado a criar uma nova ao entrar. Esta ação fica registrada na auditoria.
          </DialogDescription>
        </DialogHeader>
        <form
          id="reset-password-form"
          className="space-y-3"
          onSubmit={e => { e.preventDefault(); if (valid && !pending) onConfirm(password); }}
        >
          <div className="space-y-1.5">
            <Label htmlFor="reset-password-new">Nova senha temporária</Label>
            <Input id="reset-password-new" type="password" autoComplete="new-password" value={password}
              onChange={e => setPassword(e.target.value)} aria-invalid={tooShort} aria-describedby="reset-password-help" />
            <p id="reset-password-help" className={tooShort ? "text-xs text-destructive" : "text-xs text-muted-foreground"}>
              Mínimo de {RESET_PASSWORD_MIN} caracteres.
            </p>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="reset-password-repeat">Repita a senha</Label>
            <Input id="reset-password-repeat" type="password" autoComplete="new-password" value={repeat}
              onChange={e => setRepeat(e.target.value)} aria-invalid={mismatch} />
            {mismatch && <p className="text-xs text-destructive">As senhas não coincidem.</p>}
          </div>
        </form>
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={onCancel}>Cancelar</Button>
          <Button type="submit" form="reset-password-form" disabled={!valid || pending}>
            {pending ? "Salvando…" : "Redefinir senha"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
