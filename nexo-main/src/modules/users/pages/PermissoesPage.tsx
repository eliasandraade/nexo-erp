import { useState, useCallback } from "react";
import { useSearchParams, useNavigate } from "react-router-dom";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { Skeleton } from "@/components/ui/skeleton";
import { Button } from "@/components/ui/button";
import { ArrowLeft, Save } from "lucide-react";
import { useToast } from "@/hooks/use-toast";
import { userService } from "../services/userService";
import type { UserRole, PermissionAction, PermissionMatrix } from "../types";
import { roleLabels, permissionModules } from "../types";
import { PermissionGroupCard, RolePresetSelector } from "../components/PermissionGroupCard";

const roles: { value: UserRole; label: string }[] = [
  { value: "diretoria", label: "Diretoria" },
  { value: "gerente", label: "Gerente" },
  { value: "vendedor", label: "Vendedor" },
  { value: "estoquista", label: "Estoquista" },
];

export default function PermissoesPage() {
  const navigate = useNavigate();
  const { toast } = useToast();
  const queryClient = useQueryClient();
  const [params] = useSearchParams();
  const initialRole = (params.get("role") as UserRole) || "diretoria";
  const [selectedRole, setSelectedRole] = useState<UserRole>(initialRole);

  const { data: matrix, isLoading } = useQuery({
    queryKey: ["permissions", selectedRole],
    queryFn: () => userService.getPermissionsByRole(selectedRole),
  });

  const [local, setLocal] = useState<PermissionMatrix | null>(null);
  const current = local ?? matrix ?? {};

  // Reset local when role changes
  const handleSelectRole = (role: string) => {
    setSelectedRole(role as UserRole);
    setLocal(null);
  };

  const handleToggle = useCallback((moduleKey: string, action: PermissionAction, checked: boolean) => {
    setLocal((prev) => {
      const base = prev ?? matrix ?? {};
      const actions = base[moduleKey] ?? [];
      const updated = checked ? [...actions, action] : actions.filter((a) => a !== action);
      return { ...base, [moduleKey]: updated };
    });
  }, [matrix]);

  const saveMutation = useMutation({
    mutationFn: () => userService.updatePermissions(selectedRole, current),
    onSuccess: () => {
      toast({ title: "Permissões salvas com sucesso" });
      queryClient.invalidateQueries({ queryKey: ["permissions"] });
      setLocal(null);
    },
    onError: () => toast({ title: "Erro ao salvar permissões", variant: "destructive" }),
  });

  return (
    <div className="space-y-6">
      <PageHeader
        title="Permissões"
        description="Defina os acessos disponíveis por perfil e operação."
        actions={
          <div className="flex gap-2">
            <Button variant="outline" onClick={() => navigate("/usuarios")}>
              <ArrowLeft className="h-4 w-4 mr-2" /> Voltar
            </Button>
            <Button onClick={() => saveMutation.mutate()} disabled={!local || saveMutation.isPending}>
              <Save className="h-4 w-4 mr-2" /> {saveMutation.isPending ? "Salvando..." : "Salvar alterações"}
            </Button>
          </div>
        }
      />

      <div className="grid grid-cols-1 lg:grid-cols-4 gap-6">
        <SectionCard title="Perfil">
          <RolePresetSelector
            roles={roles}
            selected={selectedRole}
            onSelect={handleSelectRole}
          />
        </SectionCard>

        <div className="lg:col-span-3 space-y-4">
          <SectionCard description={`Permissões do perfil: ${roleLabels[selectedRole]}`}>
            <p className="text-xs text-muted-foreground mb-4">
              Marque as ações permitidas para cada módulo. As alterações só serão aplicadas após salvar.
            </p>
          </SectionCard>

          {isLoading ? (
            <div className="space-y-3">
              {Array.from({ length: 5 }).map((_, i) => <Skeleton key={i} className="h-20 w-full" />)}
            </div>
          ) : (
            permissionModules.map((mod) => (
              <PermissionGroupCard
                key={mod.key}
                module={mod}
                enabled={(current[mod.key] ?? []) as PermissionAction[]}
                onToggle={handleToggle}
              />
            ))
          )}
        </div>
      </div>
    </div>
  );
}
