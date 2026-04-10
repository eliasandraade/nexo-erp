export interface CompanySettings {
  name: string;
  tradeName: string;
  cnpj: string;
  email: string;
  phone: string;
}

export interface OperationSettings {
  defaultStore: string;
  defaultOperator: string;
}

export interface InventorySettings {
  noMovementAlertDays: number;
  minStockBehavior: "alert" | "block" | "ignore";
  enableLowStockAlerts: boolean;
  enableZeroStockAlerts: boolean;
  enableHighRotationAlerts: boolean;
}

export interface CommissionSettings {
  defaultCommissionRate: number;
  enableProductCommission: boolean;
  policyNotes: string;
}

export interface PosSettings {
  allowValueDiscount: boolean;
  allowPercentDiscount: boolean;
  requireManagerAuth: boolean;
  maxDiscountPercent: number;
}

export interface SystemSettings {
  language: string;
  dateFormat: string;
  currencySymbol: string;
}

export interface AppSettings {
  company: CompanySettings;
  operation: OperationSettings;
  inventory: InventorySettings;
  commission: CommissionSettings;
  pos: PosSettings;
  system: SystemSettings;
}

export const minStockBehaviorLabels: Record<
  InventorySettings["minStockBehavior"],
  string
> = {
  alert: "Alertar (operação permitida)",
  block: "Bloquear (operação negada)",
  ignore: "Ignorar (sem alertas)",
};
