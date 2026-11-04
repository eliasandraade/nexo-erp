import { useMemo, useState } from "react";
import { Sparkles } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import type { BuildDailyLogDto } from "../../api/build.api";
import { useActiveSuggestion, useAiGate, useGenerateDailyLogCopilot } from "../../hooks/use-build-ai";
import { aiErrorMessage, readDailyLogDraft } from "../../lib/ai";
import { fmtDate, localToday } from "../../lib/format";
import { AiErrorAlert, AiPanel, ConfidenceBar } from "./common";
import { AiSuggestionCard } from "./AiSuggestionCard";

const MAX_TEXT = 8000;
const MAX_PHOTOS = 5;

/** The structured draft of the day (activities by stage, crew, equipment, issues, weather, client summary). */
export function DailyLogDraftView({ output }: { output: unknown }) {
  const d = readDailyLogDraft(output);
  const byStage = new Map<string, typeof d.activities>();
  for (const a of d.activities) {
    const k = a.stageName ?? "Sem etapa";
    byStage.set(k, [...(byStage.get(k) ?? []), a]);
  }
  const empty = d.activities.length === 0 && d.workforce.length === 0 && d.equipment.length === 0 && d.issues.length === 0;
  return (
    <div className="space-y-2 text-sm" data-testid="copilot-draft">
      {d.date && <p className="text-xs text-muted-foreground">Rascunho do diário de {fmtDate(d.date)}</p>}
      {empty && <p className="text-xs text-muted-foreground">Nada estruturado a partir do relato.</p>}
      {[...byStage.entries()].map(([stage, acts]) => (
        <div key={stage} className="space-y-1">
          <p className="text-xs font-semibold">{stage}</p>
          <ul className="space-y-1">
            {acts.map((a, i) => (
              <li key={`${stage}-${i}`} className="flex flex-wrap items-center justify-between gap-2 rounded bg-muted/40 px-2 py-1">
                <span className="min-w-0 flex-1">{a.description}{a.progressHint != null && <span className="text-xs text-muted-foreground"> · indício de {a.progressHint}%</span>}</span>
                <ConfidenceBar value={a.confidence} compact label={`Confiança: ${a.description}`} />
              </li>
            ))}
          </ul>
        </div>
      ))}
      {d.workforce.length > 0 && (
        <p><span className="text-xs font-semibold">Equipe: </span>{d.workforce.map((w) => `${w.count} ${w.role}`).join(", ")}</p>
      )}
      {d.equipment.length > 0 && <p><span className="text-xs font-semibold">Equipamentos: </span>{d.equipment.join(", ")}</p>}
      {d.issues.length > 0 && (
        <div><p className="text-xs font-semibold">Ocorrências</p>
          <ul className="list-disc pl-5">{d.issues.map((x, i) => <li key={i}>{x}</li>)}</ul></div>
      )}
      {d.weatherNote && <p><span className="text-xs font-semibold">Clima: </span>{d.weatherNote}</p>}
      {d.clientSummary ? (
        <div className="rounded-md border border-dashed border-border p-2">
          <p className="text-xs font-semibold">Resumo para o cliente</p>
          <p className="whitespace-pre-line">{d.clientSummary}</p>
          <p className="mt-1 text-[11px] text-muted-foreground">Fica oculto do cliente até você torná-lo visível no registro do diário.</p>
        </div>
      ) : (
        <p className="text-xs text-muted-foreground">Sem resumo para o cliente nesta sugestão.</p>
      )}
      {d.discardedRefs > 0 && (
        <p className="text-[11px] text-muted-foreground">{d.discardedRefs} referência(s) que não eram desta obra foram descartadas.</p>
      )}
    </div>
  );
}

