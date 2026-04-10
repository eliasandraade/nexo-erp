import { ExternalLink } from "lucide-react";
import { useNavigate } from "react-router-dom";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Button } from "@/components/ui/button";
import type { CommissionRecord } from "../types";
import { CommissionStatusBadge } from "./CommissionStatusBadge";
import { formatCurrency, formatDateTime } from "@/lib/formatters";

interface CommissionRecordsTableProps {
  records: CommissionRecord[];
}

function formatRate(rate: number) {
  return `${(rate * 100).toFixed(0)}%`;
}

export function CommissionRecordsTable({ records }: CommissionRecordsTableProps) {
  const navigate = useNavigate();

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Data / Hora</TableHead>
          <TableHead>Operador</TableHead>
          <TableHead>Venda</TableHead>
          <TableHead>Produto</TableHead>
          <TableHead className="text-right">Base</TableHead>
          <TableHead className="text-center">%</TableHead>
          <TableHead className="text-right">Comissão</TableHead>
          <TableHead>Status</TableHead>
          <TableHead>Motivo</TableHead>
          <TableHead className="w-10" />
        </TableRow>
      </TableHeader>
      <TableBody>
        {records.map((record) => (
          <TableRow
            key={record.id}
            className={record.status === "reversed" ? "opacity-60" : undefined}
          >
            <TableCell className="text-xs text-muted-foreground tabular-nums whitespace-nowrap">
              {formatDateTime(record.createdAt)}
            </TableCell>
            <TableCell className="text-sm font-medium">{record.operator}</TableCell>
            <TableCell className="font-mono text-xs text-muted-foreground">
              {record.saleId}
            </TableCell>
            <TableCell className="text-sm">
              <div>
                <span className="font-mono text-xs text-muted-foreground mr-2">
                  {record.productCode}
                </span>
                {record.productDescription}
              </div>
            </TableCell>
            <TableCell className="text-right text-sm tabular-nums text-muted-foreground">
              {formatCurrency(record.baseAmount)}
            </TableCell>
            <TableCell className="text-center text-sm tabular-nums">
              {formatRate(record.commissionRate)}
            </TableCell>
            <TableCell className="text-right text-sm font-semibold tabular-nums">
              {formatCurrency(record.commissionAmount)}
            </TableCell>
            <TableCell>
              <CommissionStatusBadge status={record.status} />
            </TableCell>
            <TableCell className="text-xs text-muted-foreground max-w-32 truncate">
              {record.reason ?? "—"}
            </TableCell>
            <TableCell>
              <Button
                variant="ghost"
                size="icon"
                className="h-7 w-7 text-muted-foreground hover:text-foreground"
                title="Ver venda"
                onClick={() => navigate(`/vendas/${record.saleId}`)}
              >
                <ExternalLink className="h-3.5 w-3.5" />
              </Button>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
