import { Suspense, useState } from "react";
import { Download, Link2, Orbit, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import type { BuildStageDto } from "../../api/build.api";
import {
  uploadRealityCapture, type BuildModelZoneDto, type BuildRealityCaptureDto, type BuildRealityCaptureType, type RealityCaptureFilters,
} from "../../api/build-twin.api";
import {
  useDeleteRealityCapture, useInvalidateTwin, useRealityCaptures, useSetCaptureVisibility, useUpdateRealityCapture,
} from "../../hooks/use-build-twin";
import { CAPTURE_TYPES, captureLimitBytes, formatBytes, twinErrorMessage } from "../../lib/twin";
import { fmtDateTime, localToday } from "../../lib/format";
import { Card, LazyPano360Viewer, ProgressBar, selectClass } from "./common";
import { ViewerBoundary } from "./viewer/ViewerFallback";

const TYPES = Object.keys(CAPTURE_TYPES) as BuildRealityCaptureType[];

interface Props {
  projectId: string;
  stages: BuildStageDto[];
  zones: BuildModelZoneDto[];
}

/** Photos, 360°, videos, drone and point clouds of the obra — private, linked to stage/zone/element, client-visible when marked. */
export function RealityCaptureSection({ projectId, stages, zones }: Props) {
  const [filters, setFilters] = useState<RealityCaptureFilters>({});
  const { data: captures = [], isLoading, isError } = useRealityCaptures(projectId, filters);
  const [uploading, setUploading] = useState(false);
  const [pano, setPano] = useState<BuildRealityCaptureDto | null>(null);
  const stageName = new Map(stages.map((s) => [s.id, s.name]));
  const zoneName = new Map(zones.map((z) => [z.id, z.name]));

  return (
    <div className="space-y-4">
      <Card title="Captura da realidade"
        actions={!uploading && <Button type="button" size="sm" onClick={() => setUploading(true)}><Plus className="mr-1 h-4 w-4" />Enviar captura</Button>}>
        <p className="text-xs text-muted-foreground">
          Arquivos privados. O cliente só vê as capturas marcadas como visíveis, e só com "Captura da realidade" ligada no portal.
        </p>
        {uploading && <CaptureUploadForm projectId={projectId} stages={stages} zones={zones} onDone={() => setUploading(false)} />}
        <div className="grid gap-2 sm:grid-cols-5">
          <select aria-label="Filtrar por tipo" className={selectClass} value={filters.type ?? ""}
            onChange={(e) => setFilters({ ...filters, type: e.target.value as BuildRealityCaptureType | "" })}>
            <option value="">Todos os tipos</option>
            {TYPES.map((t) => <option key={t} value={t}>{CAPTURE_TYPES[t].label}</option>)}
          </select>
          <select aria-label="Filtrar por etapa" className={selectClass} value={filters.stageId ?? ""} onChange={(e) => setFilters({ ...filters, stageId: e.target.value || undefined })}>
            <option value="">Todas as etapas</option>
            {stages.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
          <select aria-label="Filtrar por zona" className={selectClass} value={filters.zoneId ?? ""} onChange={(e) => setFilters({ ...filters, zoneId: e.target.value || undefined })}>
            <option value="">Todas as zonas</option>
            {zones.map((z) => <option key={z.id} value={z.id}>{z.name}</option>)}
          </select>
          <Input aria-label="De" type="date" value={filters.from ?? ""} max={localToday()} onChange={(e) => setFilters({ ...filters, from: e.target.value || undefined })} />
          <Input aria-label="Até" type="date" value={filters.to ?? ""} max={localToday()} onChange={(e) => setFilters({ ...filters, to: e.target.value || undefined })} />
        </div>
      </Card>

      {isError && <p role="alert" className="text-sm text-destructive">Não foi possível carregar as capturas.</p>}
      {isLoading && <p className="text-sm text-muted-foreground">Carregando…</p>}
      {!isLoading && captures.length === 0 && <p className="text-sm text-muted-foreground">Nenhuma captura nesta seleção.</p>}
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
        {captures.map((c) => (
          <CaptureCard key={c.id} projectId={projectId} capture={c} stages={stages} zones={zones}
            stageName={c.stageId ? stageName.get(c.stageId) : undefined} zoneName={c.zoneId ? zoneName.get(c.zoneId) : undefined}
            onOpen360={() => setPano(c)} />
        ))}
      </div>

      <Dialog open={!!pano} onOpenChange={(o) => !o && setPano(null)}>
        <DialogContent className="max-w-3xl">
          <DialogHeader><DialogTitle className="break-words">{pano?.title}</DialogTitle></DialogHeader>
          <div className="h-[60vh] min-h-[16rem] w-full overflow-hidden rounded-md">
            {pano?.url && (
              <ViewerBoundary>
                <Suspense fallback={<div className="grid h-full place-items-center text-sm text-muted-foreground">Carregando…</div>}>
                  <LazyPano360Viewer url={pano.url} title={pano.title} />
                </Suspense>
              </ViewerBoundary>
            )}
          </div>
          <p className="text-[11px] text-muted-foreground">Arraste para olhar em volta; role para aproximar.</p>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function CaptureUploadForm({ projectId, stages, zones, onDone }: Props & { onDone: () => void }) {
  const invalidate = useInvalidateTwin(projectId);
  const [type, setType] = useState<BuildRealityCaptureType>("Photo");
  const [title, setTitle] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [capturedAt, setCapturedAt] = useState(localToday());
  const [stageId, setStageId] = useState("");
  const [zoneId, setZoneId] = useState("");
  const [elementId, setElementId] = useState("");
  const [notes, setNotes] = useState("");
  const [clientVisible, setClientVisible] = useState(false);
  const [progress, setProgress] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const limit = file ? captureLimitBytes(type, file) : null;
  const tooBig = !!file && limit != null && file.size > limit;

  const submit = async () => {
    if (!file || tooBig) return;
    if (!title.trim()) { setError("Informe um título."); return; }
    setError(null);
    setProgress(0);
    try {
      await uploadRealityCapture(projectId, {
        file, type, title: title.trim(), capturedAt: capturedAt ? `${capturedAt}T12:00:00` : null, stageId: stageId || null,
        zoneId: zoneId || null, elementExternalId: elementId || null, notes: notes || null, clientVisible,
      }, (loaded, total) => setProgress(total > 0 ? (loaded / total) * 100 : 0));
      invalidate();
      toast.success("Captura enviada.");
      onDone();
    } catch (e) {
      setError(twinErrorMessage(e, "Não foi possível enviar a captura."));
    } finally {
      setProgress(null);
    }
  };

  return (
    <div className="space-y-2 rounded-md border border-border p-3" data-testid="capture-upload-form">
      <div className="grid gap-2 sm:grid-cols-3">
        <div className="space-y-1">
          <Label htmlFor="cap-type">Tipo</Label>
          <select id="cap-type" className={selectClass} value={type} onChange={(e) => { setType(e.target.value as BuildRealityCaptureType); setFile(null); }}>
            {TYPES.map((t) => <option key={t} value={t}>{CAPTURE_TYPES[t].label}</option>)}
          </select>
        </div>
        <div className="space-y-1 sm:col-span-2">
          <Label htmlFor="cap-title">Título</Label>
          <Input id="cap-title" value={title} maxLength={200} onChange={(e) => setTitle(e.target.value)} placeholder="Ex.: Laje do 2º pavimento" />
        </div>
      </div>
      <div className="space-y-1">
        <Label htmlFor="cap-file">Arquivo</Label>
        <Input key={type} id="cap-file" type="file" accept={CAPTURE_TYPES[type].accept} onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
        <p className="text-[11px] text-muted-foreground">{CAPTURE_TYPES[type].label}: {CAPTURE_TYPES[type].limit}.</p>
        {tooBig && <p role="alert" className="text-xs text-destructive">Arquivo de {formatBytes(file!.size)} acima do limite de {formatBytes(limit!)}.</p>}
      </div>
      <div className="grid gap-2 sm:grid-cols-4">
        <div className="space-y-1">
          <Label htmlFor="cap-date">Data da captura</Label>
          <Input id="cap-date" type="date" max={localToday()} value={capturedAt} onChange={(e) => setCapturedAt(e.target.value)} />
        </div>
        <div className="space-y-1">
          <Label htmlFor="cap-stage">Etapa</Label>
          <select id="cap-stage" className={selectClass} value={stageId} onChange={(e) => setStageId(e.target.value)}>
            <option value="">—</option>{stages.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="cap-zone">Zona</Label>
          <select id="cap-zone" className={selectClass} value={zoneId} onChange={(e) => setZoneId(e.target.value)}>
            <option value="">—</option>{zones.map((z) => <option key={z.id} value={z.id}>{z.name}</option>)}
          </select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="cap-element">Elemento (id)</Label>
          <Input id="cap-element" value={elementId} maxLength={100} onChange={(e) => setElementId(e.target.value)} />
        </div>
      </div>
      <div className="space-y-1">
        <Label htmlFor="cap-notes">Observação interna</Label>
        <Textarea id="cap-notes" rows={2} maxLength={2000} value={notes} onChange={(e) => setNotes(e.target.value)} />
      </div>
      <label className="flex items-center gap-2 text-sm">
        <Switch checked={clientVisible} onCheckedChange={setClientVisible} aria-label="Visível ao cliente" />Visível ao cliente
      </label>
      {progress !== null && <ProgressBar value={progress} label="Enviando a captura" />}
      {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
      <div className="flex gap-2">
        <Button type="button" size="sm" disabled={!file || tooBig || progress !== null} onClick={() => void submit()}>Enviar</Button>
        <Button type="button" size="sm" variant="ghost" disabled={progress !== null} onClick={onDone}>Cancelar</Button>
      </div>
    </div>
  );
}

function CaptureCard({ projectId, capture: c, stages, zones, stageName, zoneName, onOpen360 }: {
  projectId: string; capture: BuildRealityCaptureDto; stages: BuildStageDto[]; zones: BuildModelZoneDto[];
  stageName?: string; zoneName?: string; onOpen360: () => void;
}) {
  const visibility = useSetCaptureVisibility(projectId);
  const update = useUpdateRealityCapture(projectId);
  const remove = useDeleteRealityCapture(projectId);
  const [linking, setLinking] = useState(false);
  const [link, setLink] = useState({ stageId: c.stageId ?? "", zoneId: c.zoneId ?? "", element: c.elementExternalId ?? "" });
  const isImage = c.contentType.startsWith("image/");
  const isVideo = c.contentType.startsWith("video/");

  return (
    <article className="min-w-0 space-y-2 rounded-md border border-border bg-card p-3">
      <div className="overflow-hidden rounded-md bg-muted">
        {c.url && c.type === "Photo360" ? (
          <button type="button" onClick={onOpen360} className="relative block w-full" aria-label={`Abrir 360° de ${c.title}`}>
            <img src={c.url} alt="" className="aspect-video w-full object-cover" loading="lazy" />
            <span className="absolute inset-0 grid place-items-center bg-black/30 text-sm font-medium text-white"><span className="flex items-center gap-1"><Orbit className="h-4 w-4" />Abrir 360°</span></span>
          </button>
        ) : c.url && isVideo ? (
          <video src={c.url} controls preload="metadata" className="aspect-video w-full bg-black" />
        ) : c.url && isImage ? (
          <img src={c.url} alt={c.title} className="aspect-video w-full object-cover" loading="lazy" />
        ) : (
          <div className="grid aspect-video place-items-center p-2 text-center text-xs text-muted-foreground">
            {c.type === "PointCloud" ? "Nuvem de pontos guardada sem processamento." : "Pré-visualização indisponível."}
          </div>
        )}
      </div>
      <div className="min-w-0">
        <p className="break-words text-sm font-medium">{c.title}</p>
        <p className="text-[11px] text-muted-foreground">{CAPTURE_TYPES[c.type]?.label ?? c.type} · {fmtDateTime(c.capturedAt)} · {formatBytes(c.sizeBytes)}</p>
        {(stageName || zoneName || c.elementExternalId) && (
          <p className="break-all text-[11px] text-muted-foreground">
            {[stageName && `Etapa: ${stageName}`, zoneName && `Zona: ${zoneName}`, c.elementExternalId && `Elemento: ${c.elementExternalId}`].filter(Boolean).join(" · ")}
          </p>
        )}
      </div>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <label className="flex items-center gap-2 text-xs">
          <Switch checked={c.clientVisible} disabled={visibility.isPending} aria-label={`Visível ao cliente: ${c.title}`}
            onCheckedChange={(on) => visibility.mutate({ id: c.id, clientVisible: on }, { onError: (e) => toast.error(twinErrorMessage(e, "Não foi possível alterar.")) })} />
          Visível ao cliente
        </label>
        <div className="flex gap-1">
          {c.url && c.type === "PointCloud" && (
            <Button asChild size="icon" variant="ghost" aria-label={`Baixar ${c.title}`}><a href={c.url} download><Download className="h-4 w-4" /></a></Button>
          )}
          <Button type="button" size="icon" variant="ghost" aria-label={`Vincular ${c.title}`} onClick={() => setLinking(!linking)}><Link2 className="h-4 w-4" /></Button>
          <Button type="button" size="icon" variant="ghost" aria-label={`Excluir ${c.title}`} disabled={remove.isPending}
            onClick={() => remove.mutate(c.id, { onError: (e) => toast.error(twinErrorMessage(e, "Não foi possível excluir.")) })}>
            <Trash2 className="h-4 w-4" />
          </Button>
        </div>
      </div>
      {linking && (
        <div className="space-y-2 border-t border-border pt-2">
          <select aria-label="Etapa da captura" className={selectClass} value={link.stageId} onChange={(e) => setLink({ ...link, stageId: e.target.value })}>
            <option value="">Sem etapa</option>{stages.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
          <select aria-label="Zona da captura" className={selectClass} value={link.zoneId} onChange={(e) => setLink({ ...link, zoneId: e.target.value })}>
            <option value="">Sem zona</option>{zones.map((z) => <option key={z.id} value={z.id}>{z.name}</option>)}
          </select>
          <Input aria-label="Elemento da captura" placeholder="Id do elemento" value={link.element} onChange={(e) => setLink({ ...link, element: e.target.value })} />
          <Button type="button" size="sm" disabled={update.isPending} onClick={() => update.mutate({ id: c.id, req: {
            title: c.title, notes: c.notes, capturedAt: c.capturedAt, stageId: link.stageId || null, zoneId: link.zoneId || null,
            elementExternalId: link.element.trim() || null,
          } }, { onSuccess: () => { setLinking(false); toast.success("Vínculos da captura salvos."); },
            onError: (e) => toast.error(twinErrorMessage(e, "Não foi possível salvar.")) })}>Salvar vínculos</Button>
        </div>
      )}
    </article>
  );
}
