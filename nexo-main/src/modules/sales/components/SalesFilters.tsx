import { Search } from "lucide-react";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import type { SaleListFilters } from "../types";

interface SalesFiltersProps {
  filters: SaleListFilters;
  onChange: (filters: SaleListFilters) => void;
}

export function SalesFilters({ filters, onChange }: SalesFiltersProps) {
  function update(patch: Partial<SaleListFilters>) {
    onChange({ ...filters, ...patch });
  }

  return (
    <div className="flex flex-wrap items-center gap-3">
      <div className="relative flex-1 min-w-48">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
        <Input
          placeholder="Buscar por ID, operador ou produto..."
          value={filters.search}
          onChange={(e) => update({ search: e.target.value })}
          className="pl-9"
        />
      </div>

      <Select
        value={filters.paymentMethod}
        onValueChange={(v) => update({ paymentMethod: v as SaleListFilters["paymentMethod"] })}
      >
        <SelectTrigger className="w-40">
          <SelectValue placeholder="Pagamento" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todos pagamentos</SelectItem>
          <SelectItem value="cash">Dinheiro</SelectItem>
          <SelectItem value="pix">PIX</SelectItem>
          <SelectItem value="card">Cartão</SelectItem>
        </SelectContent>
      </Select>

      <Select
        value={filters.status}
        onValueChange={(v) => update({ status: v as SaleListFilters["status"] })}
      >
        <SelectTrigger className="w-36">
          <SelectValue placeholder="Status" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todos status</SelectItem>
          <SelectItem value="completed">Concluídas</SelectItem>
          <SelectItem value="cancelled">Canceladas</SelectItem>
          <SelectItem value="partially_cancelled">Parc. canceladas</SelectItem>
        </SelectContent>
      </Select>
    </div>
  );
}
