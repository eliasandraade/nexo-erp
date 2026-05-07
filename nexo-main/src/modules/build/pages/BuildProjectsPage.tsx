import { useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  Plus, HardHat, Search, TrendingUp, TrendingDown, Minus,
  AlertCircle, CheckCircle2, PauseCircle, XCircle, Clock,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter,
} from "@/components/ui/dialog";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { PageHeader } from "@/components/shared/PageHeader";
import { toast } from "sonner";
import { useProjects, useCreateProject } from "../hooks/use-build";
import { ProjectStatusBadge } from "../components/ProjectStatusBadge";
import type { BuildProjectDto, BuildProjectStatus } from "../api/build.api";

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmt(v: number | null | undefined): string {
  if (v == null) return "—";
  return v.toLocaleString("pt-BR", { style: "currency", currency: "BRL", maximumFractionDigits: 0 });
}

function fmtDate(d: string | null | undefined): string {
  if (!d) return "—";
  return new Date(d + "T12:00:00").toLocaleDateString("pt-BR", {
    day: "2-digit", month: "short", year: "numeric",
  });
}

function projectProgress(project: BuildProjectDto): number {
  // If the project has stages, progress comes from them.
  // For the list view we estimate from status.
  if (project.status === "Completed") return 100;
  if (project.status === "Cancelled") return 0;
  if (project.status === "Planning")  return 0;
  if (project.status === "Paused")    return 50;
  return 60; // InProgress baseline — detail view shows real %
}

function varianceColor(variance: number | null): string {
  if (variance == null) return "text-muted-foreground";
  if (variance > 0)  return "text-red-600 dark:text-red-400";
  if (variance < 0)  return "text-emerald-600 dark:text-emerald-400";
  return "text-muted-foreground";
}

function StatusFilterBtn({
  label, active, onClick,
}: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      className={cn(
        "px-3.5 py-1.5 rounded-full text-sm font-medium transition-colors whitespace-nowrap",
        active
          ? "bg-primary text-primary-foreground"
          : "bg-muted text-muted-foreground hover:text-foreground",
      )}
    >
      {label}
    </button>
  );
}

// ── Project card ──────────────────────────────────────────────────────────────

