import { useState } from "react";
import { FlaskConical, Send, RotateCcw, ChevronDown, ChevronUp, Zap, Clock, Hash } from "lucide-react";
import { Button } from "@/components/ui/button";
import { usePlayground } from "../../hooks/useAiOperations";
import type { FieldStatus, AnalyzerProvider } from "../../types/aiOperations";

const STATUS_STYLE: Record<FieldStatus, string> = {
  AutoFilled:     "bg-green-500/10 text-green-600 border-green-200",
  NeedsAttention: "bg-amber-500/10 text-amber-600 border-amber-200",
  RequiresInput:  "bg-red-500/10   text-red-600   border-red-200",
};

const STATUS_LABEL: Record<FieldStatus, string> = {
  AutoFilled:     "Auto",
  NeedsAttention: "Atenção",
  RequiresInput:  "Manual",
};

function ConfidenceBar({ value }: { value: number }) {
  const pct = Math.round(value * 100);
  const color = pct >= 90 ? "bg-green-500" : pct >= 70 ? "bg-amber-500" : "bg-red-400";
  return (
    <div className="flex items-center gap-2">
      <div className="h-1.5 w-20 rounded-full bg-muted overflow-hidden">
        <div className={`h-full rounded-full ${color}`} style={{ width: `${pct}%` }} />
      </div>
      <span className="text-xs tabular-nums text-muted-foreground">{pct}%</span>
    </div>
  );
}

function Collapsible({ label, children }: { label: string; children: React.ReactNode }) {
  const [open, setOpen] = useState(false);
  return (
    <div className="border border-border rounded-lg overflow-hidden">
      <button
        onClick={() => setOpen(v => !v)}
        className="w-full flex items-center justify-between px-4 py-2.5 text-sm font-medium text-foreground hover:bg-muted/50 transition-colors"
      >
        {label}
        {open ? <ChevronUp className="h-4 w-4 text-muted-foreground" /> : <ChevronDown className="h-4 w-4 text-muted-foreground" />}
      </button>
      {open && (
        <div className="px-4 pb-4 pt-1 border-t border-border bg-muted/30">
          {children}
        </div>
      )}
    </div>
  );
}

const EXAMPLE_TEXTS = [
  "Pix enviado em 07/05/2026\npara: João Carlos da Silva\nR$ 850,00\nconta: Banco Nubank",
  "Pagamento fornecedor XPTO\nvalor: R$ 1.234,56\ndata: 05/05/2026",
  "Recebimento de cliente\nR$2500 dia 02/05/2026",
];

