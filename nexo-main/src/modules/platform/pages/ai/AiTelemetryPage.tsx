import { useState } from "react";
import { ChevronDown, ChevronUp, AlertCircle, Check, GitBranch } from "lucide-react";
import { useAiTelemetry } from "../../hooks/useAiOperations";
import type { AiTelemetryRecord } from "../../types/aiOperations";

function formatCost(micros: number): string {
  if (micros === 0) return "Grátis";
  if (micros < 1000) return `$${micros}μ`;
  return `$${(micros / 1_000_000).toFixed(6)}`;
}

function ProviderChip({ provider }: { provider: string }) {
  const cls =
    provider === "RuleBased" ? "bg-blue-500/10 text-blue-600" :
    provider === "Claude"    ? "bg-violet-500/10 text-violet-600" :
    "bg-green-500/10 text-green-600";
  return (
    <span className={`inline-flex items-center px-1.5 py-0.5 rounded text-[10px] font-medium ${cls}`}>
      {provider}
    </span>
  );
}

function TelemetryRow({ rec }: { rec: AiTelemetryRecord }) {
  const [expanded, setExpanded] = useState(false);

  const ts = new Date(rec.createdAt);
  const timeStr = ts.toLocaleString("pt-BR", {
    day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit",
  });

  return (
    <>
      <tr
        onClick={() => setExpanded(v => !v)}
        className={`cursor-pointer transition-colors hover:bg-muted/50 ${expanded ? "bg-muted/30" : ""}`}
      >
        <td className="px-4 py-2.5 text-xs text-muted-foreground tabular-nums whitespace-nowrap">{timeStr}</td>
        <td className="px-4 py-2.5 text-xs text-foreground max-w-[120px] truncate">{rec.tenantName}</td>
        <td className="px-4 py-2.5"><ProviderChip provider={rec.provider} /></td>
        <td className="px-4 py-2.5 text-xs text-muted-foreground">{rec.operationType}</td>
        <td className="px-4 py-2.5 text-xs tabular-nums text-foreground">{rec.durationMs}ms</td>
        <td className="px-4 py-2.5 text-xs tabular-nums text-muted-foreground">
          {rec.inputTokens + rec.outputTokens > 0 ? `${rec.inputTokens + rec.outputTokens} tok` : "—"}
        </td>
        <td className="px-4 py-2.5 text-xs tabular-nums text-muted-foreground">{formatCost(rec.estimatedCostMicros)}</td>
        <td className="px-4 py-2.5">
          {rec.success ? (
            <span className="flex items-center gap-1 text-green-600 text-xs"><Check className="h-3 w-3" />OK</span>
          ) : (
            <span className="flex items-center gap-1 text-destructive text-xs"><AlertCircle className="h-3 w-3" />Erro</span>
          )}
        </td>
        <td className="px-4 py-2.5 text-muted-foreground">
          {expanded ? <ChevronUp className="h-3.5 w-3.5" /> : <ChevronDown className="h-3.5 w-3.5" />}
        </td>
      </tr>

      {expanded && (
        <tr className="bg-muted/20">
          <td colSpan={9} className="px-6 py-4">
            <div className="grid grid-cols-2 lg:grid-cols-4 gap-4 text-xs">
              <div>
                <p className="text-muted-foreground mb-1">Request ID</p>
                <p className="font-mono text-foreground">{rec.id}</p>
              </div>
              <div>
                <p className="text-muted-foreground mb-1">Prompt Type</p>
                <p className="font-mono text-foreground">{rec.promptType} v{rec.promptVersion}</p>
              </div>
              <div>
                <p className="text-muted-foreground mb-1">Prompt Hash</p>
                <p className="font-mono text-foreground">{rec.promptHash}</p>
              </div>
              <div>
                <p className="text-muted-foreground mb-1">Movement ID</p>
                <p className="font-mono text-foreground">{rec.movementId ?? "—"}</p>
              </div>
              <div>
                <p className="text-muted-foreground mb-1">Input Tokens</p>
                <p className="tabular-nums text-foreground">{rec.inputTokens}</p>
              </div>
              <div>
                <p className="text-muted-foreground mb-1">Output Tokens</p>
                <p className="tabular-nums text-foreground">{rec.outputTokens}</p>
              </div>
              <div>
                <p className="text-muted-foreground mb-1">Confidence (amt/date)</p>
                <p className="tabular-nums text-foreground">
                  {Math.round(rec.amountConfidence * 100)}% / {Math.round(rec.dateConfidence * 100)}%
                </p>
              </div>
              <div>
                <p className="text-muted-foreground mb-1">RequiresInput</p>
                <p className="tabular-nums text-foreground">{rec.requiresInputCount} campos</p>
              </div>
              {rec.analyzerChain.length > 1 && (
                <div className="col-span-2">
                  <p className="text-muted-foreground mb-1 flex items-center gap-1">
                    <GitBranch className="h-3 w-3" /> Analyzer Chain
                  </p>
                  <p className="font-mono text-foreground">{rec.analyzerChain.join(" → ")}</p>
                </div>
              )}
              {rec.fallbackUsed && (
                <div className="col-span-2">
                  <p className="text-muted-foreground mb-1">Fallback</p>
                  <p className="text-amber-600 font-medium">
                    {rec.fallbackFromProvider} → {rec.provider}
                  </p>
                </div>
              )}
              {rec.errorMessage && (
                <div className="col-span-4">
                  <p className="text-muted-foreground mb-1">Erro</p>
                  <p className="text-destructive font-mono">{rec.errorMessage}</p>
                </div>
              )}
              {!rec.rawPrompt && (
                <div className="col-span-4">
                  <p className="text-xs text-muted-foreground italic">
                    Prompt raw não disponível — ative <span className="font-mono">EnablePromptLogging</span> nas Feature Flags.
                  </p>
                </div>
              )}
            </div>
          </td>
        </tr>
      )}
    </>
  );
}

