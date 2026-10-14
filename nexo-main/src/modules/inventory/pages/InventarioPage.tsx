import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ClipboardCheck, Plus } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { ErrorState } from "@/components/shared/ErrorState";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  applyCount, cancelCount, listCounts, openCount, saveCount,
  type InventoryCountDto,
} from "../api/stock-operations.api";
import { STOCK_KEY } from "../hooks/use-stock";

const COUNTS_KEY = [...STOCK_KEY, "counts"] as const;
const STATUS_LABEL = { Open: "Em contagem", Applied: "Aplicado", Cancelled: "Cancelado" } as const;

const qty = (n: number | null | undefined) => (n === null || n === undefined ? "—" : n.toLocaleString("pt-BR"));

/**
 * Inventário: open a count (snapshot of the system), type what was physically counted, apply it —
 * each counted product is set to the counted quantity and the difference becomes an adjustment.
 */
export default function InventarioPage() {
  const qc = useQueryClient();
  const { data: counts, isLoading, isError, refetch } = useQuery({ queryKey: COUNTS_KEY, queryFn: listCounts });
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const selected = counts?.find((c) => c.id === selectedId) ?? counts?.find((c) => c.status === "Open") ?? null;

  const refresh = () => {
    qc.invalidateQueries({ queryKey: COUNTS_KEY });
    qc.invalidateQueries({ queryKey: STOCK_KEY });
  };
  const open = useMutation({
    mutationFn: () => openCount({}),
    onSuccess: (c) => { setSelectedId(c.id); refresh(); toast.success(`Inventário #${c.number} aberto.`); },
    onError: (e: Error) => toast.error(e.message),
  });

  return (
    <div className="space-y-6">
      <PageHeader
        title="Inventário"
        description="Conte o estoque físico desta loja e ajuste o sistema pelo que foi contado."
        actions={
          <Button onClick={() => open.mutate()} disabled={open.isPending || counts?.some((c) => c.status === "Open")}>
            <Plus className="mr-1 h-4 w-4" /> Novo inventário
          </Button>
        }
      />

      {isLoading ? (
        <Skeleton className="h-48 w-full" />
      ) : isError ? (
        <ErrorState title="Não foi possível carregar os inventários" onRetry={() => void refetch()} />
      ) : !counts?.length ? (
        <SectionCard>
          <EmptyState
            icon={ClipboardCheck}
            title="Nenhum inventário ainda"
            description="Abra um inventário para contar todos os produtos com controle de estoque."
          />
        </SectionCard>
      ) : (
        <>
          {selected && <CountEditor key={selected.id} count={selected} onChanged={refresh} />}
          <SectionCard title="Histórico">
            <ul className="divide-y divide-border text-sm">
              {counts.map((c) => (
                <li key={c.id} className="flex flex-wrap items-center justify-between gap-2 py-2">
                  <button type="button" className="font-medium hover:underline" onClick={() => setSelectedId(c.id)}>
                    Inventário #{c.number}
                  </button>
                  <span className="text-muted-foreground">
                    {new Date(c.createdAt).toLocaleDateString("pt-BR")} · {c.countedItems} de {c.items.length} contados
                  </span>
                  <Badge variant={c.status === "Open" ? "default" : "secondary"}>{STATUS_LABEL[c.status]}</Badge>
                </li>
              ))}
            </ul>
          </SectionCard>
        </>
      )}
    </div>
  );
}

