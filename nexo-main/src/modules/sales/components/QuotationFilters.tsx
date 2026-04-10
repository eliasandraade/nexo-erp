import { Search } from "lucide-react";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import type { QuotationListFilters } from "../types/quotation";

interface QuotationFiltersProps {
  filters: QuotationListFilters;
  operators: string[];
  onChange: (filters: QuotationListFilters) => void;
}

export function QuotationFilters({
  filters,
  operators,
  onChange,
}: QuotationFiltersProps) {
  function update(patch: Partial<QuotationListFilters>) {
    onChange({ ...filters, ...patch });
  }

  return (
    <div className="flex flex-wrap items-center gap-3">
      <div className="relative flex-1 min-w-48">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
        <Input
          placeholder="Buscar por ID, cliente ou operador..."
          value={filters.search}
          onChange={(e) => update({ search: e.target.value })}
          className="pl-9"
        />
      </div>

      <Select
        value={filters.status}
        onValueChange={(v) =>
          update({ status: v as QuotationListFilters["status"] })
        }
      >
        <SelectTrigger className="w-40">
          <SelectValue placeholder="Status" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todos status</SelectItem>
          <SelectItem value="draft">Rascunho</SelectItem>
          <SelectItem value="sent">Enviado</SelectItem>
          <SelectItem value="approved">Aprovado</SelectItem>
          <SelectItem value="expired">Expirado</SelectItem>
          <SelectItem value="converted">Convertido</SelectItem>
        </SelectContent>
      </Select>

      <Select
        value={filters.operator}
        onValueChange={(v) => update({ operator: v })}
      >
        <SelectTrigger className="w-44">
          <SelectValue placeholder="Operador" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todos operadores</SelectItem>
          {operators.map((op) => (
            <SelectItem key={op} value={op}>
              {op}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );
}
