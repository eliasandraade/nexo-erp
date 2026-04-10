import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import type { CommissionFilters } from "../types";

interface CommissionFiltersProps {
  filters: CommissionFilters;
  operators: string[];
  onChange: (filters: CommissionFilters) => void;
}

export function CommissionFilters({
  filters,
  operators,
  onChange,
}: CommissionFiltersProps) {
  function update(patch: Partial<CommissionFilters>) {
    onChange({ ...filters, ...patch });
  }

  return (
    <div className="flex flex-wrap items-center gap-3">
      <Select
        value={filters.operator}
        onValueChange={(v) => update({ operator: v })}
      >
        <SelectTrigger className="w-48">
          <SelectValue placeholder="Vendedor" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todos os vendedores</SelectItem>
          {operators.map((op) => (
            <SelectItem key={op} value={op}>
              {op}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>

      <Select
        value={filters.status}
        onValueChange={(v) =>
          update({ status: v as CommissionFilters["status"] })
        }
      >
        <SelectTrigger className="w-40">
          <SelectValue placeholder="Status" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todos status</SelectItem>
          <SelectItem value="active">Ativas</SelectItem>
          <SelectItem value="reversed">Estornadas</SelectItem>
        </SelectContent>
      </Select>
    </div>
  );
}
