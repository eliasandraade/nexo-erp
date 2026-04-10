import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import type { TopProductRow } from "../types";
import { formatCurrency } from "@/lib/formatters";

interface TopProductsTableProps {
  rows: TopProductRow[];
}

function RankBadge({ position }: { position: number }) {
  if (position === 1) {
    return (
      <span className="inline-flex items-center justify-center w-6 h-6 rounded-full text-xs font-bold bg-yellow-400 text-yellow-900">
        1
      </span>
    );
  }
  if (position === 2) {
    return (
      <span className="inline-flex items-center justify-center w-6 h-6 rounded-full text-xs font-bold bg-zinc-300 text-zinc-800">
        2
      </span>
    );
  }
  if (position === 3) {
    return (
      <span className="inline-flex items-center justify-center w-6 h-6 rounded-full text-xs font-bold bg-amber-600 text-amber-50">
        3
      </span>
    );
  }
  return (
    <span className="text-sm text-muted-foreground tabular-nums">{position}</span>
  );
}

export function TopProductsTable({ rows }: TopProductsTableProps) {
  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead className="w-12 text-center">Pos</TableHead>
          <TableHead className="w-28">Código</TableHead>
          <TableHead>Descrição</TableHead>
          <TableHead className="text-center">Qtd. Vendida</TableHead>
          <TableHead className="text-right">Receita</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.map((row, index) => (
          <TableRow key={row.productCode}>
            <TableCell className="text-center">
              <RankBadge position={index + 1} />
            </TableCell>
            <TableCell className="font-mono text-sm text-muted-foreground">
              {row.productCode}
            </TableCell>
            <TableCell className="font-medium">{row.productDescription}</TableCell>
            <TableCell className="text-center tabular-nums text-sm">
              {row.quantitySold}
            </TableCell>
            <TableCell className="text-right tabular-nums text-sm font-semibold">
              {formatCurrency(row.revenueGenerated)}
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
