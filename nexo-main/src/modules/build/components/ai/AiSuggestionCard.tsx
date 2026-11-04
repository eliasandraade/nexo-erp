import { useEffect, useMemo, useState, type ReactNode } from "react";
import { Bot, Check, Cog, X } from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { ApiError } from "@/services/api-client";
import type { BuildAiSuggestionDto } from "../../api/build-ai.api";
import { useAcceptSuggestion, useRejectSuggestion } from "../../hooks/use-build-ai";
import {
  AI_TAGLINE, CAPABILITY_LABEL, RULES_SOURCE_TEXT, STATUS_LABEL, aiErrorMessage, fmtUsdMicros, isExpired, readProvenance,
  shortId,
} from "../../lib/ai";
import { fmtDateTime } from "../../lib/format";
import { AiErrorAlert, ConfidenceBar } from "./common";

export interface SelectableItem {
  id: string;
  content: ReactNode;
  /** Accessible name of the checkbox. */
  label: string;
}

interface Props {
  suggestion: BuildAiSuggestionDto;
  /** Items the person can pick (partial acceptance). Without them the whole suggestion is accepted. */
  items?: SelectableItem[];
  /** The proposal itself (what is not selectable item by item). */
  children?: ReactNode;
  acceptLabel?: string;
  /** What accepting does, said before the click. */
  acceptHint?: ReactNode;
  /** Resolves an id of the provenance (diary entry → its date…) to something people read. */
  resolveRef?: (groupKey: string, id: string) => string | undefined;
  onDecided?: (s: BuildAiSuggestionDto) => void;
}

const STATUS_CLASS: Record<string, string> = {
  Pending:           "bg-primary/10 text-primary",
  Accepted:          "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400",
  PartiallyAccepted: "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400",
  Rejected:          "bg-muted text-muted-foreground",
  Expired:           "bg-muted text-muted-foreground",
  Failed:            "bg-destructive/10 text-destructive",
};

/**
 * "Sugestão da IA": what the AI (or the rules) proposed, with provenance — source, confidence, provider/model, prompt
 * version and hash, tokens and cost, what was read and when it expires. Nothing is applied without the person's click:
 * accept (all or the selected items) or reject, both with an optional note.
 */
