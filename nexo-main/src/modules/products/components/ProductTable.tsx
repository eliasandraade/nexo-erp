import { useNavigate } from "react-router-dom";
import { Pencil } from "lucide-react";
import { Button } from "@/components/ui/button";
import { StatusBadge } from "@/components/shared/StatusBadge";
import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from "@/components/ui/table";
import type { CategoryDto, ProductDto } from "../types";
import { format } from "date-fns";

interface ProductTableProps {
  products: ProductDto[];
  categories: CategoryDto[];
}

export function ProductTable({ products, categories }: ProductTableProps) {
  const navigate = useNavigate();

  function categoryName(id: string | null): string {
    if (!id) return "—";
    return categories.find((c) => c.id === id)?.name ?? "—";
  }

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Código</TableHead>
          <TableHead>Nome</TableHead>
          <TableHead>Categoria</TableHead>
          <TableHead>Unidade</TableHead>
          <TableHead className="text-right">Preço</TableHead>
          <TableHead className="text-right">Est. mín.</TableHead>
          <TableHead>Status</TableHead>
          <TableHead>Última atualização</TableHead>
          <TableHead className="text-right">Ações</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {products.map((p) => (
          <TableRow key={p.id}>
            <TableCell className="font-medium font-mono text-xs">{p.code}</TableCell>
            <TableCell>
              <div>
                <p className="font-medium">{p.name}</p>
                {p.barcode && (
                  <p className="text-xs text-muted-foreground font-mono">{p.barcode}</p>
                )}
              </div>
            </TableCell>
            <TableCell className="text-muted-foreground text-sm">
              {categoryName(p.categoryId)}
            </TableCell>
            <TableCell className="text-sm">{p.unit}</TableCell>
            <TableCell className="text-right text-sm">
              {p.salePrice.toLocaleString("pt-BR", { style: "currency", currency: "BRL" })}
            </TableCell>
            <TableCell className="text-right text-sm">
              {p.minStockQuantity ?? "—"}
            </TableCell>
            <TableCell>
              <StatusBadge
                label={p.isActive ? "Ativo" : "Inativo"}
                variant={p.isActive ? "success" : "neutral"}
              />
            </TableCell>
            <TableCell className="text-muted-foreground text-xs">
              {format(new Date(p.updatedAt), "dd/MM/yyyy HH:mm")}
            </TableCell>
            <TableCell className="text-right">
              <Button
                variant="ghost"
                size="icon"
                onClick={() => navigate(`/produtos/${p.id}`)}
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