function CountEditor({ count, onChanged }: { count: InventoryCountDto; onChanged: () => void }) {
  const editable = count.status === "Open";
  const [values, setValues] = useState<Record<string, string>>({});
  const [filter, setFilter] = useState("");

  useEffect(() => {
    setValues(Object.fromEntries(count.items.map((i) => [i.productId, i.countedQuantity?.toString() ?? ""])));
  }, [count]);

  const parsed = (raw: string | undefined) => {
    if (raw === undefined || raw.trim() === "") return null;
    const n = Number(raw.replace(",", "."));
    return Number.isFinite(n) && n >= 0 ? n : NaN;
  };
  const invalid = count.items.some((i) => Number.isNaN(parsed(values[i.productId]) as number));
  const rows = useMemo(
    () => count.items.filter((i) => `${i.productName} ${i.productCode}`.toLowerCase().includes(filter.toLowerCase())),
    [count.items, filter],
  );

  const save = useMutation({
    mutationFn: () => saveCount(count.id, count.items.map((i) => ({ productId: i.productId, countedQuantity: parsed(values[i.productId]) }))),
    onSuccess: () => { onChanged(); toast.success("Contagem salva."); },
    onError: (e: Error) => toast.error(e.message),
  });
  const apply = useMutation({
    mutationFn: async () => { await save.mutateAsync(); return applyCount(count.id); },
    onSuccess: (c) => {
      onChanged();
      const adjusted = c.items.filter((i) => (i.difference ?? 0) !== 0).length;
      toast.success(`Inventário aplicado: ${adjusted} produto(s) ajustado(s).`);
    },
    onError: (e: Error) => toast.error(e.message),
  });
  const cancel = useMutation({
    mutationFn: () => cancelCount(count.id),
    onSuccess: () => { onChanged(); toast.success("Inventário cancelado."); },
    onError: (e: Error) => toast.error(e.message),
  });
  const busy = save.isPending || apply.isPending || cancel.isPending;

  return (
    <SectionCard
      title={`Inventário #${count.number} — ${STATUS_LABEL[count.status]}`}
      description={editable
        ? "Informe a quantidade contada. Produtos sem contagem não são ajustados. O ajuste usa o estoque no momento de aplicar."
        : "Contagem encerrada. As diferenças abaixo já foram lançadas como ajustes de estoque."}
    >
      <div className="space-y-4">
        <Input placeholder="Filtrar produto" value={filter} onChange={(e) => setFilter(e.target.value)} aria-label="Filtrar produto" />
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Produto</TableHead>
              <TableHead className="text-right">No sistema</TableHead>
              <TableHead className="text-right">Contado</TableHead>
              <TableHead className="text-right">Diferença</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map((i) => (
              <TableRow key={i.productId}>
                <TableCell className="text-sm">
                  {i.productName}
                  <span className="block text-xs text-muted-foreground">{i.productCode}</span>
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  {qty(editable ? i.expectedQuantity : i.systemQuantityAtApply ?? i.expectedQuantity)}
                </TableCell>
                <TableCell className="text-right">
                  {editable ? (
                    <Input
                      className="ml-auto w-24 text-right"
                      inputMode="decimal"
                      aria-label={`Quantidade contada de ${i.productName}`}
                      value={values[i.productId] ?? ""}
                      onChange={(e) => setValues((v) => ({ ...v, [i.productId]: e.target.value }))}
                    />
                  ) : (
                    <span className="tabular-nums">{qty(i.countedQuantity)}</span>
                  )}
                </TableCell>
                <TableCell className={`text-right tabular-nums ${(i.difference ?? 0) < 0 ? "text-red-600 dark:text-red-400" : (i.difference ?? 0) > 0 ? "text-amber-700 dark:text-amber-400" : "text-muted-foreground"}`}>
                  {i.difference === null ? "—" : `${i.difference > 0 ? "+" : ""}${qty(i.difference)}`}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        {invalid && <p className="text-xs text-destructive">Use apenas números positivos na contagem.</p>}
        {editable && (
          <div className="flex flex-wrap justify-end gap-2">
            <Button variant="outline" onClick={() => cancel.mutate()} disabled={busy}>Cancelar inventário</Button>
            <Button variant="outline" onClick={() => save.mutate()} disabled={busy || invalid}>Salvar contagem</Button>
            <Button onClick={() => apply.mutate()} disabled={busy || invalid}>Aplicar ajustes</Button>
          </div>
        )}
      </div>
    </SectionCard>
  );
}