export function AiSuggestionCard({ suggestion: s, items, children, acceptLabel, acceptHint, resolveRef, onDecided }: Props) {
  const qc = useQueryClient();
  const accept = useAcceptSuggestion();
  const reject = useRejectSuggestion();
  const ids = useMemo(() => (items ?? []).map((i) => i.id), [items]);
  const [selected, setSelected] = useState<Set<string>>(() => new Set(ids));
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);

  // a new suggestion (or new items) starts with everything selected
  const idsKey = ids.join("|");
  useEffect(() => { setSelected(new Set(idsKey ? idsKey.split("|") : [])); setError(null); }, [s.id, idsKey]);

  const expired = isExpired(s);
  const status = expired ? "Expired" : s.status;
  const pending = status === "Pending";
  const busy = accept.isPending || reject.isPending;
  const rules = s.source === "Rules";
  const provenance = readProvenance(s.inputRefs);
  const hasItems = !!items && items.length > 0;

  const onError = (e: unknown) => {
    setError(aiErrorMessage(e));
    // expired or decided elsewhere: the list is stale
    if (e instanceof ApiError && e.status === 409) void qc.invalidateQueries({ queryKey: ["build", "ai", s.projectId] });
  };

  const doAccept = () => {
    setError(null);
    const all = !hasItems || selected.size === ids.length;
    accept.mutate(
      { id: s.id, req: { itemIds: all ? undefined : ids.filter((i) => selected.has(i)), note: note.trim() || undefined } },
      { onSuccess: (r) => onDecided?.(r), onError },
    );
  };
  const doReject = () => {
    setError(null);
    reject.mutate({ id: s.id, note: note.trim() || undefined }, { onSuccess: (r) => onDecided?.(r), onError });
  };

  const toggle = (id: string) => setSelected((prev) => {
    const next = new Set(prev);
    if (next.has(id)) next.delete(id); else next.add(id);
    return next;
  });

  const tokens = (s.inputTokens ?? 0) + (s.outputTokens ?? 0);

  return (
    <article data-testid="ai-suggestion" aria-label={`Sugestão da IA: ${CAPABILITY_LABEL[s.capability] ?? s.capability}`}
      className="min-w-0 space-y-3 rounded-md border border-border bg-card p-3">
      <header className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0 space-y-0.5">
          <p className="text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">Sugestão da IA</p>
          <p className="text-sm font-semibold">{CAPABILITY_LABEL[s.capability] ?? s.capability}</p>
        </div>
        <div className="flex flex-wrap items-center gap-1.5">
          <span data-testid="ai-source" className={cn("inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-medium",
            rules ? "bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-300" : "bg-violet-100 text-violet-700 dark:bg-violet-900/30 dark:text-violet-300")}>
            {rules ? <Cog className="h-3 w-3" aria-hidden /> : <Bot className="h-3 w-3" aria-hidden />}
            {rules ? `Regras — ${RULES_SOURCE_TEXT}` : "IA"}
          </span>
          <span className={cn("rounded-full px-2 py-0.5 text-[11px] font-medium", STATUS_CLASS[status] ?? STATUS_CLASS.Pending)}>
            {STATUS_LABEL[status] ?? status}
          </span>
        </div>
      </header>

      <ConfidenceBar value={s.confidence} />

      {children}

      {hasItems && (
        <fieldset className="space-y-1.5">
          <legend className="sr-only">Itens da sugestão</legend>
          {pending && ids.length > 1 && (
            <div className="flex items-center justify-between text-xs text-muted-foreground">
              <span>{selected.size} de {ids.length} selecionados</span>
              <button type="button" className="font-medium text-primary hover:underline"
                onClick={() => setSelected(selected.size === ids.length ? new Set() : new Set(ids))}>
                {selected.size === ids.length ? "Desmarcar todos" : "Selecionar todos"}
              </button>
            </div>
          )}
          <ul className="space-y-1.5">
            {items!.map((it) => (
              <li key={it.id} className="flex items-start gap-2 rounded-md border border-border p-2">
                {pending && (
                  <input type="checkbox" className="mt-1 h-4 w-4 shrink-0 accent-primary" aria-label={it.label}
                    checked={selected.has(it.id)} onChange={() => toggle(it.id)} disabled={busy} />
                )}
                <div className="min-w-0 flex-1">{it.content}</div>
              </li>
            ))}
          </ul>
        </fieldset>
      )}

      <dl className="grid gap-x-4 gap-y-1 text-[11px] text-muted-foreground sm:grid-cols-2" data-testid="ai-provenance-meta">
        {!rules && (s.provider || s.model) && (
          <div><dt className="inline">Provedor: </dt><dd className="inline text-foreground/80">{[s.provider, s.model].filter(Boolean).join(" · ")}</dd></div>
        )}
        {(s.promptType || s.promptHash) && (
          <div>
            <dt className="inline">Prompt: </dt>
            <dd className="inline font-mono text-foreground/80">
              {s.promptType ?? "—"}{s.promptVersion ? ` v${s.promptVersion}` : s.promptVersionId ? "" : " (embutido)"}
              {s.promptHash ? ` #${s.promptHash.slice(0, 8)}` : ""}
            </dd>
          </div>
        )}
        {tokens > 0 && (
          <div><dt className="inline">Tokens: </dt><dd className="inline text-foreground/80">
            {tokens.toLocaleString("pt-BR")} (entrada {(s.inputTokens ?? 0).toLocaleString("pt-BR")} · saída {(s.outputTokens ?? 0).toLocaleString("pt-BR")})
          </dd></div>
        )}
        {s.costMicros > 0 && (
          <div><dt className="inline">Custo: </dt><dd className="inline text-foreground/80">{fmtUsdMicros(s.costMicros)}</dd></div>
        )}
        <div><dt className="inline">Gerada: </dt><dd className="inline text-foreground/80">{fmtDateTime(s.createdAt)} · {s.createdByName}</dd></div>
        {s.expiresAt && (
          <div><dt className="inline">{expired ? "Venceu: " : "Vence: "}</dt><dd className="inline text-foreground/80">{fmtDateTime(s.expiresAt)}</dd></div>
        )}
      </dl>

      {provenance.length > 0 && (
        <details className="rounded-md bg-muted/40 px-2 py-1.5 text-xs">
          <summary className="cursor-pointer font-medium">Fontes usadas</summary>
          <ul className="mt-1 space-y-1" data-testid="ai-provenance">
            {provenance.map((g) => (
              <li key={g.key}>
                <span className="text-muted-foreground">{g.label}: </span>
                {g.value ?? (
                  <span className="inline-flex flex-wrap gap-1">
                    {g.ids.map((id) => (
                      <code key={id} title={id} className="rounded bg-background px-1 text-[11px]">{resolveRef?.(g.key, id) ?? shortId(id)}</code>
                    ))}
                  </span>
                )}
              </li>
            ))}
          </ul>
        </details>
      )}

      {pending ? (
        <div className="space-y-2 border-t border-border pt-2">
          {acceptHint && <p className="text-xs text-muted-foreground">{acceptHint}</p>}
          <div className="space-y-1">
            <Label htmlFor={`ai-note-${s.id}`} className="text-xs">Observação (opcional)</Label>
            <Textarea id={`ai-note-${s.id}`} value={note} maxLength={1000} rows={2} onChange={(e) => setNote(e.target.value)}
              className="min-h-[52px] resize-y text-sm" disabled={busy} />
          </div>
          <AiErrorAlert message={error} />
          <div className="flex flex-wrap justify-end gap-2">
            <Button size="sm" variant="outline" onClick={doReject} disabled={busy}>
              <X className="mr-1 h-3.5 w-3.5" /> {reject.isPending ? "Recusando…" : "Recusar"}
            </Button>
            <Button size="sm" onClick={doAccept} disabled={busy || (hasItems && selected.size === 0)}>
              <Check className="mr-1 h-3.5 w-3.5" />
              {accept.isPending ? "Aplicando…" : hasItems && selected.size < ids.length
                ? `Aceitar ${selected.size} selecionado${selected.size === 1 ? "" : "s"}`
                : acceptLabel ?? "Aceitar"}
            </Button>
          </div>
          <p className="text-[11px] text-muted-foreground">{AI_TAGLINE} Nada é aplicado sem o seu clique.</p>
        </div>
      ) : (
        <div className="space-y-1 border-t border-border pt-2 text-xs text-muted-foreground">
          {status === "Expired"
            ? <p>Esta sugestão venceu sem decisão. Gere uma nova se ainda precisar.</p>
            : <p>{STATUS_LABEL[status] ?? status}{s.decidedByName ? ` por ${s.decidedByName}` : ""}{s.decidedAt ? ` em ${fmtDateTime(s.decidedAt)}` : ""}.</p>}
          {s.decisionNote && <p>Observação: {s.decisionNote}</p>}
          <AiErrorAlert message={error} />
        </div>
      )}
    </article>
  );
}
