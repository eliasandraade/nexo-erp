import { useState } from "react";
import { KeyRound, Lock } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useRemovePortalPassword, useSetPortalPassword } from "../../hooks/use-build-portal";

interface Props {
  projectId:   string;
  hasPassword: boolean;
  isLocked:    boolean;
}

/** Sets or removes the portal password. The password is never shown — only whether one exists. */
export function PortalPasswordControl({ projectId, hasPassword, isLocked }: Props) {
  const setMut    = useSetPortalPassword(projectId);
  const removeMut = useRemovePortalPassword(projectId);
  const [password, setPassword] = useState("");
  const tooShort = password.length > 0 && password.length < 8;

  const save = () => {
    if (password.length < 8) { toast.error("A senha do portal precisa ter pelo menos 8 caracteres."); return; }
    setMut.mutate(password, {
      onSuccess: () => { setPassword(""); toast.success("Senha do portal definida. Quem estava conectado precisará entrar de novo."); },
      onError:   (e) => toast.error(e instanceof Error ? e.message : "Não foi possível definir a senha."),
    });
  };

  const remove = () => {
    if (!confirm("Remover a senha do portal? Quem estava conectado será desconectado.")) return;
    removeMut.mutate(undefined, {
      onSuccess: () => toast.success("Senha do portal removida."),
      onError:   (e) => toast.error(e instanceof Error ? e.message : "Não foi possível remover a senha."),
    });
  };

  return (
    <div className="space-y-2 rounded-md border border-border bg-muted/20 p-3">
      <p className="flex items-center gap-1.5 text-xs font-medium">
        <KeyRound className="h-3.5 w-3.5" />
        {hasPassword ? "Senha definida" : "Nenhuma senha definida"}
      </p>
      {isLocked && (
        <p className="flex items-center gap-1 text-xs text-amber-700 dark:text-amber-400">
          <Lock className="h-3 w-3" /> Portal bloqueado temporariamente por muitas tentativas de senha erradas.
        </p>
      )}
      <form className="flex flex-wrap items-end gap-2" onSubmit={(e) => { e.preventDefault(); save(); }}>
        <div className="space-y-1">
          <Label htmlFor="portal-password" className="text-xs">{hasPassword ? "Nova senha" : "Senha"}</Label>
          <Input id="portal-password" type="password" autoComplete="new-password" value={password} maxLength={128}
            onChange={(e) => setPassword(e.target.value)} className="w-56 text-sm" placeholder="mínimo 8 caracteres" />
        </div>
        <Button type="submit" size="sm" variant="outline" disabled={setMut.isPending || password.length < 8}>
          {hasPassword ? "Trocar senha" : "Definir senha"}
        </Button>
        {hasPassword && (
          <Button type="button" size="sm" variant="ghost" className="text-destructive" onClick={remove} disabled={removeMut.isPending}>
            Remover senha
          </Button>
        )}
      </form>
      {tooShort && <p className="text-xs text-destructive">Use pelo menos 8 caracteres.</p>}
      <p className="text-[11px] text-muted-foreground">
        Envie a senha ao cliente por um canal seguro. Trocar ou remover a senha desconecta quem estava no portal.
      </p>
    </div>
  );
}
