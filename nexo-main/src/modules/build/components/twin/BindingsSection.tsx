import { useMemo, useState } from "react";
import { Link2, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import type { BuildStageDto } from "../../api/build.api";
import type {
  BuildElementState, BuildModelStageBindingDto, BuildModelVersionDto, BuildModelZoneDto, BulkBuildBindingRequest,
} from "../../api/build-twin.api";
import { useBindings, useBulkBind, useDeleteBinding, useElements, useVersionZones } from "../../hooks/use-build-twin";
import { ELEMENT_STATES, STATE_META, ZONE_KINDS, twinErrorMessage } from "../../lib/twin";
import { localToday } from "../../lib/format";
import { filterBindRequest, selectionBindRequest } from "../../lib/twin-forms";
import { Card, StateDot, selectClass } from "./common";

const PAGE = 50;

function zoneLabel(z: BuildModelZoneDto) {
  return `${ZONE_KINDS[z.kind] ?? z.kind}: ${z.name}`;
}

interface Props {
  projectId: string;
  version: BuildModelVersionDto | null;
  stages: BuildStageDto[];
  coveragePercent: number | null;
}

/** Elements table (filters, multi-select → stage), bulk bind by category + zone, and the list of bindings. */
export function BindingsSection({ projectId, version, stages, coveragePercent }: Props) {
  const versionId = version?.id ?? null;
  const [category, setCategory] = useState("");
  const [zoneId, setZoneId] = useState("");
  const [state, setState] = useState<BuildElementState | "Unbound" | "">("");
  const [skip, setSkip] = useState(0);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [stageForSelection, setStageForSelection] = useState("");
  const [bulk, setBulk] = useState({ stageId: "", category: "", zoneId: "" });

  const today = localToday();
  const { data: page, isLoading, isError } = useElements(versionId, { category, zoneId, state, date: today, skip, take: PAGE });
  const { data: zones = [] } = useVersionZones(versionId);
  const bindBulk = useBulkBind(projectId);

  const resetPage = () => { setSkip(0); setSelected(new Set()); };
  const items = page?.items ?? [];
  const allOnPage = items.length > 0 && items.every((e) => selected.has(e.externalId));
  const stageName = useMemo(() => new Map(stages.map((s) => [s.id, s.name])), [stages]);

  const run = (req: BulkBuildBindingRequest, after?: () => void) => bindBulk.mutate(req, {
    onSuccess: (r) => { toast.success(`${r.created} elemento(s) vinculado(s)${r.replaced ? `, ${r.replaced} vínculo(s) substituído(s)` : ""}.`); after?.(); },
    onError: (e) => toast.error(twinErrorMessage(e, "Não foi possível vincular.")),
  });

  return (
    <div className="space-y-4">
      {coveragePercent != null && (
        <div className="rounded-md border border-border bg-card p-3" data-testid="binding-coverage">
          <div className="flex items-baseline justify-between gap-2">
            <p className="text-sm font-medium">Cobertura de vínculos</p>
            <p className="text-lg font-bold tabular-nums">{coveragePercent.toLocaleString("pt-BR", { maximumFractionDigits: 1 })}%</p>
          </div>
          <div className="mt-1 h-2 w-full overflow-hidden rounded-full bg-muted"><div className="h-full bg-primary" style={{ width: `${Math.min(100, coveragePercent)}%` }} /></div>
          <p className="mt-1 text-[11px] text-muted-foreground">Elemento sem etapa fica neutro: não conta como avanço nem como atraso.</p>
        </div>
      )}

      {!version ? (
        <p className="text-sm text-muted-foreground">Envie e processe um modelo com elementos para vincular as etapas.</p>
      ) : (
        <>
          <Card title="Vincular por categoria e zona">
            <p className="text-xs text-muted-foreground">Ex.: todas as paredes (IfcWall) do Térreo → Alvenaria. Os vínculos de elemento anteriores dos encontrados são substituídos.</p>
            <div className="grid gap-2 sm:grid-cols-3">
              <div className="space-y-1">
                <Label htmlFor="bulk-stage">Etapa</Label>
                <select id="bulk-stage" className={selectClass} value={bulk.stageId} onChange={(e) => setBulk({ ...bulk, stageId: e.target.value })}>
                  <option value="">Escolha…</option>
                  {stages.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
                </select>
              </div>
              <div className="space-y-1">
                <Label htmlFor="bulk-category">Categoria</Label>
                <select id="bulk-category" className={selectClass} value={bulk.category} onChange={(e) => setBulk({ ...bulk, category: e.target.value })}>
                  <option value="">Qualquer</option>
                  {(page?.categories ?? []).map((c) => <option key={c} value={c}>{c}</option>)}
                </select>
              </div>
              <div className="space-y-1">
                <Label htmlFor="bulk-zone">Zona</Label>
                <select id="bulk-zone" className={selectClass} value={bulk.zoneId} onChange={(e) => setBulk({ ...bulk, zoneId: e.target.value })}>
                  <option value="">Qualquer</option>
                  {zones.map((z) => <option key={z.id} value={z.id}>{zoneLabel(z)}</option>)}
                </select>
              </div>
            </div>
            <Button type="button" size="sm" disabled={!bulk.stageId || (!bulk.category && !bulk.zoneId) || bindBulk.isPending}
              onClick={() => run(filterBindRequest(bulk.stageId, version.id, bulk.category, bulk.zoneId))}>
              <Link2 className="mr-1.5 h-4 w-4" />Vincular em lote
            </Button>
          </Card>

          <Card title={`Elementos${page ? ` (${page.total})` : ""}`}>
            <div className="grid gap-2 sm:grid-cols-3">
              <select aria-label="Filtrar por categoria" className={selectClass} value={category} onChange={(e) => { setCategory(e.target.value); resetPage(); }}>
                <option value="">Todas as categorias</option>
                {(page?.categories ?? []).map((c) => <option key={c} value={c}>{c}</option>)}
              </select>
              <select aria-label="Filtrar por zona" className={selectClass} value={zoneId} onChange={(e) => { setZoneId(e.target.value); resetPage(); }}>
                <option value="">Todas as zonas</option>
                {zones.map((z) => <option key={z.id} value={z.id}>{zoneLabel(z)}</option>)}
              </select>
              <select aria-label="Filtrar por estado" className={selectClass} value={state}
                onChange={(e) => { setState(e.target.value as BuildElementState | "Unbound" | ""); resetPage(); }}>
                <option value="">Todos os estados</option>
                {ELEMENT_STATES.map((s) => <option key={s} value={s}>{STATE_META[s].label}</option>)}
                <option value="Unbound">Sem vínculo</option>
              </select>
            </div>

            <div className="flex flex-wrap items-end gap-2 rounded-md bg-muted/40 p-2">
              <span className="text-xs text-muted-foreground">{selected.size} selecionado(s)</span>
              <select aria-label="Etapa para os selecionados" className={`${selectClass} sm:w-56`} value={stageForSelection}
                onChange={(e) => setStageForSelection(e.target.value)}>
                <option value="">Etapa…</option>
                {stages.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
              </select>
              <Button type="button" size="sm" disabled={selected.size === 0 || !stageForSelection || bindBulk.isPending}
                onClick={() => run(selectionBindRequest(stageForSelection, version.id, selected), () => setSelected(new Set()))}>
                Vincular à etapa
              </Button>
            </div>

            {isError && <p role="alert" className="text-sm text-destructive">Não foi possível carregar os elementos.</p>}
            {isLoading && <p className="text-sm text-muted-foreground">Carregando…</p>}
            {page && items.length === 0 && <p className="text-sm text-muted-foreground">Nenhum elemento nesta seleção.</p>}
            {items.length > 0 && (
              <div className="max-w-full overflow-x-auto">
                <table className="w-full min-w-[36rem] text-sm">
                  <thead className="text-left text-xs text-muted-foreground">
                    <tr>
                      <th className="w-8 p-1.5">
                        <input type="checkbox" aria-label="Selecionar todos da página" checked={allOnPage}
                          onChange={(e) => {
                            const next = new Set(selected);
                            items.forEach((it) => (e.target.checked ? next.add(it.externalId) : next.delete(it.externalId)));
                            setSelected(next);
                          }} />
                      </th>
                      <th className="p-1.5">Elemento</th><th className="p-1.5">Categoria</th><th className="p-1.5">Zona</th>
                      <th className="p-1.5">Etapa</th><th className="p-1.5">Estado (hoje)</th>
                    </tr>
                  </thead>
                  <tbody>
                    {items.map((el) => (
                      <tr key={el.externalId} className="border-t border-border">
                        <td className="p-1.5">
                          <input type="checkbox" aria-label={`Selecionar ${el.name ?? el.externalId}`} checked={selected.has(el.externalId)}
                            onChange={(e) => {
                              const next = new Set(selected);
                              if (e.target.checked) next.add(el.externalId); else next.delete(el.externalId);
                              setSelected(next);
                            }} />
                        </td>
                        <td className="max-w-[14rem] truncate p-1.5" title={el.externalId}>{el.name ?? el.externalId}</td>
                        <td className="p-1.5">{el.category}</td>
                        <td className="p-1.5">{el.zoneName ?? "—"}</td>
                        <td className="p-1.5">{el.stageId ? stageName.get(el.stageId) ?? "—" : "—"}</td>
                        <td className="p-1.5"><StateDot state={el.state} />{el.explicitState && <span className="ml-1 text-[10px] text-muted-foreground">(registrado)</span>}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
            {page && page.total > PAGE && (
              <div className="flex items-center justify-between gap-2 text-xs">
                <span className="text-muted-foreground">{skip + 1}–{Math.min(skip + PAGE, page.total)} de {page.total}</span>
                <div className="flex gap-1">
                  <Button type="button" size="sm" variant="outline" disabled={skip === 0} onClick={() => setSkip(Math.max(0, skip - PAGE))}>Anterior</Button>
                  <Button type="button" size="sm" variant="outline" disabled={skip + PAGE >= page.total} onClick={() => setSkip(skip + PAGE)}>Próxima</Button>
                </div>
              </div>
            )}
          </Card>
        </>
      )}

      <BindingsList projectId={projectId} />
    </div>
  );
}

function bindingTarget(b: BuildModelStageBindingDto): string {
  if (b.scope === "Element") return `Elemento ${b.elementExternalId ?? ""}`;
  if (b.scope === "Zone") return "Zona";
  return `Categoria ${b.category ?? ""}`;
}

function BindingsList({ projectId }: { projectId: string }) {
  const { data: bindings = [], isLoading } = useBindings(projectId);
  const remove = useDeleteBinding(projectId);
  const [showAll, setShowAll] = useState(false);
  const shown = showAll ? bindings : bindings.slice(0, 50);
  return (
    <Card title={`Vínculos (${bindings.length})`}>
      {isLoading && <p className="text-sm text-muted-foreground">Carregando…</p>}
      {!isLoading && bindings.length === 0 && <p className="text-sm text-muted-foreground">Nenhum vínculo ainda.</p>}
      <ul className="divide-y divide-border">
        {shown.map((b) => (
          <li key={b.id} className="flex items-center justify-between gap-2 py-1.5 text-sm">
            <span className="min-w-0">
              <span className="font-medium">{b.stageName}</span>
              <span className="ml-1.5 break-all text-xs text-muted-foreground">← {bindingTarget(b)}</span>
              {b.source !== "Manual" && <span className="ml-1.5 text-[10px] uppercase text-muted-foreground">{b.source}</span>}
            </span>
            <Button type="button" size="icon" variant="ghost" aria-label={`Remover vínculo de ${b.stageName}`} disabled={remove.isPending}
              onClick={() => remove.mutate(b.id, { onError: (e) => toast.error(twinErrorMessage(e, "Não foi possível remover.")) })}>
              <Trash2 className="h-4 w-4" />
            </Button>
          </li>
        ))}
      </ul>
      {bindings.length > 50 && !showAll && (
        <Button type="button" size="sm" variant="outline" onClick={() => setShowAll(true)}>Mostrar todos</Button>
      )}
    </Card>
  );
}
