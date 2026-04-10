import { useNavigate } from "react-router-dom";
import { format } from "date-fns";
import { ptBR } from "date-fns/locale";
import { MoreHorizontal, Pencil, History } from "lucide-react";
import { StatusBadge } from "@/components/shared/StatusBadge";
import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from "@/components/ui/table";
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Button } from "@/components/ui/button";
import type { StockItemEnriched } from "../types";
import { STOCK_STATUS_LABEL, STOCK_STATUS_VARIANT } from "../types";

interface InventoryTableProps {
  items: StockItemEnriched[];
}

export function InventoryTable({ items }: InventoryTableProps) {
  const navigate = useNavigate();

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Código</TableHead>
          <TableHead>Produto</TableHead>
          <TableHead>Categoria</TableHead>
          <TableHead>Unidade</TableHead>
          <TableHead className="text-right">Disponível</TableHead>
          <TableHead className="text-right">Reservado</TableHead>
          <TableHead className="text-right">Est. mínimo</TableHead>
          <TableHead>Status</TableHead>
          <TableHead>Última mov.</TableHead>
          <TableHead className="w-[60px]" />
        </TableRow>
      </TableHeader>
      <TableBody>
        {items.map((item) => (
          <TableRow key={item.id}>
            <TableCell className="font-mono text-xs text-muted-foreground">
              {item.productCode}
            </TableCell>
            <TableCell className="font-medium text-foreground">
              {item.productName}
            </TableCell>
            <TableCell className="text-sm">{item.categoryName || "—"}</TableCell>
            <TableCell className="text-sm">{item.unit}</TableCell>
            <TableCell className="text-right font-medium tabular-nums">
              {item.availableQuantity}
            </TableCell>
            <TableCell className="text-right tabular-nums text-muted-foreground text-sm">
              {item.reservedQuantity > 0 ? item.reservedQuantity : "—"}
            </TableCell>
            <TableCell className="text-right tabular-nums text-sm">
              {item.minStockQuantity ?? "—"}
            </TableCell>
            <TableCell>
              <StatusBadge
                label={STOCK_STATUS_LABEL[item.status]}
                variant={STOCK_STATUS_VARIANT[item.status]}
              />
            </TableCell>
            <TableCell className="text-muted-foreground text-xs">
              {item.lastMovementAt
                ? format(new Date(item.lastMovementAt), "dd/MM/yy HH:mm", { locale: ptBR })
                : "—"}
            </TableCell>
            <TableCell>
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button variant="ghost" size="icon" className="h-8 w-8">
                    <MoreHorizontal className="h-4 w-4" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuItem
                    onClick={() => navigate(`/estoque/movimentacoes?productId=${item.productId}`)}
                  >
                    <History className="h-4 w-4 mr-2" /> Ver movimentações
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    onClick={() => navigate(`/estoque/ajustes?productId=${item.productId}`)}
                  >
                    <Pencil className="h-4 w-4 mr-2" /> Ajustar
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
