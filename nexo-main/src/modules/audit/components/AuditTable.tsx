import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import type { AuditRecord } from "../types";
import { AuditSeverityBadge } from "./AuditSeverityBadge";
import { AuditActionBadge } from "./AuditActionBadge";
import { formatDateTime } from "@/lib/formatters";

interface AuditTableProps {
  records: AuditRecord[];
}

export function AuditTable({ records }: AuditTableProps) {
  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead className="whitespace-nowrap">Data / Hora</TableHead>
          <TableHead>Ação</TableHead>
          <TableHead>Usuário</TableHead>
          <TableHead>Entidade</TableHead>
          <TableHead>Descrição</TableHead>
          <TableHead>Severidade</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {records.map((record) => (
          <TableRow key={record.id}>
            <TableCell className="text-xs text-muted-foreground tabular-nums whitespace-nowrap">
              {formatDateTime(record.timestamp)}
            </TableCell>
            <TableCell>
              <AuditActionBadge actionType={record.actionType} />
            </TableCell>
            <TableCell className="text-sm font-medium">{record.actor}</TableCell>
            <TableCell className="text-xs text-muted-foreground">
              <div>
                <span className="font-medium text-foreground">{record.entityType}</span>
                <span className="ml-1 font-mono">#{record.entityId}</span>
              </div>
            </TableCell>
            <TableCell className="text-sm text-muted-foreground max-w-xs truncate">
              {record.description}
            </TableCell>
            <TableCell>
              <AuditSeverityBadge severity={record.severity} />
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
