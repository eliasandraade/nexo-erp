import { Badge } from "@/components/ui/badge";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { MOVEMENT_TYPE_LABEL, MOVEMENT_TYPE_VARIANT } from "../types";
import type { CashMovementDto, CashMovementType } from "../types";
import { formatCurrency } from "@/lib/formatters";

interface CashMovementsTableProps {
  movements: CashMovementDto[];
}

const OUTFLOW_TYPES: CashMovementType[] = ["Withdrawal"];
const NEUTRAL_TYPES: CashMovementType[] = ["Opening", "Closing"];

function formatTime(iso: string) {
  return new Date(iso).toLocaleTimeString("pt-BR", {
    hour: "2-digit",
    minute: "2-digit",
  });
}

function amountClass(type: CashMovementType): string {
  if (NEUTRAL_TYPES.includes(type)) return "text-muted-foreground";
  if (OUTFLOW_TYPES.includes(type)) return "text-red-600 dark:text-red-400";
  return "text-green-700 dark:text-green-400";
}

function displayAmount(type: CashMovementType, amount: number): string {
  if (NEUTRAL_TYPES.includes(type)) return formatCurrency(amount);
  if (OUTFLOW_TYPES.includes(type)) return `- ${formatCurrency(amount)}`;
  return `+ ${formatCurrency(amount)}`;
}

export function CashMovementsTable({ movements }: CashMovementsTableProps) {
  if (movements.length === 0) {
    return (
      <p className="text-sm text-muted-foreground py-4 text-center">
        Nenhuma movimentação registrada nesta sessão.
      </p>
    );
  }

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Hora</TableHead>
          <TableHead>Tipo</TableHead>
          <TableHead>Descrição</TableHead>
          <TableHead className="text-right">Valor</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {movements.map((m) => (
          <TableRow key={m.id}>
            <TableCell className="text-xs text-muted-foreground tabular-nums">
              {formatTime(m.createdAt)}
            </TableCell>
            <TableCell>
              <Badge variant={MOVEMENT_TYPE_VARIANT[m.movementType as CashMovementType]} className="text-xs">
                {MOVEMENT_TYPE_LABEL[m.movementType as CashMovementType] ?? m.movementType}
              </Badge>
            </TableCell>
            <TableCell className="text-sm">{m.description}</TableCell>
            <TableCell
              className={`text-right text-sm font-medium tabular-nums ${amountClass(m.movementType as CashMovementType)}`}
            >
              {displayAmount(m.movementType as CashMovementType, m.amount)}
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
