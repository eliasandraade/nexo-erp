import { useMemo, useState } from "react";
import { Sparkles } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { useDailyLogs } from "../../hooks/use-build";
import { useActiveSuggestion, useAiGate, useGenerateProgressInterpretation } from "../../hooks/use-build-ai";
import { aiErrorMessage, readProgressSuggestion } from "../../lib/ai";
import { fmtDate, localToday } from "../../lib/format";
import { AiErrorAlert, AiPanel, ConfidenceBar, selectClass } from "./common";
import { AiSuggestionCard, type SelectableItem } from "./AiSuggestionCard";

const MAX_PHOTOS = 5;

function daysAgo(n: number): string {
  const d = new Date(`${localToday()}T12:00:00`);
  d.setDate(d.getDate() - n);
  return d.toISOString().slice(0, 10);
}

/**
 * "Interpretar progresso com IA": recent diary (+ photos with vision) → suggested % per stage with rationale, evidence and
 * confidence. Accepting records progress events marked "confirmado a partir da IA"; nothing changes before that.
 */
export function ProgressInterpreterAi({ projectId }: { projectId: string }) {
  const gate = useAiGate();
  const generate = useGenerateProgressInterpretation(projectId);
  const { suggestion, setCurrent } = useActiveSuggestion(projectId, "ProgressInterpreter");
  const [lookback, setLookback] = useState("14");
  const [photoIds, setPhotoIds] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);

  // diary only for vision (photo picking) and to name the evidence
  const { data: logsData } = useDailyLogs(projectId);
  const logs = useMemo(() => (logsData?.pages ?? []).flatMap((p) => (Array.isArray(p?.items) ? p.items : [])), [logsData]);
  const days = Math.max(1, Math.min(60, Number.parseInt(lookback, 10) || 14));
  const since = daysAgo(days);
  const recentPhotos = useMemo(
    () => logs.filter((l) => (l.date ?? "") >= since).flatMap((l) => (Array.isArray(l.photos) ? l.photos.map((p) => ({ ...p, date: l.date })) : [])),
    [logs, since]);

  const resolveRef = (_key: string, id: string): string | undefined => {
    const log = logs.find((l) => l.id === id);
    if (log) return `Diário ${fmtDate(log.date)}`;
    const withPhoto = logs.find((l) => l.photos?.some((p) => p.id === id));
    return withPhoto ? `Foto ${fmtDate(withPhoto.date)}` : undefined;
  };

  const out = suggestion ? readProgressSuggestion(suggestion.output) : null;
  const items: SelectableItem[] = (out?.items ?? []).map((it) => ({
    id: it.id,
    label: `Aplicar ${it.stageName}: ${it.currentPercent}% → ${it.suggestedPercent}%`,
    content: (
      <div className="space-y-1 text-sm">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <p className="font-medium">{it.stageName}: <span className="tabular-nums">{it.currentPercent}% → {it.suggestedPercent}%</span></p>
          <ConfidenceBar value={it.confidence} compact label={`Confiança: ${it.stageName}`} />
        </div>
        {it.rationale && <p className="text-xs text-muted-foreground">{it.rationale}</p>}
        {it.evidenceRefs.length > 0 && (
          <p className="text-[11px] text-muted-foreground">
            Evidências: {it.evidenceRefs.map((r) => resolveRef("evidence", r) ?? r.slice(0, 8)).join(", ")}
          </p>
        )}
      </div>
    ),
  }));

  const submit = () => {
    setError(null);
    generate.mutate({ lookbackDays: days, photoIds: gate.visionEnabled && photoIds.length ? photoIds : undefined }, {
      onSuccess: (s) => setCurrent(s),
      onError:   (e) => setError(aiErrorMessage(e, "Não foi possível interpretar o progresso.")),
    });
  };

  const pending = suggestion?.status === "Pending";

  return (
    <AiPanel testId="progress-interpreter" title="Interpretar progresso com IA" aiEnabled={gate.aiEnabled} loading={gate.loading}
      description="Lê o diário recente (e fotos, quando habilitado) e sugere o % de cada etapa, com justificativa e evidências.">
      {!pending && (
        <div className="flex flex-wrap items-end gap-2">
          <div className="w-28 space-y-1">
            <Label htmlFor="pi-days" className="text-xs">Diário dos últimos</Label>
            <select id="pi-days" className={selectClass} value={lookback} onChange={(e) => setLookback(e.target.value)}>
              {["7", "14", "30", "60"].map((d) => <option key={d} value={d}>{d} dias</option>)}
            </select>
          </div>
          <Button size="sm" onClick={submit} disabled={!gate.aiEnabled || generate.isPending}>
            <Sparkles className="mr-1 h-3.5 w-3.5" /> {generate.isPending ? "Interpretando…" : "Interpretar progresso com IA"}
          </Button>
        </div>
      )}
      {!pending && gate.visionEnabled && recentPhotos.length > 0 && (
        <fieldset className="space-y-1">
          <legend className="text-xs font-medium">Fotos do diário no período (opcional, até 5)</legend>
          <div className="flex flex-wrap gap-2">
            {recentPhotos.slice(0, 30).map((p, i) => (
              <label key={p.id} className="flex cursor-pointer items-center gap-1.5 rounded-md border border-border p-1 text-xs">
                <input type="checkbox" className="accent-primary" aria-label={`Usar a foto ${i + 1} (${fmtDate(p.date)})`}
                  checked={photoIds.includes(p.id)}
                  onChange={() => setPhotoIds((prev) => prev.includes(p.id) ? prev.filter((x) => x !== p.id) : prev.length >= MAX_PHOTOS ? prev : [...prev, p.id])} />
                {p.url ? <img src={p.url} alt="" className="h-10 w-10 rounded object-cover" /> : fmtDate(p.date)}
              </label>
            ))}
          </div>
        </fieldset>
      )}
      <AiErrorAlert message={error} />

      {suggestion && (
        <AiSuggestionCard suggestion={suggestion} items={items} resolveRef={resolveRef} acceptLabel="Aplicar progresso"
          acceptHint={<>Aceitar registra eventos de progresso marcados como <strong>“confirmado a partir da IA”</strong> nas etapas selecionadas (nunca para trás).</>}
          onDecided={(s) => {
            setCurrent(s);
            toast.success(s.status === "Rejected" ? "Sugestão recusada. Nenhum progresso mudou." : "Progresso registrado (confirmado a partir da IA).");
          }}>
          {items.length === 0 && <p className="text-xs text-muted-foreground">A IA não encontrou avanço a sugerir no período.</p>}
          {(out?.discardedRefs ?? 0) > 0 && (
            <p className="text-[11px] text-muted-foreground">{out!.discardedRefs} referência(s) inventada(s) foram descartadas.</p>
          )}
        </AiSuggestionCard>
      )}
      {suggestion && !pending && (
        <div className="flex justify-end"><Button size="sm" variant="ghost" onClick={() => setCurrent(null)}>Fechar</Button></div>
      )}
    </AiPanel>
  );
}
