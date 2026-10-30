import { useEffect, useMemo, useRef, useState } from "react";
import { Eraser, Undo2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { BuildManualZoneKind, BuildModelDto, BuildModelVersionDto, BuildModelZoneDto, BuildZonePoint } from "../../api/build-twin.api";
import { useSaveZone, useTwinState } from "../../hooks/use-build-twin";
import {
  MANUAL_ZONE_KINDS, MAX_OUTLINE_POINTS, ZONE_KINDS, normalizePoint, stateColor, twinErrorMessage, validateOutline, zoneStateIndex,
} from "../../lib/twin";
import { localToday } from "../../lib/format";
import { Card, selectClass } from "./common";

interface Props {
  projectId: string;
  sheets: Array<{ model: BuildModelDto; version: BuildModelVersionDto }>;
  zones: BuildModelZoneDto[];
}

const toPoints = (pts: BuildZonePoint[]) => pts.map((p) => `${p.x * 1000},${p.y * 1000}`).join(" ");

/**
 * Assisted mapping on a 2D sheet (PDF/image/DXF version): the user draws zone outlines (normalized 0..1, ≤ 200 points)
 * over the sheet image. The API has no route to read the sheet back, so the image is opened locally (same file); a PDF is
 * shown for reference only — drawing needs an image of the page. Saved outlines are colored by the bound stage's state.
 */
export function SheetMapper({ projectId, sheets, zones }: Props) {
  const [sheetId, setSheetId] = useState(sheets[0]?.version.id ?? "");
  const [local, setLocal] = useState<{ url: string; pdf: boolean } | null>(null);
  const [points, setPoints] = useState<BuildZonePoint[]>([]);
  const [name, setName] = useState("");
  const [kind, setKind] = useState<BuildManualZoneKind>("Room");
  const [error, setError] = useState<string | null>(null);
  const boxRef = useRef<HTMLDivElement>(null);
  const save = useSaveZone(projectId);
  const { data: state } = useTwinState(sheetId || null, localToday());
  const zoneStates = useMemo(() => zoneStateIndex(state), [state]);
  const outlined = zones.filter((z) => z.sheetVersionId === sheetId && z.outline && z.outline.length >= 3);

  useEffect(() => () => { if (local) URL.revokeObjectURL(local.url); }, [local]);

  const pick = (file: File | undefined) => {
    if (!file) return;
    setLocal({ url: URL.createObjectURL(file), pdf: file.type === "application/pdf" || /\.pdf$/i.test(file.name) });
    setPoints([]);
  };

  const addPoint = (e: React.MouseEvent<HTMLDivElement>) => {
    if (!boxRef.current || local?.pdf) return;
    if (points.length >= MAX_OUTLINE_POINTS) { setError(`No máximo ${MAX_OUTLINE_POINTS} pontos por zona.`); return; }
    const r = boxRef.current.getBoundingClientRect();
    setPoints([...points, normalizePoint(e.clientX - r.left, e.clientY - r.top, r.width, r.height)]);
    setError(null);
  };

  /** Keyboard: Backspace/Delete removes the last point, Escape clears the outline (points themselves need a pointer). */
  const onCanvasKey = (e: React.KeyboardEvent<HTMLDivElement>) => {
    if (e.key === "Backspace" || e.key === "Delete") { e.preventDefault(); setPoints(points.slice(0, -1)); }
    else if (e.key === "Escape") setPoints([]);
  };

  const submit = () => {
    const problem = validateOutline(points) ?? (name.trim() ? null : "Informe o nome da zona.");
    if (problem) { setError(problem); return; }
    save.mutate({ req: { kind, name: name.trim(), outline: points, sheetVersionId: sheetId } }, {
      onSuccess: () => { toast.success("Zona desenhada salva."); setPoints([]); setName(""); },
      onError: (e) => setError(twinErrorMessage(e, "Não foi possível salvar a zona.")),
    });
  };

  return (
    <Card title="Mapeamento sobre a planta (2D)">
      <p className="text-xs text-muted-foreground">Não é um modelo 3D: você desenha as zonas sobre a planta e vincula cada uma a uma etapa.</p>
      <div className="grid gap-2 sm:grid-cols-2">
        <div className="space-y-1">
          <Label htmlFor="sheet-version">Planta</Label>
          <select id="sheet-version" className={selectClass} value={sheetId} onChange={(e) => { setSheetId(e.target.value); setPoints([]); }}>
            {sheets.map(({ model, version }) => (
              <option key={version.id} value={version.id}>{model.name} · v{version.number} ({version.sourceFileName})</option>
            ))}
          </select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="sheet-file">Abrir a imagem da planta (do seu computador)</Label>
          <Input id="sheet-file" type="file" accept="image/png,image/jpeg,image/webp,application/pdf" onChange={(e) => pick(e.target.files?.[0])} />
        </div>
      </div>

      {local?.pdf && (
        <div className="space-y-1">
          <object data={local.url} type="application/pdf" className="h-80 w-full rounded-md border border-border" aria-label="Planta em PDF">
            <p className="p-2 text-xs text-muted-foreground">O navegador não exibe PDF aqui.</p>
          </object>
          <p className="text-xs text-muted-foreground">Para desenhar as zonas, abra uma imagem (PNG/JPEG) da página da planta.</p>
        </div>
      )}

      {local && !local.pdf && (
        <div className="space-y-2">
          <div ref={boxRef} onClick={addPoint} onKeyDown={onCanvasKey} tabIndex={0} role="application"
            aria-label="Planta: clique para adicionar pontos; Backspace desfaz o último, Esc limpa" className="relative w-full cursor-crosshair select-none overflow-hidden rounded-md border border-border"
            data-testid="sheet-canvas">
            <img src={local.url} alt="Planta da obra" className="block h-auto w-full" draggable={false} />
            <svg viewBox="0 0 1000 1000" preserveAspectRatio="none" className="pointer-events-none absolute inset-0 h-full w-full">
              {outlined.map((z) => {
                const c = stateColor(zoneStates.get(z.id) ?? null);
                return <polygon key={z.id} points={toPoints(z.outline!)} fill={c.color} fillOpacity={c.transparent ? 0.2 : 0.45} stroke={c.color} strokeWidth={3} />;
              })}
              {points.length > 0 && (
                <polyline points={toPoints(points.length >= 3 ? [...points, points[0]] : points)} fill="rgba(14,165,233,0.15)" stroke="#0ea5e9" strokeWidth={3} />
              )}
              {points.map((p, i) => <circle key={i} cx={p.x * 1000} cy={p.y * 1000} r={6} fill="#0ea5e9" />)}
            </svg>
          </div>
          <div className="flex flex-wrap items-end gap-2">
            <span className="text-xs text-muted-foreground">{points.length} ponto(s)</span>
            <Button type="button" size="sm" variant="outline" disabled={points.length === 0} onClick={() => setPoints(points.slice(0, -1))}><Undo2 className="mr-1 h-3.5 w-3.5" />Desfazer ponto</Button>
            <Button type="button" size="sm" variant="ghost" disabled={points.length === 0} onClick={() => setPoints([])}><Eraser className="mr-1 h-3.5 w-3.5" />Limpar</Button>
          </div>
          <div className="grid gap-2 sm:grid-cols-[1fr_12rem_auto] sm:items-end">
            <div className="space-y-1">
              <Label htmlFor="sheet-zone-name">Nome da zona</Label>
              <Input id="sheet-zone-name" value={name} maxLength={200} onChange={(e) => setName(e.target.value)} placeholder="Ex.: Cozinha" />
            </div>
            <div className="space-y-1">
              <Label htmlFor="sheet-zone-kind">Tipo</Label>
              <select id="sheet-zone-kind" className={selectClass} value={kind} onChange={(e) => setKind(e.target.value as BuildManualZoneKind)}>
                {MANUAL_ZONE_KINDS.map((k) => <option key={k} value={k}>{ZONE_KINDS[k]}</option>)}
              </select>
            </div>
            <Button type="button" onClick={submit} disabled={save.isPending}>Salvar zona</Button>
          </div>
        </div>
      )}
      {!local && <p className="text-xs text-muted-foreground">Abra a mesma imagem enviada como planta para desenhar por cima.</p>}
      {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
    </Card>
  );
}
