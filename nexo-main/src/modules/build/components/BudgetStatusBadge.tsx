import { cn } from "@/lib/utils";
import type { BuildBudgetStatus } from "../api/build.api";

const STATUS_CONFIG: Record<BuildBudgetStatus, { label: string; className: string }> = {
  Draft:     { label: "Rascunho",  className: "bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400" },
  Sent:      { label: "Enviado",   className: "bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400" },
  Approved:  { label: "Aprovado",  className: "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400" },
  Rejected:  { label: "Rejeitado", className: "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400" },
  Converted: { label: "Convertido",className: "bg-purple-100 text-purple-700 dark:bg-purple-900/30 dark:text-purple-400" },
};

interface Props {
  status: BuildBudgetStatus;
  className?: string;
}

export function BudgetStatusBadge({ status, className }: Props) {
  const cfg = STATUS_CONFIG[status] ?? { label: status, className: "bg-muted text-muted-foreground" };
  return (
    <span className={cn(
      "inline-flex items-center px-2 py-0.5 rounded-full text-xs font-semibold",
      cfg.className, className,
    )}>
      {cfg.label}
    </span>
  );
}
