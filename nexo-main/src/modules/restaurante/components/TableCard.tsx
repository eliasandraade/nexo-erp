import { cn } from "@/lib/utils";
import type { TableDto, TableStatus } from "../types";

interface TableCardProps {
  table: TableDto;
  onClick: () => void;
}

const statusStyles: Record<TableStatus, string> = {
  Available:   "bg-card border-border hover:border-primary",
  Occupied:    "bg-primary/10 border-primary text-primary",
  Reserved:    "bg-amber-500/10 border-amber-500 text-amber-600",
  Maintenance: "bg-red-500/10 border-red-500 text-red-600",
};

const statusLabel: Record<TableStatus, string> = {
  Available:   "Livre",
  Occupied:    "Ocupada",
  Reserved:    "Reservada",
  Maintenance: "Manutenção",
};

export function TableCard({ table, onClick }: TableCardProps) {
  return (
    <button
      onClick={onClick}
      className={cn(
        "relative rounded-xl border-2 p-4 text-left transition-all min-h-[88px]",
        "flex flex-col justify-between active:scale-95",
        statusStyles[table.status]
      )}
    >
      <span className="text-xl font-bold">Mesa {table.number}</span>
      <span className="text-xs font-medium opacity-70">
        {statusLabel[table.status]}
      </span>
      {table.status === "Occupied" && (
        <span className="absolute top-2 right-2 h-2 w-2 rounded-full bg-primary animate-pulse" />
      )}
    </button>
  );
}
