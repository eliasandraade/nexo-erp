import { Suspense, useEffect, useMemo, useState } from "react";
import type { BuildProjectDetailsDto } from "../../api/build.api";
import type { BuildModelVersionDto } from "../../api/build-twin.api";
import { useModelViewer, useTwinState } from "../../hooks/use-build-twin";
import { addDays, daysBetween, legendRows, needsBrowserConversion, stateIndex } from "../../lib/twin";
import { fmtDate, localToday } from "../../lib/format";
import { SLIDER_DEBOUNCE_MS, timelineStart } from "../../lib/twin-forms";
import { cn } from "@/lib/utils";
import { Card, LazyModelViewer3D } from "./common";
import { ElementPanel } from "./ElementPanel";
import { IfcBrowserConvert } from "./IfcBrowserConvert";
import { ViewerBoundary, ViewerFallback } from "./viewer/ViewerFallback";


function useDebounced<T>(value: T, ms: number): T {
  const [v, setV] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setV(value), ms);
    return () => clearTimeout(t);
  }, [value, ms]);
  return v;
}

interface Props {
  projectId: string;
  project: BuildProjectDetailsDto;
  version: BuildModelVersionDto;
  build3DEnabled: boolean;
}

/** Viewer + time slider ("como estava a obra em X") + legend with counts + the selected element's panel. */
export function TwinViewerPanel({ projectId, project, version, build3DEnabled }: Props) {
  const today = localToday();
  const start = useMemo(() => {
    const s = timelineStart(project, version.createdAt);
    return s > today ? today : s;
  }, [project, version.createdAt, today]);
  const span = daysBetween(start, today);
  const [offset, setOffset] = useState(span);
  useEffect(() => setOffset(span), [span]);
  const date = addDays(start, Math.min(offset, span));
  const debouncedDate = useDebounced(date, SLIDER_DEBOUNCE_MS);

  const { data: state, isFetching } = useTwinState(version.id, debouncedDate);
  const states = useMemo(() => stateIndex(state), [state]);
  const [selected, setSelected] = useState<string | null>(null);

  const viewer = useModelViewer(version.id, version.hasViewer);
  const viewerUrl = viewer.data?.viewerUrl ?? null;

  return (
    <div className="grid min-w-0 gap-4 lg:grid-cols-[minmax(0,1fr)_20rem]">
      <div className="min-w-0 space-y-3">
        <div className="h-[22rem] min-w-0 overflow-hidden rounded-md border border-border bg-muted/20 sm:h-[28rem]">
          {version.hasViewer ? (
            viewerUrl ? (
              <ViewerBoundary>
                <Suspense fallback={<div className="grid h-full place-items-center text-sm text-muted-foreground">Carregando o visualizador 3D…</div>}>
                  <LazyModelViewer3D url={viewerUrl} sourceFormat={version.sourceFormat} states={states}
                    selectedId={selected} onSelect={setSelected} />
                </Suspense>
              </ViewerBoundary>
            ) : viewer.isError || (viewer.isSuccess && !viewerUrl) ? (
              <ViewerFallback reason="load" />
            ) : (
              <div className="grid h-full place-items-center text-sm text-muted-foreground">Preparando o link do visualizador…</div>
            )
          ) : needsBrowserConversion(version) ? (
            <IfcBrowserConvert projectId={projectId} version={version} enabled={build3DEnabled} />
          ) : (
            <div className="grid h-full place-items-center p-4 text-center text-sm text-muted-foreground">
              Esta versão não tem arquivo de visualização 3D. Os estados e vínculos continuam valendo para os elementos.
            </div>
          )}
        </div>

        <Card>
          <div className="space-y-2">
            <div className="flex flex-wrap items-baseline justify-between gap-2">
              <label htmlFor="twin-time-slider" className="text-sm font-medium">
                Como estava a obra em <span data-testid="twin-slider-date">{fmtDate(date)}</span>
              </label>
              {isFetching && <span className="text-[11px] text-muted-foreground">atualizando…</span>}
            </div>
            <input id="twin-time-slider" type="range" min={0} max={span} step={1} value={Math.min(offset, span)}
              aria-valuetext={fmtDate(date)} disabled={span === 0}
              onChange={(e) => setOffset(Number(e.target.value))} className="w-full accent-primary" />
            <div className="flex justify-between text-[11px] text-muted-foreground"><span>{fmtDate(start)}</span><span>Hoje</span></div>
          </div>
          <ul className="grid grid-cols-2 gap-x-3 gap-y-1 sm:grid-cols-4" aria-label="Legenda de estados" data-testid="twin-legend">
            {legendRows(state).map((r) => (
              <li key={r.key} className="flex items-center justify-between gap-2 text-xs">
                <span className="flex min-w-0 items-center gap-1.5">
                  <span className={cn("inline-block h-2.5 w-2.5 shrink-0 rounded-full", r.dot)} aria-hidden />
                  <span className="truncate">{r.label}</span>
                </span>
                <span className="tabular-nums font-semibold">{r.count}</span>
              </li>
            ))}
          </ul>
        </Card>
      </div>

      <ElementPanel projectId={projectId} version={version} externalId={selected} date={debouncedDate}
        state={selected ? states.get(selected) ?? null : null} stages={project.stages} onClose={() => setSelected(null)} />
    </div>
  );
}
