import { useMemo, useState } from "react";
import { Boxes, MoreHorizontal, Plus } from "lucide-react";
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
import { useCustomers } from "@/modules/customers/hooks/use-customers";
import type { SvcSubjectDto, SvcSubjectKind } from "../api/service.api";
import { useSubjects, useSetSubjectActive } from "../hooks/useSubjects";
import { useServicePreset } from "../context/ServicePresetContext";
import { SubjectDialog } from "../components/SubjectDialog";
import { SubjectRecordsDialog } from "../components/SubjectRecordsDialog";

const KIND_LABELS: Record<SvcSubjectKind, string> = {
  Pet: "Pet",
  Vehicle: "Veículo",
  Student: "Aluno",
  Dependent: "Dependente",
  Other: "Outro",
};

export default function SubjectsPage() {
  const { capabilities } = useServicePreset();
  const term = capabilities?.subjectKind ? KIND_LABELS[capabilities.subjectKind] : "Cadastro";

  const [showInactive, setShowInactive] = useState(false);
  const [editDialog, setEditDialog] = useState<{ open: boolean; editing: SvcSubjectDto | null }>({
    open: false,
    editing: null,
  });
  const [recordsFor, setRecordsFor] = useState<SvcSubjectDto | null>(null);

  const { data, isLoading, isError, refetch } = useSubjects({
    active: showInactive ? undefined : true,
  });
  const { data: customers } = useCustomers(false);
  const setActive = useSetSubjectActive();

  const customerName = useMemo(() => {
    const map = new Map((customers ?? []).map((c) => [c.id, c.name]));
    return (id: string) => map.get(id) ?? "—";
  }, [customers]);

  const items = data ?? [];

  const toggleActive = async (s: SvcSubjectDto) => {
    try {
      await setActive.mutateAsync({ id: s.id, active: !s.isActive });
      toast.success(s.isActive ? "Cadastro desativado." : "Cadastro reativado.");
    } catch {
      toast.error("Não foi possível alterar o status.");
    }
  };

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Cadastros"
        title={`${term}s`}
        description={`${term}s dos clientes atendidos.`}
        actions={
          <Button onClick={() => setEditDialog({ open: true, editing: null })}>
            <Plus className="mr-2 h-4 w-4" /> Novo {term.toLowerCase()}
          </Button>
        }
      />

      <SectionCard noPadding>
        <div className="flex items-center justify-end gap-2 px-5 py-3">
          <Label htmlFor="subj-inactive" className="text-[12px] text-muted-foreground">Mostrar inativos</Label>
          <Switch id="subj-inactive" checked={showInactive} onCheckedChange={setShowInactive} />
        </div>

        {isLoading && (
          <div className="space-y-2 p-5 pt-0">
            {[1, 2, 3].map((i) => <Skeleton key={i} className="h-12 w-full" />)}
          </div>
        )}

        {isError && <ErrorState onRetry={refetch} />}

        {!isLoading && !isError && items.length === 0 && (
          <EmptyState
            icon={Boxes}
            title={`Nenhum ${term.toLowerCase()} cadastrado`}
            description={`Cadastre os ${term.toLowerCase()}s dos seus clientes.`}
            action={
              <Button variant="outline" onClick={() => setEditDialog({ open: true, editing: null })}>
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
                <TableHead>Tipo</TableHead>
                <TableHead>Cliente</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="w-10" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {items.map((s) => (
                <TableRow key={s.id}>
                  <TableCell className="font-medium text-foreground">{s.displayName}</TableCell>
                  <TableCell className="text-muted-foreground">{KIND_LABELS[s.kind]}</TableCell>
                  <TableCell className="text-muted-foreground">{customerName(s.customerId)}</TableCell>
                  <TableCell>
                    <StatusBadge
                      variant={s.isActive ? "success" : "neutral"}
                      label={s.isActive ? "Ativo" : "Inativo"}
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
                        <DropdownMenuItem onClick={() => setEditDialog({ open: true, editing: s })}>
                          Editar
                        </DropdownMenuItem>
                        <DropdownMenuItem onClick={() => setRecordsFor(s)}>
                          Registros
                        </DropdownMenuItem>
                        <DropdownMenuItem onClick={() => toggleActive(s)}>
                          {s.isActive ? "Desativar" : "Reativar"}
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

      <SubjectDialog
        open={editDialog.open}
        subject={editDialog.editing}
        onClose={() => setEditDialog({ open: false, editing: null })}
      />
      <SubjectRecordsDialog
        open={!!recordsFor}
        subject={recordsFor}
        onClose={() => setRecordsFor(null)}
      />
    </div>
  );
}
