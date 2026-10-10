import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Search } from "lucide-react";

interface CustomerFiltersProps {
  search: string;
  onSearchChange: (v: string) => void;
  personType: string;
  onPersonTypeChange: (v: string) => void;
  isActive: string;
  onIsActiveChange: (v: string) => void;
}

export function CustomerFilters({
  search, onSearchChange,
  personType, onPersonTypeChange,
  isActive, onIsActiveChange,
}: CustomerFiltersProps) {
  return (
    <div className="flex flex-wrap items-center gap-3">
      <div className="relative flex-1 min-w-0 basis-full sm:basis-64">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
        <Input
          aria-label="Buscar clientes" placeholder="Nome, documento ou contato"
          value={search}
          onChange={(e) => onSearchChange(e.target.value)}
          className="pl-9"
        />
      </div>
      <Select value={personType} onValueChange={onPersonTypeChange}>
        <SelectTrigger aria-label="Tipo de pessoa nesta página" className="w-full sm:w-[180px]"><SelectValue placeholder="Tipo" /></SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todos os tipos</SelectItem>
          <SelectItem value="Individual">Pessoa física</SelectItem>
          <SelectItem value="Company">Pessoa jurídica</SelectItem>
        </SelectContent>
      </Select>
      <Select value={isActive} onValueChange={onIsActiveChange}>
        <SelectTrigger aria-label="Situação nesta página" className="w-full sm:w-[160px]"><SelectValue placeholder="Status" /></SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todos</SelectItem>
          <SelectItem value="true">Ativo</SelectItem>
          <SelectItem value="false">Inativo</SelectItem>
        </SelectContent>
      </Select>
      <p className="basis-full text-xs text-muted-foreground">Tipo e situação filtram os registros da página atual.</p>
    </div>
  );
}
