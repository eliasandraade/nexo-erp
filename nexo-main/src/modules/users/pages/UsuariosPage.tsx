import { useState, useMemo } from "react";
import { useNavigate } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { UserPlus, UserCog } from "lucide-react";
import { userService } from "../services/userService";
import { UserFilters } from "../components/UserFilters";
import { UserTable } from "../components/UserTable";

export default function UsuariosPage() {
  const navigate = useNavigate();
  const [search, setSearch] = useState("");
  const [role, setRole] = useState("all");
  const [store, setStore] = useState("all");
  const [status, setStatus] = useState("all");

  const { data: users = [], isLoading, isError } = useQuery({
    queryKey: ["users"],
    queryFn: userService.list,
  });

  const { data: stores = [] } = useQuery({
    queryKey: ["user-stores"],
    queryFn: userService.listStores,
  });

  const filtered = useMemo(() => {
    let result = users;
    if (search) {
      const q = search.toLowerCase();
      result = result.filter(
        (u) => u.name.toLowerCase().includes(q) || u.email.toLowerCase().includes(q) || u.login.toLowerCase().includes(q)
      );
    }
    if (role !== "all") result = result.filter((u) => u.role === role);
    if (store !== "all") result = result.filter((u) => u.store === store);
    if (status !== "all") result = result.filter((u) => u.status === status);
    return result;
  }, [users, search, role, store, status]);

  return (
    <div className="space-y-6">
      <PageHeader
        title="Usuários"
        description="Gerencie usuários, perfis e acessos do sistema."
        actions={
          <div className="flex gap-2">
            <Button variant="outline" onClick={() => navigate("/usuarios/permissoes")}>
              <UserCog className="h-4 w-4 mr-2" /> Permissões
            </Button>
            <Button onClick={() => navigate("/usuarios/novo")}>
              <UserPlus className="h-4 w-4 mr-2" /> Novo usuário
            </Button>
          </div>
        }
      />

      <SectionCard>
        <div className="space-y-4">
          <UserFilters
            search={search} onSearchChange={setSearch}
            role={role} onRoleChange={setRole}
            store={store} onStoreChange={setStore}
            status={status} onStatusChange={setStatus}
            stores={stores}
          />

          {isLoading ? (
            <div className="space-y-3">
              {Array.from({ length: 5 }).map((_, i) => (
                <Skeleton key={i} className="h-10 w-full" />
              ))}
            </div>
          ) : isError ? (
            <EmptyState icon={UserCog} title="Erro ao carregar usuários" description="Tente novamente mais tarde." />
          ) : filtered.length === 0 ? (
            <EmptyState icon={UserCog} title="Nenhum usuário encontrado" description="Ajuste os filtros ou cadastre um novo usuário." />
          ) : (
            <>
              <p className="text-xs text-muted-foreground">{filtered.length} usuário(s) encontrado(s)</p>
              <UserTable users={filtered} />
            </>
          )}
        </div>
      </SectionCard>
    </div>
  );
}
