import { useEffect, useState } from "react";
import { useParams, useNavigate } from "react-router-dom";
import {
  ArrowLeft, Play, Pause, CheckCircle, XCircle, Plus, Pencil, Undo2,
  Check, X, Camera, TrendingUp, TrendingDown, FileText, Calendar,
  MapPin, User, DollarSign, BarChart2, BookOpen, HardHat, Cloud, Loader2, Globe,
  FilePlus2, ListChecks, Package, Ruler, ShieldCheck, KeyRound,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { toast } from "sonner";

import {
  useProjectDetails, useProjectFinancial, useStages,
  useStartProject, usePauseProject, useCompleteProject, useCancelProject,
  useDailyLogs, useCreateDailyLog, useAddDailyLogPhoto, useRemoveDailyLogPhoto,
} from "../hooks/use-build";
import { useProjectMovements, useVoidMovement } from "../hooks/use-interpreter";
import { ProjectStatusBadge } from "../components/ProjectStatusBadge";
import { BuildExpenseDialog } from "../components/BuildExpenseDialog";
import { EditProjectDialog } from "../components/EditProjectDialog";
import { BuildStagesTab } from "../components/stages/BuildStagesTab";
import { localToday } from "../lib/format";
import { BuildProgressTab } from "../components/progress/BuildProgressTab";
import { CostByStageSection } from "../components/finance/CostByStageSection";
import { MovementStageSelect } from "../components/finance/MovementStageSelect";
import { BuildPortalTab } from "../components/portal/BuildPortalTab";
import { BuildBudgetTab } from "../components/budget/BuildBudgetTab";
import { ChangeOrdersTab } from "../components/change-orders/ChangeOrdersTab";
import { SelectionsTab } from "../components/selections/SelectionsTab";
import { MaterialsTab } from "../components/materials/MaterialsTab";
import { MeasurementsTab } from "../components/measurements/MeasurementsTab";
import { QualityTab } from "../components/quality/QualityTab";
import { PostConstructionTab } from "../components/post-construction/PostConstructionTab";
import { DailyLogClientVisibility, PhotoClientVisibilityToggle } from "../components/diary/DailyLogClientVisibility";
import type {
  BuildProjectDetailsDto, BuildDailyLogDto,
} from "../api/build.api";
import {
  getWeatherCurrent, getWeatherHistory,
  type WeatherResult,
} from "@/services/weather.api";
import { uploadPrivateFile } from "@/services/storage.api";
import { ApiError } from "@/services/api-client";
import { useQuery } from "@tanstack/react-query";
import { fetchSuppliers } from "@/modules/suppliers/api/suppliers.api";

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmt(v: number | null | undefined) {
  if (v == null) return "—";
  return v.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

function fmtDate(d: string | null | undefined) {
  if (!d) return "—";
  return new Date(d + "T12:00:00").toLocaleDateString("pt-BR", {
    day: "2-digit", month: "short", year: "numeric",
  });
}

function fmtPct(v: number) {
  return `${v.toFixed(1)}%`;
}

type Tab =
  | "geral" | "etapas" | "progresso" | "orcamento" | "aditivos" | "escolhas" | "materiais" | "medicoes"
  | "qualidade" | "pos-obra" | "diario" | "financeiro" | "portal";

// ── Tab selector ──────────────────────────────────────────────────────────────

function TabBar({ active, onChange }: { active: Tab; onChange: (t: Tab) => void }) {
  const tabs: Array<{ key: Tab; label: string; icon: React.ElementType }> = [
    { key: "geral",      label: "Geral",       icon: HardHat },
    { key: "etapas",     label: "Etapas",      icon: CheckCircle },
    { key: "progresso",  label: "Progresso",   icon: TrendingUp },
    { key: "orcamento",  label: "Orçamento",   icon: FileText },
    { key: "aditivos",   label: "Aditivos",    icon: FilePlus2 },
    { key: "escolhas",   label: "Escolhas do cliente", icon: ListChecks },
    { key: "materiais",  label: "Materiais",   icon: Package },
    { key: "medicoes",   label: "Medições",    icon: Ruler },
    { key: "qualidade",  label: "Qualidade",   icon: ShieldCheck },
    { key: "pos-obra",   label: "Entrega e pós-obra", icon: KeyRound },
    { key: "diario",     label: "Diário",      icon: BookOpen },
    { key: "financeiro", label: "Financeiro",  icon: BarChart2 },
    { key: "portal",     label: "Portal do cliente", icon: Globe },
  ];

  return (
    <div className="flex gap-0 border-b border-border overflow-x-auto shrink-0">
      {tabs.map(({ key, label, icon: Icon }) => (
        <button
          key={key}
          onClick={() => onChange(key)}
          className={cn(
            "flex items-center gap-1.5 px-4 py-3 text-sm font-medium whitespace-nowrap border-b-2 transition-colors",
            active === key
              ? "border-primary text-primary"
              : "border-transparent text-muted-foreground hover:text-foreground",
          )}
        >
          <Icon className="h-3.5 w-3.5" />
          {label}
        </button>
      ))}
    </div>
  );
}

// ── Tab: Geral ────────────────────────────────────────────────────────────────

function TabGeral({ project }: { project: BuildProjectDetailsDto }) {
  const navigate = useNavigate();
  const startMut    = useStartProject(project.id);
  const pauseMut    = usePauseProject(project.id);
  const completeMut = useCompleteProject(project.id);
  const cancelMut   = useCancelProject(project.id);

  const [editOpen, setEditOpen] = useState(false);

  const isActive = project.status === "InProgress" || project.status === "Planning" || project.status === "Paused";

  const handleAction = (
    mut: ReturnType<typeof useStartProject>,
    label: string,
  ) => {
    mut.mutate(undefined as never, {
      onSuccess: () => toast.success(`Obra ${label} com sucesso!`),
      onError:   (e) => toast.error(e instanceof Error ? e.message : "Erro ao atualizar obra."),
    });
  };

  const typeLabel: Record<string, string> = {
    House:      "Residencial",
    Commercial: "Comercial",
    Renovation: "Reforma",
    Building:   "Edifício",
    Other:      "Outro",
  };

  return (
    <div className="space-y-6">
      {/* Edit action — only for non-terminal projects */}
      {isActive && (
        <div className="flex justify-end">
          <Button variant="outline" size="sm" onClick={() => setEditOpen(true)}>
            <Pencil className="h-3.5 w-3.5 mr-1.5" /> Editar obra
          </Button>
        </div>
      )}

      {/* Info cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        {[
          { icon: User,        label: "Cliente",         value: project.clientName },
          { icon: MapPin,      label: "Localização",     value: project.location ?? "—" },
          { icon: HardHat,     label: "Tipo",            value: typeLabel[project.type] ?? project.type },
          { icon: Calendar,    label: "Início",          value: fmtDate(project.startDate) },
          { icon: Calendar,    label: "Previsão término",value: fmtDate(project.expectedEndDate) },
          { icon: Calendar,    label: "Término real",    value: fmtDate(project.actualEndDate) },
          { icon: DollarSign,  label: "Orçamento previsto", value: fmt(project.budgetEstimated) },
          { icon: DollarSign,  label: "Orçamento aprovado", value: fmt(project.budgetApproved) },
        ].map(({ icon: Icon, label, value }) => (
          <div key={label} className="rounded-md border border-border bg-card p-3 flex items-center gap-3">
            <div className="p-2 rounded-lg bg-muted/60 text-muted-foreground shrink-0">
              <Icon className="h-4 w-4" />
            </div>
            <div className="min-w-0">
              <p className="text-[11px] text-muted-foreground uppercase tracking-wide">{label}</p>
              <p className="text-sm font-medium truncate">{value}</p>
            </div>
          </div>
        ))}
      </div>

      {/* Etapas recentes */}
      {project.stages.length > 0 && (
        <div className="space-y-2">
          <h3 className="text-sm font-semibold">Progresso das etapas</h3>
          <div className="space-y-2">
            {project.stages.map((s) => (
              <div key={s.id} className="space-y-1">
                <div className="flex items-center justify-between text-sm">
                  <span className="font-medium">{s.name}</span>
                  <span className="text-muted-foreground tabular-nums">{s.progressPercent}%</span>
                </div>
                <div className="h-2 rounded-full bg-muted overflow-hidden">
                  <div
                    className={cn(
                      "h-full rounded-full transition-all",
                      s.progressPercent === 100 ? "bg-emerald-500" :
                      s.progressPercent > 0 ? "bg-primary" : "bg-muted-foreground/20",
                    )}
                    style={{ width: `${s.progressPercent}%` }}
                  />
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Status transitions */}
      {isActive && (
        <div className="rounded-md border border-border bg-card p-4 space-y-3">
          <h3 className="text-sm font-semibold">Ações</h3>
          <div className="flex flex-wrap gap-2">
            {project.status !== "InProgress" && (
              <Button
                size="sm"
                onClick={() => handleAction(startMut as ReturnType<typeof useStartProject>, "iniciada")}
                disabled={startMut.isPending}
              >
                <Play className="h-3.5 w-3.5 mr-1.5" />
                {project.status === "Paused" ? "Retomar" : "Iniciar"}
              </Button>
            )}
            {project.status === "InProgress" && (
              <Button
                size="sm"
                variant="outline"
                onClick={() => handleAction(pauseMut as ReturnType<typeof useStartProject>, "pausada")}
                disabled={pauseMut.isPending}
              >
                <Pause className="h-3.5 w-3.5 mr-1.5" />
                Pausar
              </Button>
            )}
            <Button
              size="sm"
              variant="outline"
              className="text-emerald-600 border-emerald-200 hover:bg-emerald-50 dark:border-emerald-800 dark:hover:bg-emerald-900/20"
              onClick={() => handleAction(completeMut as ReturnType<typeof useStartProject>, "concluída")}
              disabled={completeMut.isPending}
            >
              <CheckCircle className="h-3.5 w-3.5 mr-1.5" />
              Concluir
            </Button>
            <Button
              size="sm"
              variant="outline"
              className="text-destructive border-destructive/30 hover:bg-destructive/5"
              onClick={() => {
                if (confirm("Cancelar esta obra? Esta ação não pode ser desfeita.")) {
                  cancelMut.mutate(undefined as never, {
                    onSuccess: () => { toast.success("Obra cancelada com sucesso!"); navigate("/build"); },
                    onError:   (e) => toast.error(e instanceof Error ? e.message : "Erro ao cancelar a obra."),
                  });
                }
              }}
              disabled={cancelMut.isPending}
            >
              <XCircle className="h-3.5 w-3.5 mr-1.5" />
              Cancelar
            </Button>
          </div>
        </div>
      )}

      <EditProjectDialog project={project} open={editOpen} onClose={() => setEditOpen(false)} />
    </div>
  );
}

// ── Tab: Diário ───────────────────────────────────────────────────────────────

function TabDiario({ projectId }: { projectId: string }) {
  const { data, isLoading, hasNextPage, fetchNextPage, isFetchingNextPage } = useDailyLogs(projectId);
  const createMut   = useCreateDailyLog(projectId);
  const addPhotoMut = useAddDailyLogPhoto(projectId);

  const [adding, setAdding]         = useState(false);
  const [logDate, setLogDate]       = useState(localToday);
  const [logNotes, setLogNotes]     = useState("");
  const [logWeather, setLogWeather] = useState("");

  // Weather lookup state
  const [weatherLat, setWeatherLat]       = useState("");
  const [weatherLon, setWeatherLon]       = useState("");
  const [weatherLoading, setWeatherLoading] = useState(false);
  const [weatherResult, setWeatherResult] = useState<WeatherResult | null>(null);
  const [weatherStatus, setWeatherStatus] = useState<"idle" | "not_found" | "unavailable">("idle");

  const handleGeolocate = () => {
    if (!navigator.geolocation) {
      toast.error("Geolocalização não suportada pelo navegador.");
      return;
    }
    navigator.geolocation.getCurrentPosition(
      (pos) => {
        setWeatherLat(pos.coords.latitude.toFixed(4));
        setWeatherLon(pos.coords.longitude.toFixed(4));
      },
      () => toast.error("Não foi possível obter localização."),
    );
  };

  const handleFetchWeather = async () => {
    const lat = parseFloat(weatherLat);
    const lon = parseFloat(weatherLon);
    if (isNaN(lat) || isNaN(lon)) {
      toast.error("Informe latitude e longitude válidas.");
      return;
    }
    setWeatherLoading(true);
    setWeatherResult(null);
    setWeatherStatus("idle");
    try {
      const isToday = logDate === localToday();
      const response = isToday
        ? await getWeatherCurrent(lat, lon)
        : await getWeatherHistory(lat, lon, logDate);
      if (response.unavailable) {
        setWeatherStatus("unavailable");
      } else if (!response.found || !response.data) {
        setWeatherStatus("not_found");
      } else {
        setWeatherResult(response.data);
      }
    } catch {
      setWeatherStatus("unavailable");
    } finally {
      setWeatherLoading(false);
    }
  };

  const handleApplyWeather = () => {
    if (!weatherResult) return;
    setLogWeather(weatherResult.summary);
    setWeatherResult(null);
    setWeatherStatus("idle");
    toast.success("Clima aplicado ao diário.");
  };

  // Photo upload
  const removePhotoMut = useRemoveDailyLogPhoto(projectId);
  const [uploadingLogId, setUploadingLogId] = useState<string | null>(null);

  const logs = data?.pages.flatMap((pg) => pg.items) ?? [];
  const totalLogs = data?.pages[0]?.total ?? 0;

  const handleCreateLog = () => {
    if (!logNotes.trim()) return;
    createMut.mutate({
      date:            logDate,
      notes:           logNotes.trim(),
      weatherSummary:  logWeather.trim() || undefined,
    }, {
      onSuccess: () => {
        setAdding(false);
        setLogNotes(""); setLogWeather(""); setLogDate(localToday());
        toast.success("Diário registrado!");
      },
      onError: (e) => {
        const msg = e instanceof Error ? e.message : "Erro ao criar diário.";
        if (msg.includes("422") || msg.toLowerCase().includes("já existe")) {
          toast.error("Já existe um diário para esta data.");
        } else {
          toast.error(msg);
        }
      },
    });
  };

  const handleUploadPhoto = async (logId: string, file: File) => {
    if (!file.type.startsWith("image/")) {
      toast.error("Selecione uma imagem (JPG, PNG ou WebP).");
      return;
    }
    setUploadingLogId(logId);
    try {
      // Persist only the durable storage key — the public URL is composed on read.
      const { key } = await uploadPrivateFile(file, "build-daily-log");
      addPhotoMut.mutate({ logId, req: { storageKey: key } }, {
        onSuccess: () => toast.success("Foto adicionada!"),
        onError:   (e) => toast.error(e instanceof Error ? e.message : "Erro ao salvar foto."),
      });
    } catch (e) {
      // Storage off/misconfigured → backend returns a controlled 404. Show an honest state.
      const disabled = e instanceof ApiError
        ? e.status === 404
        : /habilitado/i.test(e instanceof Error ? e.message : "");
      toast.error(disabled
        ? "Upload de fotos indisponível neste ambiente."
        : "Falha ao enviar foto. Tente novamente.");
    } finally {
      setUploadingLogId(null);
    }
  };

  const handleRemovePhoto = (photoId: string) => {
    if (!confirm("Remover esta foto?")) return;
    removePhotoMut.mutate(photoId, {
      onSuccess: () => toast.success("Foto removida."),
      onError:   (e) => toast.error(e instanceof Error ? e.message : "Erro ao remover foto."),
    });
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <p className="text-sm text-muted-foreground">
          {totalLogs} registro{totalLogs !== 1 ? "s" : ""}
        </p>
        {!adding && (
          <Button size="sm" variant="outline" onClick={() => setAdding(true)}>
            <Plus className="h-3.5 w-3.5 mr-1" /> Novo registro
          </Button>
        )}
      </div>

      {adding && (
        <div className="rounded-md border border-dashed border-primary/40 p-4 space-y-3">
          <p className="text-sm font-medium">Novo registro de obra</p>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <Label className="text-xs">Data</Label>
              <Input type="date" value={logDate} onChange={(e) => {
                setLogDate(e.target.value);
                setWeatherResult(null);
                setWeatherStatus("idle");
              }}
                className="mt-0.5 text-sm" />
            </div>
            <div>
              <Label className="text-xs">Clima</Label>
              <Input value={logWeather} onChange={(e) => setLogWeather(e.target.value)}
                placeholder="Ensolarado 28°C" className="mt-0.5 text-sm" />
            </div>
          </div>

          {/* Weather lookup section */}
          <div className="rounded-lg border border-border bg-muted/30 p-3 space-y-3">
            <div className="flex items-center gap-1.5">
              <Cloud className="h-3.5 w-3.5 text-muted-foreground" />
              <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                Buscar clima automaticamente
              </p>
            </div>

            <div className="grid grid-cols-2 gap-2">
              <div>
                <Label className="text-xs">Latitude</Label>
                <Input
                  type="number"
                  min={-90}
                  max={90}
                  step="any"
                  value={weatherLat}
                  onChange={(e) => setWeatherLat(e.target.value)}
                  placeholder="-23.5505"
                  className="mt-0.5 h-8 text-sm"
                />
              </div>
              <div>
                <Label className="text-xs">Longitude</Label>
                <Input
                  type="number"
                  min={-180}
                  max={180}
                  step="any"
                  value={weatherLon}
                  onChange={(e) => setWeatherLon(e.target.value)}
                  placeholder="-46.6333"
                  className="mt-0.5 h-8 text-sm"
                />
              </div>
            </div>

            <div className="flex gap-2 flex-wrap">
              <Button
                type="button"
                size="sm"
                variant="outline"
                className="h-7 text-xs"
                onClick={handleGeolocate}
              >
                <MapPin className="h-3 w-3 mr-1" />
                Usar minha localização
              </Button>
              <Button
                type="button"
                size="sm"
                variant="outline"
                className="h-7 text-xs"
                onClick={handleFetchWeather}
                disabled={weatherLoading}
              >
                {weatherLoading
                  ? <Loader2 className="h-3 w-3 mr-1 animate-spin" />
                  : <Cloud className="h-3 w-3 mr-1" />}
                {weatherLoading ? "Buscando…" : "Buscar clima"}
              </Button>
            </div>

            {/* Result card */}
            {weatherResult && (
              <div className="rounded-lg border border-primary/30 bg-primary/5 p-3 space-y-2">
                <div className="flex items-start justify-between gap-2">
                  <div className="space-y-0.5">
                    <p className="text-sm font-semibold">
                      {weatherResult.temperatureMax.toFixed(1)}°C / {weatherResult.temperatureMin.toFixed(1)}°C
                    </p>
                    <p className="text-xs text-muted-foreground">{weatherResult.description}</p>
                    <p className="text-xs text-muted-foreground">
                      Chuva: {weatherResult.precipitationMm.toFixed(1)} mm
                    </p>
                  </div>
                  <Button
                    type="button"
                    size="sm"
                    className="h-7 text-xs shrink-0"
                    onClick={handleApplyWeather}
                  >
                    <Check className="h-3 w-3 mr-1" />
                    Aplicar ao diário
                  </Button>
                </div>
              </div>
            )}

            {/* Status messages */}
            {weatherStatus === "unavailable" && (
              <p className="text-xs text-muted-foreground italic">
                Consulta de clima indisponível. Preencha manualmente.
              </p>
            )}
            {weatherStatus === "not_found" && (
              <p className="text-xs text-muted-foreground italic">
                Nenhum dado climático encontrado para essa localização.
              </p>
            )}
          </div>

          <div>
            <Label className="text-xs">Notas *</Label>
            <Textarea
              value={logNotes}
              onChange={(e) => setLogNotes(e.target.value)}
              placeholder="Descreva as atividades do dia…"
              className="mt-0.5 text-sm min-h-[80px] resize-none"
              autoFocus
            />
          </div>
          <div className="flex gap-2 justify-end">
            <Button variant="outline" size="sm" onClick={() => { setAdding(false); setLogNotes(""); }}>
              Cancelar
            </Button>
            <Button size="sm"
              disabled={!logNotes.trim() || createMut.isPending}
              onClick={handleCreateLog}>
              {createMut.isPending ? "Salvando…" : "Registrar"}
            </Button>
          </div>
        </div>
      )}

      {isLoading ? (
        <div className="space-y-3">
          {[1, 2, 3].map((i) => (
            <div key={i} className="h-24 rounded-md bg-muted animate-pulse" />
          ))}
        </div>
      ) : logs.length === 0 ? (
        <div className="rounded-md border border-dashed border-border p-8 text-center">
          <BookOpen className="h-8 w-8 mx-auto text-muted-foreground opacity-40 mb-2" />
          <p className="text-sm text-muted-foreground">Nenhum registro no diário.</p>
          <Button variant="outline" size="sm" className="mt-3" onClick={() => setAdding(true)}>
            <Plus className="h-3.5 w-3.5 mr-1" /> Criar primeiro registro
          </Button>
        </div>
      ) : (
        <div className="space-y-3">
          {logs.map((log: BuildDailyLogDto) => (
            <div key={log.id} className="rounded-md border border-border bg-card p-4 space-y-2">
              <div className="flex items-center justify-between gap-2">
                <div className="flex items-center gap-2">
                  <Calendar className="h-4 w-4 text-muted-foreground shrink-0" />
                  <p className="text-sm font-semibold">
                    {fmtDate(log.date)}
                  </p>
                  {log.weatherSummary && (
                    <span className="text-xs text-muted-foreground">· {log.weatherSummary}</span>
                  )}
                </div>
                <label className={cn(
                  "flex items-center gap-1 text-xs text-muted-foreground hover:text-primary transition-colors cursor-pointer",
                  uploadingLogId === log.id && "opacity-60 pointer-events-none",
                )}>
                  {uploadingLogId === log.id
                    ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
                    : <Camera className="h-3.5 w-3.5" />}
                  <span>{uploadingLogId === log.id ? "Enviando…" : "Adicionar foto"}</span>
                  <input
                    type="file"
                    accept="image/jpeg,image/png,image/webp"
                    className="hidden"
                    onChange={(e) => {
                      const f = e.target.files?.[0];
                      e.target.value = "";
                      if (f) handleUploadPhoto(log.id, f);
                    }}
                  />
                </label>
              </div>

              <p className="text-sm text-foreground/80 leading-relaxed">{log.notes}</p>

              {/* Photos */}
              {log.photos.length > 0 && (
                <div className="flex gap-2 flex-wrap pt-1">
                  {log.photos.map((photo) => (
                    <div key={photo.id} className="relative group/photo">
                      {photo.url ? (
                        <a href={photo.url} target="_blank" rel="noopener noreferrer">
                          <img src={photo.url} alt={photo.caption ?? "Foto da obra"}
                            className="h-16 w-16 rounded-lg object-cover border border-border" />
                        </a>
                      ) : (
                        <div className="h-16 w-16 rounded-lg bg-muted flex items-center justify-center border border-border"
                          title={photo.caption ?? photo.storageKey}>
                          <Camera className="h-5 w-5 text-muted-foreground" />
                        </div>
                      )}
                      <button
                        onClick={() => handleRemovePhoto(photo.id)}
                        disabled={removePhotoMut.isPending}
                        className="absolute -top-1.5 -right-1.5 rounded-full bg-destructive text-destructive-foreground h-4 w-4 flex items-center justify-center opacity-0 group-hover/photo:opacity-100 transition-opacity disabled:opacity-40"
                        title="Remover foto"
                      >
                        <X className="h-2.5 w-2.5" />
                      </button>
                      <PhotoClientVisibilityToggle projectId={projectId} photo={photo} logVisible={!!log.clientVisible} />
                    </div>
                  ))}
                </div>
              )}

              <DailyLogClientVisibility projectId={projectId} log={log} />
            </div>
          ))}
        </div>
      )}
      {hasNextPage && (
        <div className="text-center">
          <Button size="sm" variant="outline" onClick={() => void fetchNextPage()} disabled={isFetchingNextPage}>
            {isFetchingNextPage ? <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" /> : null}
            Carregar registros anteriores ({logs.length} de {totalLogs})
          </Button>
        </div>
      )}
    </div>
  );
}

// ── Tab: Financeiro ───────────────────────────────────────────────────────────

function TabFinanceiro({ projectId }: { projectId: string }) {
  const [expenseOpen, setExpenseOpen] = useState(false);

  const { data: financial, isLoading, isError } = useProjectFinancial(projectId);
  const { data: movementsData, isLoading: movLoading, hasNextPage, fetchNextPage, isFetchingNextPage } =
    useProjectMovements(projectId);
  const voidMut = useVoidMovement(projectId);
  const { data: stages = [] } = useStages(projectId);
  const [confirmVoid, setConfirmVoid] = useState<string | null>(null);

  const movements = movementsData?.pages.flatMap((pg) => pg.items) ?? [];
  const totalMovements = movementsData?.pages[0]?.totalCount ?? 0;

  useEffect(() => {
    if (!confirmVoid) return;
    const t = setTimeout(() => setConfirmVoid(null), 4000);   // armed for 4 s, then back to safe
    return () => clearTimeout(t);
  }, [confirmVoid]);

  const handleVoid = (id: string) => {
    if (confirmVoid !== id) { setConfirmVoid(id); return; }
    voidMut.mutate(id, {
      onSuccess: () => { setConfirmVoid(null); toast.success("Despesa estornada. Ela saiu da obra e o financeiro recebeu o estorno."); },
      onError:   (e) => { setConfirmVoid(null); toast.error(e instanceof ApiError ? e.message : "Não foi possível estornar."); },
    });
  };

  const { data: suppliers = [] } = useQuery({
    queryKey:  ["suppliers", "list"],
    queryFn:   () => fetchSuppliers(),
    staleTime: 60_000,
  });
  const supplierName = (id: string | null) =>
    id ? suppliers.find((s) => s.id === id)?.name ?? null : null;

  if (isLoading) {
    return (
      <div className="space-y-3">
        {[1, 2, 3].map((i) => (
          <div key={i} className="h-20 rounded-md bg-muted animate-pulse" />
        ))}
      </div>
    );
  }

  if (isError || !financial) {
    return (
      <div className="rounded-md border border-destructive/20 bg-destructive/5 p-8 text-center">
        <p className="text-sm text-destructive">Erro ao carregar resumo financeiro.</p>
      </div>
    );
  }

  const isOverBudget    = financial.approvedBudget != null &&
                          financial.totalRealizedExpenses > financial.approvedBudget;
  // the API's variance is approved − realized: negative means the project went over budget;
  // with no approved budget there is nothing to compare against
  const noApproved      = financial.approvedBudget == null;
  const varianceIsOver  = !noApproved && financial.varianceAmount < 0;
  const varianceIsEqual = financial.varianceAmount === 0;

  const coveragePercent = financial.approvedBudget
    ? Math.min(150, (financial.totalRealizedExpenses / financial.approvedBudget) * 100)
    : null;

  return (
    <div className="space-y-5">

      {/* ── Alert: over budget ─────────────────────────────────────────── */}
      {isOverBudget && (
        <div className="flex items-start gap-2.5 rounded-md border border-red-200 bg-red-50 dark:border-red-800 dark:bg-red-900/20 p-3">
          <TrendingUp className="h-4 w-4 text-red-600 dark:text-red-400 shrink-0 mt-0.5" />
          <p className="text-sm text-red-700 dark:text-red-300 font-medium">
            Realizado supera o orçamento aprovado em{" "}
            {fmt(financial.totalRealizedExpenses - (financial.approvedBudget ?? 0))}.
          </p>
        </div>
      )}

      {/* ── KPI grid ───────────────────────────────────────────────────── */}
      <div className="grid grid-cols-2 gap-3">
        <div className="rounded-md border border-border bg-card p-4">
          <div className="flex items-center gap-2 mb-2">
            <FileText className="h-3.5 w-3.5 text-blue-500" />
            <span className="text-xs text-muted-foreground">Orçamento aprovado</span>
          </div>
          <p className="text-xl font-bold tabular-nums text-blue-600 dark:text-blue-400">
            {fmt(financial.approvedBudget)}
          </p>
        </div>

        <div className={cn(
          "rounded-md border p-4",
          isOverBudget
            ? "border-red-200 bg-red-50 dark:border-red-800 dark:bg-red-900/10"
            : "border-border bg-card",
        )}>
          <div className="flex items-center gap-2 mb-2">
            <DollarSign className={cn("h-3.5 w-3.5", isOverBudget ? "text-red-500" : "text-foreground")} />
            <span className="text-xs text-muted-foreground">Realizado</span>
          </div>
          <p className={cn(
            "text-xl font-bold tabular-nums",
            isOverBudget ? "text-red-600 dark:text-red-400" : "text-foreground",
          )}>
            {fmt(financial.totalRealizedExpenses)}
          </p>
          <p className="text-xs text-muted-foreground mt-1">
            {financial.movementCount} movimentaç{financial.movementCount !== 1 ? "ões" : "ão"}
          </p>
        </div>

        <div className="rounded-md border border-border bg-card p-4">
          <div className="flex items-center gap-2 mb-2">
            {varianceIsOver
              ? <TrendingUp className="h-3.5 w-3.5 text-red-500" />
              : varianceIsEqual
                ? <BarChart2 className="h-3.5 w-3.5 text-muted-foreground" />
                : <TrendingDown className="h-3.5 w-3.5 text-emerald-500" />}
            <span className="text-xs text-muted-foreground">Desvio</span>
          </div>
          <p className={cn(
            "text-xl font-bold tabular-nums",
            varianceIsOver  ? "text-red-600 dark:text-red-400" :
            varianceIsEqual ? "text-muted-foreground" :
                              "text-emerald-600 dark:text-emerald-400",
          )}>
            {noApproved ? "—" : fmt(Math.abs(financial.varianceAmount))}
          </p>
          {noApproved
            ? <p className="text-xs text-muted-foreground">sem orçamento aprovado</p>
            : !varianceIsEqual && (
              <p className="text-xs text-muted-foreground">{varianceIsOver ? "acima do aprovado" : "abaixo do aprovado"}</p>
            )}
          {!noApproved && financial.variancePercent !== 0 && (
            <p className="text-xs text-muted-foreground mt-1">
              {Math.abs(financial.variancePercent).toFixed(1)}% do aprovado
            </p>
          )}
        </div>

        <div className="rounded-md border border-border bg-card p-4">
          <div className="flex items-center gap-2 mb-2">
            <FileText className="h-3.5 w-3.5 text-muted-foreground" />
            <span className="text-xs text-muted-foreground">Estimado</span>
          </div>
          <p className="text-xl font-bold tabular-nums text-muted-foreground">
            {fmt(financial.estimatedBudget)}
          </p>
        </div>
      </div>

      {/* ── Coverage bar ───────────────────────────────────────────────── */}
      {coveragePercent != null && (
        <div className="rounded-md border border-border bg-card p-4 space-y-2">
          <div className="flex items-center justify-between text-sm">
            <span className="font-medium">Consumo do orçamento</span>
            <span className={cn(
              "tabular-nums text-sm font-semibold",
              coveragePercent >= 100 ? "text-red-600 dark:text-red-400" :
              coveragePercent >= 80  ? "text-yellow-600 dark:text-yellow-400" :
                                       "text-muted-foreground",
            )}>
              {fmtPct(Math.min(100, coveragePercent))}
            </span>
          </div>
          <div className="h-3 rounded-full bg-muted overflow-hidden">
            <div
              className={cn(
                "h-full rounded-full transition-all",
                coveragePercent >= 100 ? "bg-red-500" :
                coveragePercent >= 80  ? "bg-yellow-500" :
                                         "bg-primary",
              )}
              style={{ width: `${Math.min(100, coveragePercent)}%` }}
            />
          </div>
          <p className="text-xs text-muted-foreground">
            {fmt(financial.totalRealizedExpenses)} de {fmt(financial.approvedBudget)} utilizados
            {financial.lastMovementDate && (
              <> · última movimentação {fmtDate(financial.lastMovementDate)}</>
            )}
          </p>
        </div>
      )}

      <CostByStageSection projectId={projectId} />

      {/* ── CTA: registrar despesa ─────────────────────────────────────── */}
      <Button
        className="w-full"
        onClick={() => setExpenseOpen(true)}
      >
        <Plus className="h-4 w-4 mr-1.5" />
        Registrar despesa
      </Button>

      {/* ── Movement list ──────────────────────────────────────────────── */}
      <div className="space-y-2">
        <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
          Despesas confirmadas
        </p>
        {movLoading ? (
          <div className="space-y-2">
            {[1, 2].map((i) => (
              <div key={i} className="h-14 rounded-md bg-muted animate-pulse" />
            ))}
          </div>
        ) : movements.length === 0 ? (
          <div className="rounded-md border border-dashed border-border p-6 text-center">
            <DollarSign className="h-7 w-7 mx-auto text-muted-foreground opacity-30 mb-2" />
            <p className="text-sm text-muted-foreground">
              Nenhuma despesa registrada ainda.
            </p>
            <p className="text-xs text-muted-foreground mt-1">
              Use o botão acima para registrar a primeira despesa.
            </p>
          </div>
        ) : (
          <div className="rounded-md border border-border overflow-hidden">
            {movements.map((m, idx) => (
              <div
                key={m.id}
                className={cn(
                  "flex items-center justify-between p-3 gap-3",
                  idx !== 0 && "border-t border-border",
                  "hover:bg-muted/20 transition-colors",
                )}
              >
                <div className="min-w-0">
                  <p className="text-sm font-medium truncate">{m.description}</p>
                  <p className="text-xs text-muted-foreground">
                    {new Date(m.date + "T12:00:00").toLocaleDateString("pt-BR", {
                      day: "2-digit", month: "short", year: "numeric",
                    })}
                    {" · "}
                    {m.nature === "Expense" ? "Despesa" :
                     m.nature === "Transfer" ? "Transferência" :
                     m.nature === "Reimbursement" ? "Reembolso" : "Adiantamento"}
                    {supplierName(m.supplierId) ? ` · ${supplierName(m.supplierId)}` : ""}
                  </p>
                </div>
                <div className="flex shrink-0 items-center gap-2">
                  <MovementStageSelect projectId={projectId} movementId={m.id} description={m.description}
                    stageId={m.stageId} stages={stages} />
                  <p className="text-sm font-bold tabular-nums text-red-600 dark:text-red-400">
                    -{fmt(m.amount)}
                  </p>
                  <Button
                    size="sm" variant={confirmVoid === m.id ? "destructive" : "ghost"} className="h-7 px-2"
                    disabled={voidMut.isPending}
                    onBlur={() => setConfirmVoid((c) => (c === m.id ? null : c))}
                    onClick={() => handleVoid(m.id)}
                    aria-label={`${confirmVoid === m.id ? "Confirmar estorno de" : "Estornar"} ${m.description}`}
                  >
                    {confirmVoid === m.id ? "Estornar" : <Undo2 className="h-3.5 w-3.5" />}
                  </Button>
                </div>
              </div>
            ))}
          </div>
        )}
        {hasNextPage && (
          <div className="text-center">
            <Button size="sm" variant="outline" onClick={() => void fetchNextPage()} disabled={isFetchingNextPage}>
              Carregar despesas anteriores ({movements.length} de {totalMovements})
            </Button>
          </div>
        )}
      </div>

      {/* ── Expense dialog ─────────────────────────────────────────────── */}
      <BuildExpenseDialog
        open={expenseOpen}
        onClose={() => setExpenseOpen(false)}
        projectId={projectId}
      />
    </div>
  );
}

// ── Main detail page ──────────────────────────────────────────────────────────

export default function BuildProjectDetailPage() {
  const { id = "" }   = useParams<{ id: string }>();
  const navigate      = useNavigate();
  const [tab, setTab] = useState<Tab>("geral");

  const { data: project, isLoading, isError } = useProjectDetails(id);

  if (isLoading) {
    return (
      <div className="p-6 space-y-4">
        <div className="h-8 w-64 rounded-lg bg-muted animate-pulse" />
        <div className="h-10 rounded-md bg-muted animate-pulse" />
        <div className="grid grid-cols-2 gap-3">
          {[1, 2, 3, 4].map((i) => (
            <div key={i} className="h-20 rounded-md bg-muted animate-pulse" />
          ))}
        </div>
      </div>
    );
  }

  if (isError || !project) {
    return (
      <div className="p-6">
        <div className="rounded-md border border-destructive/20 bg-destructive/5 p-8 text-center">
          <p className="text-sm text-destructive font-medium">Obra não encontrada.</p>
          <Button variant="outline" className="mt-4" onClick={() => navigate("/build")}>
            Voltar para obras
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full">
      {/* Header */}
      <div className="px-6 pt-6 pb-0 space-y-3 shrink-0">
        <div className="flex items-center gap-3">
          <button
            onClick={() => navigate("/build")}
            className="p-1.5 text-muted-foreground hover:text-foreground transition-colors"
          >
            <ArrowLeft className="h-5 w-5" />
          </button>
          <div className="min-w-0 flex-1">
            <h1 className="text-lg font-bold leading-tight truncate">{project.name}</h1>
            <p className="text-xs text-muted-foreground">{project.clientName}</p>
          </div>
          <ProjectStatusBadge status={project.status} />
        </div>

        <TabBar active={tab} onChange={setTab} />
      </div>

      {/* Tab content */}
      <div className="flex-1 overflow-auto p-6">
        {tab === "geral"      && <TabGeral project={project} />}
        {tab === "etapas"     && <BuildStagesTab projectId={project.id} />}
        {tab === "progresso"  && <BuildProgressTab projectId={project.id} />}
        {tab === "orcamento"  && <BuildBudgetTab project={project} />}
        {tab === "aditivos"   && <ChangeOrdersTab projectId={project.id} />}
        {tab === "escolhas"   && <SelectionsTab projectId={project.id} onOpenChangeOrders={() => setTab("aditivos")} />}
        {tab === "materiais"  && <MaterialsTab projectId={project.id} />}
        {tab === "medicoes"   && <MeasurementsTab projectId={project.id} />}
        {tab === "qualidade"  && <QualityTab projectId={project.id} />}
        {tab === "pos-obra"   && <PostConstructionTab projectId={project.id} />}
        {tab === "diario"     && <TabDiario projectId={project.id} />}
        {tab === "financeiro" && <TabFinanceiro projectId={project.id} />}
        {tab === "portal"     && <BuildPortalTab projectId={project.id} />}
      </div>
    </div>
  );
}
