import { useState } from "react";
import { RefreshCw, Play, ChevronDown, ChevronRight, Activity, Server } from "lucide-react";
import { cn } from "@/lib/utils";
import { useApiEndpoints, usePlatformHealth } from "../hooks/usePlatformSystem";
import { apiClient } from "@/services/api-client";
import type { ApiEndpoint } from "../types";

// ─── Method badge ─────────────────────────────────────────────────────────────

const METHOD_COLORS: Record<string, string> = {
  GET:    "bg-green-500/15 text-green-600",
  POST:   "bg-blue-500/15 text-blue-600",
  PUT:    "bg-amber-500/15 text-amber-600",
  PATCH:  "bg-orange-500/15 text-orange-600",
  DELETE: "bg-red-500/15 text-red-600",
};

function MethodBadge({ method }: { method: string }) {
  return (
    <span className={cn(
      "inline-block px-2 py-0.5 rounded text-[10px] font-bold uppercase font-mono min-w-[48px] text-center",
      METHOD_COLORS[method] ?? "bg-muted text-muted-foreground"
    )}>
      {method}
    </span>
  );
}

// ─── Status dot ───────────────────────────────────────────────────────────────

function StatusDot({ status }: { status: string }) {
  const color =
    status === "healthy"   ? "bg-green-500" :
    status === "degraded"  ? "bg-amber-500" :
    "bg-red-500";
  return <span className={`inline-block w-2 h-2 rounded-full ${color} shrink-0 animate-pulse`} />;
}

// ─── Endpoint row ─────────────────────────────────────────────────────────────

