import { useEffect, useMemo, useState } from "react";
import { Plus, RotateCcw, Upload } from "lucide-react";
import { toast } from "sonner";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import type { BuildProjectDetailsDto } from "../../api/build.api";
import type { BuildModelDto, BuildModelVersionDto } from "../../api/build-twin.api";
import {
  useBuildSettings, useModels, useProjectZones, useReprocessVersion, useTwinOverview,
} from "../../hooks/use-build-twin";
import {
  BUILD_3D_DISABLED_MESSAGE, DISCIPLINES, GEOMETRY_LABEL, VERSION_STATUS, formatBytes, primaryVersion, twinErrorMessage,
  versionPollInterval,
} from "../../lib/twin";
import { fmtDateTime } from "../../lib/format";
import { StatusPill } from "../commercial/StatusPill";
import { BindingsSection } from "./BindingsSection";
import { ModelMapperAi } from "../ai/ModelMapperAi";
import { ModelUploadForm } from "./ModelUploadForm";
import { RealityCaptureSection } from "./RealityCaptureSection";
import { TwinOnboarding } from "./TwinOnboarding";
import { TwinViewerPanel } from "./TwinViewerPanel";
import { ZonesSection } from "./ZonesSection";

type Section = "modelo" | "zonas" | "vinculos" | "captura";

const SECTIONS: Array<{ key: Section; label: string }> = [
  { key: "modelo",   label: "Modelo" },
  { key: "zonas",    label: "Zonas e planta" },
  { key: "vinculos", label: "Vínculos" },
  { key: "captura",  label: "Captura da realidade" },
];

