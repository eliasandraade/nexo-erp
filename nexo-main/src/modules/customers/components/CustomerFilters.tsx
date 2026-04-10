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
  city: string;
  onCityChange: (v: string) => void;
  cities: string[];
}

export function CustomerFilters({
  search, onSearchChange,
  personType, onPersonTypeChange,
  isActive, onIsActiveChange,
  city, onCityChange,
  cities,
}: CustomerFiltersProps) {
  return (
    <div className="flex flex-wrap items-center gap-3">
      <div className="relative flex-1 min-w-[220px]">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
        <Input
          placeholder="Buscar por nome, documento, telefone ou e-mail..."
          value={search}
          onChange={(e) => onSearchChange(e.target.value)}
          className="pl-9"
        />
      </div>
      <Select value={personType} onValueChange={onPersonTypeChange}>
        <SelectTrigger className="w-[160px]"><SelectValue placeholder="Tipo" /></SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todos os tipos</SelectItem>
          <SelectItem value="Individual">Pessoa física</SelectItem>
          <SelectItem value="Company">Pessoa jurídica</SelectItem>
        </SelectContent>
      </Select>
      <Select value={isActive} onValueChange={onIsActiveChange}>
        <SelectTrigger className="w-[140px]"><SelectValue placeholder="Status" /></SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todos</SelectItem>
          <SelectItem value="true">Ativo</SelectItem>
          <SelectItem value="false">Inativo</SelectItem>
        </SelectContent>
      </Select>
      <Select value={city} onValueChange={onCityChange}>
        <SelectTrigger className="w-[180px]"><SelectValue placeholder="Cidade" /></SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todas as cidades</SelectItem>
          {cities.map((c) => (
            <SelectItem key={c} value={c}>{c}</SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );
}
