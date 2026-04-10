import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Search } from "lucide-react";
import type { UserRole, UserStatus } from "../types";
import { roleLabels, statusLabels } from "../types";

interface UserFiltersProps {
  search: string;
  onSearchChange: (v: string) => void;
  role: string;
  onRoleChange: (v: string) => void;
  store: string;
  onStoreChange: (v: string) => void;
  status: string;
  onStatusChange: (v: string) => void;
  stores: string[];
}

export function UserFilters({
  search, onSearchChange,
  role, onRoleChange,
  store, onStoreChange,
  status, onStatusChange,
  stores,
}: UserFiltersProps) {
  return (
    <div className="flex flex-wrap items-center gap-3">
      <div className="relative flex-1 min-w-[220px]">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
        <Input
          placeholder="Buscar por nome, e-mail ou login..."
          value={search}
          onChange={(e) => onSearchChange(e.target.value)}
          className="pl-9"
        />
      </div>
      <Select value={role} onValueChange={onRoleChange}>
        <SelectTrigger className="w-[160px]"><SelectValue placeholder="Perfil" /></SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todos os perfis</SelectItem>
          {(Object.keys(roleLabels) as UserRole[]).map((r) => (
            <SelectItem key={r} value={r}>{roleLabels[r]}</SelectItem>
          ))}
        </SelectContent>
      </Select>
      <Select value={store} onValueChange={onStoreChange}>
        <SelectTrigger className="w-[160px]"><SelectValue placeholder="Loja" /></SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todas as lojas</SelectItem>
          {stores.map((s) => (
            <SelectItem key={s} value={s}>{s}</SelectItem>
          ))}
        </SelectContent>
      </Select>
      <Select value={status} onValueChange={onStatusChange}>
        <SelectTrigger className="w-[140px]"><SelectValue placeholder="Status" /></SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todos</SelectItem>
          {(Object.keys(statusLabels) as UserStatus[]).map((s) => (
            <SelectItem key={s} value={s}>{statusLabels[s]}</SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );
}