/** "Gêmeo digital": onboarding, models and versions, viewer with the 4D state, zones, bindings and reality capture. */
export function DigitalTwinTab({ project }: { project: BuildProjectDetailsDto }) {
  const projectId = project.id;
  const [section, setSection] = useState<Section>("modelo");
  const [onboarding, setOnboarding] = useState(false);
  const [selectedVersionId, setSelectedVersionId] = useState<string | null>(null);

  const settings = useBuildSettings();
  const overview = useTwinOverview(projectId);
  // tolerate an unexpected body (e.g. an older API without the twin routes) instead of crashing the tab
  const ov = overview.data && typeof overview.data.bindingCoveragePercent === "number" ? overview.data : undefined;
  const modelsQuery = useModels(projectId);
  const models = useMemo(() => (Array.isArray(modelsQuery.data) ? modelsQuery.data : []), [modelsQuery.data]);
  const { data: zonesData } = useProjectZones(projectId);
  const zones = Array.isArray(zonesData) ? zonesData : [];
  const build3DEnabled = settings.data?.build3DEnabled ?? ov?.build3DEnabled ?? true;

  // keep the list fresh while any version is still in the server's queue
  const working = models.some((m) => m.versions.some((v) => versionPollInterval(v.status) !== false));
  const { refetch } = modelsQuery;
  useEffect(() => {
    if (!working) return;
    const t = setInterval(() => void refetch(), 3000);
    return () => clearInterval(t);
  }, [working, refetch]);

  const allVersions = models.flatMap((m) => m.versions);
  const primary = primaryVersion(models);
  const selected = allVersions.find((v) => v.id === selectedVersionId) ?? primary ?? null;
  const bindingVersion = selected?.status === "Ready" && selected.elementCount > 0
    ? selected
    : allVersions.find((v) => v.status === "Ready" && v.elementCount > 0) ?? null;

  const showOnboarding = onboarding || (modelsQuery.isSuccess && models.length === 0);

  return (
    <div className="min-w-0 space-y-4">
      {!build3DEnabled && (
        <p role="alert" className="rounded-md border border-amber-300 bg-amber-50 p-2 text-sm text-amber-900 dark:border-amber-800 dark:bg-amber-900/20 dark:text-amber-200">
          {BUILD_3D_DISABLED_MESSAGE} As leituras continuam disponíveis.
        </p>
      )}

      {ov && (
        <div className="grid grid-cols-2 gap-2 sm:grid-cols-4" data-testid="twin-summary">
          <Summary label="Fonte" value={GEOMETRY_LABEL[ov.geometrySource] ?? ov.geometrySource} />
          <Summary label="Elementos" value={`${ov.boundElements} de ${ov.totalElements} vinculados`} />
          <Summary label="Cobertura" value={`${ov.bindingCoveragePercent.toLocaleString("pt-BR", { maximumFractionDigits: 1 })}%`} />
          <Summary label="Em processamento" value={String(ov.pendingJobs)} />
        </div>
      )}

      <div className="flex gap-1 overflow-x-auto rounded-md bg-muted p-1" role="tablist" aria-label="Seções do gêmeo digital">
        {SECTIONS.map((s) => (
          <button key={s.key} type="button" role="tab" aria-selected={section === s.key} onClick={() => setSection(s.key)}
            className={cn("whitespace-nowrap rounded px-3 py-1.5 text-sm font-medium",
              section === s.key ? "bg-background shadow-sm" : "text-muted-foreground hover:text-foreground")}>
            {s.label}
          </button>
        ))}
      </div>

      {section === "modelo" && (
        <div className="space-y-4">
          {modelsQuery.isLoading && <p className="text-sm text-muted-foreground">Carregando…</p>}
          {modelsQuery.isError && <p role="alert" className="text-sm text-destructive">Não foi possível carregar os modelos da obra.</p>}
          {showOnboarding && (
            <TwinOnboarding projectId={projectId} build3DEnabled={build3DEnabled}
              initialStep={models.length > 0 ? 2 : 1}
              onCancel={models.length > 0 ? () => setOnboarding(false) : undefined}
              onNoProject={() => { setOnboarding(false); setSection("zonas"); }}
              onBind={(v) => { setOnboarding(false); setSelectedVersionId(v.id); setSection(v.geometrySource === "AssistedMapping" ? "zonas" : "vinculos"); }} />
          )}
          {models.length > 0 && (
            <ModelsList projectId={projectId} models={models} selectedId={selected?.id ?? null} build3DEnabled={build3DEnabled}
              onSelect={setSelectedVersionId} onNewModel={showOnboarding ? undefined : () => setOnboarding(true)} />
          )}
          {selected && selected.status === "Ready" && (selected.geometrySource === "NativeBim" || selected.geometrySource === "Converted3D") && (
            <TwinViewerPanel projectId={projectId} project={project} version={selected} build3DEnabled={build3DEnabled} />
          )}
          {selected && selected.status === "Ready" && selected.geometrySource === "AssistedMapping" && (
            <p className="rounded-md border border-border p-3 text-sm">
              Esta versão é uma planta 2D (mapeamento assistido).{" "}
              <button type="button" className="font-medium text-primary hover:underline" onClick={() => setSection("zonas")}>Desenhar as zonas na planta</button>
            </p>
          )}
        </div>
      )}

      {section === "zonas" && <ZonesSection projectId={projectId} models={models} stages={project.stages} />}

      {section === "vinculos" && (
        <div className="min-w-0 space-y-4">
          <ModelMapperAi projectId={projectId} version={selected?.status === "Ready" ? selected : allVersions.find((v) => v.status === "Ready") ?? null} />
          <BindingsSection projectId={projectId} version={bindingVersion} stages={project.stages} coveragePercent={ov?.bindingCoveragePercent ?? null} />
        </div>
      )}

      {section === "captura" && (
        <RealityCaptureSection projectId={projectId} stages={project.stages} zones={zones} />
      )}
    </div>
  );
}

function Summary({ label, value }: { label: string; value: string }) {
  return (
    <div className="min-w-0 rounded-md border border-border bg-card p-2.5">
      <p className="text-[11px] text-muted-foreground">{label}</p>
      <p className="truncate text-sm font-semibold" title={value}>{value}</p>
    </div>
  );
}

