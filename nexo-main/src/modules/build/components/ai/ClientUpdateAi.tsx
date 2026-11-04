import { useState } from "react";
import { CalendarDays, Sparkles } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { BuildAiSuggestionDto } from "../../api/build-ai.api";
import { useActiveSuggestion, useAiGate, useGenerateClientUpdate, useGenerateWeeklyDigest } from "../../hooks/use-build-ai";
import { aiErrorMessage, readClientUpdateDraft } from "../../lib/ai";
import { fmtDate, localToday } from "../../lib/format";
import { AiErrorAlert, AiPanel } from "./common";
import { AiSuggestionCard } from "./AiSuggestionCard";

function weekAgo(): string {
  const d = new Date(`${localToday()}T12:00:00`);
  d.setDate(d.getDate() - 7);
  return d.toISOString().slice(0, 10);
}

export function ClientUpdateDraftView({ output }: { output: unknown }) {
  const d = readClientUpdateDraft(output);
  return (
    <div className="space-y-1.5 text-sm" data-testid="client-update-draft">
      {d.from && d.to && <p className="text-[11px] text-muted-foreground">Período: {fmtDate(d.from)} a {fmtDate(d.to)}</p>}
      <p className="font-medium">{d.title || "Sem título"}</p>
      <p className="whitespace-pre-line text-foreground/80">{d.body}</p>
      {d.highlights.length > 0 && <ul className="list-disc pl-5 text-xs">{d.highlights.map((h, i) => <li key={i}>{h}</li>)}</ul>}
    </div>
  );
}

/**
 * AI for the client updates: "Gerar atualização" (a period) and "Resumo semanal". Only facts the client can already see go
 * to the provider. Accepting creates a DRAFT update — the person still edits and publishes it below.
 */
export function ClientUpdateAi({ projectId }: { projectId: string }) {
  const gate = useAiGate();
  const genUpdate = useGenerateClientUpdate(projectId);
  const genDigest = useGenerateWeeklyDigest(projectId);
  const update = useActiveSuggestion(projectId, "ClientUpdate");
  const digest = useActiveSuggestion(projectId, "WeeklyDigest");
  const [from, setFrom] = useState(weekAgo);
  const [to, setTo] = useState(localToday);
  const [error, setError] = useState<string | null>(null);

  const busy = genUpdate.isPending || genDigest.isPending;
  const onError = (e: unknown) => setError(aiErrorMessage(e, "Não foi possível gerar o texto."));

  const shown: Array<{ s: BuildAiSuggestionDto; clear: () => void; set: (s: BuildAiSuggestionDto) => void }> = [];
  if (update.suggestion) shown.push({ s: update.suggestion, clear: () => update.setCurrent(null), set: update.setCurrent });
  if (digest.suggestion) shown.push({ s: digest.suggestion, clear: () => digest.setCurrent(null), set: digest.setCurrent });

  return (
    <AiPanel testId="client-update-ai" title="Texto para o cliente com IA" aiEnabled={gate.aiEnabled} loading={gate.loading}
      description="Usa só o que o cliente já pode ver no portal. Sem provedor de IA, monta o texto só com os fatos (regras).">
      <div className="flex flex-wrap items-end gap-2">
        <div className="space-y-1">
          <Label htmlFor="cu-ai-from" className="text-xs">De</Label>
          <Input id="cu-ai-from" type="date" value={from} max={to} onChange={(e) => setFrom(e.target.value)} className="w-40" />
        </div>
        <div className="space-y-1">
          <Label htmlFor="cu-ai-to" className="text-xs">Até</Label>
          <Input id="cu-ai-to" type="date" value={to} max={localToday()} onChange={(e) => setTo(e.target.value)} className="w-40" />
        </div>
        <Button size="sm" disabled={!gate.aiEnabled || busy || !from || !to}
          onClick={() => { setError(null); genUpdate.mutate({ from, to }, { onSuccess: (s) => update.setCurrent(s), onError }); }}>
          <Sparkles className="mr-1 h-3.5 w-3.5" /> {genUpdate.isPending ? "Gerando…" : "Gerar atualização"}
        </Button>
        <Button size="sm" variant="outline" disabled={!gate.aiEnabled || busy}
          onClick={() => { setError(null); genDigest.mutate(undefined, { onSuccess: (s) => digest.setCurrent(s), onError }); }}>
          <CalendarDays className="mr-1 h-3.5 w-3.5" /> {genDigest.isPending ? "Gerando…" : "Resumo semanal"}
        </Button>
      </div>
      <AiErrorAlert message={error} />
      {shown.map(({ s, clear, set }) => (
        <div key={s.id} className="space-y-1">
          <AiSuggestionCard suggestion={s} acceptLabel="Criar rascunho"
            acceptHint="Aceitar cria um rascunho na lista de atualizações abaixo. Você ainda revisa, edita e publica — nada vai ao cliente sem a sua publicação."
            onDecided={(r) => {
              set(r);
              toast.success(r.status === "Rejected" ? "Sugestão recusada." : "Rascunho criado. Revise e publique quando quiser.");
            }}>
            <ClientUpdateDraftView output={s.output} />
          </AiSuggestionCard>
          {s.status !== "Pending" && (
            <div className="flex justify-end"><Button size="sm" variant="ghost" onClick={clear}>Fechar</Button></div>
          )}
        </div>
      ))}
    </AiPanel>
  );
}
