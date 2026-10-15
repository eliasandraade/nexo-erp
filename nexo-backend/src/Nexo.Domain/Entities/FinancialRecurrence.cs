using Nexo.Domain.Common;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Entities;

/// <summary>
/// Lançamento recorrente (aluguel, internet, mensalidade…): a model that becomes one título per month,
/// due on <see cref="DayOfMonth"/> (the month's last day when shorter). Títulos are generated ahead —
/// through the end of next month — and <see cref="GeneratedThrough"/> keeps generation idempotent.
/// Each generated título is an ordinary one: paid, renegotiated or cancelled on its own.
/// </summary>
public class FinancialRecurrence : TenantEntity
{
    private FinancialRecurrence() { }
    private FinancialRecurrence(Guid tenantId) : base(tenantId) { }

    public TransactionType TransactionType { get; private set; }
    public FinancialCategory Category { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public int DayOfMonth { get; private set; }
    /// <summary>First month generated (always the 1st of the month).</summary>
    public DateOnly StartMonth { get; private set; }
    /// <summary>Last month generated, inclusive (1st of the month); null = no end.</summary>
    public DateOnly? EndMonth { get; private set; }
    public DateOnly? GeneratedThrough { get; private set; }
    public bool IsActive { get; private set; }
    public Guid? SupplierId { get; private set; }
    public Guid? CustomerId { get; private set; }
    public Guid? StoreId { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    public static FinancialRecurrence Create(
        Guid tenantId, TransactionType type, FinancialCategory category, string description, decimal amount,
        int dayOfMonth, DateOnly startMonth, DateOnly? endMonth, Guid createdByUserId,
        Guid? supplierId = null, Guid? customerId = null, Guid? storeId = null)
    {
        var r = new FinancialRecurrence(tenantId)
        {
            TransactionType = type,
            Category        = category,
            StartMonth      = FirstOf(startMonth),
            IsActive        = true,
            CreatedByUserId = createdByUserId,
            SupplierId      = supplierId,
            CustomerId      = customerId,
            StoreId         = storeId,
        };
        r.Apply(description, amount, dayOfMonth, endMonth);
        return r;
    }

    public void Update(string description, decimal amount, int dayOfMonth, DateOnly? endMonth)
    {
        if (!IsActive)
            throw new DomainException("Recorrência encerrada não se altera: crie uma nova.");
        if (endMonth is { } end && GeneratedThrough is { } through && FirstOf(end) < through)
            throw new DomainException(
                $"Os títulos até {through:MM/yyyy} já foram gerados: escolha um fim a partir desse mês ou cancele os títulos que sobrarem.");
        Apply(description, amount, dayOfMonth, endMonth);
        SetUpdatedAt();
    }

    public void Deactivate() { IsActive = false; SetUpdatedAt(); }

    /// <summary>Longest description: the generated títulos append " — MM/yyyy" and hold 500 characters.</summary>
    public const int MaxDescriptionLength = 480;

    /// <summary>Most months one generation may create (a safety net; the start is validated too).</summary>
    public const int MaxMonthsPerGeneration = 24;

    /// <summary>Months still to generate through <paramref name="horizonMonth"/> (inclusive), at most 24.</summary>
    public IReadOnlyList<DateOnly> PendingMonths(DateOnly horizonMonth)
    {
        if (!IsActive) return [];
        var last = FirstOf(horizonMonth);
        if (EndMonth is { } end && end < last) last = end;
        var months = new List<DateOnly>();
        for (var m = GeneratedThrough?.AddMonths(1) ?? StartMonth; m <= last && months.Count < MaxMonthsPerGeneration; m = m.AddMonths(1))
            months.Add(m);
        return months;
    }

    /// <summary>The due day inside a month: <see cref="DayOfMonth"/>, or the month's last day when shorter.</summary>
    public DateOnly DueDayIn(DateOnly month)
        => new(month.Year, month.Month, Math.Min(DayOfMonth, DateTime.DaysInMonth(month.Year, month.Month)));

    public void MarkGenerated(DateOnly month)
    {
        GeneratedThrough = FirstOf(month);
        SetUpdatedAt();
    }

    private void Apply(string description, decimal amount, int dayOfMonth, DateOnly? endMonth)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("A recorrência precisa de uma descrição.");
        if (description.Trim().Length > MaxDescriptionLength)
            throw new DomainException($"Descrição da recorrência com até {MaxDescriptionLength} caracteres.");
        var cents = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (cents <= 0 || cents != amount)
            throw new DomainException("O valor precisa ser maior que zero e em centavos.");
        if (dayOfMonth is < 1 or > 31)
            throw new DomainException("Dia do vencimento entre 1 e 31.");
        if (endMonth is { } end && FirstOf(end) < StartMonth)
            throw new DomainException("O fim da recorrência não pode ser antes do início.");
        Description = description.Trim();
        Amount      = cents;
        DayOfMonth  = dayOfMonth;
        EndMonth    = endMonth is { } e ? FirstOf(e) : null;
    }

    private static DateOnly FirstOf(DateOnly d) => new(d.Year, d.Month, 1);
}
