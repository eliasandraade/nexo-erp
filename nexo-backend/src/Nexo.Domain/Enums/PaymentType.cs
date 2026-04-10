namespace Nexo.Domain.Enums;

/// <summary>
/// Defines WHEN money enters/leaves the business — independent of HOW it is paid.
///
/// PaymentMethod = "como paga" (Cash, Pix, Credit card…)
/// PaymentType   = "quando entra o dinheiro" (now vs future)
/// </summary>
public enum PaymentType
{
    Cash   = 1,  // à vista — generates CashMovement immediately
    Credit = 2,  // a prazo — generates FinancialTransaction (Receivable)
}