function EndpointRow({ endpoint }: { endpoint: ApiEndpoint }) {
  const [expanded, setExpanded] = useState(false);
  const [running, setRunning]   = useState(false);
  const [result, setResult]     = useState<{ status: number; body: string; latencyMs: number } | null>(null);

  const canRun = endpoint.method === "GET";

  const run = async () => {
    if (!canRun || running) return;
    setRunning(true);
    setResult(null);
    const t0 = Date.now();
    try {
      // apiClient prepends the base URL which already has /api — strip it
      const relPath = endpoint.path.replace(/^\/api/, "");
      const body = await apiClient.get<unknown>(relPath);
      setResult({ status: 200, body: JSON.stringify(body, null, 2), latencyMs: Date.now() - t0 });
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setResult({ status: 0, body: msg, latencyMs: Date.now() - t0 });
    } finally {
      setRunning(false);
      setExpanded(true);
    }
  };

  return (
    <div className="border-b border-border last:border-0">
      <div className="flex items-center gap-3 px-4 py-3 hover:bg-muted/30 transition-colors">
        <MethodBadge method={endpoint.method} />
        <code className="flex-1 text-xs text-foreground font-mono truncate">{endpoint.path}</code>
        <span className="text-xs text-muted-foreground hidden sm:block">{endpoint.controller}</span>
        <div className="flex items-center gap-2">
          {canRun && (
            <button
              onClick={run}
              disabled={running}
              className="flex items-center gap-1 h-6 px-2 rounded border border-border text-xs text-muted-foreground hover:text-foreground hover:bg-muted transition-colors disabled:opacity-50"
            >
              {running ? <RefreshCw className="h-3 w-3 animate-spin" /> : <Play className="h-3 w-3" />}
              {running ? "..." : "Run"}
            </button>
          )}
          <button
            onClick={() => setExpanded(v => !v)}
            className="p-1 rounded hover:bg-muted transition-colors text-muted-foreground"
          >
            {expanded ? <ChevronDown className="h-3.5 w-3.5" /> : <ChevronRight className="h-3.5 w-3.5" />}
          </button>
        </div>
      </div>

      {expanded && (
        <div className="px-4 pb-4 space-y-2">
          {endpoint.description && (
            <p className="text-xs text-muted-foreground">{endpoint.description}</p>
          )}
          {result && (
            <div className="rounded-lg bg-muted/50 border border-border overflow-hidden">
              <div className="flex items-center justify-between px-3 py-1.5 border-b border-border bg-muted/30">
                <span className={cn(
                  "text-xs font-semibold",
                  result.status === 200 ? "text-green-600" : "text-red-500"
                )}>
                  {result.status === 200 ? `✓ 200 OK` : `✗ Error`}
                </span>
                <span className="text-xs text-muted-foreground tabular-nums">{result.latencyMs}ms</span>
              </div>
              <pre className="text-xs text-foreground p-3 overflow-x-auto max-h-48 overflow-y-auto leading-relaxed">
                {result.body}
              </pre>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

// ─── Page ─────────────────────────────────────────────────────────────────────

export default function PlatformSystemPage() {
  const { data: health, refetch: refetchHealth, isFetching: healthFetching } = usePlatformHealth();
  const { data: endpoints, isLoading: endpointsLoading } = useApiEndpoints();

  const [search, setSearch] = useState("");
  const [methodFilter, setMethodFilter] = useState<string>("ALL");

  const filtered = (endpoints ?? []).filter(e => {
    const matchMethod = methodFilter === "ALL" || e.method === methodFilter;
    const matchSearch = !search ||
      e.path.toLowerCase().includes(search.toLowerCase()) ||
      e.controller.toLowerCase().includes(search.toLowerCase());
    return matchMethod && matchSearch;
  });

  const groupedByController = filtered.reduce<Record<string, ApiEndpoint[]>>((acc, e) => {
    (acc[e.controller] ??= []).push(e);
    return acc;
  }, {});

  return (
    <div className="p-6 space-y-6 max-w-5xl">

      <div>
        <h1 className="text-xl font-semibold text-foreground">Sistema</h1>
        <p className="text-sm text-muted-foreground mt-0.5">Saúde da infraestrutura e endpoints da API</p>
      </div>

      {/* Health cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div className="bg-card border border-border rounded-lg p-4">
          <div className="flex items-center justify-between mb-3">
            <div className="flex items-center gap-2">
              <Activity className="h-4 w-4 text-primary" />
              <h2 className="text-sm font-medium text-foreground">Status do sistema</h2>
            </div>
            <button
              onClick={() => refetchHealth()}
              disabled={healthFetching}
              className="p-1 rounded hover:bg-muted transition-colors"
            >
              <RefreshCw className={cn("h-3.5 w-3.5 text-muted-foreground", healthFetching && "animate-spin")} />
            </button>
          </div>

          {!health ? (
            <p className="text-xs text-muted-foreground">Verificando...</p>
          ) : (
            <div className="space-y-3">
              <div className="flex items-center gap-2">
                <StatusDot status={health.status} />
                <span className={cn(
                  "text-sm font-medium",
                  health.status === "healthy" ? "text-green-600" :
                  health.status === "degraded" ? "text-amber-600" : "text-red-600"
                )}>
                  {health.status === "healthy"  ? "Todos os sistemas operacionais" :
                   health.status === "degraded" ? "Sistema degradado" : "Instabilidade detectada"}
                </span>
              </div>
              <div className="text-xs text-muted-foreground">
                Verificado em {new Date(health.timestamp).toLocaleTimeString("pt-BR")}
              </div>
              <div className="space-y-2 pt-1">
                {health.checks.map(c => (
                  <div key={c.name} className="flex items-center justify-between">
                    <div className="flex items-center gap-2">
                      <StatusDot status={c.status} />
                      <span className="text-sm text-foreground capitalize">{c.name}</span>
                    </div>
                    <span className="text-xs text-muted-foreground tabular-nums">
                      {c.latencyMs > 1 ? `${c.latencyMs}ms` : c.latencyMs === 1 ? "< 1ms" : "—"}
                    </span>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>

        <div className="bg-card border border-border rounded-lg p-4">
          <div className="flex items-center gap-2 mb-3">
            <Server className="h-4 w-4 text-primary" />
            <h2 className="text-sm font-medium text-foreground">Endpoints registrados</h2>
          </div>
          {endpointsLoading ? (
            <p className="text-xs text-muted-foreground">Carregando...</p>
          ) : (
            <div className="space-y-2">
              {["GET", "POST", "PUT", "DELETE", "PATCH"].map(m => {
                const count = (endpoints ?? []).filter(e => e.method === m).length;
                return count > 0 ? (
                  <div key={m} className="flex items-center justify-between">
                    <MethodBadge method={m} />
                    <span className="text-sm text-muted-foreground tabular-nums">{count} endpoints</span>
                  </div>
                ) : null;
              })}
              <div className="pt-1 border-t border-border text-xs text-muted-foreground">
                Total: {endpoints?.length ?? 0} endpoints
              </div>
            </div>
          )}
        </div>
      </div>

      {/* Endpoints explorer */}
      <div className="bg-card border border-border rounded-lg overflow-hidden">
        <div className="px-4 py-3 border-b border-border flex flex-wrap items-center gap-3">
          <h2 className="text-sm font-medium text-foreground flex-1">Endpoints</h2>

          {/* Method filter */}
          <div className="flex gap-1">
            {["ALL", "GET", "POST", "PUT", "DELETE"].map(m => (
              <button
                key={m}
                onClick={() => setMethodFilter(m)}
                className={cn(
                  "px-2 py-1 rounded text-xs font-medium transition-colors",
                  methodFilter === m ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:bg-muted"
                )}
              >
                {m}
              </button>
            ))}
          </div>

          {/* Search */}
          <input
            value={search}
            onChange={e => setSearch(e.target.value)}
            placeholder="Buscar endpoint..."
            className="h-7 px-3 rounded-lg bg-muted border-none text-xs placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary/20 w-44"
          />
        </div>

        {endpointsLoading ? (
          <div className="p-8 text-center text-sm text-muted-foreground">Carregando endpoints...</div>
        ) : filtered.length === 0 ? (
          <div className="p-8 text-center text-sm text-muted-foreground">Nenhum endpoint encontrado.</div>
        ) : (
          <div>
            {Object.entries(groupedByController).map(([controller, eps]) => (
              <div key={controller}>
                <div className="px-4 py-2 bg-muted/30 border-b border-border">
                  <span className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">
                    {controller}
                  </span>
                </div>
                {eps.map((e, i) => <EndpointRow key={`${e.method}${e.path}${i}`} endpoint={e} />)}
              </div>
            ))}
          </div>
        )}
      </div>

    </div>
  );
}
