import { useEffect, useState, type FormEvent, type ReactNode } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import QRCode from "qrcode";
import { Copy, Download, Loader2, MonitorSmartphone, ShieldCheck, ShieldAlert } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { clearTokens } from "@/services/api-client";
import { formatDateTime } from "@/lib/formatters";
import { describeAgent } from "@/lib/userAgent";
import { PLATFORM_SECURITY_KEY, usePlatformSecurity } from "../hooks/usePlatformSecurity";
import {
  disablePlatformMfa,
  enablePlatformMfa,
  revokeAllPlatformSessions,
  revokePlatformSession,
  startPlatformMfaSetup,
} from "../services/platformSecurityApi";
import { platformErrorMessage } from "../services/platformErrors";
import type { PlatformMfaSetup, PlatformSecurity, PlatformSession } from "../types";

const card = "bg-card border border-border rounded-lg p-5 space-y-4";

/** A 6-digit authenticator code (no regex). */
const isSixDigits = (v: string) => v.length === 6 && [...v].every(c => c >= "0" && c <= "9");

// ─── Recovery codes (shown once) ──────────────────────────────────────────────

function RecoveryCodes({ codes, onDone }: Readonly<{ codes: string[]; onDone: () => void }>) {
  const [saved, setSaved] = useState(false);
  const text = codes.join("\n");

  const copy = () => {
    navigator.clipboard?.writeText(text)
      .then(() => toast.success("Códigos copiados."))
      .catch(() => toast.error("Não foi possível copiar. Selecione e copie os códigos manualmente."));
  };

  const download = () => {
    const url = URL.createObjectURL(new Blob([`Códigos de recuperação — Orken Plataforma\n\n${text}\n`], { type: "text/plain" }));
    const a = document.createElement("a");
    a.href = url;
    a.download = "orken-plataforma-codigos-de-recuperacao.txt";
    a.click();
    URL.revokeObjectURL(url);
  };

  return (
    <div role="alert" className="rounded-md border border-amber-500/40 bg-amber-500/10 p-4 text-sm space-y-3">
      <div>
        <p className="font-semibold text-foreground">Guarde estes códigos de recuperação agora.</p>
        <p className="mt-0.5 text-muted-foreground">
          Cada um vale uma vez, se você perder o celular. Eles não aparecem de novo. Todas as suas sessões foram
          encerradas: depois de guardar, entre de novo com a senha e o código do aplicativo.
        </p>
      </div>
      <ul aria-label="Códigos de recuperação" className="grid grid-cols-2 gap-1 font-mono text-[13px]">
        {codes.map(c => <li key={c}>{c}</li>)}
      </ul>
      <div className="flex flex-wrap gap-2">
        <Button type="button" size="sm" variant="outline" onClick={copy}><Copy className="mr-2 h-4 w-4" />Copiar</Button>
        <Button type="button" size="sm" variant="outline" onClick={download}><Download className="mr-2 h-4 w-4" />Baixar .txt</Button>
      </div>
      <label className="flex items-center gap-2 text-sm text-foreground cursor-pointer select-none">
        <input type="checkbox" checked={saved} onChange={e => setSaved(e.target.checked)} className="h-4 w-4 rounded" />
        Guardei os códigos em local seguro
      </label>
      <Button type="button" size="sm" disabled={!saved} onClick={onDone}>Entrar de novo</Button>
    </div>
  );
}

// ─── Two-step verification ────────────────────────────────────────────────────

type Step =
  | { kind: "idle" }
  | { kind: "password" }
  | { kind: "scan"; setup: PlatformMfaSetup }
  | { kind: "codes"; codes: string[] }
  | { kind: "disable" };