function ProjectCard({ project, onClick }: { project: BuildProjectDto; onClick: () => void }) {
  const budgetRef = project.budgetApproved ?? project.budgetEstimated;
  const hasFinancial = budgetRef != null;

  // placeholder realized — will be real on detail page
  const realized = null as number | null;
  const variance = (realized != null && budgetRef != null)
    ? realized - budgetRef
    : null;

  const StatusIcon =
    project.status === "Completed" ? CheckCircle2 :
    project.status === "Cancelled" ? XCircle :
    project.status === "Paused"    ? PauseCircle :
    project.status === "Planning"  ? Clock :
    AlertCircle;

  return (
    <button
      onClick={onClick}
      className="w-full text-left rounded-xl border border-border bg-card hover:bg-muted/30 transition-colors p-4 space-y-3 group"
    >
      {/* Row 1: name + status */}
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="font-semibold text-foreground text-[15px] leading-tight group-hover:text-primary transition-colors truncate">
            {project.name}
          </p>
          <p className="text-xs text-muted-foreground mt-0.5 truncate">{project.clientName}</p>
        </div>
        <ProjectStatusBadge status={project.status} className="shrink-0" />
      </div>

      {/* Row 2: financials */}
      {hasFinancial ? (
        <div className="grid grid-cols-3 gap-2">
          <div>
            <p className="text-[10px] uppercase tracking-wide text-muted-foreground font-medium">Previsto</p>
            <p className="text-sm font-semibold tabular-nums">{fmt(budgetRef)}</p>
          </div>
          <div>
            <p className="text-[10px] uppercase tracking-wide text-muted-foreground font-medium">Realizado</p>
            <p className="text-sm font-semibold tabular-nums">{fmt(realized)}</p>
          </div>
          <div>
            <p className="text-[10px] uppercase tracking-wide text-muted-foreground font-medium">Desvio</p>
            <div className="flex items-center gap-1">
              {variance != null && (
                variance > 0
                  ? <TrendingUp className="h-3 w-3 text-red-500 shrink-0" />
                  : variance < 0
                    ? <TrendingDown className="h-3 w-3 text-emerald-500 shrink-0" />
                    : <Minus className="h-3 w-3 text-muted-foreground shrink-0" />
              )}
              <p className={cn("text-sm font-semibold tabular-nums", varianceColor(variance))}>
                {fmt(variance)}
              </p>
            </div>
          </div>
        </div>
      ) : (
        <p className="text-xs text-muted-foreground italic">Sem orçamento definido</p>
      )}

      {/* Row 3: dates + status icon */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3 text-xs text-muted-foreground">
          {project.startDate && (
            <span>Início: {fmtDate(project.startDate)}</span>
          )}
          {project.expectedEndDate && (
            <span>Prev.: {fmtDate(project.expectedEndDate)}</span>
          )}
        </div>
        <StatusIcon className={cn(
          "h-4 w-4 shrink-0",
          project.status === "Completed" ? "text-emerald-500" :
          project.status === "Cancelled" ? "text-red-500" :
          project.status === "Paused"    ? "text-yellow-500" :
          project.status === "InProgress"? "text-blue-500" :
          "text-muted-foreground",
        )} />
      </div>

      {/* Row 4: location if present */}
      {project.location && (
        <p className="text-xs text-muted-foreground truncate">📍 {project.location}</p>
      )}
    </button>
  );
}

// ── New project dialog ────────────────────────────────────────────────────────

type ProjectTypeOption = { value: number; label: string };
const PROJECT_TYPES: ProjectTypeOption[] = [
  { value: 0, label: "Residencial" },
  { value: 1, label: "Comercial" },
  { value: 2, label: "Industrial" },
  { value: 3, label: "Infraestrutura" },
];

function NewProjectDialog({
  open,
  onClose,
}: {
  open: boolean;
  onClose: () => void;
}) {
  const navigate = useNavigate();
  const createMut = useCreateProject();

  const [name,      setName]      = useState("");
  const [client,    setClient]    = useState("");
  const [location,  setLocation]  = useState("");
  const [typeVal,   setTypeVal]   = useState<number>(0);
  const [budget,    setBudget]    = useState("");
  const [startDate, setStartDate] = useState("");

  const reset = () => {
    setName(""); setClient(""); setLocation(""); setTypeVal(0);
    setBudget(""); setStartDate("");
  };

  const handleClose = () => { reset(); onClose(); };

  const handleSubmit = () => {
    if (!name.trim() || !client.trim()) return;
    createMut.mutate({
      name:            name.trim(),
      clientName:      client.trim(),
      location:        location.trim() || undefined,
      type:            typeVal,
      budgetEstimated: budget ? parseFloat(budget) : undefined,
      startDate:       startDate || undefined,
    }, {
      onSuccess: (dto) => {
        toast.success("Obra criada com sucesso!");
        handleClose();
        navigate(`/build/projetos/${dto.id}`);
      },
      onError: (err) => {
        toast.error(err instanceof Error ? err.message : "Erro ao criar obra.");
      },
    });
  };

  return (
    <Dialog open={open} onOpenChange={(v) => !v && handleClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <HardHat className="h-5 w-5 text-primary" />
            Nova obra
          </DialogTitle>
        </DialogHeader>

        <div className="space-y-3 py-1">
          <div className="space-y-1">
            <Label className="text-xs">Nome da obra *</Label>
            <Input
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Ex: Residencial Parque Sul"
              autoFocus
            />
          </div>

          <div className="space-y-1">
            <Label className="text-xs">Cliente *</Label>
            <Input
              value={client}
              onChange={(e) => setClient(e.target.value)}
              placeholder="Nome do cliente"
            />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1">
              <Label className="text-xs">Tipo</Label>
              <Select value={String(typeVal)} onValueChange={(v) => setTypeVal(Number(v))}>
                <SelectTrigger className="text-sm">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {PROJECT_TYPES.map((t) => (
                    <SelectItem key={t.value} value={String(t.value)}>{t.label}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1">
              <Label className="text-xs">Orçamento previsto</Label>
              <div className="relative">
                <span className="absolute left-2.5 top-1/2 -translate-y-1/2 text-xs text-muted-foreground">R$</span>
                <Input
                  value={budget}
                  onChange={(e) => setBudget(e.target.value)}
                  type="number"
                  min={0}
                  className="pl-7 text-sm"
                  placeholder="0,00"
                />
              </div>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1">
              <Label className="text-xs">Data de início</Label>
              <Input
                value={startDate}
                onChange={(e) => setStartDate(e.target.value)}
                type="date"
                className="text-sm"
              />
            </div>
            <div className="space-y-1">
              <Label className="text-xs">Localização</Label>
              <Input
                value={location}
                onChange={(e) => setLocation(e.target.value)}
                placeholder="Endereço / cidade"
                className="text-sm"
              />
            </div>
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={handleClose}>Cancelar</Button>
          <Button
            onClick={handleSubmit}
            disabled={!name.trim() || !client.trim() || createMut.isPending}
          >
            {createMut.isPending ? "Criando…" : "Criar obra"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

const STATUS_FILTERS: Array<{ key: BuildProjectStatus | "all"; label: string }> = [
  { key: "all",        label: "Todas" },
  { key: "Planning",   label: "Planejamento" },
  { key: "InProgress", label: "Em andamento" },
  { key: "Paused",     label: "Pausadas" },
  { key: "Completed",  label: "Concluídas" },
  { key: "Cancelled",  label: "Canceladas" },
];

export default function BuildProjectsPage() {
  const navigate = useNavigate();

  const [statusFilter, setStatusFilter] = useState<BuildProjectStatus | "all">("all");
  const [search, setSearch]             = useState("");
  const [newOpen, setNewOpen]           = useState(false);

  const { data, isLoading, isError } = useProjects(
    statusFilter !== "all" ? statusFilter : undefined,
  );

  const projects = data?.items ?? [];

  const filtered = search
    ? projects.filter(
        (p) =>
          p.name.toLowerCase().includes(search.toLowerCase()) ||
          p.clientName.toLowerCase().includes(search.toLowerCase()),
      )
    : projects;

  // KPI summary
  const activeCount   = projects.filter((p) => p.status === "InProgress").length;
  const planningCount = projects.filter((p) => p.status === "Planning").length;
  const totalBudget   = projects.reduce((s, p) => s + (p.budgetApproved ?? p.budgetEstimated ?? 0), 0);

  return (
    <div className="p-6 space-y-6">
      <PageHeader
        title="Obras"
        description="Gestão completa de projetos de construção."
        actions={
          <Button onClick={() => setNewOpen(true)}>
            <Plus className="h-4 w-4 mr-1.5" />
            Nova obra
          </Button>
        }
      />

      {/* ── KPI strip ────────────────────────────────────────────────────────── */}
      {!isLoading && projects.length > 0 && (
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
          {[
            { label: "Total de obras",    value: String(projects.length), color: "text-foreground" },
            { label: "Em andamento",       value: String(activeCount),   color: "text-blue-600 dark:text-blue-400" },
            { label: "Planejamento",       value: String(planningCount), color: "text-muted-foreground" },
            { label: "Budget total",       value: fmt(totalBudget),      color: "text-primary" },
          ].map((kpi) => (
            <div key={kpi.label} className="rounded-xl border border-border bg-card p-3">
              <p className="text-[11px] text-muted-foreground uppercase tracking-wide">{kpi.label}</p>
              <p className={cn("text-xl font-bold tabular-nums mt-0.5", kpi.color)}>{kpi.value}</p>
            </div>
          ))}
        </div>
      )}

      {/* ── Filters ──────────────────────────────────────────────────────────── */}
      <div className="space-y-3">
        <div className="flex gap-1.5 flex-wrap">
          {STATUS_FILTERS.map((f) => (
            <StatusFilterBtn
              key={f.key}
              label={f.label}
              active={statusFilter === f.key}
              onClick={() => setStatusFilter(f.key)}
            />
          ))}
        </div>

        <div className="relative max-w-sm">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
          <Input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Buscar por obra ou cliente…"
            className="pl-9"
          />
        </div>
      </div>

      {/* ── List ─────────────────────────────────────────────────────────────── */}
      {isLoading ? (
        <div className="space-y-3">
          {[1, 2, 3].map((i) => (
            <div key={i} className="rounded-xl border border-border bg-card p-4 h-28 animate-pulse bg-muted/30" />
          ))}
        </div>
      ) : isError ? (
        <div className="rounded-xl border border-destructive/20 bg-destructive/5 p-8 text-center">
          <p className="text-sm text-destructive font-medium">Erro ao carregar obras.</p>
          <p className="text-xs text-muted-foreground mt-1">Verifique a conexão e tente novamente.</p>
        </div>
      ) : filtered.length === 0 ? (
        <div className="rounded-xl border border-border bg-card p-12 text-center">
          <HardHat className="h-10 w-10 text-muted-foreground mx-auto mb-3 opacity-40" />
          <p className="text-sm font-medium text-muted-foreground">
            {search
              ? "Nenhuma obra encontrada para esta busca."
              : statusFilter !== "all"
                ? "Nenhuma obra neste status."
                : "Nenhuma obra cadastrada ainda."}
          </p>
          {!search && statusFilter === "all" && (
            <Button variant="outline" className="mt-4" onClick={() => setNewOpen(true)}>
              <Plus className="h-4 w-4 mr-1.5" />
              Criar primeira obra
            </Button>
          )}
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-3">
          {filtered.map((p) => (
            <ProjectCard
              key={p.id}
              project={p}
              onClick={() => navigate(`/build/projetos/${p.id}`)}
            />
          ))}
        </div>
      )}

      {/* ── Totals ───────────────────────────────────────────────────────────── */}
      {!isLoading && filtered.length > 0 && (
        <p className="text-xs text-muted-foreground">
          {filtered.length} obra{filtered.length !== 1 ? "s" : ""}
          {search && ` · filtradas por "${search}"`}
        </p>
      )}

      <NewProjectDialog open={newOpen} onClose={() => setNewOpen(false)} />
    </div>
  );
}
