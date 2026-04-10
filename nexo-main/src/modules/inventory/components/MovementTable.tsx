import { format } from "date-fns";
import { ptBR } from "date-fns/locale";
import { StatusBadge } from "@/components/shared/StatusBadge";
import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from "@/components/ui/table";
import type { StockMovementDto } from "../types";
import { MOVEMENT_TYPE_LABEL, MOVEMENT_TYPE_VARIANT } from "../types";

interface MovementTableProps {
  movements: StockMovementDto[];
}

export function MovementTable({ movements }: MovementTableProps) {
  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Data/hora</TableHead>
          <TableHead>Tipo</TableHead>
          <TableHead className="text-right">Qtd.</TableHead>
          <TableHead className="text-right">Antes</TableHead>
          <TableHead className="text-right">Depois</TableHead>
          <TableHead>Origem</TableHead>
          <TableHead>Observações</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {movements.map((m) => {
          const label = MOVEMENT_TYPE_LABEL[m.movementType] ?? m.movementType;
          const variant = MOVEMENT_TYPE_VARIANT[m.movementType] ?? "neutral";
          return (
            <TableRow key={m.id}>
              <TableCell className="text-xs text-muted-foreground whitespace-nowrap">
                {format(new Date(m.createdAt), "dd/MM/yy HH:mm", { locale: ptBR })}
              </TableCell>
              <TableCell>
                <StatusBadge label={label} variant={variant} />
              </TableCell>
              <TableCell className="text-right font-medium tabular-nums">
                {m.quantity}
              </TableCell>
              <TableCell className="text-right tabular-nums text-muted-foreground text-sm">
                {m.quantityBefore}
              </TableCell>
              <TableCell className="text-right tabular-nums text-sm">
                {m.quantityAfter}
              </TableCell>
              <TableCell className="text-sm text-muted-foreground">
                {m.referenceType ?? "Manual"}
              </TableCell>
              <TableCell className="text-sm text-muted-foreground max-w-[200px] truncate">
                {m.notes ?? "—"}
              </TableCell>
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
}
