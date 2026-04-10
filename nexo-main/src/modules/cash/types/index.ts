// ── Backend DTO types ──────────────────────────────────────────────────────────

export type CashMovementType =
  | "Opening"
  | "SaleReceipt"
  | "Withdrawal"
  | "Deposit"
  | "Closing";

export type CashSessionStatus = "Open" | "Closed";

export interface CashMovementDto {
  id: string;
  movementType: CashMovementType;
  amount: number;          // always positive; type defines direction
  description: string;
  referenceType: string | null;
  referenceId: string | null;
  createdByUserId: string;
  createdAt: string;
}

export interface CashSessionDto {
  id: string;
  status: CashSessionStatus;
  openedByUserId: string;
  openedByName: string;
  closedByUserId: string | null;
  closedByName: string | null;
  openingBalance: number;
  closingBalance: number | null;
  openedAt: string;
  closedAt: string | null;
  notes: string | null;
  movements: CashMovementDto[] | null;
}

// ── Request types ──────────────────────────────────────────────────────────────

export interface OpenCashSessionRequest {
  openingBalance: number;
  notes?: string;
}

export interface CloseCashSessionRequest {
  closingBalance: number;
}

export interface AddCashMovementRequest {
  movementType: "Deposit" | "Withdrawal";
  amount: number;
  description: string;
}

// ── Labels & variants ──────────────────────────────────────────────────────────

export const MOVEMENT_TYPE_LABEL: Record<CashMovementType, string> = {
  Opening:     "Abertura",
  SaleReceipt: "Venda",
  Withdrawal:  "Sangria",
  Deposit:     "Suprimento",
  Closing:     "Fechamento",
};

export const MOVEMENT_TYPE_VARIANT: Record<
  CashMovementType,
  "default" | "secondary" | "destructive" | "outline"
> = {
  Opening:     "secondary",
  SaleReceipt: "default",
  Withdrawal:  "destructive",
  Deposit:     "default",
  Closing:     "secondary",
};

// ── Helpers ────────────────────────────────────────────────────────────────────

/** Derives the expected physical cash balance from session data + movements. */
export function deriveExpectedBalance(
  openingBalance: number,
  movements: CashMovementDto[]
): number {
  return movements.reduce((acc, m) => {
    if (m.movementType === "Deposit" || m.movementType === "SaleReceipt") {
      return acc + m.amount;
    }
    if (m.movementType === "Withdrawal") {
      return acc - m.amount;
    }
    return acc;
  }, openingBalance);
}
