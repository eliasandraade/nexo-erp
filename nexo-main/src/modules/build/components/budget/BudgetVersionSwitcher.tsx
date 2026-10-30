import { cn } from "@/lib/utils";
import { useBudgetVersions } from "../../hooks/use-build-commercial";
import { BudgetStatusBadge } from "../BudgetStatusBadge";
import { fmtBRL } from "../../lib/format";

interface Props {
  budgetId: string;
  onSelect: (id: string) => void;
}

/** Versions of the budget's family (Versão 1, 2…), each with its status; click switches to it. */
export function BudgetVersionSwitcher({ budgetId, onSelect }: Props) {
  const { data: versions } = useBudgetVersions(budgetId);
  if (!versions || versions.length < 2) return null;
  const sorted = [...versions].sort((a, b) => a.version - b.version);

  return (
    <div className="space-y-1">
      <p className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">Versões</p>
      <div className="flex flex-wrap gap-1.5" role="group" aria-label="Versões do orçamento">
        {sorted.map((v) => (
          <button key={v.id} onClick={() => onSelect(v.id)}
            className={cn(
              "flex items-center gap-1.5 rounded-lg border px-2.5 py-1 text-xs transition-colors",
              v.id === budgetId ? "border-primary bg-primary/5 font-semibold" : "border-border text-muted-foreground hover:text-foreground",
            )}>
            Versão {v.version}
            <BudgetStatusBadge status={v.status} className="px-1.5 py-0 text-[10px]" />
            <span className="tabular-nums">{fmtBRL(v.finalPrice)}</span>
            {v.isExpired && <span className="text-[10px] font-semibold text-amber-600">vencido</span>}
          </button>
        ))}
      </div>
    </div>
  );
}
