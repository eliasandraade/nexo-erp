import { useNavigate } from "react-router-dom";
import { Pencil } from "lucide-react";
import {
  Table, TableBody, TableCell, TableHead,
  TableHeader, TableRow,
} from "@/components/ui/table";
import { Button } from "@/components/ui/button";
import { StatusBadge } from "@/components/shared/StatusBadge";
import type { SupplierDto } from "../types";
import { parseAddress } from "../types";

interface SupplierTableProps {
  suppliers: SupplierDto[];
}

export function SupplierTable({ suppliers }: SupplierTableProps) {
  const navigate = useNavigate();

  return (
    <div className="overflow-x-auto">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Nome / Razão social</TableHead>
            <TableHead>Documento</TableHead>
            <TableHead>Telefone</TableHead>
            <TableHead>Contato principal</TableHead>
            <TableHead>Cidade</TableHead>
            <TableHead>Status</TableHead>
            <TableHead className="text-right">Ações</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {suppliers.map((s) => {
            const addr = parseAddress(s.addressJson);
            const cityDisplay = addr.city
              ? `${addr.city}${addr.state ? `/${addr.state}` : ""}`
              : "—";
            return (
              <TableRow key={s.id}>
                <TableCell>
                  <div>
                    <p className="font-medium text-foreground leading-tight">{s.name}</p>
                    {s.tradeName && (
                      <p className="text-xs text-muted-foreground mt-0.5">{s.tradeName}</p>
                    )}
                  </div>
                </TableCell>
                <TableCell className="font-mono text-xs">{s.documentNumber}</TableCell>
                <TableCell>{s.phone ?? "—"}</TableCell>
                <TableCell className="text-sm text-muted-foreground">
                  {s.contactName ?? "—"}
                </TableCell>
                <TableCell>{cityDisplay}</TableCell>
                <TableCell>
                  <StatusBadge
                    label={s.isActive ? "Ativo" : "Inativo"}
                    variant={s.isActive ? "success" : "neutral"}
                  />
                </TableCell>
                <TableCell className="text-right">
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => navigate(`/fornecedores/${s.id}`)}
                  >
                    <Pencil className="h-4 w-4 mr-1.5" />
                    Editar
                  </Button>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </div>
  );
}
