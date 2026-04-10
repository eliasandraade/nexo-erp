import { Badge } from "@/components/ui/badge";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { cashMovementTypeLabels } from "../types";
import type { CashMovement } from "../types";
import { formatCurrency } from "@/lib/formatters";

interface CashMovementsTableProps {
  movements: CashMovement[];
}

function formatTime(iso: string) {
  return new Date(iso).toLocaleTimeString("pt-BR", {
    hour: "2-digit",
    minute: "2-digit",
  });
}

function movementBadgeVariant(type: CashMovement["type"]) {
  switch (type) {
    case "opening":
      return "secondary";
    case "reinforcement":
      return "default";
    case "withdrawal":
      return "destructive";
    case "sale":
      return "default";
    case "adjustment":
      return "outline";
    case "closing":
      return "secondary";
    default:
      return "outline";
  }
}

const paymentMethodLabels: Record<string, string> = {
  cash: "Dinheiro",
  pix: "PIX",
  card: "Cartão",
};

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
          <TableHead>Pagamento</TableHead>
          <TableHead className="text-right">Valor</TableHead>
          <TableHead>Operador</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {movements.map((m) => (
          <TableRow key={m.id}>
            <TableCell className="text-xs text-muted-foreground tabular-nums">
              {formatTime(m.timestamp)}
            </TableCell>
            <TableCell>
              <Badge variant={movementBadgeVariant(m.type)} className="text-xs">
                {cashMovementTypeLabels[m.type]}
              </Badge>
            </TableCell>
            <TableCell className="text-sm">{m.description}</TableCell>
            <TableCell className="text-xs text-muted-foreground">
              {m.paymentMethod ? paymentMethodLabels[m.paymentMethod] : "—"}
            </TableCell>
            <TableCell
              className={`text-right text-sm font-medium tabular-nums ${
                m.amount < 0
                  ? "text-red-600 dark:text-red-400"
                  : m.amount === 0
                  ? "text-muted-foreground"
                  : "text-green-700 dark:text-green-400"
              }`}
            >
              {m.amount === 0 ? "—" : formatCurrency(m.amount)}
            </TableCell>
            <TableCell className="text-xs text-muted-foreground">{m.operator}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
