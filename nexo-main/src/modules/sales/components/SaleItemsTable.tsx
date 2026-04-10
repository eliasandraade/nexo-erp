import { useState } from "react";
import { XCircle } from "lucide-react";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import type { CartItem, CompletedSale } from "../types";
import { SaleCancellationDialog } from "./SaleCancellationDialog";
import type { CancellationConfirmPayload } from "./SaleCancellationDialog";
import { formatCurrency } from "@/lib/formatters";

interface SaleItemsTableProps {
  items: CartItem[];
  sale?: CompletedSale;
  onCancelItem?: (itemProductId: string, payload: CancellationConfirmPayload) => Promise<void>;
}

export function SaleItemsTable({ items, sale, onCancelItem }: SaleItemsTableProps) {
  const [pendingCancelProductId, setPendingCancelProductId] = useState<string | null>(null);
  const [isCancelling, setIsCancelling] = useState(false);

  const grandTotal = items
    .filter((i) => !i.status || i.status === "active")
    .reduce((acc, i) => acc + i.totalPrice, 0);

  const canCancelItems =
    !!onCancelItem &&
    sale?.status !== "cancelled";

  async function handleConfirmItemCancel(payload: CancellationConfirmPayload) {
    if (!pendingCancelProductId || !onCancelItem) return;
    setIsCancelling(true);
    try {
      await onCancelItem(pendingCancelProductId, payload);
      setPendingCancelProductId(null);
    } finally {
      setIsCancelling(false);
    }
  }

  const pendingItem = pendingCancelProductId
    ? items.find((i) => i.productId === pendingCancelProductId)
    : null;

  return (
    <>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Código</TableHead>
            <TableHead>Descrição</TableHead>
            <TableHead className="text-center">Qtd</TableHead>
            <TableHead className="text-right">Unitário</TableHead>
            <TableHead className="text-right">Desconto</TableHead>
            <TableHead className="text-right">Total item</TableHead>
            {canCancelItems && <TableHead className="w-12" />}
          </TableRow>
        </TableHeader>
        <TableBody>
          {items.map((item) => {
            const isCancelled = item.status === "cancelled";
            return (
              <TableRow
                key={item.productId}
                className={isCancelled ? "opacity-50 line-through text-muted-foreground" : undefined}
              >
                <TableCell className="font-mono text-xs text-muted-foreground">
                  {item.code}
                </TableCell>
                <TableCell className="text-sm font-medium">
                  <span>{item.description}</span>
                  {isCancelled && (
                    <Badge variant="destructive" className="ml-2 text-xs py-0">
                      Cancelado
                    </Badge>
                  )}
                </TableCell>
                <TableCell className="text-center text-sm tabular-nums">{item.quantity}</TableCell>
                <TableCell className="text-right text-sm tabular-nums text-muted-foreground">
                  {formatCurrency(item.unitPrice)}
                </TableCell>
                <TableCell className="text-right text-sm tabular-nums text-muted-foreground">
                  {item.discount > 0 ? formatCurrency(item.discount) : "—"}
                </TableCell>
                <TableCell className="text-right text-sm font-semibold tabular-nums">
                  {formatCurrency(item.totalPrice)}
                </TableCell>
                {canCancelItems && (
                  <TableCell className="text-center">
                    {!isCancelled && (
                      <Button
                        variant="ghost"
                        size="icon"
                        className="h-7 w-7 text-muted-foreground hover:text-destructive"
                        title="Cancelar item"
                        onClick={() => setPendingCancelProductId(item.productId)}
                      >
                        <XCircle className="h-4 w-4" />
                      </Button>
                    )}
                  </TableCell>
                )}
              </TableRow>
            );
          })}
          <TableRow className="bg-muted/30 font-semibold">
            <TableCell colSpan={canCancelItems ? 6 : 5} className="text-right text-sm">
              Total dos itens ativos
            </TableCell>
            <TableCell className="text-right text-sm tabular-nums">
              {formatCurrency(grandTotal)}
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>

      <SaleCancellationDialog
        open={!!pendingCancelProductId}
        onClose={() => setPendingCancelProductId(null)}
        onConfirm={handleConfirmItemCancel}
        mode="item"
        itemDescription={pendingItem?.description}
        isLoading={isCancelling}
      />
    </>
  );
}
