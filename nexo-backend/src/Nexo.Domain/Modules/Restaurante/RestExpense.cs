using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Despesa operacional do restaurante (energia, gás, água, aluguel, etc.).
/// CompetenceDate é o mês de competência — usado para filtrar no resumo financeiro.
/// </summary>
public class RestExpense : StoreEntity
{
    private RestExpense() { }
    private RestExpense(Guid tenantId) : base(tenantId) { }

    public string    Description    { get; private set; } = string.Empty;
    public string    Category       { get; private set; } = string.Empty;
    public decimal   Amount         { get; private set; }
    public DateOnly  CompetenceDate { get; private set; }
    public DateOnly? PaymentDate    { get; private set; }
    public bool      IsRecurring    { get; private set; }

    public static RestExpense Create(
        Guid tenantId, string description, string category,
        decimal amount, DateOnly competenceDate, DateOnly? paymentDate, bool isRecurring)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("Expense description is required.");
        if (amount < 0)
            throw new DomainException("Expense amount cannot be negative.");

        return new RestExpense(tenantId)
        {
            Description    = description.Trim(),
            Category       = category.Trim(),
            Amount         = amount,
            CompetenceDate = competenceDate,
            PaymentDate    = paymentDate,
            IsRecurring    = isRecurring,
        };
    }

    public void Update(string description, string category, decimal amount,
        DateOnly competenceDate, DateOnly? paymentDate, bool isRecurring)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("Expense description is required.");
        if (amount < 0)
            throw new DomainException("Expense amount cannot be negative.");

        Description    = description.Trim();
        Category       = category.Trim();
        Amount         = amount;
        CompetenceDate = competenceDate;
        PaymentDate    = paymentDate;
        IsRecurring    = isRecurring;
        SetUpdatedAt();
    }
}
