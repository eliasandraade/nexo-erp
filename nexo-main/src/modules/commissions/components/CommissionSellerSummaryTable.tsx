import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import type { CommissionSummaryBySeller } from "../types";
import { formatCurrency } from "@/lib/formatters";

interface CommissionSellerSummaryTableProps {
  summaries: CommissionSummaryBySeller[];
}

export function CommissionSellerSummaryTable({
  summaries,
}: CommissionSellerSummaryTableProps) {
  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Operador</TableHead>
          <TableHead className="text-right">Comissão ativa</TableHead>
          <TableHead className="text-right">Estornada</TableHead>
          <TableHead className="text-right">Líquido</TableHead>
          <TableHead className="text-center">Vendas</TableHead>
          <TableHead className="text-center">Itens</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {summaries.map((row) => (
          <TableRow key={row.operator}>
            <TableCell className="font-medium">{row.operator}</TableCell>
            <TableCell className="text-right tabular-nums text-sm">
              {formatCurrency(row.activeCommission)}
            </TableCell>
            <TableCell className="text-right tabular-nums text-sm text-muted-foreground">
              {row.reversedCommission > 0
                ? formatCurrency(row.reversedCommission)
                : "—"}
            </TableCell>
            <TableCell className="text-right tabular-nums text-sm font-semibold">
              {formatCurrency(row.netCommission)}
            </TableCell>
            <TableCell className="text-center text-sm tabular-nums">
              {row.totalSalesCount}
            </TableCell>
            <TableCell className="text-center text-sm tabular-nums">
              {row.totalItemsCommissioned}
            </TableCell>
          </TableRow>
        ))}
        {summaries.length > 1 && (
          <TableRow className="bg-muted/30 font-semibold">
            <TableCell>Total</TableCell>
            <TableCell className="text-right tabular-nums text-sm">
              {formatCurrency(
                summaries.reduce((acc, s) => acc + s.activeCommission, 0)
              )}
            </TableCell>
            <TableCell className="text-right tabular-nums text-sm text-muted-foreground">
              {formatCurrency(
                summaries.reduce((acc, s) => acc + s.reversedCommission, 0)
              )}
            </TableCell>
            <TableCell className="text-right tabular-nums text-sm">
              {formatCurrency(
                summaries.reduce((acc, s) => acc + s.netCommission, 0)
              )}
            </TableCell>
            <TableCell />
            <TableCell />
          </TableRow>
        )}
      </TableBody>
    </Table>
  );
}
