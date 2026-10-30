import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import QRCode from "qrcode";
import { Copy, Loader2, ShieldCheck } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { SectionCard } from "@/components/shared/SectionCard";
import { ApiError } from "@/services/api-client";
import { disableMfa, enableMfa, fetchMfaStatus, startMfaSetup } from "@/modules/auth/services/authService";
import { useAuth } from "@/modules/auth/context/AuthContext";

const KEY = ["auth", "mfa"];

/** Two-step verification with an authenticator app: set up (QR + key), recovery codes, turn off. */
export function MfaSection() {
  const qc = useQueryClient();
  const { logout } = useAuth();
  const [starting, setStarting] = useState(false);
  const status = useQuery({ queryKey: KEY, queryFn: fetchMfaStatus });
  const [setup, setSetup] = useState<{ secret: string; otpAuthUri: string } | null>(null);
  const [qr, setQr] = useState<string | null>(null);
  const [code, setCode] = useState("");
  const [recovery, setRecovery] = useState<string[] | null>(null);
  const [disabling, setDisabling] = useState(false);
  const [password, setPassword] = useState("");

  useEffect(() => {
    if (!setup) { setQr(null); return; }
    QRCode.toDataURL(setup.otpAuthUri, { margin: 1, width: 180 }).then(setQr).catch(() => setQr(null));
  }, [setup]);

  const fail = (e: unknown, fallback: string) => toast.error(e instanceof ApiError ? e.message : fallback);
  const start = useMutation({
    mutationFn: () => startMfaSetup(password),
    onSuccess: (d) => { setSetup(d); setCode(""); setPassword(""); setStarting(false); },
    onError: (e) => fail(e, "Não foi possível começar."),
  });
  const enable = useMutation({
    mutationFn: () => enableMfa(code),
    onSuccess: (d) => { setRecovery(d.recoveryCodes); setSetup(null); setCode(""); void qc.invalidateQueries({ queryKey: KEY }); },
    onError: (e) => fail(e, "Código inválido."),
  });
  const disable = useMutation({
    mutationFn: () => disableMfa(password, code),
    onSuccess: () => { setDisabling(false); setPassword(""); setCode(""); toast.success("Verificação em duas etapas desativada."); void qc.invalidateQueries({ queryKey: KEY }); },
    onError: (e) => fail(e, "Não foi possível desativar."),
  });

  const enabled = status.data?.enabled ?? false;

  return (
    <SectionCard title="Verificação em duas etapas" description="Além da senha, um código do aplicativo autenticador no celular a cada entrada.">
      {recovery && (
        <div role="alert" className="mb-4 rounded-md border border-warning/40 bg-warning/10 p-4 text-[13px]">
          <p className="font-semibold">Guarde estes códigos de recuperação agora.</p>
          <p className="mt-0.5 text-muted-foreground">Cada um entra uma vez, se você perder o celular. Eles não aparecem de novo. Depois, entre de novo — agora com o código do app.</p>
          <ul className="mt-3 grid grid-cols-2 gap-1 font-mono text-[13px]">
            {recovery.map((c) => <li key={c}>{c}</li>)}
          </ul>
          <div className="mt-3 flex gap-2">
            <Button size="sm" variant="outline" onClick={() => { void navigator.clipboard.writeText(recovery.join("\n")); toast.success("Códigos copiados."); }}>
              <Copy className="mr-2 h-4 w-4" /> Copiar
            </Button>
            <Button size="sm" onClick={() => { setRecovery(null); logout("/login?motivo=mfa"); }}>Já guardei — entrar de novo</Button>
          </div>
        </div>
      )}

      {status.isLoading ? <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" /> : enabled ? (
        <div className="space-y-3 text-[13px]">
          <p className="flex items-center gap-2 text-success"><ShieldCheck className="h-4 w-4" /> Ativa. {status.data?.recoveryCodesLeft ?? 0} códigos de recuperação restantes.</p>
          {disabling ? (
            <div className="grid gap-2 sm:grid-cols-2">
              <div className="space-y-1">
                <Label htmlFor="mfa-off-password">Senha</Label>
                <Input id="mfa-off-password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} />
              </div>
              <div className="space-y-1">
                <Label htmlFor="mfa-off-code">Código do app ou de recuperação</Label>
                <Input id="mfa-off-code" value={code} onChange={(e) => setCode(e.target.value)} autoComplete="one-time-code" />
              </div>
              <div className="flex gap-2 sm:col-span-2">
                <Button size="sm" variant="destructive" disabled={!password || !code.trim() || disable.isPending} onClick={() => disable.mutate()}>Desativar</Button>
                <Button size="sm" variant="ghost" onClick={() => setDisabling(false)}>Cancelar</Button>
              </div>
            </div>
          ) : (
            <Button size="sm" variant="outline" onClick={() => setDisabling(true)}>Desativar verificação em duas etapas</Button>
          )}
        </div>
      ) : setup ? (
        <div className="grid gap-4 text-[13px] sm:grid-cols-[auto_1fr]">
          {qr ? <img src={qr} alt="QR code para o aplicativo autenticador" className="h-[180px] w-[180px] rounded border border-border" /> : <div className="h-[180px] w-[180px]" />}
          <div className="space-y-3">
            <p>1. No app autenticador, adicione uma conta lendo o QR code — ou digite a chave:</p>
            <p className="break-all rounded bg-muted px-2 py-1 font-mono text-[12px]">{setup.secret}</p>
            <div className="space-y-1">
              <Label htmlFor="mfa-first-code">2. Digite o código de 6 números que o app mostra</Label>
              <Input id="mfa-first-code" value={code} onChange={(e) => setCode(e.target.value)} inputMode="numeric" autoComplete="one-time-code" className="max-w-[10rem] tracking-widest" />
            </div>
            <div className="flex gap-2">
              <Button size="sm" disabled={code.replace(/\D/g, "").length !== 6 || enable.isPending} onClick={() => enable.mutate()}>Ativar</Button>
              <Button size="sm" variant="ghost" onClick={() => setSetup(null)}>Cancelar</Button>
            </div>
          </div>
        </div>
      ) : (
        <div className="space-y-2 text-[13px]">
          <p className="text-muted-foreground">Desativada. Recomendado para quem administra a empresa.</p>
          {starting ? (
            <div className="flex flex-wrap items-end gap-2">
              <div className="space-y-1">
                <Label htmlFor="mfa-setup-password">Sua senha</Label>
                <Input id="mfa-setup-password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} className="w-56" />
              </div>
              <Button size="sm" disabled={!password || start.isPending} onClick={() => start.mutate()}>Continuar</Button>
              <Button size="sm" variant="ghost" onClick={() => { setStarting(false); setPassword(""); }}>Cancelar</Button>
            </div>
          ) : (
            <Button size="sm" variant="outline" onClick={() => setStarting(true)}>Ativar verificação em duas etapas</Button>
          )}
        </div>
      )}
    </SectionCard>
  );
}
