import { useState } from "react";
import { BookMarked, MoreHorizontal, Plus } from "lucide-react";
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
import type { SvcCatalogItemDto } from "../api/service.api";
import { useCatalog, useSetCatalogItemActive } from "../hooks/useCatalog";
import { useServicePreset } from "../context/ServicePresetContext";
import { CatalogItemDialog } from "../components/CatalogItemDialog";

export default function CatalogoPage() {
  const { labels } = useServicePreset();
  const term = labels?.catalogItem ?? "Serviço";

  const [showInactive, setShowInactive] = useState(false);
  const [dialog, setDialog] = useState<{ open: boolean; editing: SvcCatalogItemDto | null }>({
    open: false,
    editing: null,
  });

  const { data, isLoading, isError, refetch } = useCatalog(!showInactive);
  const setActive = useSetCatalogItemActive();

  const items = data ?? [];

  const toggleActive = async (it: SvcCatalogItemDto) => {
    try {
      await setActive.mutateAsync({ id: it.id, active: !it.isActive });
      toast.success(it.isActive ? `${term} desativado.` : `${term} reativado.`);
    } catch {
      toast.error("Não foi possível alterar o status.");
    }
  };

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Cadastros"
        title="Catálogo"
        description={`${term}s oferecidos, com duração e preço.`}
        actions={
          <Button onClick={() => setDialog({ open: true, editing: null })}>
            <Plus className="mr-2 h-4 w-4" /> Novo {term.toLowerCase()}
          </Button>
        }
      />

      <SectionCard noPadding>
        <div className="flex items-center justify-end gap-2 px-5 py-3">
          <Label htmlFor="cat-inactive" className="text-[12px] text-muted-foreground">Mostrar inativos</Label>
          <Switch id="cat-inactive" checked={showInactive} onCheckedChange={setShowInactive} />
        </div>

        {isLoading && (
          <div className="space-y-2 p-5 pt-0">
            {[1, 2, 3].map((i) => <Skeleton key={i} className="h-12 w-full" />)}
          </div>
        )}

        {isError && <ErrorState onRetry={refetch} />}

        {!isLoading && !isError && items.length === 0 && (
          <EmptyState
            icon={BookMarked}
            title="Catálogo vazio"
            description={`Cadastre os ${term.toLowerCase()}s oferecidos.`}
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
                <TableHead>Categoria</TableHead>
                <TableHead className="text-right">Duração</TableHead>
                <TableHead className="text-right">Preço</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="w-10" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {items.map((it) => (
                <TableRow key={it.id}>
                  <TableCell className="font-medium text-foreground">{it.name}</TableCell>
                  <TableCell className="text-muted-foreground">{it.category || "—"}</TableCell>
                  <TableCell className="text-right text-muted-foreground">{it.durationMinutes} min</TableCell>
                  <TableCell className="text-right text-foreground">{formatCurrency(it.price)}</TableCell>
                  <TableCell>
                    <StatusBadge
                      variant={it.isActive ? "success" : "neutral"}
                      label={it.isActive ? "Ativo" : "Inativo"}
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
                        <DropdownMenuItem onClick={() => setDialog({ open: true, editing: it })}>
                          Editar
                        </DropdownMenuItem>
                        <DropdownMenuItem onClick={() => toggleActive(it)}>
                          {it.isActive ? "Desativar" : "Reativar"}
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

      <CatalogItemDialog
        open={dialog.open}
        item={dialog.editing}
        onClose={() => setDialog({ open: false, editing: null })}
      />
    </div>
  );
}
