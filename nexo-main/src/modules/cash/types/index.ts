export type CashSessionStatus = "open" | "closed";

export type CashMovementType =
  | "opening"
  | "reinforcement"
  | "withdrawal"
  | "sale"
  | "adjustment"
  | "closing";

export const cashMovementTypeLabels: Record<CashMovementType, string> = {
  opening: "Abertura",
  reinforcement: "Suprimento",
  withdrawal: "Sangria",
  sale: "Venda",
  adjustment: "Ajuste",
  closing: "Fechamento",
};

export interface CashSession {
  id: string;
  status: CashSessionStatus;
  operator: string;
  store?: string;
  openedAt: string;
  closedAt?: string;
  openingAmount: number;
  expectedBalance: number;
  countedBalance?: number;
  divergence?: number;
  notes?: string;
}

export interface CashMovement {
  id: string;
  sessionId: string;
  type: CashMovementType;
  amount: number;
  description: string;
  operator: string;
  timestamp: string;
  notes?: string;
  source?: "manual" | "sale" | "opening" | "closing";
  paymentMethod?: "cash" | "pix" | "card";
}

export interface CashOpenInput {
  openingAmount: number;
  operator: string;
  store?: string;
}

export interface CashMovementInput {
  type: "reinforcement" | "withdrawal" | "adjustment";
  amount: number;
  description: string;
  operator?: string;
  notes?: string;
}

export interface CashCloseInput {
  countedAmount: number;
  notes?: string;
}
