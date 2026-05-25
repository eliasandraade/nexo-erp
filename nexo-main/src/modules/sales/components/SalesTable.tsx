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
import { formatCurrency, formatDateTime } from "@/lib/formatters";
import type { SaleListItemDto } from "../api/sales.api";

const backendMethodLabels: Record<string, string> = {
  Cash:     "Dinheiro",
  Pix:      "PIX",
  Debit:    "Débito",
  Credit:   "Crédito",
  Transfer: "Transferência",
  Check:    "Cheque",
  Mixed:    "Misto",
  Other:    "Outro",
};

function itemsSummary(dto: SaleListItemDto): string {
  if (dto.itemCount === 0) return "—";
  if (dto.itemCount === 1 && dto.firstItemName) {
    return `${dto.totalQuantity}x ${dto.firstItemName}`;
  }
  return `${dto.totalQuantity} itens (${dto.itemCount} produtos)`;
}

function paymentSummary(dto: SaleListItemDto): string {
  if (dto.paymentMethods.length === 0) return "—";
  return dto.paymentMethods.map((m) => backendMethodLabels[m] ?? m).join(" + ");
}

interface SalesTableProps {
  sales: SaleListItemDto[];
}

export function SalesTable({ sales }: SalesTableProps) {
  const navigate = useNavigate();

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead className="w-20">#</TableHead>
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
              #{sale.number}
            </TableCell>
            <TableCell className="text-sm tabular-nums">
              {formatDateTime(sale.timestamp)}
            </TableCell>
            <TableCell className="text-sm">{sale.soldByName}</TableCell>
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
              <SaleStatusBadge status={sale.status as never} />
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
