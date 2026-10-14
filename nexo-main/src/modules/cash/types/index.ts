// ── Backend DTO types ──────────────────────────────────────────────────────────

export type CashMovementType =
  | "Opening"
  | "SaleReceipt"
  | "Withdrawal"
  | "Deposit"
  | "Closing"
  | "SaleRefund";

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
  /** Frozen at closing (closed sessions). */
  expectedBalance: number | null;
  /** Counted − expected: positive = sobra, negative = falta. */
  difference: number | null;
  closingNotes: string | null;
  /** Drawer totals computed by the backend (open sessions: live). */
  summary: CashSessionSummaryDto | null;
}

export interface CashPaymentTotalDto {
  method: string;
  type: string;
  count: number;
  amount: number;
}

export interface CashSessionSummaryDto {
  openingBalance: number;
  cashSales: number;
  deposits: number;
  withdrawals: number;
  refunds: number;
  expectedBalance: number;
  receiptsByMethod: CashPaymentTotalDto[];
}

// ── Request types ──────────────────────────────────────────────────────────────

export interface OpenCashSessionRequest {
  openingBalance: number;
  notes?: string;
}

export interface CloseCashSessionRequest {
  /** What was counted in the drawer. */
  closingBalance: number;
  /** Explains a difference — kept with the closing. */
  notes?: string;
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
  SaleRefund:  "Estorno de venda",
};

export const PAYMENT_METHOD_LABEL: Record<string, string> = {
  Cash:     "Dinheiro",
  Pix:      "Pix",
  Debit:    "Cartão de débito",
  Credit:   "Cartão de crédito",
  Transfer: "Transferência",
  Check:    "Cheque",
  Other:    "Outro",
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
  SaleRefund:  "destructive",
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
    if (m.movementType === "Withdrawal" || m.movementType === "SaleRefund") {
      return acc - m.amount;
    }
    return acc;
  }, openingBalance);
}