function disciplineLabel(d: string) {
  return DISCIPLINES.find((x) => x.value === d)?.label ?? d;
}

function ModelsList({ projectId, models, selectedId, build3DEnabled, onSelect, onNewModel }: {
  projectId: string; models: BuildModelDto[]; selectedId: string | null; build3DEnabled: boolean;
  onSelect: (id: string) => void; onNewModel?: () => void;
}) {
  const [newVersionOf, setNewVersionOf] = useState<string | null>(null);
  const reprocess = useReprocessVersion(projectId);

  const row = (v: BuildModelVersionDto, current: boolean) => {
    const meta = VERSION_STATUS[v.status];
    return (
      <li key={v.id} className={cn("flex flex-wrap items-center justify-between gap-2 rounded-md px-2 py-1.5", selectedId === v.id && "bg-primary/5")}>
        <button type="button" className="min-w-0 flex-1 text-left" onClick={() => onSelect(v.id)} aria-pressed={selectedId === v.id}>
          <span className="block break-all text-sm">v{v.number} · {v.sourceFileName}{current && <span className="ml-1 text-[10px] uppercase text-primary">atual</span>}</span>
          <span className="block text-[11px] text-muted-foreground">
            {GEOMETRY_LABEL[v.geometrySource] ?? v.geometrySource} · {formatBytes(v.sizeBytes)} · {v.elementCount} elementos · {fmtDateTime(v.createdAt)}
            {v.hasViewer ? " · visualização 3D" : ""}
          </span>
          {v.statusMessage && (v.status === "Failed" || v.status === "Rejected") && <span className="block break-words text-[11px] text-destructive">{v.statusMessage}</span>}
        </button>
        <span className="flex items-center gap-1.5">
          <StatusPill label={meta.label} tone={meta.tone} />
          {v.status === "Failed" && (
            <Button type="button" size="sm" variant="outline" disabled={!build3DEnabled || reprocess.isPending} aria-label={`Reprocessar v${v.number}`}
              onClick={() => reprocess.mutate(v.id, { onError: (e) => toast.error(twinErrorMessage(e, "Não foi possível reprocessar.")) })}>
              <RotateCcw className="h-3.5 w-3.5" />
            </Button>
          )}
        </span>
      </li>
    );
  };

  return (
    <section className="space-y-3 rounded-md border border-border bg-card p-3 sm:p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-sm font-semibold">Modelos da obra</h3>
        {onNewModel && build3DEnabled && (
          <Button type="button" size="sm" variant="outline" onClick={onNewModel}><Plus className="mr-1 h-4 w-4" />Novo modelo</Button>
        )}
      </div>
      <p className="text-[11px] text-muted-foreground">O arquivo original do projeto fica privado; o cliente só pode ver o arquivo de visualização.</p>
      {models.map((m) => (
        <div key={m.id} className="space-y-1.5 border-t border-border pt-2">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <p className="text-sm font-medium">{m.name} <span className="text-xs font-normal text-muted-foreground">· {disciplineLabel(m.discipline)}</span></p>
            {build3DEnabled && newVersionOf !== m.id && (
              <Button type="button" size="sm" variant="ghost" onClick={() => setNewVersionOf(m.id)}><Upload className="mr-1 h-3.5 w-3.5" />Nova versão</Button>
            )}
          </div>
          {newVersionOf === m.id && (
            <ModelUploadForm projectId={projectId} modelId={m.id} onCancel={() => setNewVersionOf(null)}
              onUploaded={(r) => { setNewVersionOf(null); onSelect(r.version.id); toast.success("Nova versão enviada."); }} />
          )}
          <ul className="space-y-0.5">{m.versions.map((v) => row(v, v.id === m.currentVersionId))}</ul>
        </div>
      ))}
    </section>
  );
}
