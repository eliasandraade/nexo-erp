import { useState } from "react";
import { ShieldCheck } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";

export interface ManagerApproval {
  login: string;
  password: string;
}

/**
 * A manager authorizes a discount above the limit by typing login and password at the PDV.
 * The server checks them when the sale is closed (and records who authorized).
 */
export function ManagerApprovalDialog({ open, percent, limit, onCancel, onApprove }: Readonly<{
  open: boolean;
  percent: number;
  limit: number;
  onCancel: () => void;
  onApprove: (approval: ManagerApproval) => void;
}>) {
  const [login, setLogin] = useState("");
  const [password, setPassword] = useState("");

  const close = () => {
    setLogin("");
    setPassword("");
    onCancel();
  };

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) close(); }}>
      <DialogContent className="max-w-sm">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2"><ShieldCheck className="h-5 w-5" /> Autorização de gerente</DialogTitle>
          <DialogDescription>
            Desconto de {percent.toLocaleString("pt-BR")}% — o limite sem gerente é {limit.toLocaleString("pt-BR")}%.
          </DialogDescription>
        </DialogHeader>
        <form
          id="manager-approval"
          className="space-y-3"
          onSubmit={(e) => {
            e.preventDefault();
            if (!login.trim() || !password) return;
            onApprove({ login: login.trim(), password });
            setPassword("");
          }}
        >
          <div className="space-y-1">
            <Label htmlFor="mgr-login">Login do gerente</Label>
            <Input id="mgr-login" autoComplete="off" value={login} onChange={(e) => setLogin(e.target.value)} />
          </div>
          <div className="space-y-1">
            <Label htmlFor="mgr-password">Senha</Label>
            <Input id="mgr-password" type="password" autoComplete="off" value={password} onChange={(e) => setPassword(e.target.value)} />
          </div>
        </form>
        <DialogFooter>
          <Button variant="outline" onClick={close}>Cancelar</Button>
          <Button type="submit" form="manager-approval" disabled={!login.trim() || !password}>Autorizar e finalizar</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
