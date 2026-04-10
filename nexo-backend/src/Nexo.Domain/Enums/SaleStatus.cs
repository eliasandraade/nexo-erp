namespace Nexo.Domain.Enums;

/// <summary>
/// Official CORE sale state machine.
///
/// Allowed transitions:
///   Draft      → Confirmed  (ConfirmAsync — freezes items, deducts stock, generates cash/financial)
///   Confirmed  → Paid       (MarkPaidAsync — all credit amounts later settled)
///   Draft      → Cancelled  (CancelAsync — no stock/cash/financial side-effects)
///   Confirmed  → Cancelled  (CancelAsync — reverses stock + cash/financial)
///
/// Forbidden:
///   Paid      → any state
///   Cancelled → any state
///   Draft     → Paid directly (must go through Confirmed first)
/// </summary>
public enum SaleStatus { Draft, Confirmed, Paid, Cancelled }
