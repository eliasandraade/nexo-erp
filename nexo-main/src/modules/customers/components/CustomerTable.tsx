import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Button } from "@/components/ui/button";
import { StatusBadge } from "@/components/shared/StatusBadge";
import { Pencil } from "lucide-react";
import { useNavigate } from "react-router-dom";
import type { CustomerDto } from "../types";
import { parseAddress } from "../types";

interface CustomerTableProps {
  customers: CustomerDto[];
}

export function CustomerTable({ customers }: CustomerTableProps) {
  const navigate = useNavigate();

  return (
    <div className="overflow-x-auto">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Nome / Razão social</TableHead>
            <TableHead>Tipo</TableHead>
            <TableHead>Documento</TableHead>
            <TableHead>Telefone</TableHead>
            <TableHead>Cidade</TableHead>
            <TableHead>Status</TableHead>
            <TableHead className="text-right">Ações</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {customers.map((c) => {
            const addr = parseAddress(c.addressJson);
            const cityDisplay = addr.city ? `${addr.city}${addr.state ? `/${addr.state}` : ""}` : "—";
            return (
              <TableRow key={c.id}>
                <TableCell className="font-medium">{c.name}</TableCell>
                <TableCell>{c.personType === "Individual" ? "Física" : "Jurídica"}</TableCell>
                <TableCell className="font-mono text-xs">{c.documentNumber}</TableCell>
                <TableCell>{c.phone ?? "—"}</TableCell>
                <TableCell>{cityDisplay}</TableCell>
                <TableCell>
                  <StatusBadge
                    label={c.isActive ? "Ativo" : "Inativo"}
                    variant={c.isActive ? "success" : "neutral"}
                  />
                </TableCell>
                <TableCell className="text-right">
                  <Button variant="ghost" size="sm" onClick={() => navigate(`/clientes/${c.id}`)}>
                    <Pencil className="h-4 w-4 mr-1" /> Editar
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
