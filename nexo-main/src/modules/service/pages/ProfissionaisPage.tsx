import { useState } from "react";
import { MoreHorizontal, Plus, Users } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { ErrorState } from "@/components/shared/ErrorState";
import { StatusBadge } from "@/components/shared/StatusBadge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";
import { Label } from "@/components/ui/label";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import type { SvcProfessionalDto } from "../api/service.api";
import { useProfessionals, useSetProfessionalActive } from "../hooks/useProfessionals";
import { useServicePreset } from "../context/ServicePresetContext";
import { ProfessionalDialog } from "../components/ProfessionalDialog";

export default function ProfissionaisPage() {
  const { labels, capabilities } = useServicePreset();
  const term = labels?.professional ?? "Profissional";

  const [showInactive, setShowInactive] = useState(false);
  const [dialog, setDialog] = useState<{ open: boolean; editing: SvcProfessionalDto | null }>({
    open: false,
    editing: null,
  });

  const { data, isLoading, isError, refetch } = useProfessionals(!showInactive);
  const setActive = useSetProfessionalActive();

  const items = data ?? [];

  const toggleActive = async (p: SvcProfessionalDto) => {
    try {
      await setActive.mutateAsync({ id: p.id, active: !p.isActive });
      toast.success(p.isActive ? `${term} desativado.` : `${term} reativado.`);
    } catch {
      toast.error("Não foi possível alterar o status.");
    }
  };

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Cadastros"
        title={`${term}s`}
        description={`Equipe que executa os serviços${capabilities?.commissions ? " e suas comissões" : ""}.`}
        actions={
          <Button onClick={() => setDialog({ open: true, editing: null })}>
            <Plus className="mr-2 h-4 w-4" /> Novo {term.toLowerCase()}
          </Button>
        }
      />

      <SectionCard noPadding>
        <div className="flex items-center justify-end gap-2 px-5 py-3">
          <Label htmlFor="prof-inactive" className="text-[12px] text-muted-foreground">Mostrar inativos</Label>
          <Switch id="prof-inactive" checked={showInactive} onCheckedChange={setShowInactive} />
        </div>

        {isLoading && (
          <div className="space-y-2 p-5 pt-0">
            {[1, 2, 3].map((i) => <Skeleton key={i} className="h-12 w-full" />)}
          </div>
        )}

        {isError && <ErrorState onRetry={refetch} />}

        {!isLoading && !isError && items.length === 0 && (
          <EmptyState
            icon={Users}
            title={`Nenhum ${term.toLowerCase()} cadastrado`}
            description={`Cadastre a equipe que executa os serviços.`}
            action={
              <Button variant="outline" onClick={() => setDialog({ open: true, editing: null })}>
                <Plus className="mr-2 h-4 w-4" /> Cadastrar {term.toLowerCase()}
              </Button>
            }
          />
        )}

        {!isLoading && !isError && items.length > 0 && (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Nome</TableHead>
                <TableHead>Função</TableHead>
                <TableHead>Contato</TableHead>
                {capabilities?.commissions && <TableHead className="text-right">Comissão</TableHead>}
                <TableHead>Status</TableHead>
                <TableHead className="w-10" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {items.map((p) => (
                <TableRow key={p.id}>
                  <TableCell>
                    <div className="flex items-center gap-2">
                      <span className="h-2.5 w-2.5 shrink-0 rounded-full" style={{ background: p.color ?? "#94a3b8" }} />
                      <span className="font-medium text-foreground">{p.name}</span>
                    </div>
                  </TableCell>
                  <TableCell className="text-muted-foreground">
                    {[p.role, p.specialty].filter(Boolean).join(" · ") || "—"}
                  </TableCell>
                  <TableCell className="text-muted-foreground">
                    {[p.phone, p.email].filter(Boolean).join(" · ") || "—"}
                  </TableCell>
                  {capabilities?.commissions && (
                    <TableCell className="text-right text-muted-foreground">
                      {p.defaultCommissionPercent != null ? `${p.defaultCommissionPercent}%` : "—"}
                    </TableCell>
                  )}
                  <TableCell>
                    <StatusBadge
                      variant={p.isActive ? "success" : "neutral"}
                      label={p.isActive ? "Ativo" : "Inativo"}
                      dot
                    />
                  </TableCell>
                  <TableCell>
                    <DropdownMenu>
                      <DropdownMenuTrigger asChild>
                        <Button variant="ghost" size="icon" className="h-8 w-8">
                          <MoreHorizontal className="h-4 w-4" />
                        </Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end">
                        <DropdownMenuItem onClick={() => setDialog({ open: true, editing: p })}>
                          Editar
                        </DropdownMenuItem>
                        <DropdownMenuItem onClick={() => toggleActive(p)}>
                          {p.isActive ? "Desativar" : "Reativar"}
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </SectionCard>

      <ProfessionalDialog
        open={dialog.open}
        professional={dialog.editing}
        onClose={() => setDialog({ open: false, editing: null })}
      />
    </div>
  );
}
