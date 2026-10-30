import { useState } from "react";
import { FileText, LayoutTemplate, Plus } from "lucide-react";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { useBudget, useBudgets } from "../../hooks/use-build";
import type { BuildBudgetDto, BuildProjectDto } from "../../api/build.api";
import { BudgetStatusBadge } from "../BudgetStatusBadge";
import { fmtDateTime } from "../../lib/format";
import { BudgetItemsSection } from "./BudgetItemsSection";
import { BudgetTotalsPanel } from "./BudgetTotalsPanel";
import { BudgetTermsSection } from "./BudgetTermsSection";
import { BudgetActions } from "./BudgetActions";
import { BudgetVersionSwitcher } from "./BudgetVersionSwitcher";
import { BudgetTemplatesDialog } from "./BudgetTemplatesDialog";
import { NewBudgetDialog } from "./NewBudgetDialog";

const familyOf = (b: BuildBudgetDto) => b.rootBudgetId ?? b.id;

/** One chip per version family: the newest version stands for it (the switcher shows the others). */
function latestPerFamily(budgets: BuildBudgetDto[]): BuildBudgetDto[] {
  const byFamily = new Map<string, BuildBudgetDto>();
  for (const b of budgets) {
    const cur = byFamily.get(familyOf(b));
    if (!cur || (b.version ?? 1) > (cur.version ?? 1)) byFamily.set(familyOf(b), b);
  }
  return [...byFamily.values()];
}

/** "Orçamento" tab of an obra: budgets, items by kind, totals, terms, versions, PDF, models and conversion. */
export function BuildBudgetTab({ project }: { project: Pick<BuildProjectDto, "id" | "areaM2"> }) {
  const { data: budgets, isLoading } = useBudgets(project.id);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [newMode, setNewMode] = useState<"blank" | "template" | null>(null);
  const [templatesOpen, setTemplatesOpen] = useState(false);

  const all = budgets?.items ?? [];
  const chips = latestPerFamily(all);
  const listed = all.find((b) => b.id === selectedId) ?? chips[0];
  const { data: detail } = useBudget(selectedId ?? listed?.id ?? "");
  const budget = detail ?? listed;
  const editable = budget?.status === "Draft" || budget?.status === "Sent";

  const toolbar = (
    <div className="flex flex-wrap gap-2">
      <Button size="sm" variant="outline" onClick={() => setNewMode("blank")}>
        <Plus className="mr-1 h-3.5 w-3.5" /> Novo orçamento
      </Button>
      <Button size="sm" variant="outline" onClick={() => setNewMode("template")}>
        <LayoutTemplate className="mr-1 h-3.5 w-3.5" /> Novo a partir de modelo
      </Button>
      <Button size="sm" variant="ghost" onClick={() => setTemplatesOpen(true)}>Modelos</Button>
    </div>
  );

  return (
    <div className="space-y-4">
      {isLoading ? (
        <div className="h-20 animate-pulse rounded-md bg-muted" />
      ) : all.length === 0 ? (
        <div className="space-y-3 rounded-md border border-dashed border-border p-8 text-center">
          <FileText className="mx-auto h-8 w-8 text-muted-foreground opacity-40" />
          <p className="text-sm text-muted-foreground">Nenhum orçamento criado.</p>
          <div className="flex justify-center">{toolbar}</div>
        </div>
      ) : (
        <>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="flex flex-wrap gap-1.5">
              {chips.map((b) => (
                <button key={b.id} onClick={() => setSelectedId(b.id)}
                  className={cn(
                    "rounded-lg border px-3 py-1.5 text-xs font-medium transition-colors",
                    budget && familyOf(budget) === familyOf(b)
                      ? "border-primary bg-primary text-primary-foreground"
                      : "border-border text-muted-foreground hover:text-foreground",
                  )}>
                  {b.name}{(b.version ?? 1) > 1 ? ` · v${b.version}` : ""}
                </button>
              ))}
            </div>
            {toolbar}
          </div>

          {budget && (
            <div className="space-y-4 rounded-md border border-border bg-card p-4">
              <div className="flex flex-wrap items-start justify-between gap-2">
                <div>
                  <p className="text-sm font-semibold">
                    {budget.name} <span className="font-normal text-muted-foreground">· Versão {budget.version ?? 1}</span>
                  </p>
                  {budget.status === "Approved" && budget.approvedByName && (
                    <p className="text-xs text-muted-foreground">Aprovado por {budget.approvedByName} em {fmtDateTime(budget.approvedAt)}</p>
                  )}
                  {budget.status === "Rejected" && (
                    <p className="text-xs text-muted-foreground">
                      Recusado{budget.rejectedByName ? ` por ${budget.rejectedByName}` : ""}
                      {budget.rejectedAt ? ` em ${fmtDateTime(budget.rejectedAt)}` : ""}
                      {budget.rejectionReason ? ` — “${budget.rejectionReason}”` : ""}
                    </p>
                  )}
                </div>
                <div className="flex items-center gap-1.5">
                  {budget.isExpired && (
                    <span className="rounded-full bg-amber-100 px-2 py-0.5 text-xs font-semibold text-amber-700 dark:bg-amber-900/30 dark:text-amber-400">
                      Vencido
                    </span>
                  )}
                  <BudgetStatusBadge status={budget.status} />
                </div>
              </div>

              <BudgetVersionSwitcher budgetId={budget.id} onSelect={setSelectedId} />
              <BudgetActions budget={budget} projectId={project.id} onCreated={setSelectedId} />
              <BudgetTotalsPanel budget={budget} editable={editable} />
              <BudgetTermsSection budget={budget} editable={editable} />
              <BudgetItemsSection budget={budget} editable={editable} />
            </div>
          )}
        </>
      )}

      <NewBudgetDialog projectId={project.id} defaultArea={project.areaM2} mode={newMode}
        onClose={() => setNewMode(null)} onCreated={setSelectedId} />
      <BudgetTemplatesDialog open={templatesOpen} onClose={() => setTemplatesOpen(false)} />
    </div>
  );
}