function MfaCard({ security }: Readonly<{ security: PlatformSecurity }>) {
  const qc = useQueryClient();
  const { endLocalSession } = useAuth();
  const [step, setStep] = useState<Step>({ kind: "idle" });
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [useRecovery, setUseRecovery] = useState(false);
  const [qr, setQr] = useState<string | null>(null);

  const setup = step.kind === "scan" ? step.setup : null;
  useEffect(() => {
    if (!setup) { setQr(null); return; }
    QRCode.toDataURL(setup.otpauthUri, { margin: 1, width: 180 }).then(setQr).catch(() => setQr(null));
  }, [setup]);

  const fail = (e: unknown) => toast.error(platformErrorMessage(e));

  const start = useMutation({
    mutationFn: () => startPlatformMfaSetup(password),
    onSuccess: (data) => { setPassword(""); setCode(""); setStep({ kind: "scan", setup: data }); },
    onError: fail,
  });

  const enable = useMutation({
    mutationFn: () => enablePlatformMfa(code.trim()),
    onSuccess: (data) => {
      // The server ended every session, this one included: never send its token again (a background refetch
      // would otherwise end the page before the codes are saved).
      clearTokens();
      void qc.cancelQueries();
      setCode("");
      setStep({ kind: "codes", codes: data.recoveryCodes });
    },
    onError: fail,
  });

  const disable = useMutation({
    mutationFn: () => disablePlatformMfa(useRecovery
      ? { password, recoveryCode: code.trim() }
      : { password, code: code.trim() }),
    onSuccess: () => {
      toast.success("Verificação em duas etapas desativada.");
      endLocalSession("/login?motivo=mfa-desativada");
    },
    onError: fail,
  });

  const cancel = () => { setStep({ kind: "idle" }); setPassword(""); setCode(""); setUseRecovery(false); };

  const submitPassword = (e: FormEvent) => { e.preventDefault(); if (password) start.mutate(); };
  const submitCode = (e: FormEvent) => { e.preventDefault(); if (isSixDigits(code.trim())) enable.mutate(); };
  const submitDisable = (e: FormEvent) => { e.preventDefault(); if (password && code.trim()) disable.mutate(); };

  const { mfa } = security;

  let body: ReactNode;
  if (step.kind === "codes") {
    body = (
        <RecoveryCodes codes={step.codes} onDone={() => endLocalSession("/login?motivo=mfa")} />
    );
  } else if (mfa.enabled) {
    body = (
        <div className="space-y-3 text-sm">
          <p className="flex items-center gap-2 text-green-600">
            <ShieldCheck className="h-4 w-4" aria-hidden /> Ativa. {mfa.recoveryCodesLeft} códigos de recuperação restantes.
          </p>
          {step.kind === "disable" ? (
            <form onSubmit={submitDisable} className="grid gap-3 sm:grid-cols-2 max-w-xl">
              <div className="space-y-1">
                <Label htmlFor="mfa-off-password">Senha</Label>
                <Input id="mfa-off-password" type="password" autoComplete="current-password" value={password} onChange={e => setPassword(e.target.value)} />
              </div>
              <div className="space-y-1">
                <Label htmlFor="mfa-off-code">{useRecovery ? "Código de recuperação" : "Código do aplicativo"}</Label>
                <Input id="mfa-off-code" value={code} onChange={e => setCode(e.target.value)} autoComplete="one-time-code"
                  inputMode={useRecovery ? "text" : "numeric"} />
              </div>
              <button type="button" onClick={() => { setUseRecovery(v => !v); setCode(""); }}
                className="sm:col-span-2 justify-self-start text-xs text-primary hover:underline">
                {useRecovery ? "Usar o código do aplicativo" : "Usar um código de recuperação"}
              </button>
              <p className="sm:col-span-2 text-xs text-muted-foreground">Desativar encerra todas as suas sessões.</p>
              <div className="flex gap-2 sm:col-span-2">
                <Button type="submit" size="sm" variant="destructive" disabled={!password || !code.trim() || disable.isPending}>
                  {disable.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}Desativar
                </Button>
                <Button type="button" size="sm" variant="ghost" onClick={cancel}>Cancelar</Button>
              </div>
            </form>
          ) : (
            <Button type="button" size="sm" variant="outline" onClick={() => setStep({ kind: "disable" })}>
              Desativar verificação em duas etapas
            </Button>
          )}
        </div>
    );
  } else if (step.kind === "scan") {
    body = (
        <form onSubmit={submitCode} className="grid gap-4 text-sm sm:grid-cols-[auto_1fr]">
          {qr
            ? <img src={qr} alt="QR code para o aplicativo autenticador" className="h-[180px] w-[180px] rounded border border-border" />
            : <div className="h-[180px] w-[180px] rounded border border-dashed border-border" aria-hidden />}
          <div className="space-y-3 min-w-0">
            <p>1. No aplicativo autenticador, leia o QR code — ou digite a chave:</p>
            <p aria-label="Chave secreta" className="break-all rounded bg-muted px-2 py-1 font-mono text-[12px]">{step.setup.secret}</p>
            <details className="text-xs text-muted-foreground">
              <summary className="cursor-pointer">Link otpauth</summary>
              <p className="mt-1 break-all font-mono">{step.setup.otpauthUri}</p>
            </details>
            <div className="space-y-1">
              <Label htmlFor="mfa-first-code">2. Digite o código de 6 números que o aplicativo mostra</Label>
              <Input id="mfa-first-code" value={code} onChange={e => setCode(e.target.value)} inputMode="numeric"
                autoComplete="one-time-code" maxLength={6} className="max-w-[10rem] tracking-widest" />
            </div>
            <p className="text-xs text-muted-foreground">Ao ativar, todas as suas sessões são encerradas e você entra de novo com o código.</p>
            <div className="flex gap-2">
              <Button type="submit" size="sm" disabled={!isSixDigits(code.trim()) || enable.isPending}>
                {enable.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}Ativar
              </Button>
              <Button type="button" size="sm" variant="ghost" onClick={cancel}>Cancelar</Button>
            </div>
          </div>
        </form>
    );
  } else {
    body = (
        <div className="space-y-3 text-sm">
          <p className="flex items-center gap-2 text-amber-600">
            <ShieldAlert className="h-4 w-4" aria-hidden /> Desativada.
          </p>
          {step.kind === "password" ? (
            <form onSubmit={submitPassword} className="flex flex-wrap items-end gap-2">
              <div className="space-y-1">
                <Label htmlFor="mfa-setup-password">Sua senha</Label>
                <Input id="mfa-setup-password" type="password" autoComplete="current-password" value={password}
                  onChange={e => setPassword(e.target.value)} className="w-56" />
              </div>
              <Button type="submit" size="sm" disabled={!password || start.isPending}>
                {start.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}Continuar
              </Button>
              <Button type="button" size="sm" variant="ghost" onClick={cancel}>Cancelar</Button>
            </form>
          ) : (
            <Button type="button" size="sm" onClick={() => setStep({ kind: "password" })}>
              Ativar verificação em duas etapas
            </Button>
          )}
        </div>
    );
  }

  return (
    <section aria-labelledby="mfa-title" className={card}>
      <div>
        <h2 id="mfa-title" className="text-sm font-medium text-foreground">Verificação em duas etapas</h2>
        <p className="text-xs text-muted-foreground mt-0.5">
          Além da senha, um código do aplicativo autenticador a cada entrada. Obrigatória para ações administrativas.
        </p>
      </div>

      {body}
    </section>
  );
}

