import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Plus, Truck } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { ErrorState } from "@/components/shared/ErrorState";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatCurrency } from "@/lib/formatters";
import { useSuppliers } from "@/modules/suppliers/hooks/use-suppliers";
import { createPurchase, listPurchases, PURCHASES_KEY, PURCHASE_STATUS_LABEL } from "../api/purchases.api";



/** Compras: pedidos ao fornecedor, recebimento (estoque + custo + conta a pagar) e histórico. */
export default function ComprasPage() {
  const navigate = useNavigate();
  const { data: purchases, isLoading, isError, refetch } = useQuery({ queryKey: PURCHASES_KEY, queryFn: listPurchases });
  const { data: suppliers = [] } = useSuppliers();
  const [supplierId, setSupplierId] = useState("");

  const create = useMutation({
    mutationFn: () => createPurchase({ supplierId }),
    onSuccess: (p) => navigate(`/compras/${p.id}`),
    onError: (e: Error) => toast.error(e.message),
  });

  return (
    <div className="space-y-6">
      <PageHeader title="Compras" description="Pedidos aos fornecedores e entrada de mercadoria nesta loja." />

      <SectionCard title="Nova compra">
        <div className="flex flex-wrap items-end gap-3">
          <div className="min-w-[16rem] flex-1 space-y-1.5">
            <Label htmlFor="purchase-supplier">Fornecedor</Label>
            <Select value={supplierId} onValueChange={setSupplierId}>
              <SelectTrigger id="purchase-supplier"><SelectValue placeholder="Escolha o fornecedor" /></SelectTrigger>
              <SelectContent>
                {suppliers.map((s) => <SelectItem key={s.id} value={s.id}>{s.tradeName || s.name}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
          <Button onClick={() => create.mutate()} disabled={!supplierId || create.isPending}>
            <Plus className="mr-1 h-4 w-4" /> Criar pedido
          </Button>
        </div>
        {suppliers.length === 0 && (
          <p className="mt-2 text-xs text-muted-foreground">Cadastre um fornecedor para começar uma compra.</p>
        )}
      </SectionCard>

      <SectionCard title="Pedidos e entradas">
        {isLoading ? (
          <Skeleton className="h-40 w-full" />
        ) : isError ? (
          <ErrorState title="Não foi possível carregar as compras" onRetry={() => void refetch()} />
        ) : !purchases?.length ? (
          <EmptyState icon={Truck} title="Nenhuma compra ainda" description="Crie um pedido para um fornecedor acima." />
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Compra</TableHead>
                <TableHead>Fornecedor</TableHead>
                <TableHead>Situação</TableHead>
                <TableHead className="text-right">Total</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {purchases.map((p) => (
                <TableRow key={p.id} className="cursor-pointer" onClick={() => navigate(`/compras/${p.id}`)}>
                  <TableCell className="font-medium">
                    <a href={`/compras/${p.id}`} onClick={(e) => { e.preventDefault(); navigate(`/compras/${p.id}`); }}>
                      #{p.purchaseNumber}
                    </a>
                    <span className="block text-xs text-muted-foreground">{new Date(p.createdAt).toLocaleDateString("pt-BR")}</span>
                  </TableCell>
                  <TableCell>{p.supplierName}</TableCell>
                  <TableCell>
                    <Badge variant={p.status === "Cancelled" ? "destructive" : p.status === "Confirmed" ? "secondary" : "default"}>
                      {PURCHASE_STATUS_LABEL[p.status]}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-right tabular-nums">{formatCurrency(p.totalAmount)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </SectionCard>
    </div>
  );
}
