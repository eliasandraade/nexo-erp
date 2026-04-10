import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from "@/components/ui/table";
import { Button } from "@/components/ui/button";
import { StatusBadge } from "@/components/shared/StatusBadge";
import { format } from "date-fns";
import { Pencil, Shield } from "lucide-react";
import { useNavigate } from "react-router-dom";
import type { User } from "../types";
import { roleLabels, statusLabels, statusVariant } from "../types";

interface UserTableProps {
  users: User[];
}

export function UserTable({ users }: UserTableProps) {
  const navigate = useNavigate();

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Nome</TableHead>
          <TableHead>E-mail</TableHead>
          <TableHead>Perfil</TableHead>
          <TableHead>Loja vinculada</TableHead>
          <TableHead>Status</TableHead>
          <TableHead>Último acesso</TableHead>
          <TableHead className="text-right">Ações</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {users.map((u) => (
          <TableRow key={u.id}>
            <TableCell className="font-medium">{u.name}</TableCell>
            <TableCell>{u.email}</TableCell>
            <TableCell>{roleLabels[u.role]}</TableCell>
            <TableCell>{u.store || "—"}</TableCell>
            <TableCell>
              <StatusBadge label={statusLabels[u.status]} variant={statusVariant[u.status]} />
            </TableCell>
            <TableCell>
              {u.lastAccess ? format(new Date(u.lastAccess), "dd/MM/yyyy HH:mm") : "—"}
            </TableCell>
            <TableCell className="text-right">
              <div className="flex items-center justify-end gap-1">
                <Button variant="ghost" size="sm" onClick={() => navigate(`/usuarios/${u.id}`)}>
                  <Pencil className="h-4 w-4 mr-1" /> Editar
                </Button>
                <Button variant="ghost" size="sm" onClick={() => navigate(`/usuarios/permissoes?role=${u.role}`)}>
                  <Shield className="h-4 w-4 mr-1" /> Permissões
                </Button>
              </div>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
