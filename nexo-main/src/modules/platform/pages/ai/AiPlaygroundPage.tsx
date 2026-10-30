import { useState } from "react";
import { FlaskConical, Send, RotateCcw, Zap, Clock, Hash, CheckCircle2, XCircle, DollarSign } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { useAiProviders, usePlayground } from "../../hooks/useAiOperations";
import type { AnalyzerProvider, FieldStatus, PlaygroundResult } from "../../types/aiOperations";
import { ApiErrorPanel, ProviderStatusBadge, formatUsd, reasonLabel } from "./aiShared";

const TEXT_MAX = 1000;

const STATUS_STYLE: Record<FieldStatus, string> = {
  AutoFilled:     "bg-green-500/10 text-green-600 border-green-200",
  NeedsAttention: "bg-amber-500/10 text-amber-600 border-amber-200",
  RequiresInput:  "bg-red-500/10 text-red-600 border-red-200",
};

const STATUS_LABEL: Record<FieldStatus, string> = {
  AutoFilled:     "Auto",
  NeedsAttention: "Atenção",
  RequiresInput:  "Manual",
};

function ConfidenceBar({ value }: Readonly<{ value: number }>) {
  const pct = Math.round(value * 100);
  let color = "bg-red-400";
  if (pct >= 90) color = "bg-green-500";
  else if (pct >= 70) color = "bg-amber-500";
  return (
    <div className="flex items-center gap-2">
      <div className="h-1.5 w-20 rounded-full bg-muted overflow-hidden">
        <div className={`h-full rounded-full ${color}`} style={{ width: `${pct}%` }} />
      </div>
      <span className="text-xs tabular-nums text-muted-foreground">{pct}%</span>
    </div>
  );
}

const EXAMPLE_TEXTS = [
  "Pix enviado em 07/05/2026\npara: João Carlos da Silva\nR$ 850,00\nconta: Banco Nubank",
  "Pagamento fornecedor XPTO\nvalor: R$ 1.234,56\ndata: 05/05/2026",
  "Recebimento de cliente\nR$2500 dia 02/05/2026",
];

const AUTO = "auto";

// ── Result ────────────────────────────────────────────────────────────────────

