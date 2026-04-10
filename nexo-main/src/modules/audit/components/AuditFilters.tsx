import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { auditActionTypeLabels, auditSeverityLabels } from "../types";
import type { AuditFilters as AuditFiltersType, AuditActionType, AuditSeverity } from "../types";

interface AuditFiltersProps {
  filters: AuditFiltersType;
  onChange: (filters: AuditFiltersType) => void;
}

const ACTION_TYPES: AuditActionType[] = [
  "stock_adjustment",
  "stock_transfer",
  "cash_open",
  "cash_movement",
  "cash_close",
  "sale_completed",
  "sale_cancelled",
  "sale_item_cancelled",
  "manager_authorization",
  "user_created",
  "user_updated",
];

const SEVERITIES: AuditSeverity[] = ["critical", "warning", "info"];

export function AuditFilters({ filters, onChange }: AuditFiltersProps) {
  function update(patch: Partial<AuditFiltersType>) {
    onChange({ ...filters, ...patch });
  }

  return (
    <div className="flex flex-wrap items-center gap-3">
      <Select
        value={filters.actionType}
        onValueChange={(v) => update({ actionType: v as AuditFiltersType["actionType"] })}
      >
        <SelectTrigger className="w-52">
          <SelectValue placeholder="Ação" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todas as ações</SelectItem>
          {ACTION_TYPES.map((t) => (
            <SelectItem key={t} value={t}>
              {auditActionTypeLabels[t]}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>

      <Select
        value={filters.severity}
        onValueChange={(v) => update({ severity: v as AuditFiltersType["severity"] })}
      >
        <SelectTrigger className="w-36">
          <SelectValue placeholder="Severidade" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todas</SelectItem>
          {SEVERITIES.map((s) => (
            <SelectItem key={s} value={s}>
              {auditSeverityLabels[s]}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>

      <Input
        placeholder="Filtrar por usuário..."
        value={filters.actor === "all" ? "" : filters.actor}
        onChange={(e) => update({ actor: e.target.value || "all" })}
        className="w-48"
      />
    </div>
  );
}
