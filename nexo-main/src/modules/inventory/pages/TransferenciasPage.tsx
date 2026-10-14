import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeftRight } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { useMyStores } from "@/modules/stores/hooks/useMyStores";
import { createTransfer, listTransfers } from "../api/stock-operations.api";
import { STOCK_KEY, useStockItems } from "../hooks/use-stock";

const TRANSFERS_KEY = [...STOCK_KEY, "transfers"] as const;

/**
 * Transferência de estoque from this store to another store of the company. It is atomic: the
 * origin goes down and the destination goes up together, or nothing changes.
 */
export default function TransferenciasPage() {
  const qc = useQueryClient();
  const { session } = useAuth();
  const { data: stores = [] } = useMyStores();
  const { data: stock = [] } = useStockItems();
  const { data: transfers = [] } = useQuery({ queryKey: TRANSFERS_KEY, queryFn: listTransfers });

  const others = stores.filter((s) => s.id !== session?.storeId);
  const storeName = (id: string) => stores.find((s) => s.id === id)?.name ?? "Outra loja";
  const [toStoreId, setToStoreId] = useState("");
  const [quantities, setQuantities] = useState<Record<string, string>>({});
  const [filter, setFilter] = useState("");
  const [notes, setNotes] = useState("");

  const rows = useMemo(
    () => stock.filter((s) => `${s.productName} ${s.productCode}`.toLowerCase().includes(filter.toLowerCase())),
    [stock, filter],
  );
  const lines = stock
    .map((s) => ({ s, q: Number((quantities[s.productId] ?? "").replace(",", ".")) || 0 }))
    .filter((l) => l.q > 0);
  const invalid = lines.some((l) => l.q > l.s.availableQuantity);

  const transfer = useMutation({
    mutationFn: () => createTransfer({
      toStoreId,
      items: lines.map((l) => ({ productId: l.s.productId, quantity: l.q })),
      notes: notes.trim() || undefined,
    }),
    onSuccess: (t) => {
      qc.invalidateQueries({ queryKey: STOCK_KEY });
      setQuantities({});
      setNotes("");
      toast.success(`Transferência #${t.number} concluída.`);
    },
    onError: (e: Error) => toast.error(e.message),
  });

  if (others.length === 0) {
    return (
      <div className="space-y-6">
        <PageHeader title="Transferências" description="Mova estoque entre as lojas da empresa." />
        <SectionCard>
          <EmptyState
            icon={ArrowLeftRight}
            title="Só existe uma loja"
            description="Transferências ficam disponíveis quando a empresa tem mais de uma loja."
          />
        </SectionCard>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <PageHeader title="Transferências" description="Envie estoque desta loja para outra loja da empresa." />

      <SectionCard title="Nova transferência">
        <div className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="to-store">Loja de destino</Label>
              <Select value={toStoreId} onValueChange={setToStoreId}>
                <SelectTrigger id="to-store"><SelectValue placeholder="Escolha a loja" /></SelectTrigger>
                <SelectContent>
                  {others.map((s) => <SelectItem key={s.id} value={s.id}>{s.name}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="transfer-notes">Observação</Label>
              <Input id="transfer-notes" maxLength={500} value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="Opcional" />
            </div>
          </div>
          <Input placeholder="Filtrar produto" aria-label="Filtrar produto" value={filter} onChange={(e) => setFilter(e.target.value)} />
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Produto</TableHead>
                <TableHead className="text-right">Disponível</TableHead>
                <TableHead className="text-right">Transferir</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((s) => (
                <TableRow key={s.productId}>
                  <TableCell className="text-sm">
                    {s.productName}
                    <span className="block text-xs text-muted-foreground">{s.productCode}</span>
                  </TableCell>
                  <TableCell className="text-right tabular-nums">{s.availableQuantity.toLocaleString("pt-BR")}</TableCell>
                  <TableCell className="text-right">
                    <Input
                      className="ml-auto w-24 text-right"
                      inputMode="decimal"
                      aria-label={`Quantidade a transferir de ${s.productName}`}
                      disabled={s.availableQuantity <= 0}
                      value={quantities[s.productId] ?? ""}
                      onChange={(e) => setQuantities((q) => ({ ...q, [s.productId]: e.target.value }))}
                    />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          {invalid && <p className="text-xs text-destructive">A quantidade não pode passar do disponível nesta loja.</p>}
          <div className="flex justify-end">
            <Button
              onClick={() => transfer.mutate()}
              disabled={!toStoreId || lines.length === 0 || invalid || transfer.isPending}
            >
              {transfer.isPending ? "Transferindo..." : `Transferir ${lines.length} produto(s)`}
            </Button>
          </div>
        </div>
      </SectionCard>

      <SectionCard title="Histórico">
        {transfers.length === 0 ? (
          <p className="text-sm text-muted-foreground">Nenhuma transferência ainda.</p>
        ) : (
          <ul className="divide-y divide-border text-sm">
            {transfers.map((t) => (
              <li key={t.id} className="py-2">
                <p className="font-medium">
                  #{t.number} · {t.direction === "out" ? `Enviada para ${storeName(t.toStoreId)}` : `Recebida de ${storeName(t.fromStoreId)}`}
                </p>
                <p className="text-xs text-muted-foreground">
                  {new Date(t.createdAt).toLocaleString("pt-BR")} · {t.items.map((i) => `${i.quantity.toLocaleString("pt-BR")} × ${i.productName}`).join(" · ")}
                </p>
              </li>
            ))}
          </ul>
        )}
      </SectionCard>
    </div>
  );
}
