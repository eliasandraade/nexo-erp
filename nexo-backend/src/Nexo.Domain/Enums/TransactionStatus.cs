namespace Nexo.Domain.Enums;

/// <summary>
/// Abated: the part of a título extinguished by a desconto in a baixa — no money moved. It counts on
/// competência (the fact happened) and never on caixa; the matching desconto lançamento is abated too.
/// </summary>
public enum TransactionStatus { Pending, Paid, Overdue, Cancelled, Abated }
