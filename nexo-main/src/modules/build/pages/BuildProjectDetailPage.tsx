import { useState } from "react";
import { useParams, useNavigate } from "react-router-dom";
import {
  ArrowLeft, Play, Pause, CheckCircle, XCircle, Plus, Trash2, Pencil,
  Check, X, Camera, TrendingUp, TrendingDown, FileText, Calendar,
  MapPin, User, DollarSign, BarChart2, BookOpen, HardHat,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { toast } from "sonner";

import {
  useProjectDetails, useProjectFinancial,
  useStartProject, usePauseProject, useCompleteProject, useCancelProject,
  useStages, useCreateStage, useUpdateStageProgress, useDeleteStage,
  useBudgets, useBudget, useCreateBudget,
  useSendBudget, useApproveBudget, useRejectBudget,
  useAddBudgetItem,
  useDailyLogs, useCreateDailyLog, useAddDailyLogPhoto,
} from "../hooks/use-build";
import { useProjectMovements } from "../hooks/use-interpreter";
import { ProjectStatusBadge } from "../components/ProjectStatusBadge";
import { BudgetStatusBadge } from "../components/BudgetStatusBadge";
import { BuildExpenseDialog } from "../components/BuildExpenseDialog";
import type {
  BuildProjectDetailsDto, BuildStageDto, BuildBudgetDto, BuildDailyLogDto,
} from "../api/build.api";

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

type Tab = "geral" | "etapas" | "orcamento" | "diario" | "financeiro";

// ── Tab selector ──────────────────────────────────────────────────────────────

function TabBar({ active, onChange }: { active: Tab; onChange: (t: Tab) => void }) {
  const tabs: Array<{ key: Tab; label: string; icon: React.ElementType }> = [
    { key: "geral",      label: "Geral",       icon: HardHat },
    { key: "etapas",     label: "Etapas",      icon: CheckCircle },
    { key: "orcamento",  label: "Orçamento",   icon: FileText },
    { key: "diario",     label: "Diário",      icon: BookOpen },
    { key: "financeiro", label: "Financeiro",  icon: BarChart2 },
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
          <div key={label} className="rounded-xl border border-border bg-card p-3 flex items-center gap-3">
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
        <div className="rounded-xl border border-border bg-card p-4 space-y-3">
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
                  handleAction(cancelMut as ReturnType<typeof useStartProject>, "cancelada");
                  navigate("/build");
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
    </div>
  );
}

// ── Tab: Etapas ───────────────────────────────────────────────────────────────

function TabEtapas({ projectId }: { projectId: string }) {
  const { data: stages = [], isLoading } = useStages(projectId);
  const createMut   = useCreateStage(projectId);
  const progressMut = useUpdateStageProgress(projectId);
  const deleteMut   = useDeleteStage(projectId);

  const [adding, setAdding]   = useState(false);
  const [newName, setNewName] = useState("");
  const [editId,  setEditId]  = useState<string | null>(null);
  const [editPct, setEditPct] = useState(0);

  const handleAddStage = () => {
    if (!newName.trim()) return;
    createMut.mutate({ name: newName.trim() }, {
      onSuccess: () => { setAdding(false); setNewName(""); toast.success("Etapa adicionada!"); },
      onError:   (e) => toast.error(e instanceof Error ? e.message : "Erro ao criar etapa."),
    });
  };

  const handleUpdateProgress = (stage: BuildStageDto) => {
    progressMut.mutate({ id: stage.id, req: { progressPercent: editPct } }, {
      onSuccess: () => { setEditId(null); toast.success("Progresso atualizado!"); },
      onError:   (e) => toast.error(e instanceof Error ? e.message : "Erro ao atualizar."),
    });
  };

  const handleDelete = (id: string) => {
    if (!confirm("Remover esta etapa?")) return;
    deleteMut.mutate(id, {
      onSuccess: () => toast.success("Etapa removida."),
      onError:   (e) => toast.error(e instanceof Error ? e.message : "Erro ao remover."),
    });
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <p className="text-sm text-muted-foreground">
          {stages.length} etapa{stages.length !== 1 ? "s" : ""}
        </p>
        {!adding && (
          <Button size="sm" variant="outline" onClick={() => setAdding(true)}>
            <Plus className="h-3.5 w-3.5 mr-1" /> Adicionar etapa
          </Button>
        )}
      </div>

      {adding && (
        <div className="flex items-center gap-2 p-3 rounded-xl border border-dashed border-primary/40">
          <Input
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
            placeholder="Nome da etapa…"
            className="flex-1 text-sm"
            autoFocus
            onKeyDown={(e) => e.key === "Enter" && handleAddStage()}
          />
          <button onClick={handleAddStage} disabled={!newName.trim() || createMut.isPending}
            className="text-primary disabled:opacity-40">
            <Check className="h-4 w-4" />
          </button>
          <button onClick={() => { setAdding(false); setNewName(""); }}
            className="text-muted-foreground">
            <X className="h-4 w-4" />
          </button>
        </div>
      )}

      {isLoading ? (
        <div className="space-y-2">
          {[1, 2, 3].map((i) => (
            <div key={i} className="h-16 rounded-xl bg-muted animate-pulse" />
          ))}
        </div>
      ) : stages.length === 0 ? (
        <div className="rounded-xl border border-dashed border-border p-8 text-center">
          <p className="text-sm text-muted-foreground">Nenhuma etapa cadastrada.</p>
          <Button variant="outline" size="sm" className="mt-3" onClick={() => setAdding(true)}>
            <Plus className="h-3.5 w-3.5 mr-1" /> Adicionar primeira etapa
          </Button>
        </div>
      ) : (
        <div className="space-y-2">
          {stages.map((stage) => (
            <div key={stage.id} className="rounded-xl border border-border bg-card p-4 space-y-3">
              <div className="flex items-center justify-between gap-2">
                <div className="min-w-0">
                  <p className="font-medium text-sm truncate">{stage.name}</p>
                  {stage.description && (
                    <p className="text-xs text-muted-foreground truncate">{stage.description}</p>
                  )}
                </div>
                <div className="flex items-center gap-1 shrink-0">
                  <span className={cn(
                    "text-xs px-2 py-0.5 rounded-full font-medium",
                    stage.status === "Completed" ? "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400" :
                    stage.status === "InProgress" ? "bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400" :
                    "bg-muted text-muted-foreground",
                  )}>
                    {stage.status === "Completed" ? "Concluída" :
                     stage.status === "InProgress" ? "Em andamento" : "Pendente"}
                  </span>
                  <button
                    onClick={() => { setEditId(stage.id); setEditPct(stage.progressPercent); }}
                    className="text-muted-foreground hover:text-foreground transition-colors p-1"
                  >
                    <Pencil className="h-3.5 w-3.5" />
                  </button>
                  <button
                    onClick={() => handleDelete(stage.id)}
                    className="text-muted-foreground hover:text-destructive transition-colors p-1"
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </button>
                </div>
              </div>

              {/* Progress bar + inline edit */}
              {editId === stage.id ? (
                <div className="flex items-center gap-2">
                  <input
                    type="range"
                    min={0}
                    max={100}
                    value={editPct}
                    onChange={(e) => setEditPct(Number(e.target.value))}
                    className="flex-1 accent-primary"
                  />
                  <span className="text-sm tabular-nums w-10 text-right">{editPct}%</span>
                  <button onClick={() => handleUpdateProgress(stage)} disabled={progressMut.isPending}
                    className="text-primary disabled:opacity-40">
                    <Check className="h-4 w-4" />
                  </button>
                  <button onClick={() => setEditId(null)} className="text-muted-foreground">
                    <X className="h-4 w-4" />
                  </button>
                </div>
              ) : (
                <div className="space-y-1">
                  <div className="flex items-center justify-between text-xs text-muted-foreground">
                    <span>Progresso</span>
                    <span className="tabular-nums">{stage.progressPercent}%</span>
                  </div>
                  <div className="h-2 rounded-full bg-muted overflow-hidden">
                    <div
                      className={cn(
                        "h-full rounded-full transition-all",
                        stage.progressPercent === 100 ? "bg-emerald-500" :
                        stage.progressPercent > 0 ? "bg-primary" : "bg-muted-foreground/20",
                      )}
                      style={{ width: `${stage.progressPercent}%` }}
                    />
                  </div>
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// ── Tab: Orçamento ────────────────────────────────────────────────────────────

function TabOrcamento({ project }: { project: BuildProjectDetailsDto }) {
  const { data: budgets, isLoading } = useBudgets(project.id);
  const createBudgetMut = useCreateBudget();
  const sendMut    = useSendBudget(project.id);
  const approveMut = useApproveBudget(project.id);
  const rejectMut  = useRejectBudget(project.id);

  const [selectedBudgetId, setSelectedBudgetId] = useState<string | null>(null);
  const [addingBudget, setAddingBudget] = useState(false);
  const [budgetName, setBudgetName]     = useState("");
  const [budgetMargin, setBudgetMargin] = useState("15");

  // Item form
  const [addingItem, setAddingItem]     = useState(false);
  const [itemName, setItemName]         = useState("");
  const [itemCat, setItemCat]           = useState("Materiais");
  const [itemQty, setItemQty]           = useState("1");
  const [itemUnit, setItemUnit]         = useState("un");
  const [itemCost, setItemCost]         = useState("");

  const items = budgets?.items ?? [];
  const selectedBudget = items.find((b) => b.id === selectedBudgetId) ?? items[0];

  const { data: budgetDetail } = useBudget(selectedBudget?.id ?? "");
  const budget = budgetDetail ?? selectedBudget;

  const addItemMut = useAddBudgetItem(budget?.id ?? "");

  const handleCreateBudget = () => {
    if (!budgetName.trim()) return;
    createBudgetMut.mutate({
      name:          budgetName.trim(),
      projectId:     project.id,
      marginPercent: parseFloat(budgetMargin) || 15,
    }, {
      onSuccess: (dto) => {
        setAddingBudget(false); setBudgetName(""); setBudgetMargin("15");
        setSelectedBudgetId(dto.id);
        toast.success("Orçamento criado!");
      },
      onError: (e) => toast.error(e instanceof Error ? e.message : "Erro ao criar orçamento."),
    });
  };

  const handleAddItem = () => {
    if (!itemName.trim() || !itemCost) return;
    addItemMut.mutate({
      name:     itemName.trim(),
      category: itemCat,
      quantity: parseFloat(itemQty) || 1,
      unit:     itemUnit,
      unitCost: parseFloat(itemCost) || 0,
    }, {
      onSuccess: () => {
        setAddingItem(false);
        setItemName(""); setItemQty("1"); setItemCost("");
        toast.success("Item adicionado!");
      },
      onError: (e) => toast.error(e instanceof Error ? e.message : "Erro ao adicionar item."),
    });
  };

  const handleTransition = (
    mut: ReturnType<typeof useSendBudget>,
    id: string,
    label: string,
  ) => {
    mut.mutate(id, {
      onSuccess: () => toast.success(`Orçamento ${label}!`),
      onError:   (e) => toast.error(e instanceof Error ? e.message : "Erro."),
    });
  };

  return (
    <div className="space-y-4">
      {isLoading ? (
        <div className="h-20 rounded-xl bg-muted animate-pulse" />
      ) : items.length === 0 && !addingBudget ? (
        <div className="rounded-xl border border-dashed border-border p-8 text-center">
          <FileText className="h-8 w-8 mx-auto text-muted-foreground opacity-40 mb-2" />
          <p className="text-sm text-muted-foreground">Nenhum orçamento criado.</p>
          <Button variant="outline" size="sm" className="mt-3" onClick={() => setAddingBudget(true)}>
            <Plus className="h-3.5 w-3.5 mr-1" /> Criar orçamento
          </Button>
        </div>
      ) : (
        <>
          {/* Budget selector */}
          <div className="flex items-center justify-between">
            <div className="flex gap-1.5 flex-wrap">
              {items.map((b) => (
                <button
                  key={b.id}
                  onClick={() => setSelectedBudgetId(b.id)}
                  className={cn(
                    "px-3 py-1.5 rounded-lg text-xs font-medium border transition-colors",
                    (selectedBudget?.id === b.id)
                      ? "bg-primary text-primary-foreground border-primary"
                      : "border-border text-muted-foreground hover:text-foreground",
                  )}
                >
                  {b.name}
                </button>
              ))}
            </div>
            {!addingBudget && (
              <Button size="sm" variant="outline" onClick={() => setAddingBudget(true)}>
                <Plus className="h-3.5 w-3.5" />
              </Button>
            )}
          </div>

          {/* Budget summary */}
          {budget && (
            <div className="rounded-xl border border-border bg-card p-4 space-y-4">
              <div className="flex items-start justify-between gap-2">
                <div>
                  <p className="font-semibold text-sm">{budget.name}</p>
                  <p className="text-xs text-muted-foreground mt-0.5">
                    Margem {fmtPct(budget.marginPercent)}
                  </p>
                </div>
                <BudgetStatusBadge status={budget.status} />
              </div>

              {/* KPIs */}
              <div className="grid grid-cols-3 gap-3">
                <div>
                  <p className="text-[10px] text-muted-foreground uppercase tracking-wide">Custo total</p>
                  <p className="text-sm font-bold tabular-nums">{fmt(budget.totalCost)}</p>
                </div>
                <div>
                  <p className="text-[10px] text-muted-foreground uppercase tracking-wide">Margem</p>
                  <p className="text-sm font-bold tabular-nums">{fmtPct(budget.marginPercent)}</p>
                </div>
                <div>
                  <p className="text-[10px] text-muted-foreground uppercase tracking-wide">Preço final</p>
                  <p className="text-sm font-bold tabular-nums text-primary">{fmt(budget.finalPrice)}</p>
                </div>
              </div>

              {/* Actions */}
              {budget.status !== "Rejected" && budget.status !== "Converted" && (
                <div className="flex gap-2 flex-wrap">
                  {budget.status === "Draft" && (
                    <Button size="sm" variant="outline"
                      onClick={() => handleTransition(sendMut, budget.id, "enviado")}
                      disabled={sendMut.isPending}>
                      Marcar como enviado
                    </Button>
                  )}
                  {(budget.status === "Draft" || budget.status === "Sent") && (
                    <>
                      <Button size="sm" variant="outline"
                        className="text-emerald-600 border-emerald-200 hover:bg-emerald-50 dark:border-emerald-800"
                        onClick={() => handleTransition(approveMut, budget.id, "aprovado")}
                        disabled={approveMut.isPending}>
                        <Check className="h-3.5 w-3.5 mr-1" /> Aprovar
                      </Button>
                      <Button size="sm" variant="outline"
                        className="text-destructive border-destructive/30 hover:bg-destructive/5"
                        onClick={() => handleTransition(rejectMut, budget.id, "rejeitado")}
                        disabled={rejectMut.isPending}>
                        <X className="h-3.5 w-3.5 mr-1" /> Rejeitar
                      </Button>
                    </>
                  )}
                </div>
              )}

              {/* Items */}
              <div className="space-y-2">
                <div className="flex items-center justify-between">
                  <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Itens</p>
                  {(budget.status === "Draft" || budget.status === "Sent") && !addingItem && (
                    <button onClick={() => setAddingItem(true)}
                      className="text-xs text-primary hover:underline">
                      + Adicionar item
                    </button>
                  )}
                </div>

                {addingItem && (
                  <div className="p-3 rounded-xl border border-dashed border-primary/40 space-y-2">
                    <div className="grid grid-cols-2 gap-2">
                      <div>
                        <Label className="text-xs">Descrição</Label>
                        <Input value={itemName} onChange={(e) => setItemName(e.target.value)}
                          placeholder="Ex: Cimento CP II" className="h-8 text-sm mt-0.5" autoFocus />
                      </div>
                      <div>
                        <Label className="text-xs">Categoria</Label>
                        <Input value={itemCat} onChange={(e) => setItemCat(e.target.value)}
                          placeholder="Materiais" className="h-8 text-sm mt-0.5" />
                      </div>
                    </div>
                    <div className="grid grid-cols-3 gap-2">
                      <div>
                        <Label className="text-xs">Qtd</Label>
                        <Input value={itemQty} onChange={(e) => setItemQty(e.target.value)}
                          type="number" min={0} className="h-8 text-sm mt-0.5" />
                      </div>
                      <div>
                        <Label className="text-xs">Unidade</Label>
                        <Input value={itemUnit} onChange={(e) => setItemUnit(e.target.value)}
                          placeholder="m³, kg, un…" className="h-8 text-sm mt-0.5" />
                      </div>
                      <div>
                        <Label className="text-xs">Custo unit.</Label>
                        <div className="relative mt-0.5">
                          <span className="absolute left-2 top-1/2 -translate-y-1/2 text-xs text-muted-foreground">R$</span>
                          <Input value={itemCost} onChange={(e) => setItemCost(e.target.value)}
                            type="number" min={0} className="h-8 text-sm pl-6" placeholder="0,00" />
                        </div>
                      </div>
                    </div>
                    <div className="flex gap-1.5 justify-end">
                      <button onClick={handleAddItem}
                        disabled={!itemName.trim() || !itemCost || addItemMut.isPending}
                        className="text-primary disabled:opacity-40">
                        <Check className="h-4 w-4" />
                      </button>
                      <button onClick={() => { setAddingItem(false); setItemName(""); setItemCost(""); }}
                        className="text-muted-foreground">
                        <X className="h-4 w-4" />
                      </button>
                    </div>
                  </div>
                )}

                {budget.items.length === 0 ? (
                  <p className="text-sm text-muted-foreground py-2 text-center">
                    Nenhum item no orçamento.
                  </p>
                ) : (
                  <div className="rounded-xl border border-border overflow-hidden">
                    <table className="w-full text-sm min-w-[480px]">
                      <thead className="bg-muted/40 border-b border-border">
                        <tr>
                          <th className="text-left text-xs font-medium text-muted-foreground px-3 py-2">Descrição</th>
                          <th className="text-right text-xs font-medium text-muted-foreground px-3 py-2">Qtd</th>
                          <th className="text-right text-xs font-medium text-muted-foreground px-3 py-2">Unit.</th>
                          <th className="text-right text-xs font-medium text-muted-foreground px-3 py-2">Total</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-border">
                        {budget.items.map((item) => (
                          <tr key={item.id} className="hover:bg-muted/20">
                            <td className="px-3 py-2">
                              <p className="font-medium">{item.name}</p>
                              <p className="text-xs text-muted-foreground">{item.category}</p>
                            </td>
                            <td className="px-3 py-2 text-right tabular-nums">
                              {item.quantity} {item.unit}
                            </td>
                            <td className="px-3 py-2 text-right tabular-nums">{fmt(item.unitCost)}</td>
                            <td className="px-3 py-2 text-right tabular-nums font-medium">{fmt(item.totalCost)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </div>
            </div>
          )}
        </>
      )}

      {/* New budget form */}
      {addingBudget && (
        <div className="rounded-xl border border-dashed border-primary/40 p-4 space-y-3">
          <p className="text-sm font-medium">Novo orçamento</p>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <Label className="text-xs">Nome do orçamento</Label>
              <Input value={budgetName} onChange={(e) => setBudgetName(e.target.value)}
                placeholder="Ex: Orçamento V1" className="mt-0.5" autoFocus />
            </div>
            <div>
              <Label className="text-xs">Margem (%)</Label>
              <Input value={budgetMargin} onChange={(e) => setBudgetMargin(e.target.value)}
                type="number" min={0} max={100} className="mt-0.5" />
            </div>
          </div>
          <div className="flex gap-2 justify-end">
            <Button variant="outline" size="sm" onClick={() => setAddingBudget(false)}>Cancelar</Button>
            <Button size="sm"
              disabled={!budgetName.trim() || createBudgetMut.isPending}
              onClick={handleCreateBudget}>
              {createBudgetMut.isPending ? "Criando…" : "Criar"}
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}

// ── Tab: Diário ───────────────────────────────────────────────────────────────

function TabDiario({ projectId }: { projectId: string }) {
  const { data, isLoading } = useDailyLogs(projectId);
  const createMut   = useCreateDailyLog(projectId);
  const addPhotoMut = useAddDailyLogPhoto(projectId);

  const [adding, setAdding]         = useState(false);
  const [logDate, setLogDate]       = useState(() => new Date().toISOString().slice(0, 10));
  const [logNotes, setLogNotes]     = useState("");
  const [logWeather, setLogWeather] = useState("");

  // Photo form
  const [photoLogId, setPhotoLogId] = useState<string | null>(null);
  const [photoKey, setPhotoKey]     = useState("");
  const [photoCaption, setPhotoCaption] = useState("");

  const logs = data?.items ?? [];

  const handleCreateLog = () => {
    if (!logNotes.trim()) return;
    createMut.mutate({
      date:            logDate,
      notes:           logNotes.trim(),
      weatherSummary:  logWeather.trim() || undefined,
    }, {
      onSuccess: () => {
        setAdding(false);
        setLogNotes(""); setLogWeather(""); setLogDate(new Date().toISOString().slice(0, 10));
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

  const handleAddPhoto = (logId: string) => {
    if (!photoKey.trim()) return;
    addPhotoMut.mutate({ logId, req: { storageKey: photoKey.trim(), caption: photoCaption.trim() || undefined } }, {
      onSuccess: () => {
        setPhotoLogId(null); setPhotoKey(""); setPhotoCaption("");
        toast.success("Foto adicionada!");
      },
      onError: (e) => toast.error(e instanceof Error ? e.message : "Erro ao adicionar foto."),
    });
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <p className="text-sm text-muted-foreground">
          {logs.length} registro{logs.length !== 1 ? "s" : ""}
        </p>
        {!adding && (
          <Button size="sm" variant="outline" onClick={() => setAdding(true)}>
            <Plus className="h-3.5 w-3.5 mr-1" /> Novo registro
          </Button>
        )}
      </div>

      {adding && (
        <div className="rounded-xl border border-dashed border-primary/40 p-4 space-y-3">
          <p className="text-sm font-medium">Novo registro de obra</p>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <Label className="text-xs">Data</Label>
              <Input type="date" value={logDate} onChange={(e) => setLogDate(e.target.value)}
                className="mt-0.5 text-sm" />
            </div>
            <div>
              <Label className="text-xs">Clima</Label>
              <Input value={logWeather} onChange={(e) => setLogWeather(e.target.value)}
                placeholder="Ensolarado 28°C" className="mt-0.5 text-sm" />
            </div>
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
            <div key={i} className="h-24 rounded-xl bg-muted animate-pulse" />
          ))}
        </div>
      ) : logs.length === 0 ? (
        <div className="rounded-xl border border-dashed border-border p-8 text-center">
          <BookOpen className="h-8 w-8 mx-auto text-muted-foreground opacity-40 mb-2" />
          <p className="text-sm text-muted-foreground">Nenhum registro no diário.</p>
          <Button variant="outline" size="sm" className="mt-3" onClick={() => setAdding(true)}>
            <Plus className="h-3.5 w-3.5 mr-1" /> Criar primeiro registro
          </Button>
        </div>
      ) : (
        <div className="space-y-3">
          {logs.map((log: BuildDailyLogDto) => (
            <div key={log.id} className="rounded-xl border border-border bg-card p-4 space-y-2">
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
                <button
                  onClick={() => setPhotoLogId(log.id)}
                  className="flex items-center gap-1 text-xs text-muted-foreground hover:text-primary transition-colors"
                >
                  <Camera className="h-3.5 w-3.5" />
                  <span>Foto</span>
                </button>
              </div>

              <p className="text-sm text-foreground/80 leading-relaxed">{log.notes}</p>

              {/* Photos */}
              {log.photos.length > 0 && (
                <div className="flex gap-2 flex-wrap pt-1">
                  {log.photos.map((photo) => (
                    <div key={photo.id}
                      className="flex items-center gap-1.5 px-2 py-1 rounded-lg bg-muted text-xs text-muted-foreground max-w-[200px] truncate">
                      <Camera className="h-3 w-3 shrink-0" />
                      <span className="truncate">{photo.caption ?? photo.storageKey.split("/").pop()}</span>
                    </div>
                  ))}
                </div>
              )}

              {/* Add photo inline */}
              {photoLogId === log.id && (
                <div className="flex items-end gap-2 pt-1 p-2 rounded-lg border border-dashed border-primary/30 flex-wrap">
                  <div className="flex-1 min-w-[160px]">
                    <Label className="text-xs">Storage key (após upload)</Label>
                    <Input value={photoKey} onChange={(e) => setPhotoKey(e.target.value)}
                      placeholder="uploads/2026/foto.jpg" className="h-8 text-xs mt-0.5" autoFocus />
                  </div>
                  <div className="w-36">
                    <Label className="text-xs">Legenda</Label>
                    <Input value={photoCaption} onChange={(e) => setPhotoCaption(e.target.value)}
                      placeholder="Opcional" className="h-8 text-xs mt-0.5" />
                  </div>
                  <button onClick={() => handleAddPhoto(log.id)}
                    disabled={!photoKey.trim() || addPhotoMut.isPending}
                    className="text-primary disabled:opacity-40 pb-1">
                    <Check className="h-4 w-4" />
                  </button>
                  <button onClick={() => { setPhotoLogId(null); setPhotoKey(""); setPhotoCaption(""); }}
                    className="text-muted-foreground pb-1">
                    <X className="h-4 w-4" />
                  </button>
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// ── Tab: Financeiro ───────────────────────────────────────────────────────────

function TabFinanceiro({ projectId }: { projectId: string }) {
  const [expenseOpen, setExpenseOpen] = useState(false);

  const { data: financial, isLoading, isError } = useProjectFinancial(projectId);
  const { data: movementsData, isLoading: movLoading } = useProjectMovements(projectId);

  const movements = movementsData?.items ?? [];

  if (isLoading) {
    return (
      <div className="space-y-3">
        {[1, 2, 3].map((i) => (
          <div key={i} className="h-20 rounded-xl bg-muted animate-pulse" />
        ))}
      </div>
    );
  }

  if (isError || !financial) {
    return (
      <div className="rounded-xl border border-destructive/20 bg-destructive/5 p-8 text-center">
        <p className="text-sm text-destructive">Erro ao carregar resumo financeiro.</p>
      </div>
    );
  }

  const isOverBudget    = financial.budgetApproved != null &&
                          financial.totalRealizedExpenses > financial.budgetApproved;
  const varianceIsOver  = financial.varianceAmount > 0;
  const varianceIsEqual = financial.varianceAmount === 0;

  const coveragePercent = financial.budgetApproved
    ? Math.min(150, (financial.totalRealizedExpenses / financial.budgetApproved) * 100)
    : null;

  return (
    <div className="space-y-5">

      {/* ── Alert: over budget ─────────────────────────────────────────── */}
      {isOverBudget && (
        <div className="flex items-start gap-2.5 rounded-xl border border-red-200 bg-red-50 dark:border-red-800 dark:bg-red-900/20 p-3">
          <TrendingUp className="h-4 w-4 text-red-600 dark:text-red-400 shrink-0 mt-0.5" />
          <p className="text-sm text-red-700 dark:text-red-300 font-medium">
            Realizado supera o orçamento aprovado em{" "}
            {fmt(financial.totalRealizedExpenses - (financial.budgetApproved ?? 0))}.
          </p>
        </div>
      )}

      {/* ── KPI grid ───────────────────────────────────────────────────── */}
      <div className="grid grid-cols-2 gap-3">
        <div className="rounded-xl border border-border bg-card p-4">
          <div className="flex items-center gap-2 mb-2">
            <FileText className="h-3.5 w-3.5 text-blue-500" />
            <span className="text-xs text-muted-foreground">Orçamento aprovado</span>
          </div>
          <p className="text-xl font-bold tabular-nums text-blue-600 dark:text-blue-400">
            {fmt(financial.budgetApproved)}
          </p>
        </div>

        <div className={cn(
          "rounded-xl border p-4",
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

        <div className="rounded-xl border border-border bg-card p-4">
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
            {varianceIsOver ? "+" : ""}{fmt(financial.varianceAmount)}
          </p>
          {financial.variancePercent !== 0 && (
            <p className="text-xs text-muted-foreground mt-1">
              {Math.abs(financial.variancePercent).toFixed(1)}% do aprovado
            </p>
          )}
        </div>

        <div className="rounded-xl border border-border bg-card p-4">
          <div className="flex items-center gap-2 mb-2">
            <FileText className="h-3.5 w-3.5 text-muted-foreground" />
            <span className="text-xs text-muted-foreground">Estimado</span>
          </div>
          <p className="text-xl font-bold tabular-nums text-muted-foreground">
            {fmt(financial.budgetEstimated)}
          </p>
        </div>
      </div>

      {/* ── Coverage bar ───────────────────────────────────────────────── */}
      {coveragePercent != null && (
        <div className="rounded-xl border border-border bg-card p-4 space-y-2">
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
            {fmt(financial.totalRealizedExpenses)} de {fmt(financial.budgetApproved)} utilizados
            {financial.lastMovementDate && (
              <> · última movimentação {fmtDate(financial.lastMovementDate)}</>
            )}
          </p>
        </div>
      )}

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
              <div key={i} className="h-14 rounded-xl bg-muted animate-pulse" />
            ))}
          </div>
        ) : movements.length === 0 ? (
          <div className="rounded-xl border border-dashed border-border p-6 text-center">
            <DollarSign className="h-7 w-7 mx-auto text-muted-foreground opacity-30 mb-2" />
            <p className="text-sm text-muted-foreground">
              Nenhuma despesa registrada ainda.
            </p>
            <p className="text-xs text-muted-foreground mt-1">
              Use o botão acima para registrar a primeira despesa.
            </p>
          </div>
        ) : (
          <div className="rounded-xl border border-border overflow-hidden">
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
                  </p>
                </div>
                <p className="text-sm font-bold tabular-nums text-red-600 dark:text-red-400 shrink-0">
                  -{fmt(m.amount)}
                </p>
              </div>
            ))}
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
        <div className="h-10 rounded-xl bg-muted animate-pulse" />
        <div className="grid grid-cols-2 gap-3">
          {[1, 2, 3, 4].map((i) => (
            <div key={i} className="h-20 rounded-xl bg-muted animate-pulse" />
          ))}
        </div>
      </div>
    );
  }

  if (isError || !project) {
    return (
      <div className="p-6">
        <div className="rounded-xl border border-destructive/20 bg-destructive/5 p-8 text-center">
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
        {tab === "etapas"     && <TabEtapas projectId={project.id} />}
        {tab === "orcamento"  && <TabOrcamento project={project} />}
        {tab === "diario"     && <TabDiario projectId={project.id} />}
        {tab === "financeiro" && <TabFinanceiro projectId={project.id} />}
      </div>
    </div>
  );
}
