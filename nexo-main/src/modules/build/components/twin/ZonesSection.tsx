import { useMemo, useState } from "react";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { BuildStageDto } from "../../api/build.api";
import type {
  BuildElementState, BuildManualZoneKind, BuildModelDto, BuildModelZoneDto, SaveBuildZoneRequest,
} from "../../api/build-twin.api";
import {
  useBindings, useCreateBinding, useDeleteBinding, useDeleteZone, useProjectZones, useSaveZone, useTwinState,
} from "../../hooks/use-build-twin";
import { MANUAL_ZONE_KINDS, ZONE_KINDS, isSheetVersion, primaryVersion, twinErrorMessage, zoneStateIndex, zoneStatesToday } from "../../lib/twin";
import { localToday } from "../../lib/format";
import { EMPTY_ZONE_FORM as EMPTY, zoneRequest, type ZoneForm } from "../../lib/twin-forms";
import { Card, StateDot, selectClass } from "./common";
import { SheetMapper } from "./SheetMapper";
import { SimplifiedRepresentation } from "./SimplifiedRepresentation";

interface Props {
  projectId: string;
  models: BuildModelDto[];
  stages: BuildStageDto[];
}

/** Manual zones (simplified representation and assisted mapping) with their stage bindings and the 2D sheet. */
export function ZonesSection({ projectId, models, stages }: Props) {
  const today = localToday();
  const { data: zones = [], isLoading } = useProjectZones(projectId);
  const { data: bindings = [] } = useBindings(projectId);
  const save = useSaveZone(projectId);
  const remove = useDeleteZone(projectId);
  const bind = useCreateBinding(projectId);
  const unbind = useDeleteBinding(projectId);
  const [form, setForm] = useState<ZoneForm | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [bindStage, setBindStage] = useState<Record<string, string>>({});

  // any processed version answers the zone states (manual zones are included); without one, today's state from the stages
  const stateVersion = primaryVersion(models) ?? models.flatMap((m) => m.versions).find((v) => v.status === "Ready") ?? null;
  const { data: state } = useTwinState(stateVersion?.id ?? null, today);
  const zoneStates = useMemo<Map<string, BuildElementState>>(
    () => (stateVersion ? zoneStateIndex(state) : zoneStatesToday(zones, bindings, stages, today)),
    [stateVersion, state, zones, bindings, stages, today]);
  const sheets = models.flatMap((m) => m.versions.filter(isSheetVersion).map((v) => ({ model: m, version: v })));

  const submit = () => {
    if (!form) return;
    const req = zoneRequest(form);
    if (typeof req === "string") { setError(req); return; }
    setError(null);
    save.mutate({ id: form.id, req }, {
      onSuccess: () => { toast.success("Zona salva."); setForm(null); },
      onError: (e) => setError(twinErrorMessage(e, "Não foi possível salvar a zona.")),
    });
  };

  const edit = (z: BuildModelZoneDto) => setForm({
    id: z.id, kind: (MANUAL_ZONE_KINDS.includes(z.kind as BuildManualZoneKind) ? z.kind : "Area") as BuildManualZoneKind,
    name: z.name, order: String(z.order), parentZoneId: z.parentZoneId ?? "", withBox: !!z.simplifiedBox,
    width: z.simplifiedBox ? String(z.simplifiedBox.width) : "", depth: z.simplifiedBox ? String(z.simplifiedBox.depth) : "",
    height: z.simplifiedBox ? String(z.simplifiedBox.height) : "", elevation: z.simplifiedBox ? String(z.simplifiedBox.elevation) : "0",
  });

  return (
    <div className="space-y-4">
      <SimplifiedRepresentation zones={zones} zoneStates={zoneStates} />

      <Card title={`Zonas da obra (${zones.length})`}
        actions={!form && <Button type="button" size="sm" variant="outline" onClick={() => { setForm({ ...EMPTY }); setError(null); }}><Plus className="mr-1 h-4 w-4" />Nova zona</Button>}>
        <p className="text-xs text-muted-foreground">
          Sem projeto? Cadastre pavimentos e áreas com as medidas que você tem: viram caixas simples, coloridas pelo andamento da etapa vinculada.
        </p>

        {form && (
          <div className="space-y-2 rounded-md border border-border p-3" data-testid="zone-form">
            <div className="grid gap-2 sm:grid-cols-4">
              <div className="space-y-1">
                <Label htmlFor="zone-kind">Tipo</Label>
                <select id="zone-kind" className={selectClass} value={form.kind} onChange={(e) => setForm({ ...form, kind: e.target.value as BuildManualZoneKind })}>
                  {MANUAL_ZONE_KINDS.map((k) => <option key={k} value={k}>{ZONE_KINDS[k]}</option>)}
                </select>
              </div>
              <div className="space-y-1 sm:col-span-2">
                <Label htmlFor="zone-name">Nome</Label>
                <Input id="zone-name" value={form.name} maxLength={200} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="Ex.: Térreo" />
              </div>
              <div className="space-y-1">
                <Label htmlFor="zone-order">Ordem</Label>
                <Input id="zone-order" inputMode="numeric" value={form.order} onChange={(e) => setForm({ ...form, order: e.target.value })} />
              </div>
            </div>
            <div className="space-y-1">
              <Label htmlFor="zone-parent">Dentro de</Label>
              <select id="zone-parent" className={selectClass} value={form.parentZoneId} onChange={(e) => setForm({ ...form, parentZoneId: e.target.value })}>
                <option value="">— (zona principal)</option>
                {zones.filter((z) => z.id !== form.id).map((z) => <option key={z.id} value={z.id}>{z.name}</option>)}
              </select>
            </div>
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={form.withBox} onChange={(e) => setForm({ ...form, withBox: e.target.checked })} />
              Representar como caixa simplificada (medidas em metros)
            </label>
            {form.withBox && (
              <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
                {([["width", "Largura"], ["depth", "Profundidade"], ["height", "Altura"], ["elevation", "Cota (nível)"]] as const).map(([k, label]) => (
                  <div key={k} className="space-y-1">
                    <Label htmlFor={`zone-${k}`}>{label}</Label>
                    <Input id={`zone-${k}`} inputMode="decimal" value={form[k]} onChange={(e) => setForm({ ...form, [k]: e.target.value })} />
                  </div>
                ))}
              </div>
            )}
            {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
            <div className="flex gap-2">
              <Button type="button" size="sm" onClick={submit} disabled={save.isPending}>Salvar zona</Button>
              <Button type="button" size="sm" variant="ghost" onClick={() => setForm(null)}>Cancelar</Button>
            </div>
          </div>
        )}

        {isLoading && <p className="text-sm text-muted-foreground">Carregando…</p>}
        <ul className="divide-y divide-border">
          {zones.map((z) => {
            const zoneBindings = bindings.filter((b) => b.scope === "Zone" && b.zoneId === z.id);
            return (
              <li key={z.id} className="space-y-1.5 py-2">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="min-w-0">
                    <p className="break-words text-sm font-medium">{z.name} <span className="text-xs font-normal text-muted-foreground">· {ZONE_KINDS[z.kind] ?? z.kind}</span></p>
                    <p className="text-[11px] text-muted-foreground">
                      {z.simplifiedBox ? `${z.simplifiedBox.width} × ${z.simplifiedBox.depth} × ${z.simplifiedBox.height} m, cota ${z.simplifiedBox.elevation} m` : "sem caixa"}
                      {z.outline ? ` · contorno na planta (${z.outline.length} pontos)` : ""}
                    </p>
                  </div>
                  <div className="flex items-center gap-1">
                    <StateDot state={zoneStates.get(z.id) ?? null} />
                    <Button type="button" size="icon" variant="ghost" aria-label={`Editar ${z.name}`} onClick={() => edit(z)}><Pencil className="h-4 w-4" /></Button>
                    <Button type="button" size="icon" variant="ghost" aria-label={`Excluir ${z.name}`} disabled={remove.isPending}
                      onClick={() => remove.mutate(z.id, { onError: (e) => toast.error(twinErrorMessage(e, "Não foi possível excluir.")) })}>
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                </div>
                <div className="flex flex-wrap items-center gap-1.5">
                  {zoneBindings.map((b) => (
                    <span key={b.id} className="inline-flex items-center gap-1 rounded-full bg-muted px-2 py-0.5 text-xs">
                      {b.stageName}
                      <button type="button" aria-label={`Desvincular ${b.stageName} de ${z.name}`} className="text-muted-foreground hover:text-foreground"
                        onClick={() => unbind.mutate(b.id)}>×</button>
                    </span>
                  ))}
                  <select aria-label={`Etapa para ${z.name}`} className={`${selectClass} h-8 w-auto max-w-[12rem]`} value={bindStage[z.id] ?? ""}
                    onChange={(e) => setBindStage({ ...bindStage, [z.id]: e.target.value })}>
                    <option value="">Vincular etapa…</option>
                    {stages.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
                  </select>
                  <Button type="button" size="sm" variant="outline" className="h-8" disabled={!bindStage[z.id] || bind.isPending}
                    onClick={() => bind.mutate({ stageId: bindStage[z.id], scope: "Zone", zoneId: z.id }, {
                      onSuccess: () => setBindStage({ ...bindStage, [z.id]: "" }),
                      onError: (e) => toast.error(twinErrorMessage(e, "Não foi possível vincular.")),
                    })}>Vincular</Button>
                </div>
              </li>
            );
          })}
        </ul>
      </Card>

      {sheets.length > 0 && <SheetMapper projectId={projectId} sheets={sheets} zones={zones} />}
    </div>
  );
}
