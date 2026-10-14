import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { formatCurrency } from "@/lib/formatters";
import type { CashSessionDto } from "../types";

interface CashHistoryTableProps {
  sessions: CashSessionDto[];
}

function formatDateTime(iso: string | null) {
  if (!iso) return "—";
  return new Date(iso).toLocaleString("pt-BR", {
    day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit",
  });
}

function DifferenceCell({ value }: { value: number | null }) {
  if (value === null) return <span className="text-muted-foreground">—</span>;
  if (value === 0) return <span className="text-green-700 dark:text-green-400">Sem divergência</span>;
  return (
    <span className={value > 0 ? "text-amber-700 dark:text-amber-400" : "text-red-600 dark:text-red-400"}>
      {value > 0 ? "Sobra " : "Falta "}
      {formatCurrency(Math.abs(value))}
    </span>
  );
}

/** Closed shifts: expected vs counted and the difference recorded at closing. */
export function CashHistoryTable({ sessions }: CashHistoryTableProps) {
  const closed = sessions.filter((s) => s.status === "Closed");
  if (closed.length === 0) {
    return <p className="text-sm text-muted-foreground">Nenhum caixa fechado ainda.</p>;
  }

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Fechamento</TableHead>
          <TableHead>Responsável</TableHead>
          <TableHead className="text-right">Esperado</TableHead>
          <TableHead className="text-right">Contado</TableHead>
          <TableHead className="text-right">Diferença</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {closed.map((s) => (
          <TableRow key={s.id}>
            <TableCell className="text-sm tabular-nums">{formatDateTime(s.closedAt)}</TableCell>
            <TableCell className="text-sm">
              {s.closedByName || s.openedByName || "—"}
              {s.closingNotes && (
                <span className="block text-xs text-muted-foreground">{s.closingNotes}</span>
              )}
            </TableCell>
            <TableCell className="text-right text-sm tabular-nums">
              {s.expectedBalance === null ? "—" : formatCurrency(s.expectedBalance)}
            </TableCell>
            <TableCell className="text-right text-sm tabular-nums">
              {s.closingBalance === null ? "—" : formatCurrency(s.closingBalance)}
            </TableCell>
            <TableCell className="text-right text-sm font-medium tabular-nums">
              <DifferenceCell value={s.difference} />
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
