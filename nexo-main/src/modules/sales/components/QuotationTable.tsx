import { useNavigate } from "react-router-dom";
import { Pencil } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { QuotationStatusBadge } from "./QuotationStatusBadge";
import type { Quotation } from "../types/quotation";
import { formatCurrency, formatDateTime } from "@/lib/formatters";

interface QuotationTableProps {
  quotations: Quotation[];
}

function itemsSummary(q: Quotation): string {
  const totalQty = q.items.reduce((acc, i) => acc + i.quantity, 0);
  if (q.items.length === 1) {
    return `${totalQty}x ${q.items[0].description}`;
  }
  return `${totalQty} itens (${q.items.length} produtos)`;
}

export function QuotationTable({ quotations }: QuotationTableProps) {
  const navigate = useNavigate();

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead className="w-32">ID</TableHead>
          <TableHead>Data</TableHead>
          <TableHead>Cliente</TableHead>
          <TableHead>Operador</TableHead>
          <TableHead>Itens</TableHead>
          <TableHead className="text-right">Total</TableHead>
          <TableHead>Status</TableHead>
          <TableHead className="w-12" />
        </TableRow>
      </TableHeader>
      <TableBody>
        {quotations.map((q) => (
          <TableRow
            key={q.id}
            className="cursor-pointer hover:bg-muted/50"
            onClick={() => navigate(`/orcamentos/${q.id}`)}
          >
            <TableCell className="font-mono text-xs text-muted-foreground">
              {q.id}
            </TableCell>
            <TableCell className="text-sm tabular-nums">
              {formatDateTime(q.createdAt)}
            </TableCell>
            <TableCell className="text-sm">
              {q.customerName ?? (
                <span className="text-muted-foreground italic">Sem cliente</span>
              )}
            </TableCell>
            <TableCell className="text-sm">{q.operator}</TableCell>
            <TableCell className="text-sm text-muted-foreground max-w-xs truncate">
              {q.items.length === 0 ? (
                <span className="italic">Sem itens</span>
              ) : (
                itemsSummary(q)
              )}
            </TableCell>
            <TableCell className="text-right text-sm font-semibold tabular-nums">
              {formatCurrency(q.total)}
            </TableCell>
            <TableCell>
              <QuotationStatusBadge status={q.status} />
            </TableCell>
            <TableCell onClick={(e) => e.stopPropagation()}>
              <Button
                variant="ghost"
                size="sm"
                className="h-7 w-7 p-0"
                onClick={() => navigate(`/orcamentos/${q.id}`)}
              >
                <Pencil className="h-4 w-4" />
              </Button>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
