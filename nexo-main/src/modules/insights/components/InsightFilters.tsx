import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import type {
  InsightFilters as InsightFiltersType,
  InsightSeverity,
  InsightCategory,
} from "../types";
import { insightCategoryLabels, insightSeverityLabels } from "../types";

interface InsightFiltersProps {
  filters: InsightFiltersType;
  onChange: (filters: InsightFiltersType) => void;
}

const severities: InsightSeverity[] = ["critical", "warning", "info"];
const categories: InsightCategory[] = [
  "inventory",
  "cash",
  "sales",
  "commissions",
  "operations",
];

export function InsightFilters({ filters, onChange }: InsightFiltersProps) {
  function update(patch: Partial<InsightFiltersType>) {
    onChange({ ...filters, ...patch });
  }

  return (
    <div className="flex flex-wrap items-center gap-3">
      <Select
        value={filters.severity}
        onValueChange={(v) =>
          update({ severity: v as InsightFiltersType["severity"] })
        }
      >
        <SelectTrigger className="w-44">
          <SelectValue placeholder="Severidade" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todas as severidades</SelectItem>
          {severities.map((s) => (
            <SelectItem key={s} value={s}>
              {insightSeverityLabels[s]}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>

      <Select
        value={filters.category}
        onValueChange={(v) =>
          update({ category: v as InsightFiltersType["category"] })
        }
      >
        <SelectTrigger className="w-44">
          <SelectValue placeholder="Categoria" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todas as categorias</SelectItem>
          {categories.map((c) => (
            <SelectItem key={c} value={c}>
              {insightCategoryLabels[c]}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );
}
