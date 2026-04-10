import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import type { ReportFilters } from "../types";

interface ReportFilterBarProps {
  filters: ReportFilters;
  operators: string[];
  onChange: (filters: ReportFilters) => void;
}

export function ReportFilterBar({ filters, operators, onChange }: ReportFilterBarProps) {
  function update(patch: Partial<ReportFilters>) {
    onChange({ ...filters, ...patch });
  }

  return (
    <div className="flex flex-wrap items-center gap-3">
      <Select value={filters.operator} onValueChange={(v) => update({ operator: v })}>
        <SelectTrigger className="w-48">
          <SelectValue placeholder="Operador" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todos os operadores</SelectItem>
          {operators.map((op) => (
            <SelectItem key={op} value={op}>
              {op}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>

      <Select
        value={filters.status}
        onValueChange={(v) => update({ status: v as ReportFilters["status"] })}
      >
        <SelectTrigger className="w-44">
          <SelectValue placeholder="Status" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todos os status</SelectItem>
          <SelectItem value="completed">Concluídas</SelectItem>
          <SelectItem value="cancelled">Canceladas</SelectItem>
          <SelectItem value="partially_cancelled">Parc. canceladas</SelectItem>
        </SelectContent>
      </Select>

      <Select
        value={filters.paymentMethod}
        onValueChange={(v) => update({ paymentMethod: v as ReportFilters["paymentMethod"] })}
      >
        <SelectTrigger className="w-40">
          <SelectValue placeholder="Pagamento" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todos</SelectItem>
          <SelectItem value="cash">Dinheiro</SelectItem>
          <SelectItem value="pix">PIX</SelectItem>
          <SelectItem value="card">Cartão</SelectItem>
        </SelectContent>
      </Select>
    </div>
  );
}
