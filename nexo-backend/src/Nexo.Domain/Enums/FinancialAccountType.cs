namespace Nexo.Domain.Enums;

/// <summary>
/// Operational financial account types.
///
/// Default accounts created per tenant on setup:
///   Cash       — Caixa (physical cash register)
///   Bank       — Banco (bank accounts)
///   Receivable — Contas a Receber (auto-used by credit sales)
///   Payable    — Contas a Pagar
/// </summary>
public enum FinancialAccountType { Cash, Bank, Receivable, Payable }