export default function AiTelemetryPage() {
  const [page, setPage] = useState(1);
  const { data, isLoading } = useAiTelemetry(page);

  const totalPages = data ? Math.ceil(data.total / 20) : 1;

  return (
    <div className="p-6 space-y-5 max-w-6xl">

      <div>
        <h1 className="text-xl font-semibold text-foreground">Telemetry Explorer</h1>
        <p className="text-sm text-muted-foreground mt-0.5">
          Inspetor de requests — clique em uma linha para ver os detalhes completos.
        </p>
      </div>

      <div className="bg-card border border-border rounded-lg overflow-hidden">
        <div className="px-4 py-3 border-b border-border bg-muted/30 flex items-center justify-between">
          <h2 className="text-sm font-medium text-foreground">
            Requests recentes
            {data && <span className="ml-2 text-muted-foreground font-normal">({data.total} total)</span>}
          </h2>
        </div>

        {isLoading ? (
          <div className="p-8 text-center text-sm text-muted-foreground">Carregando...</div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border bg-muted/20">
                  {["Horário","Tenant","Provider","Operação","Latência","Tokens","Custo","Status",""].map(h => (
                    <th key={h} className="text-left px-4 py-2 text-xs text-muted-foreground font-medium whitespace-nowrap">
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {data?.items.map(rec => <TelemetryRow key={rec.id} rec={rec} />)}
              </tbody>
            </table>
          </div>
        )}

        {/* Pagination */}
        {!isLoading && totalPages > 1 && (
          <div className="flex items-center justify-between px-4 py-3 border-t border-border">
            <p className="text-xs text-muted-foreground">
              Página {page} de {totalPages}
            </p>
            <div className="flex gap-2">
              <button
                disabled={page <= 1}
                onClick={() => setPage(p => p - 1)}
                className="px-3 py-1 text-xs rounded border border-border hover:bg-muted disabled:opacity-40 disabled:cursor-not-allowed"
              >
                Anterior
              </button>
              <button
                disabled={page >= totalPages}
                onClick={() => setPage(p => p + 1)}
                className="px-3 py-1 text-xs rounded border border-border hover:bg-muted disabled:opacity-40 disabled:cursor-not-allowed"
              >
                Próxima
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
