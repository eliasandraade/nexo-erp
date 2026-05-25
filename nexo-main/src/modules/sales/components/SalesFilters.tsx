import { Search } from "lucide-react";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

export interface SalesServerFilters {
  search: string;
  paymentMethod: string;
  status: string;
}

interface SalesFiltersProps {
  filters: SalesServerFilters;
  onChange: (filters: SalesServerFilters) => void;
}

export function SalesFilters({ filters, onChange }: SalesFiltersProps) {
  function update(patch: Partial<SalesServerFilters>) {
    onChange({ ...filters, ...patch });
  }

  return (
    <div className="flex flex-wrap items-center gap-3">
      <div className="relative flex-1 min-w-48">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
        <Input
          placeholder="Buscar por número, operador ou cliente..."
          value={filters.search}
          onChange={(e) => update({ search: e.target.value })}
          className="pl-9"
        />
      </div>

      <Select
        value={filters.paymentMethod}
        onValueChange={(v) => update({ paymentMethod: v })}
      >
        <SelectTrigger className="w-40">
          <SelectValue placeholder="Pagamento" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todos pagamentos</SelectItem>
          <SelectItem value="Cash">Dinheiro</SelectItem>
          <SelectItem value="Pix">PIX</SelectItem>
          <SelectItem value="Card">Cartão</SelectItem>
        </SelectContent>
      </Select>

      <Select
        value={filters.status}
        onValueChange={(v) => update({ status: v })}
      >
        <SelectTrigger className="w-36">
          <SelectValue placeholder="Status" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todos status</SelectItem>
          <SelectItem value="Paid">Pagas</SelectItem>
          <SelectItem value="Confirmed">Confirmadas</SelectItem>
          <SelectItem value="Cancelled">Canceladas</SelectItem>
          <SelectItem value="Draft">Rascunho</SelectItem>
        </SelectContent>
      </Select>
    </div>
  );
}