export default function AiPlaygroundPage() {
  const [text, setText]         = useState("");
  const [provider, setProvider] = useState<AnalyzerProvider | "auto">("auto");
  const { analyze, isPending, result, reset } = usePlayground();

  const handleAnalyze = () => {
    if (!text.trim()) return;
    analyze({ text, provider: provider === "auto" ? undefined : provider });
  };

  return (
    <div className="p-6 space-y-4 max-w-6xl">

      {/* Header */}
      <div>
        <div className="flex items-center gap-2 mb-0.5">
          <FlaskConical className="h-5 w-5 text-primary" />
          <h1 className="text-xl font-semibold text-foreground">Playground Interno</h1>
        </div>
        <p className="text-sm text-muted-foreground">
          Laboratório do Interpretation Engine — teste textos, analise extrações, inspecione resultados.
        </p>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4 items-start">

        {/* ── Input panel ────────────────────────────────────────────── */}
        <div className="space-y-3">
          <div className="bg-card border border-border rounded-lg p-4 space-y-3">
            <h2 className="text-sm font-medium text-foreground">Entrada</h2>

            {/* Exemplos rápidos */}
            <div>
              <p className="text-xs text-muted-foreground mb-1.5">Exemplos rápidos:</p>
              <div className="flex flex-wrap gap-1.5">
                {EXAMPLE_TEXTS.map((ex, i) => (
                  <button
                    key={i}
                    onClick={() => { setText(ex); reset(); }}
                    className="px-2 py-1 rounded text-xs bg-muted hover:bg-muted/80 text-muted-foreground hover:text-foreground transition-colors border border-border"
                  >
                    Exemplo {i + 1}
                  </button>
                ))}
              </div>
            </div>

            {/* Text input */}
            <div>
              <label className="text-xs text-muted-foreground block mb-1">Texto do movimento</label>
              <textarea
                value={text}
                onChange={e => { setText(e.target.value); reset(); }}
                rows={7}
                placeholder={"Cole aqui o texto do comprovante, Pix, nota fiscal ou qualquer descrição financeira...\n\nExemplo:\nPix enviado em 07/05/2026\npara: João Carlos\nR$ 850,00"}
                className="w-full text-sm font-mono resize-none rounded-md border border-input bg-background px-3 py-2 text-foreground placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring"
              />
            </div>

            {/* Provider selector */}
            <div>
              <label className="text-xs text-muted-foreground block mb-1">Analyzer / Provider</label>
              <div className="flex gap-2 flex-wrap">
                {(["auto","RuleBased","Claude","OpenAI"] as const).map(p => (
                  <button
                    key={p}
                    onClick={() => setProvider(p)}
                    className={`px-3 py-1.5 rounded-md text-xs font-medium border transition-colors ${
                      provider === p
                        ? "bg-primary text-primary-foreground border-primary"
                        : "bg-background text-muted-foreground border-border hover:text-foreground hover:bg-muted"
                    }`}
                  >
                    {p === "auto" ? "Auto (padrão)" : p}
                  </button>
                ))}
              </div>
            </div>

            {/* Actions */}
            <div className="flex gap-2 pt-1">
              <Button
                onClick={handleAnalyze}
                disabled={isPending || !text.trim()}
                className="flex-1"
                size="sm"
              >
                {isPending ? (
                  <span className="flex items-center gap-2">
                    <span className="h-3 w-3 rounded-full border-2 border-primary-foreground border-t-transparent animate-spin" />
                    Analisando...
                  </span>
                ) : (
                  <span className="flex items-center gap-2">
                    <Send className="h-3.5 w-3.5" />
                    Analisar
                  </span>
                )}
              </Button>
              {result && (
                <Button variant="outline" size="sm" onClick={() => { reset(); setText(""); }}>
                  <RotateCcw className="h-3.5 w-3.5" />
                </Button>
              )}
            </div>
          </div>
        </div>

        {/* ── Results panel ──────────────────────────────────────────── */}
        <div className="space-y-3">
          {!result && !isPending && (
            <div className="bg-card border border-border border-dashed rounded-lg p-8 text-center">
              <FlaskConical className="h-8 w-8 text-muted-foreground/40 mx-auto mb-2" />
              <p className="text-sm text-muted-foreground">
                Os resultados da análise aparecerão aqui.
              </p>
            </div>
          )}

          {isPending && (
            <div className="bg-card border border-border rounded-lg p-8 text-center">
              <div className="h-6 w-6 rounded-full border-2 border-primary border-t-transparent animate-spin mx-auto mb-2" />
              <p className="text-sm text-muted-foreground">Processando...</p>
            </div>
          )}

          {result && (
            <div className="space-y-3">

              {/* Metadata strip */}
              <div className="flex items-center gap-4 text-xs text-muted-foreground bg-muted/50 rounded-lg px-4 py-2.5 border border-border">
                <span className="flex items-center gap-1.5">
                  <Zap className="h-3.5 w-3.5" />
                  {result.analyzerChain.join(" → ")}
                </span>
                <span className="flex items-center gap-1.5">
                  <Clock className="h-3.5 w-3.5" />
                  {result.durationMs}ms
                </span>
                {result.tokenUsage && (
                  <span className="flex items-center gap-1.5">
                    <Hash className="h-3.5 w-3.5" />
                    {result.tokenUsage.input + result.tokenUsage.output} tokens
                  </span>
                )}
              </div>

              {/* Extraction fields */}
              <div className="bg-card border border-border rounded-lg overflow-hidden">
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
                    {([
                      ["Valor",   result.extraction.amount.value  !== null ? `R$ ${Number(result.extraction.amount.value).toFixed(2)}` : "—", result.extraction.amount.confidence,  result.extraction.amount.status],
                      ["Data",    result.extraction.date.value    ?? "—", result.extraction.date.confidence,    result.extraction.date.status],
                      ["Pagador", result.extraction.payee.value   ?? "—", result.extraction.payee.confidence,   result.extraction.payee.status],
                      ["Conta",   result.extraction.account.value ?? "—", result.extraction.account.confidence, result.extraction.account.status],
                    ] as [string, string, number, FieldStatus][]).map(([field, val, conf, status]) => (
                      <tr key={field}>
                        <td className="px-4 py-2.5 text-xs font-mono text-muted-foreground">{field}</td>
                        <td className="px-4 py-2.5 text-sm font-medium text-foreground">{val}</td>
                        <td className="px-4 py-2.5"><ConfidenceBar value={conf} /></td>
                        <td className="px-4 py-2.5">
                          <span className={`inline-flex items-center px-1.5 py-0.5 rounded text-[10px] font-medium border ${STATUS_STYLE[status]}`}>
                            {STATUS_LABEL[status]}
                          </span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {/* Suggestion */}
              <div className="bg-card border border-border rounded-lg overflow-hidden">
                <div className="px-4 py-2.5 border-b border-border bg-muted/30">
                  <h3 className="text-xs font-semibold text-foreground uppercase tracking-wide">Sugestão de Interpretação</h3>
                </div>
                <div className="divide-y divide-border">
                  {[
                    { label: "Direção",    value: result.suggestion.direction.value, source: result.suggestion.direction.source },
                    { label: "Natureza",   value: result.suggestion.nature.value,    source: result.suggestion.nature.source },
                    { label: "Categoria",  value: result.suggestion.category.value ?? "Não identificada", source: result.suggestion.category.source },
                    { label: "Contexto",   value: result.suggestion.context.contextType ?? "Não identificado", source: result.suggestion.context.source },
                  ].map(({ label, value, source }) => (
                    <div key={label} className="flex items-center justify-between px-4 py-2.5">
                      <span className="text-xs text-muted-foreground w-24 shrink-0">{label}</span>
                      <span className="text-sm font-medium text-foreground flex-1">{value}</span>
                      <span className="text-[10px] text-muted-foreground bg-muted px-1.5 py-0.5 rounded">{source}</span>
                    </div>
                  ))}
                </div>
              </div>

              {/* Collapsible sections */}
              {result.rawPrompt && (
                <Collapsible label="Prompt enviado">
                  <pre className="text-xs font-mono text-muted-foreground whitespace-pre-wrap overflow-x-auto max-h-48">
                    {result.rawPrompt}
                  </pre>
                </Collapsible>
              )}

              {result.rawResponse && (
                <Collapsible label="Resposta crua do provider">
                  <pre className="text-xs font-mono text-muted-foreground whitespace-pre-wrap overflow-x-auto max-h-48">
                    {result.rawResponse}
                  </pre>
                </Collapsible>
              )}

              {result.tokenUsage && (
                <Collapsible label="Token usage">
                  <div className="flex gap-6 text-sm">
                    <div>
                      <p className="text-xs text-muted-foreground">Input</p>
                      <p className="font-medium tabular-nums">{result.tokenUsage.input}</p>
                    </div>
                    <div>
                      <p className="text-xs text-muted-foreground">Output</p>
                      <p className="font-medium tabular-nums">{result.tokenUsage.output}</p>
                    </div>
                    <div>
                      <p className="text-xs text-muted-foreground">Total</p>
                      <p className="font-medium tabular-nums">{result.tokenUsage.input + result.tokenUsage.output}</p>
                    </div>
                  </div>
                </Collapsible>
              )}

              {!result.rawPrompt && !result.tokenUsage && (
                <p className="text-xs text-muted-foreground px-1">
                  Prompt raw e token usage disponíveis quando <span className="font-mono">EnablePromptLogging = true</span>.
                </p>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
