import { useState } from "react";
import { X } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import type { BuildStageDto } from "../../api/build.api";
import type { BuildElementState, BuildModelVersionDto } from "../../api/build-twin.api";
import { useElementEvents, useElementLookup, useRecordElementEvents } from "../../hooks/use-build-twin";
import { ELEMENT_STATES, STATE_META, twinErrorMessage } from "../../lib/twin";
import { fmtDate, fmtDateTime, localToday } from "../../lib/format";
import { Card, StateDot, selectClass } from "./common";

const QUANTITY_LABELS: Record<string, string> = { netVolume: "Volume (m³)", netArea: "Área (m²)", length: "Comprimento (m)", count: "Quantidade" };

function quantities(props: Record<string, unknown> | null | undefined): Array<[string, string]> {
  if (!props) return [];
  return Object.entries(props)
    .filter(([, v]) => typeof v === "number" || typeof v === "string")
    .slice(0, 12)
    .map(([k, v]) => [QUANTITY_LABELS[k] ?? k, typeof v === "number" ? v.toLocaleString("pt-BR", { maximumFractionDigits: 3 }) : String(v)]);
}

interface Props {
  projectId: string;
  version: BuildModelVersionDto;
  externalId: string | null;
  date: string;
  state: BuildElementState | null;
  stages: BuildStageDto[];
  onClose: () => void;
}

/** The clicked element: what the model says, its stage, its state on the date, explicit history and "registrar estado". */
export function ElementPanel({ projectId, version, externalId, date, state, stages, onClose }: Props) {
  const lookup = useElementLookup(version.id, externalId, date);
  const events = useElementEvents(version.id, externalId);
  const record = useRecordElementEvents(projectId);
  const [form, setForm] = useState<{ state: BuildElementState; note: string; effectiveDate: string }>(
    { state: "Completed", note: "", effectiveDate: localToday() });

  if (!externalId) {
    return (
      <Card title="Elemento">
        <p className="text-sm text-muted-foreground">Toque ou clique em um elemento do modelo para ver a etapa, o estado e o histórico.</p>
      </Card>
    );
  }

  const el = lookup.data;
  const stage = el?.stageId ? stages.find((s) => s.id === el.stageId) : null;
  const shownState = el?.state ?? state;

  return (
    <Card title={<span className="break-all">{el?.name ?? externalId}</span>}
      actions={<Button type="button" size="icon" variant="ghost" aria-label="Fechar elemento" onClick={onClose}><X className="h-4 w-4" /></Button>}>
      <dl className="grid grid-cols-2 gap-2 text-sm" data-testid="element-panel">
        <div className="col-span-2"><dt className="text-xs text-muted-foreground">Estado em {fmtDate(date)}</dt><dd><StateDot state={shownState} /></dd></div>
        <div><dt className="text-xs text-muted-foreground">Categoria</dt><dd>{el?.category ?? (lookup.isLoading ? "…" : "—")}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Zona</dt><dd>{el?.zoneName ?? "—"}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Tipo</dt><dd>{el?.typeName ?? "—"}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Material</dt><dd>{el?.material ?? "—"}</dd></div>
        <div className="col-span-2"><dt className="text-xs text-muted-foreground">Etapa vinculada</dt><dd>{stage?.name ?? "Sem vínculo"}</dd></div>
        {quantities(el?.properties).map(([k, v]) => (
          <div key={k}><dt className="text-xs text-muted-foreground">{k}</dt><dd className="tabular-nums">{v}</dd></div>
        ))}
        <div className="col-span-2"><dt className="text-xs text-muted-foreground">Id externo</dt><dd className="break-all font-mono text-[11px]">{externalId}</dd></div>
      </dl>
      {lookup.isSuccess && !el && (
        <p className="text-[11px] text-muted-foreground">Detalhes não encontrados nos primeiros elementos desta versão.</p>
      )}

      <form className="space-y-2 border-t border-border pt-3" onSubmit={(e) => {
        e.preventDefault();
        record.mutate({ modelVersionId: version.id, externalIds: [externalId], state: form.state,
          note: form.note.trim() || null, effectiveDate: form.effectiveDate || null }, {
          onSuccess: () => { toast.success("Estado registrado."); setForm((f) => ({ ...f, note: "" })); },
          onError: (err) => toast.error(twinErrorMessage(err, "Não foi possível registrar o estado.")),
        });
      }}>
        <p className="text-xs font-semibold">Registrar estado</p>
        <div className="grid grid-cols-2 gap-2">
          <div className="space-y-1">
            <Label htmlFor="element-state">Estado</Label>
            <select id="element-state" className={selectClass} value={form.state}
              onChange={(e) => setForm({ ...form, state: e.target.value as BuildElementState })}>
              {ELEMENT_STATES.map((s) => <option key={s} value={s}>{STATE_META[s].label}</option>)}
            </select>
          </div>
          <div className="space-y-1">
            <Label htmlFor="element-date">A partir de</Label>
            <Input id="element-date" type="date" max={localToday()} value={form.effectiveDate}
              onChange={(e) => setForm({ ...form, effectiveDate: e.target.value })} />
          </div>
        </div>
        <div className="space-y-1">
          <Label htmlFor="element-note">Observação</Label>
          <Textarea id="element-note" rows={2} maxLength={1000} value={form.note} onChange={(e) => setForm({ ...form, note: e.target.value })} />
        </div>
        <p className="text-[11px] text-muted-foreground">Não muda o % da etapa: vale só para este elemento, a partir da data.</p>
        <Button type="submit" size="sm" disabled={record.isPending}>Registrar estado</Button>
      </form>

      <div className="space-y-1 border-t border-border pt-3">
        <p className="text-xs font-semibold">Histórico registrado</p>
        {(events.data ?? []).length === 0
          ? <p className="text-xs text-muted-foreground">Nenhum estado registrado à mão.</p>
          : (
            <ul className="space-y-1.5">
              {events.data!.map((ev) => (
                <li key={ev.id} className="text-xs">
                  <StateDot state={ev.state} /> <span className="text-muted-foreground">desde {fmtDate(ev.effectiveDate)} · {ev.recordedByName} · {fmtDateTime(ev.recordedAt)}</span>
                  {ev.note && <p className="break-words text-muted-foreground">{ev.note}</p>}
                </li>
              ))}
            </ul>
          )}
      </div>
    </Card>
  );
}
