import { useCallback, useRef, useState } from "react";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Textarea } from "@/components/ui/textarea";
import { AlertTriangle, Download, Paperclip, ShieldAlert } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { DataPagination } from "@/components/shared/DataPagination";
import { ErrorState } from "@/components/shared/ErrorState";
import { ApiError } from "@/services/api-client";
import { downloadFile } from "@/services/pdf.api";
import { cn } from "@/lib/utils";
import { REFUSALS, refusalTitle } from "../lib/labels";

/**
 * A refusal of the server explained: what happened (from the code), the server's own message and each reason it gave
 * (rules, coverage findings…). Nothing is hidden behind a generic "error".
 */
export function TaxRefusal({ error, className }: Readonly<{ error: unknown; className?: string }>) {
  if (!error) return null;
  const api = error instanceof ApiError ? error : null;
  const code = api?.code;
  const action = code ? REFUSALS[code]?.action : undefined;
  return (
    <div role="alert" className={cn("space-y-1 rounded-md border border-warning/40 bg-warning/5 p-3 text-sm", className)}>
      <p className="flex items-start gap-2 font-medium">
        <ShieldAlert className="mt-0.5 h-4 w-4 shrink-0 text-warning" aria-hidden />
        {refusalTitle(code)}
      </p>
      <p className="text-muted-foreground">{error instanceof Error ? error.message : String(error)}</p>
      {api && api.details.length > 1 && (
        <ul className="list-disc space-y-0.5 pl-5 text-muted-foreground">
          {api.details.map((d) => <li key={d}>{d}</li>)}
        </ul>
      )}
      {action && <p className="text-xs text-muted-foreground">{action}</p>}
    </div>
  );
}

/** The error state of a query: 403 and 404 say so; a refusal is explained; anything else offers a retry. */
export function TaxQueryError({ error, onRetry, what }: Readonly<{ error: unknown; onRetry?: () => void; what: string }>) {
  const status = error instanceof ApiError ? error.status : undefined;
  if (status === 403)
    return <ErrorState compact title="Sem permissão" description={`Seu perfil não pode ver ${what}. Peça acesso à diretoria.`} />;
  if (status === 404) return <ErrorState compact type="notfound" title={`${what[0].toUpperCase()}${what.slice(1)} não encontrado(a)`} />;
  if (status === 422) return <div className="p-4"><TaxRefusal error={error} /></div>;
  return <ErrorState compact title={`Não foi possível carregar ${what}`} onRetry={onRetry} />;
}

/** Toast for a failed action, with the refusal framed by its code. */
export function toastError(error: unknown) {
  const api = error instanceof ApiError ? error : null;
  toast.error(refusalTitle(api?.code), { description: error instanceof Error ? error.message : undefined });
}

/** Downloads a private file through the API (authorised on every request; never a public URL). */
export function DownloadButton({ path, fileName, label = "Baixar", size = "sm", variant = "outline" }: Readonly<{
  path: string; fileName: string; label?: string; size?: "sm" | "default"; variant?: "outline" | "ghost" | "default";
}>) {
  const [busy, setBusy] = useState(false);
  return (
    <Button
      type="button" size={size} variant={variant} disabled={busy}
      onClick={async () => {
        setBusy(true);
        try {
          await downloadFile(path, fileName, "Falha ao baixar");
        } catch (e) {
          toast.error(e instanceof Error ? e.message : "Falha ao baixar");
        } finally {
          setBusy(false);
        }
      }}
    >
      <Download className="mr-1.5 h-3.5 w-3.5" aria-hidden />{busy ? "Baixando…" : label}
    </Button>
  );
}

const ACCEPT = ".pdf,.png,.jpg,.jpeg,.webp,.xml";

/** Picks one private evidence file (PDF, image or XML). The server checks the type by its bytes; this only filters the dialog. */
export function FilePicker({ id, file, onChange, disabled }: Readonly<{ id: string; file: File | null; onChange: (f: File | null) => void; disabled?: boolean }>) {
  const input = useRef<HTMLInputElement>(null);
  return (
    <div className="flex min-w-0 flex-wrap items-center gap-2">
      <input
        ref={input} id={id} type="file" accept={ACCEPT} className="sr-only" disabled={disabled}
        onChange={(e) => onChange(e.target.files?.[0] ?? null)}
      />
      <Button type="button" variant="outline" size="sm" disabled={disabled} onClick={() => input.current?.click()}>
        <Paperclip className="mr-1.5 h-3.5 w-3.5" aria-hidden />{file ? "Trocar arquivo" : "Escolher arquivo"}
      </Button>
      <span className="min-w-0 truncate text-xs text-muted-foreground">
        {file ? `${file.name} (${(file.size / 1024).toFixed(0)} KB)` : "PDF, imagem ou XML — opcional"}
      </span>
      {file && <Button type="button" variant="ghost" size="sm" onClick={() => { onChange(null); if (input.current) input.current.value = ""; }}>Remover</Button>}
    </div>
  );
}

/** Pages of a server-paged list. */
export function TaxPager({ total, skip, take, onSkip }: Readonly<{ total: number; skip: number; take: number; onSkip: (skip: number) => void }>) {
  const totalPages = Math.max(1, Math.ceil(total / take));
  const page = Math.floor(skip / take) + 1;
  if (totalPages <= 1) return <p className="px-4 py-2 text-xs text-muted-foreground">{total} registro(s)</p>;
  return (
    <div className="flex flex-col items-center gap-1 px-4 py-3">
      <DataPagination page={page} totalPages={totalPages} onPageChange={(p) => onSkip((p - 1) * take)} />
      <p className="text-xs text-muted-foreground">{total} registro(s)</p>
    </div>
  );
}

/** A short warning line (e.g. why an action is unavailable). */
export function TaxWarning({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <p className="flex items-start gap-2 text-sm text-warning">
      <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
      <span>{children}</span>
    </p>
  );
}

interface ReasonRequest { title: string; description?: string; confirmLabel?: string; danger?: boolean; onConfirm: (reason: string) => void }

/**
 * Asks for the reason of a recorded act (revoke, hold, withdraw…) in a dialog — the reason is required and goes to the audit trail.
 * Returns the dialog element and the function that opens it.
 */
export function useReasonDialog(): [React.ReactNode, (r: ReasonRequest) => void] {
  const [request, setRequest] = useState<ReasonRequest | null>(null);
  const [reason, setReason] = useState("");
  const ask = useCallback((r: ReasonRequest) => { setReason(""); setRequest(r); }, []);
  const close = () => setRequest(null);
  const element = (
    <Dialog open={request !== null} onOpenChange={(v) => { if (!v) close(); }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>{request?.title}</DialogTitle>
          {request?.description && <DialogDescription>{request.description}</DialogDescription>}
        </DialogHeader>
        <form id="reason-form" onSubmit={(e) => { e.preventDefault(); if (reason.trim() && request) { request.onConfirm(reason.trim()); close(); } }}>
          <label htmlFor="reason-text" className="text-sm font-medium">Motivo</label>
          <Textarea id="reason-text" maxLength={1000} value={reason} onChange={(e) => setReason(e.target.value)} className="mt-1" autoFocus />
          <p className="mt-1 text-xs text-muted-foreground">Fica registrado na trilha de auditoria.</p>
        </form>
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={close}>Cancelar</Button>
          <Button type="submit" form="reason-form" variant={request?.danger ? "destructive" : "default"} disabled={!reason.trim()}>
            {request?.confirmLabel ?? "Confirmar"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
  return [element, ask];
}
