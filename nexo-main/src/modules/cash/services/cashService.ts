import type {
  CashSession,
  CashMovement,
  CashOpenInput,
  CashMovementInput,
  CashCloseInput,
} from "../types";
import { mockCashSessions, mockCashMovements } from "../data/mockCash";
import { auditService } from "@/modules/audit/services/auditService";

const sessions: CashSession[] = [...mockCashSessions];
const movements: CashMovement[] = [...mockCashMovements];

let currentSession: CashSession | null = null;

/**
 * Calculates the physical cash balance in the drawer.
 * Only movements that affect actual cash are counted:
 * - opening, reinforcement, withdrawal, adjustment → always counted
 * - sale with paymentMethod "cash" → counted (cash entered the drawer)
 * - sale with paymentMethod "pix" or "card" → NOT counted (no physical cash)
 */
function recalcExpectedBalance(session: CashSession): number {
  const sessionMovements = movements.filter((m) => m.sessionId === session.id);
  return sessionMovements.reduce((acc, m) => {
    if (m.type === "sale" && m.paymentMethod !== "cash") return acc;
    return acc + m.amount;
  }, 0);
}

const delay = (ms = 300) => new Promise((r) => setTimeout(r, ms));

export const cashService = {
  async getCurrentSession(): Promise<CashSession | null> {
    await delay(200);
    return currentSession ? { ...currentSession } : null;
  },

  async openSession(input: CashOpenInput): Promise<CashSession> {
    await delay(500);
    if (currentSession) throw new Error("Já existe uma sessão de caixa aberta.");

    const now = new Date().toISOString();
    const session: CashSession = {
      id: `session-${Date.now()}`,
      status: "open",
      operator: input.operator,
      store: input.store,
      openedAt: now,
      openingAmount: input.openingAmount,
      expectedBalance: input.openingAmount,
    };

    const openingMovement: CashMovement = {
      id: `mov-${Date.now()}`,
      sessionId: session.id,
      type: "opening",
      amount: input.openingAmount,
      description: "Abertura de caixa",
      operator: input.operator,
      timestamp: now,
      source: "opening",
    };

    sessions.push(session);
    movements.push(openingMovement);
    currentSession = session;

    auditService.addAuditRecord({
      actionType: "cash_open",
      severity: "info",
      actor: input.operator,
      entityType: "cash_session",
      entityId: session.id,
      description: `Sessão de caixa aberta por ${input.operator}. Valor de abertura: R$ ${input.openingAmount.toFixed(2)}.`,
    });

    return { ...session };
  },

  async addMovement(input: CashMovementInput): Promise<CashMovement> {
    await delay(400);
    if (!currentSession) throw new Error("Nenhuma sessão de caixa aberta.");

    const signedAmount =
      input.type === "withdrawal" ? -Math.abs(input.amount) : Math.abs(input.amount);

    const movement: CashMovement = {
      id: `mov-${Date.now()}`,
      sessionId: currentSession.id,
      type: input.type,
      amount: signedAmount,
      description: input.description,
      operator: input.operator ?? currentSession.operator,
      timestamp: new Date().toISOString(),
      notes: input.notes,
      source: "manual",
    };

    movements.push(movement);
    currentSession.expectedBalance = recalcExpectedBalance(currentSession);

    // Update in sessions array
    const idx = sessions.findIndex((s) => s.id === currentSession!.id);
    if (idx !== -1) sessions[idx] = { ...currentSession };

    const typeLabel = input.type === "withdrawal" ? "Sangria" : input.type === "reinforcement" ? "Suprimento" : "Ajuste";
    auditService.addAuditRecord({
      actionType: "cash_movement",
      severity: input.type === "withdrawal" ? "warning" : "info",
      actor: movement.operator,
      entityType: "cash_movement",
      entityId: movement.id,
      description: `${typeLabel} de caixa: R$ ${Math.abs(signedAmount).toFixed(2)} — ${input.description}. Sessão: ${currentSession.id}.`,
    });

    return movement;
  },

  /**
   * Records a cash reversal for a sale cancellation.
   * Only the cash portion of the original payment affects the physical drawer —
   * PIX and card reversals do not produce cash outflow.
   *
   * Safe to call with cashRefundAmount = 0 (no-op). Does nothing if no session
   * is open, since historical cancellations have no active session to update.
   */
  addCancellationMovement(cashRefundAmount: number, description: string): void {
    if (!currentSession) return;
    if (cashRefundAmount <= 0) return;

    const movement: CashMovement = {
      id: `mov-${Date.now()}-cancel`,
      sessionId: currentSession.id,
      type: "withdrawal",
      amount: -Math.abs(cashRefundAmount),
      description,
      operator: currentSession.operator,
      timestamp: new Date().toISOString(),
      source: "manual",
    };

    movements.push(movement);
    currentSession.expectedBalance = recalcExpectedBalance(currentSession);

    const idx = sessions.findIndex((s) => s.id === currentSession!.id);
    if (idx !== -1) sessions[idx] = { ...currentSession };

    auditService.addAuditRecord({
      actionType: "cash_movement",
      severity: "warning",
      actor: currentSession.operator,
      entityType: "cash_movement",
      entityId: movement.id,
      description: `Estorno de caixa: -R$ ${cashRefundAmount.toFixed(2)} — ${description}. Sessão: ${currentSession.id}.`,
    });
  },

  addSaleMovement(
    amount: number,
    description: string,
    paymentMethod?: "cash" | "pix" | "card"
  ): void {
    if (!currentSession) throw new Error("Nenhuma sessão de caixa aberta.");

    const movement: CashMovement = {
      id: `mov-${Date.now()}-sale`,
      sessionId: currentSession.id,
      type: "sale",
      amount: Math.abs(amount),
      description,
      operator: currentSession.operator,
      timestamp: new Date().toISOString(),
      source: "sale",
      paymentMethod,
    };

    movements.push(movement);
    currentSession.expectedBalance = recalcExpectedBalance(currentSession);

    const idx = sessions.findIndex((s) => s.id === currentSession!.id);
    if (idx !== -1) sessions[idx] = { ...currentSession };

    const methodLabel = paymentMethod === "cash" ? "Dinheiro" : paymentMethod === "pix" ? "PIX" : paymentMethod === "card" ? "Cartão" : "Misto";
    auditService.addAuditRecord({
      actionType: "cash_movement",
      severity: "info",
      actor: currentSession.operator,
      entityType: "cash_movement",
      entityId: movement.id,
      description: `Venda registrada no caixa: R$ ${Math.abs(amount).toFixed(2)} — ${description}. Método: ${methodLabel}. Sessão: ${currentSession.id}.`,
      metadata: { paymentMethod },
    });
  },

  async closeSession(input: CashCloseInput): Promise<CashSession> {
    await delay(500);
    if (!currentSession) throw new Error("Nenhuma sessão de caixa aberta.");

    const now = new Date().toISOString();

    // 1. Freeze the expected balance as it stands before the closing record.
    //    This is the authoritative figure for divergence calculation.
    const frozenExpectedBalance = currentSession.expectedBalance;

    // 2. Calculate divergence: positive = operator has extra cash, negative = shortage.
    const divergence = input.countedAmount - frozenExpectedBalance;

    // 3. Build the closed session snapshot.
    const closed: CashSession = {
      ...currentSession,
      status: "closed",
      closedAt: now,
      expectedBalance: frozenExpectedBalance,
      countedBalance: input.countedAmount,
      divergence,
      notes: input.notes,
    };

    // 4. Record the closing movement (amount = 0, purely for audit trail).
    const closingMovement: CashMovement = {
      id: `mov-${Date.now()}`,
      sessionId: currentSession.id,
      type: "closing",
      amount: 0,
      description: "Fechamento de caixa",
      operator: currentSession.operator,
      timestamp: now,
      source: "closing",
    };
    movements.push(closingMovement);

    // 5. Persist the closed session and clear the active session.
    const closingOperator = currentSession.operator;
    const closingSessionId = currentSession.id;
    const idx = sessions.findIndex((s) => s.id === currentSession!.id);
    if (idx !== -1) sessions[idx] = closed;
    currentSession = null;

    auditService.addAuditRecord({
      actionType: "cash_close",
      severity: Math.abs(divergence) > 0.01 ? "warning" : "info",
      actor: closingOperator,
      entityType: "cash_session",
      entityId: closingSessionId,
      description: `Sessão ${closingSessionId} fechada. Esperado: R$ ${frozenExpectedBalance.toFixed(2)}. Contado: R$ ${input.countedAmount.toFixed(2)}. Divergência: R$ ${divergence.toFixed(2)}.`,
      metadata: { divergence, frozenExpectedBalance, countedAmount: input.countedAmount },
    });

    return { ...closed };
  },

  async listMovements(sessionId?: string): Promise<CashMovement[]> {
    await delay(200);
    const targetId = sessionId ?? currentSession?.id;
    if (!targetId) return [];
    return movements
      .filter((m) => m.sessionId === targetId)
      .sort((a, b) => new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime());
  },

  async getSessionHistory(): Promise<CashSession[]> {
    await delay(200);
    return [...sessions]
      .filter((s) => s.status === "closed")
      .sort((a, b) => new Date(b.openedAt).getTime() - new Date(a.openedAt).getTime());
  },
};
