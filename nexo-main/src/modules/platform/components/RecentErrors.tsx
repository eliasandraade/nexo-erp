import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, ChevronDown, ChevronRight } from "lucide-react";
import { fetchPlatformErrors, type PlatformErrorEvent } from "../services/platformApi";

const DAYS = [1, 7, 30] as const;

function ErrorRow({ error }: Readonly<{ error: PlatformErrorEvent }>) {
  const [open, setOpen] = useState(false);
  const detailsId = `error-${error.id}`;
  const route = error.route === "unmatched" ? "rota não identificada" : error.route;
  return (
    <li className="border-b border-border last:border-0">
      <button
        type="button"
        onClick={() => setOpen(!open)}
        aria-expanded={open}
        aria-controls={detailsId}
        className="flex w-full items-start gap-2 px-4 py-3 text-left hover:bg-muted/40"
      >
        {open ? <ChevronDown className="mt-0.5 h-4 w-4 shrink-0" /> : <ChevronRight className="mt-0.5 h-4 w-4 shrink-0" />}
        <span className="min-w-0 flex-1">
          <span className="block break-words text-sm font-medium text-foreground">{error.summary}</span>
          <span className="block break-all font-mono text-xs text-muted-foreground">{error.method} {route}</span>
        </span>
        <span className="shrink-0 text-right text-xs text-muted-foreground tabular-nums">
          <span className="block font-semibold text-foreground">{error.count}×</span>
          {new Date(error.lastSeenAt).toLocaleString("pt-BR")}
        </span>
      </button>
      {open && (
        <div id={detailsId} className="space-y-2 px-4 pb-3 text-xs text-muted-foreground">
          <p>Primeira vez: {new Date(error.firstSeenAt).toLocaleString("pt-BR")}</p>
          {error.lastRequestId && <p>Último código: <code className="break-all">{error.lastRequestId}</code></p>}
          {error.lastTenantId && <p>Último tenant: <code className="break-all">{error.lastTenantId}</code></p>}
          <pre className="max-h-64 overflow-auto whitespace-pre-wrap break-all rounded bg-muted p-2 font-mono text-[11px]">{error.stackTop}</pre>
        </div>
      )}
    </li>
  );
}

/** Erros inesperados (HTTP 500) agrupados por tipo, os mais recentes primeiro. */
export function RecentErrors() {
  const [days, setDays] = useState<(typeof DAYS)[number]>(7);
  const { data, isLoading, isError } = useQuery({
    queryKey: ["platform", "errors", days],
    queryFn: () => fetchPlatformErrors(days),
    staleTime: 30_000,
  });

  return (
    <section className="overflow-hidden rounded-lg border border-border bg-card" aria-labelledby="recent-errors-title">
      <div className="flex flex-wrap items-center gap-3 border-b border-border px-4 py-3">
        <AlertTriangle className="h-4 w-4 text-primary" />
        <h2 id="recent-errors-title" className="flex-1 text-sm font-medium text-foreground">Erros inesperados</h2>
        <div className="flex gap-1" role="group" aria-label="Período">
          {DAYS.map((d) => (
            <button
              key={d}
              type="button"
              aria-pressed={days === d}
              onClick={() => setDays(d)}
              className={`rounded px-2 py-1 text-xs font-medium ${days === d ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:bg-muted"}`}
            >
              {d === 1 ? "24 h" : `${d} dias`}
            </button>
          ))}
        </div>
      </div>
      {isLoading && <p className="p-4 text-sm text-muted-foreground">Carregando…</p>}
      {isError && <p className="p-4 text-sm text-destructive">Não foi possível carregar os erros.</p>}
      {data?.length === 0 && <p className="p-4 text-sm text-muted-foreground">Nenhum erro inesperado no período.</p>}
      {!!data?.length && <ul>{data.map((e) => <ErrorRow key={e.id} error={e} />)}</ul>}
    </section>
  );
}