// ─── Sessions ─────────────────────────────────────────────────────────────────

function SessionsCard({ sessions }: Readonly<{ sessions: PlatformSession[] }>) {
  const qc = useQueryClient();
  const { endLocalSession } = useAuth();
  const [confirmAll, setConfirmAll] = useState(false);

  const revokeOne = useMutation({
    mutationFn: (id: string) => revokePlatformSession(id),
    onSuccess: () => {
      toast.success("Sessão encerrada.");
      void qc.invalidateQueries({ queryKey: PLATFORM_SECURITY_KEY });
    },
    onError: (e) => toast.error(platformErrorMessage(e)),
  });

  const revokeAll = useMutation({
    mutationFn: revokeAllPlatformSessions,
    onSuccess: () => endLocalSession("/login?motivo=sessoes"),
    onError: (e) => toast.error(platformErrorMessage(e)),
  });

  return (
    <section aria-labelledby="sessions-title" className={card}>
      <ConfirmDialog
        open={confirmAll}
        title="Encerrar todas as sessões"
        description="Todas as suas sessões da plataforma serão encerradas, inclusive esta. Você precisará entrar de novo."
        confirmLabel="Encerrar todas"
        variant="danger"
        onConfirm={() => { setConfirmAll(false); revokeAll.mutate(); }}
        onCancel={() => setConfirmAll(false)}
      />
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 id="sessions-title" className="text-sm font-medium text-foreground">Sessões ativas</h2>
          <p className="text-xs text-muted-foreground mt-0.5">Onde sua conta de administrador está conectada.</p>
        </div>
        <Button type="button" size="sm" variant="outline" disabled={revokeAll.isPending} onClick={() => setConfirmAll(true)}>
          Encerrar todas
        </Button>
      </div>
      {sessions.length === 0 ? (
        <p className="text-sm text-muted-foreground">Nenhuma sessão registrada.</p>
      ) : (
        <ul className="space-y-2">
          {sessions.map(s => (
            <li key={s.id} aria-label={s.current ? "Esta sessão" : `Sessão ${describeAgent(s.userAgent)}`}
              className="flex flex-wrap items-start gap-3 rounded-md border border-border px-3 py-2 text-[13px]">
              <MonitorSmartphone className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground" aria-hidden />
              <div className="min-w-0 flex-1">
                <p className="flex flex-wrap items-center gap-2 text-foreground">
                  <span className="truncate">{describeAgent(s.userAgent)}</span>
                  {s.current && <span className="rounded bg-primary/10 px-1.5 py-0.5 text-[10px] font-medium text-primary">Esta sessão</span>}
                  <span className={s.mfaVerified
                    ? "rounded bg-green-500/10 px-1.5 py-0.5 text-[10px] font-medium text-green-600"
                    : "rounded bg-muted px-1.5 py-0.5 text-[10px] font-medium text-muted-foreground"}>
                    {s.mfaVerified ? "Com verificação em duas etapas" : "Sem verificação em duas etapas"}
                  </span>
                </p>
                <p className="text-[12px] text-muted-foreground">
                  Entrou {formatDateTime(s.createdAt)} · último uso {formatDateTime(s.lastUsedAt)}
                  {s.ipAddress ? ` · IP ${s.ipAddress}` : ""} · expira {formatDateTime(s.expiresAt)}
                </p>
              </div>
              {!s.current && (
                <Button type="button" size="sm" variant="ghost" disabled={revokeOne.isPending}
                  onClick={() => revokeOne.mutate(s.id)} aria-label={`Encerrar sessão ${describeAgent(s.userAgent)}`}>
                  Encerrar
                </Button>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

// ─── Page ─────────────────────────────────────────────────────────────────────

export default function PlatformSecurityPage() {
  const { data: security, isLoading, isError, error } = usePlatformSecurity();

  return (
    <div className="p-6 space-y-5 max-w-3xl">
      <div>
        <h1 className="font-display text-[20px] font-bold text-foreground tracking-tight">Segurança</h1>
        <p className="text-sm text-muted-foreground mt-0.5">Verificação em duas etapas e sessões da sua conta de administrador.</p>
      </div>

      {isLoading && <p className="text-sm text-muted-foreground">Carregando…</p>}
      {isError && (
        <p role="alert" className="text-sm text-destructive">
          {platformErrorMessage(error, "Não foi possível consultar a segurança da conta.")}
        </p>
      )}

      {security && (
        <>
          <p className="text-sm">
            {security.currentSessionMfaVerified
              ? <span className="text-green-600">Esta sessão entrou com verificação em duas etapas.</span>
              : <span className="text-amber-600">Esta sessão entrou sem verificação em duas etapas: ações administrativas ficam bloqueadas.</span>}
          </p>
          <MfaCard security={security} />
          <SessionsCard sessions={security.sessions ?? []} />
        </>
      )}
    </div>
  );
}
