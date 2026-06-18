import { useState } from "react";
import { MoreHorizontal, Package, Plus } from "lucide-react";
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
import { formatCurrency } from "@/lib/formatters";
import type { SvcPackageDto } from "../api/service.api";
import { usePackages, useSetPackageActive } from "../hooks/usePackages";
import { PackageDialog } from "../components/PackageDialog";

export default function PacotesPage() {
  const [showInactive, setShowInactive] = useState(false);
  const [dialog, setDialog] = useState<{ open: boolean; editing: SvcPackageDto | null }>({ open: false, editing: null });

  const { data, isLoading, isError, refetch } = usePackages(showInactive ? undefined : true);
  const setActive = useSetPackageActive();
  const packages = data ?? [];

  const toggleActive = async (p: SvcPackageDto) => {
    try {
      await setActive.mutateAsync({ id: p.id, active: !p.isActive });
      toast.success(p.isActive ? "Pacote desativado." : "Pacote reativado.");
    } catch {
      toast.error("Não foi possível alterar o status.");
    }
  };

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Operação"
        title="Pacotes"
        description="Modelos de pacotes com serviços incluídos."
        actions={
          <Button onClick={() => setDialog({ open: true, editing: null })}>
            <Plus className="mr-2 h-4 w-4" /> Novo pacote
          </Button>
        }
      />

      <SectionCard noPadding>
        <div className="flex items-center justify-end gap-2 px-5 py-3">
          <Label htmlFor="pkg-inactive" className="text-[12px] text-muted-foreground">Mostrar inativos</Label>
          <Switch id="pkg-inactive" checked={showInactive} onCheckedChange={setShowInactive} />
        </div>

        {isLoading && (
          <div className="space-y-2 p-5 pt-0">{[1, 2, 3].map((i) => <Skeleton key={i} className="h-12 w-full" />)}</div>
        )}

        {isError && <ErrorState onRetry={refetch} />}

        {!isLoading && !isError && packages.length === 0 && (
          <EmptyState
            icon={Package}
            title="Nenhum pacote cadastrado"
            description="Crie modelos de pacotes para vender aos clientes."
            action={
              <Button variant="outline" onClick={() => setDialog({ open: true, editing: null })}>
                <Plus className="mr-2 h-4 w-4" /> Novo pacote
              </Button>
            }
          />
        )}

        {!isLoading && !isError && packages.length > 0 && (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Nome</TableHead>
                <TableHead className="text-center">Serviços</TableHead>
                <TableHead>Validade</TableHead>
                <TableHead className="text-right">Preço</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="w-10" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {packages.map((p) => (
                <TableRow key={p.id}>
                  <TableCell className="font-medium text-foreground">{p.name}</TableCell>
                  <TableCell className="text-center text-muted-foreground">{p.items.length}</TableCell>
                  <TableCell className="text-muted-foreground">{p.validityDays ? `${p.validityDays} dias` : "Sem validade"}</TableCell>
                  <TableCell className="text-right text-foreground">{formatCurrency(p.price)}</TableCell>
                  <TableCell>
                    <StatusBadge variant={p.isActive ? "success" : "neutral"} label={p.isActive ? "Ativo" : "Inativo"} dot />
                  </TableCell>
                  <TableCell>
                    <DropdownMenu>
                      <DropdownMenuTrigger asChild>
                        <Button variant="ghost" size="icon" className="h-8 w-8"><MoreHorizontal className="h-4 w-4" /></Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end">
                        <DropdownMenuItem onClick={() => setDialog({ open: true, editing: p })}>Editar / itens</DropdownMenuItem>
                        <DropdownMenuItem onClick={() => toggleActive(p)}>{p.isActive ? "Desativar" : "Reativar"}</DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </SectionCard>

      <PackageDialog open={dialog.open} pkg={dialog.editing} onClose={() => setDialog({ open: false, editing: null })} />
    </div>
  );
}
