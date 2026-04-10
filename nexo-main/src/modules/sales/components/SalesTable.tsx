import { useNavigate } from "react-router-dom";
import { Eye } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { SaleStatusBadge } from "./SaleStatusBadge";
import { paymentMethodLabels } from "../types";
import type { CompletedSale } from "../types";
import { formatCurrency, formatDateTime } from "@/lib/formatters";

interface SalesTableProps {
  sales: CompletedSale[];
}

function itemsSummary(sale: CompletedSale): string {
  const total = sale.items.reduce((acc, i) => acc + i.quantity, 0);
  if (sale.items.length === 1) {
    return `${total}x ${sale.items[0].description}`;
  }
  return `${total} itens (${sale.items.length} produtos)`;
}

function paymentSummary(sale: CompletedSale): string {
  if (sale.payments.length === 1) {
    return paymentMethodLabels[sale.payments[0].method];
  }
  return sale.payments.map((p) => paymentMethodLabels[p.method]).join(" + ");
}

export function SalesTable({ sales }: SalesTableProps) {
  const navigate = useNavigate();

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead className="w-32">ID</TableHead>
          <TableHead>Data / Hora</TableHead>
          <TableHead>Operador</TableHead>
          <TableHead>Itens</TableHead>
          <TableHead className="text-right">Total</TableHead>
          <TableHead>Pagamento</TableHead>
          <TableHead>Status</TableHead>
          <TableHead className="w-12" />
        </TableRow>
      </TableHeader>
      <TableBody>
        {sales.map((sale) => (
          <TableRow
            key={sale.id}
            className="cursor-pointer hover:bg-muted/50"
            onClick={() => navigate(`/vendas/${sale.id}`)}
          >
            <TableCell className="font-mono text-xs text-muted-foreground">
              {sale.id}
            </TableCell>
            <TableCell className="text-sm tabular-nums">
              {formatDateTime(sale.timestamp)}
            </TableCell>
            <TableCell className="text-sm">{sale.operator}</TableCell>
            <TableCell className="text-sm text-muted-foreground max-w-xs truncate">
              {itemsSummary(sale)}
            </TableCell>
            <TableCell className="text-right text-sm font-semibold tabular-nums">
              {formatCurrency(sale.total)}
            </TableCell>
            <TableCell className="text-sm text-muted-foreground">
              {paymentSummary(sale)}
            </TableCell>
            <TableCell>
              <SaleStatusBadge status={sale.status} />
            </TableCell>
            <TableCell onClick={(e) => e.stopPropagation()}>
              <Button
                variant="ghost"
                size="sm"
                className="h-7 w-7 p-0"
                onClick={() => navigate(`/vendas/${sale.id}`)}
              >
                <Eye className="h-4 w-4" />
              </Button>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
