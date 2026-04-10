import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import type { SalesByOperatorRow } from "../types";
import { formatCurrency } from "@/lib/formatters";

interface SalesByOperatorTableProps {
  rows: SalesByOperatorRow[];
}

export function SalesByOperatorTable({ rows }: SalesByOperatorTableProps) {
  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Operador</TableHead>
          <TableHead className="text-center">Vendas</TableHead>
          <TableHead className="text-right">Faturamento</TableHead>
          <TableHead className="text-right">Ticket médio</TableHead>
          <TableHead className="text-center">Canceladas</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.map((row) => (
          <TableRow key={row.operator}>
            <TableCell className="font-medium">{row.operator}</TableCell>
            <TableCell className="text-center tabular-nums text-sm">{row.salesCount}</TableCell>
            <TableCell className="text-right tabular-nums text-sm font-semibold">
              {formatCurrency(row.totalRevenue)}
            </TableCell>
            <TableCell className="text-right tabular-nums text-sm text-muted-foreground">
              {formatCurrency(row.averageTicket)}
            </TableCell>
            <TableCell className="text-center tabular-nums text-sm">
              {row.cancelledCount > 0 ? (
                <span className="text-destructive font-medium">{row.cancelledCount}</span>
              ) : (
                "—"
              )}
            </TableCell>
          </TableRow>
        ))}
        {rows.length > 1 && (
          <TableRow className="bg-muted/30 font-semibold">
            <TableCell>Total</TableCell>
            <TableCell className="text-center tabular-nums text-sm">
              {rows.reduce((acc, r) => acc + r.salesCount, 0)}
            </TableCell>
            <TableCell className="text-right tabular-nums text-sm">
              {formatCurrency(rows.reduce((acc, r) => acc + r.totalRevenue, 0))}
            </TableCell>
            <TableCell />
            <TableCell className="text-center tabular-nums text-sm">
              {rows.reduce((acc, r) => acc + r.cancelledCount, 0) || "—"}
            </TableCell>
          </TableRow>
        )}
      </TableBody>
    </Table>
  );
}