/** "Copiloto do diário": the day's free report (and, with vision, photos of that date) → a structured draft to accept. */
export function DailyLogCopilot({ projectId, logs }: { projectId: string; logs: BuildDailyLogDto[] }) {
  const gate = useAiGate();
  const generate = useGenerateDailyLogCopilot(projectId);
  const { suggestion, setCurrent } = useActiveSuggestion(projectId, "DailyLogCopilot");
  const [open, setOpen] = useState(false);
  const [date, setDate] = useState(localToday);
  const [text, setText] = useState("");
  const [photoIds, setPhotoIds] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);

  const photos = useMemo(
    () => logs.filter((l) => l.date?.slice(0, 10) === date).flatMap((l) => (Array.isArray(l.photos) ? l.photos : [])),
    [logs, date]);

  if (!open && !suggestion) {
    return (
      <Button size="sm" variant="outline" onClick={() => setOpen(true)}>
        <Sparkles className="mr-1 h-3.5 w-3.5" /> Copiloto do diário
      </Button>
    );
  }

  const submit = () => {
    setError(null);
    generate.mutate({ date, text: text.trim(), photoIds: gate.visionEnabled && photoIds.length ? photoIds : undefined }, {
      onSuccess: (s) => { setCurrent(s); setOpen(false); },
      onError:   (e) => setError(aiErrorMessage(e, "Não foi possível gerar o rascunho.")),
    });
  };

  const togglePhoto = (id: string) => setPhotoIds((prev) =>
    prev.includes(id) ? prev.filter((x) => x !== id) : prev.length >= MAX_PHOTOS ? prev : [...prev, id]);

  return (
    <AiPanel testId="daily-log-copilot" title="Copiloto do diário" aiEnabled={gate.aiEnabled} loading={gate.loading}
      description="Escreva o que aconteceu na obra; a IA organiza em atividades por etapa, equipe, equipamentos e ocorrências.">
      {open && (
        <div className="space-y-2">
          <div className="grid gap-2 sm:grid-cols-[180px_1fr]">
            <div className="space-y-1">
              <Label htmlFor="copilot-date" className="text-xs">Data</Label>
              <Input id="copilot-date" type="date" value={date} max={localToday()} onChange={(e) => { setDate(e.target.value); setPhotoIds([]); }} />
            </div>
          </div>
          <div className="space-y-1">
            <Label htmlFor="copilot-text" className="text-xs">Relato do dia</Label>
            <Textarea id="copilot-text" value={text} maxLength={MAX_TEXT} rows={5} onChange={(e) => setText(e.target.value)}
              placeholder="Ex.: Equipe de 4 pedreiros terminou a alvenaria do 2º pavimento; chegou o cimento; chuva à tarde."
              className="resize-y text-sm" />
            <p className="flex justify-between text-[11px] text-muted-foreground">
              <span>Não inclua CPF, telefones ou outros dados pessoais no relato.</span>
              <span className="tabular-nums">{text.length.toLocaleString("pt-BR")} / 8.000</span>
            </p>
          </div>
          {gate.visionEnabled ? (
            photos.length > 0 && (
              <fieldset className="space-y-1">
                <legend className="text-xs font-medium">Fotos desta data para a IA analisar (até 5)</legend>
                <div className="flex flex-wrap gap-2">
                  {photos.map((p, i) => (
                    <label key={p.id} className="flex cursor-pointer items-center gap-1.5 rounded-md border border-border p-1 text-xs">
                      <input type="checkbox" className="accent-primary" checked={photoIds.includes(p.id)} onChange={() => togglePhoto(p.id)}
                        aria-label={`Usar a foto ${i + 1}`} />
                      {p.url ? <img src={p.url} alt="" className="h-10 w-10 rounded object-cover" /> : `Foto ${i + 1}`}
                    </label>
                  ))}
                </div>
              </fieldset>
            )
          ) : gate.aiEnabled ? (
            <p className="text-[11px] text-muted-foreground">Análise de fotos pela IA não habilitada — só o texto será usado.</p>
          ) : null}
          <AiErrorAlert message={error} />
          <div className="flex justify-end gap-2">
            <Button size="sm" variant="ghost" onClick={() => setOpen(false)} disabled={generate.isPending}>Cancelar</Button>
            <Button size="sm" onClick={submit} disabled={!gate.aiEnabled || !text.trim() || generate.isPending}>
              <Sparkles className="mr-1 h-3.5 w-3.5" /> {generate.isPending ? "Gerando…" : "Gerar rascunho"}
            </Button>
          </div>
        </div>
      )}

      {suggestion && (
        <>
          <AiSuggestionCard suggestion={suggestion} acceptLabel="Aceitar e registrar no diário"
            acceptHint="Aceitar cria o registro do diário dessa data (ou acrescenta às notas do registro existente). O resumo para o cliente fica oculto até você torná-lo visível."
            onDecided={(s) => {
              setCurrent(s);
              if (s.status === "Accepted" || s.status === "PartiallyAccepted") { toast.success("Diário registrado a partir da sugestão."); setText(""); }
              else toast.success("Sugestão recusada. Nada mudou no diário.");
            }}>
            <DailyLogDraftView output={suggestion.output} />
          </AiSuggestionCard>
          {!open && suggestion.status !== "Pending" && (
            <div className="flex justify-end gap-2">
              <Button size="sm" variant="ghost" onClick={() => setCurrent(null)}>Fechar</Button>
              <Button size="sm" variant="outline" onClick={() => setOpen(true)}>Novo relato</Button>
            </div>
          )}
        </>
      )}
    </AiPanel>
  );
}