function ResultView({ result }: Readonly<{ result: PlaygroundResult }>) {
  const e = result.extraction;
  const i = result.interpretation;
  const fields: [string, string, number, string][] = [
    ["Valor",   e.amount.value === null ? "—" : `R$ ${Number(e.amount.value).toFixed(2)}`, e.amount.confidence, e.amount.status],
    ["Data",    e.date.value    ?? "—", e.date.confidence,    e.date.status],
    ["Pagador", e.payee.value   ?? "—", e.payee.confidence,   e.payee.status],
    ["Conta",   e.account.value ?? "—", e.account.confidence, e.account.status],
  ];
  const suggestion = [
    { label: "Direção",   value: i.direction,                          source: i.dirSource },
    { label: "Natureza",  value: i.nature,                             source: i.natureSource },
    { label: "Categoria", value: i.categoryId ?? "Não identificada",   source: i.catSource },
    { label: "Contexto",  value: i.contextType ?? "Não identificado",  source: i.ctxSource },
    { label: "Conta",     value: i.accountId ?? "Não identificada",    source: i.accSource },
  ];

  return (
    <div className="space-y-3" data-testid="playground-result">
      {/* Who ran */}
      <div className="bg-card border border-border rounded-lg p-4 space-y-2">
        <div className="flex flex-wrap items-center gap-2">
          <Zap className="h-4 w-4 text-primary" />
          <span className="text-sm text-foreground">Executado por <span className="font-semibold">{result.provider}</span></span>
          {result.modelId && <span className="text-xs font-mono text-muted-foreground">{result.modelId}</span>}
          <span className="text-[11px] px-2 py-0.5 rounded-full border border-border bg-muted text-muted-foreground">
            {result.forced ? "Forçado" : "Automático"}
          </span>
          {result.fallbackUsed && (
            <span className="text-[11px] px-2 py-0.5 rounded-full border border-amber-500/30 bg-amber-500/10 text-amber-700">
              Fallback usado
            </span>
          )}
        </div>
        <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
          <span className="flex items-center gap-1.5"><Clock className="h-3.5 w-3.5" />{result.elapsedMs} ms</span>
          <span className="flex items-center gap-1.5">
            <Hash className="h-3.5 w-3.5" />Tokens: {e.inputTokens} entrada / {e.outputTokens} saída
          </span>
          <span className="flex items-center gap-1.5"><DollarSign className="h-3.5 w-3.5" />Custo: {formatUsd(e.costUsd)}</span>
        </div>
        <p className="text-xs text-muted-foreground">
          Prompt: <span className="font-mono">{result.prompt.type} v{result.prompt.version}</span> · hash{" "}
          <span className="font-mono">{result.prompt.hash}</span>
        </p>
      </div>

      {/* Attempts */}
      {result.attempts.length > 0 && (
        <div className="bg-card border border-border rounded-lg overflow-hidden">
          <div className="px-4 py-2.5 border-b border-border bg-muted/30">
            <h3 className="text-xs font-semibold text-foreground uppercase tracking-wide">Tentativas</h3>
          </div>
          <ol className="divide-y divide-border">
            {result.attempts.map((a, idx) => (
              <li key={`${a.provider}-${idx}`} className="flex flex-wrap items-center gap-2 px-4 py-2 text-xs">
                {a.succeeded
                  ? <CheckCircle2 className="h-3.5 w-3.5 text-green-600" aria-label="sucesso" />
                  : <XCircle className="h-3.5 w-3.5 text-destructive" aria-label="falha" />}
                <span className="font-medium text-foreground">{a.provider}</span>
                {a.modelId && <span className="font-mono text-muted-foreground">{a.modelId}</span>}
                {a.failureReason && (
                  <span className="text-destructive">{reasonLabel(a.failureReason)} <span className="font-mono">({a.failureReason})</span></span>
                )}
                <span className="ml-auto tabular-nums text-muted-foreground">{a.durationMs} ms</span>
              </li>
            ))}
          </ol>
        </div>
      )}

      {/* Extraction */}
      <div className="bg-card border border-border rounded-lg overflow-x-auto">
        <div className="px-4 py-2.5 border-b border-border bg-muted/30">
          <h3 className="text-xs font-semibold text-foreground uppercase tracking-wide">Extração</h3>
        </div>
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-border">
              <th className="text-left px-4 py-2 text-xs text-muted-foreground font-medium">Campo</th>
              <th className="text-left px-4 py-2 text-xs text-muted-foreground font-medium">Valor</th>
              <th className="text-left px-4 py-2 text-xs text-muted-foreground font-medium">Confiança</th>
              <th className="text-left px-4 py-2 text-xs text-muted-foreground font-medium">Status</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {fields.map(([field, val, conf, status]) => (
              <tr key={field}>
                <td className="px-4 py-2.5 text-xs font-mono text-muted-foreground">{field}</td>
                <td className="px-4 py-2.5 text-sm font-medium text-foreground">{val}</td>
                <td className="px-4 py-2.5"><ConfidenceBar value={conf} /></td>
                <td className="px-4 py-2.5">
                  <span className={`inline-flex items-center px-1.5 py-0.5 rounded text-[10px] font-medium border ${
                    STATUS_STYLE[status as FieldStatus] ?? "bg-muted text-muted-foreground border-border"}`}>
                    {STATUS_LABEL[status as FieldStatus] ?? status}
                  </span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Interpretation */}
      <div className="bg-card border border-border rounded-lg overflow-hidden">
        <div className="px-4 py-2.5 border-b border-border bg-muted/30">
          <h3 className="text-xs font-semibold text-foreground uppercase tracking-wide">Sugestão de interpretação</h3>
        </div>
        <div className="divide-y divide-border">
          {suggestion.map(({ label, value, source }) => (
            <div key={label} className="flex items-center justify-between gap-2 px-4 py-2.5">
              <span className="text-xs text-muted-foreground w-24 shrink-0">{label}</span>
              <span className="text-sm font-medium text-foreground flex-1 break-all">{value}</span>
              <span className="text-[10px] text-muted-foreground bg-muted px-1.5 py-0.5 rounded">{source}</span>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

export default function AiPlaygroundPage() {
  const [text, setText]         = useState("");
  const [tenantId, setTenantId] = useState("");
  const [choice, setChoice]     = useState<string>(AUTO);
  const providersQuery = useAiProviders();
  const { analyze, isPending, result, error, reset } = usePlayground();

  const providers = providersQuery.data?.providers ?? [];
  const automaticOrder = providersQuery.data?.automaticOrder ?? [];
  const chosen = providers.find(p => p.provider === choice);
  // a provider that is not available is never a valid choice, even if it was picked before it changed
  const choiceValid = choice === AUTO || chosen?.status === "available";

  const handleAnalyze = () => {
    if (!text.trim() || !choiceValid) return;
    analyze({
      text,
      forceProvider: choice === AUTO ? null : (choice as AnalyzerProvider),
      tenantId: tenantId.trim() || null,
    });
  };

  return (
    <div className="p-4 sm:p-6 space-y-4 max-w-6xl">
      <div>
        <div className="flex items-center gap-2 mb-0.5">
          <FlaskConical className="h-5 w-5 text-primary" />
          <h1 className="font-display text-[20px] font-bold text-foreground tracking-tight">Playground Interno</h1>
        </div>
        <p className="text-sm text-muted-foreground">
          Rode o interpretador com o provedor real e veja o que ele extraiu, quanto custou e por que falhou.
        </p>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4 items-start">
        {/* Input */}
        <div className="bg-card border border-border rounded-lg p-4 space-y-4">
          <div>
            <p className="text-xs text-muted-foreground mb-1.5">Exemplos rápidos:</p>
            <div className="flex flex-wrap gap-1.5">
              {EXAMPLE_TEXTS.map((ex, idx) => (
                <button
                  key={ex}
                  type="button"
                  onClick={() => { setText(ex); reset(); }}
                  className="px-2 py-1 rounded text-xs bg-muted hover:bg-muted/80 text-muted-foreground hover:text-foreground transition-colors border border-border"
                >
                  Exemplo {idx + 1}
                </button>
              ))}
            </div>
          </div>

          <div>
            <label htmlFor="playground-text" className="text-xs text-muted-foreground block mb-1">Texto do movimento</label>
            <textarea
              id="playground-text"
              value={text}
              maxLength={TEXT_MAX}
              onChange={e => setText(e.target.value)}
              rows={7}
              placeholder={"Cole aqui o texto do comprovante, Pix, nota fiscal ou qualquer descrição financeira..."}
              className="w-full text-sm font-mono resize-none rounded-md border border-input bg-background px-3 py-2 text-foreground placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring"
            />
            <p className="text-[11px] text-muted-foreground text-right tabular-nums">{text.length}/{TEXT_MAX}</p>
          </div>

          <div>
            <label htmlFor="playground-tenant" className="text-xs text-muted-foreground block mb-1">ID do tenant (opcional)</label>
            <Input id="playground-tenant" value={tenantId} onChange={e => setTenantId(e.target.value)}
              placeholder="Vazio = tenant padrão do playground" className="h-8 text-xs font-mono" />
          </div>

          <fieldset className="space-y-2">
            <legend className="text-xs text-muted-foreground mb-1">Provedor</legend>
            {providersQuery.isError && (
              <ApiErrorPanel error={providersQuery.error} title="Não foi possível consultar os provedores — só o modo automático está disponível" />
            )}
            <RadioGroup value={choice} onValueChange={setChoice} className="gap-1.5">
              <label className="flex items-start gap-2.5 rounded-md border border-border px-3 py-2 cursor-pointer">
                <RadioGroupItem value={AUTO} id="provider-auto" aria-label="Automático" className="mt-0.5" />
                <span className="text-sm">
                  <span className="font-medium text-foreground">Automático</span>
                  <span className="block text-[11px] text-muted-foreground">
                    {automaticOrder.length > 0
                      ? `Usa: ${automaticOrder.join(" → ")}`
                      : "Nenhum provedor disponível no modo automático."}
                  </span>
                </span>
              </label>
              {providers.map(p => {
                const available = p.status === "available";
                return (
                  <label
                    key={p.id}
                    className={`flex items-start gap-2.5 rounded-md border border-border px-3 py-2 ${available ? "cursor-pointer" : "opacity-60 cursor-not-allowed"}`}
                  >
                    <RadioGroupItem value={p.provider} id={`provider-${p.provider}`} disabled={!available}
                      aria-label={p.name} className="mt-0.5" />
                    <span className="text-sm min-w-0">
                      <span className="flex flex-wrap items-center gap-2">
                        <span className="font-medium text-foreground">{p.name}</span>
                        {p.modelId && <span className="text-[11px] font-mono text-muted-foreground">{p.modelId}</span>}
                        <ProviderStatusBadge status={p.status} />
                      </span>
                      {!available && <span className="block text-[11px] text-muted-foreground">{p.statusMessage}</span>}
                    </span>
                  </label>
                );
              })}
            </RadioGroup>
          </fieldset>

          <div className="flex gap-2">
            <Button onClick={handleAnalyze} disabled={isPending || !text.trim() || !choiceValid} className="flex-1" size="sm">
              {isPending ? (
                <span className="flex items-center gap-2">
                  <span className="h-3 w-3 rounded-full border-2 border-primary-foreground border-t-transparent animate-spin" />
                  Analisando...
                </span>
              ) : (
                <span className="flex items-center gap-2"><Send className="h-3.5 w-3.5" />Analisar</span>
              )}
            </Button>
            {(result || error) && (
              <Button variant="outline" size="sm" aria-label="Limpar" onClick={() => { reset(); setText(""); }}>
                <RotateCcw className="h-3.5 w-3.5" />
              </Button>
            )}
          </div>
        </div>

        {/* Results */}
        <div className="space-y-3">
          {!result && !error && !isPending && (
            <div className="bg-card border border-border border-dashed rounded-lg p-6 sm:p-8 text-center">
              <FlaskConical className="h-8 w-8 text-muted-foreground/40 mx-auto mb-2" />
              <p className="text-sm text-muted-foreground">Os resultados da análise aparecerão aqui.</p>
            </div>
          )}

          {isPending && (
            <div className="bg-card border border-border rounded-lg p-6 sm:p-8 text-center">
              <div className="h-6 w-6 rounded-full border-2 border-primary border-t-transparent animate-spin mx-auto mb-2" />
              <p className="text-sm text-muted-foreground">Processando...</p>
            </div>
          )}

          {error && !isPending && <ApiErrorPanel error={error} />}
          {result && !isPending && <ResultView result={result} />}
        </div>
      </div>
    </div>
  );
}
