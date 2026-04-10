export type AuditActionType =
  | "stock_adjustment"
  | "stock_transfer"
  | "cash_open"
  | "cash_movement"
  | "cash_close"
  | "sale_completed"
  | "sale_cancelled"
  | "sale_item_cancelled"
  | "manager_authorization"
  | "user_created"
  | "user_updated";

export type AuditSeverity = "info" | "warning" | "critical";

export const auditActionTypeLabels: Record<AuditActionType, string> = {
  stock_adjustment: "Ajuste de estoque",
  stock_transfer: "Transferência de estoque",
  cash_open: "Abertura de caixa",
  cash_movement: "Movimentação de caixa",
  cash_close: "Fechamento de caixa",
  sale_completed: "Venda concluída",
  sale_cancelled: "Venda cancelada",
  sale_item_cancelled: "Item cancelado",
  manager_authorization: "Autorização gerencial",
  user_created: "Usuário criado",
  user_updated: "Usuário atualizado",
};

export const auditSeverityLabels: Record<AuditSeverity, string> = {
  info: "Info",
  warning: "Atenção",
  critical: "Crítico",
};

export interface AuditRecord {
  id: string;
  timestamp: string;
  actionType: AuditActionType;
  severity: AuditSeverity;
  /** The user/operator who performed the action */
  actor: string;
  entityType: string;
  entityId: string;
  description: string;
  metadata?: Record<string, unknown>;
}

export interface AuditFilters {
  actionType: AuditActionType | "all";
  severity: AuditSeverity | "all";
  actor: string;  // "all" or a name substring
}
