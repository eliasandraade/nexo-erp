import { cn } from "@/lib/utils";
import type { BuildProjectStatus } from "../api/build.api";

const STATUS_CONFIG: Record<BuildProjectStatus, { label: string; className: string }> = {
  Planning:   { label: "Planejamento", className: "bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400" },
  InProgress: { label: "Em andamento", className: "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400" },
  Paused:     { label: "Pausada",      className: "bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-400" },
  Completed:  { label: "Concluída",    className: "bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400" },
  Cancelled:  { label: "Cancelada",    className: "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400" },
};

interface Props {
  status: BuildProjectStatus;
  className?: string;
}

export function ProjectStatusBadge({ status, className }: Props) {
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
