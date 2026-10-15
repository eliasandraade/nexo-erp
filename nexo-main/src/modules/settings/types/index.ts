/** Which stock alerts the dashboard raises. */
export interface InventorySettings {
  enableLowStockAlerts: boolean;
  enableZeroStockAlerts: boolean;
}

/** PDV discount rules — the limit and the manager authorization are enforced by the server too. */
export interface PosSettings {
  allowValueDiscount: boolean;
  allowPercentDiscount: boolean;
  requireManagerAuth: boolean;
  maxDiscountPercent: number;
}

export interface AppSettings {
  inventory: InventorySettings;
  pos: PosSettings;
}
