using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// A catalog item — serviço / procedimento / aula (label varies by preset). Store-scoped.
///
/// <see cref="Category"/> is free-text in v1 (no relational category table yet).
/// <see cref="RequiresSubject"/> flags items that need a SvcSubject (pet/veículo/aluno) — the
/// subject entity itself arrives in a later PR; here it is just a configuration flag.
/// </summary>
public class SvcCatalogItem : StoreEntity
{
    private SvcCatalogItem() { }                                    // EF Core
    private SvcCatalogItem(Guid tenantId) : base(tenantId) { }

    public string   Name              { get; private set; } = string.Empty;
    public string?  Description       { get; private set; }
    public string?  Category          { get; private set; }
    public int      DurationMinutes   { get; private set; }
    public decimal  Price             { get; private set; }
    public decimal? CommissionPercent { get; private set; }
    public bool     RequiresSubject   { get; private set; }
    public bool     IsActive          { get; private set; }

    public static SvcCatalogItem Create(
        Guid     tenantId,
        string   name,
        int      durationMinutes,
        decimal  price,
        string?  description       = null,
        string?  category          = null,
        decimal? commissionPercent = null,
        bool     requiresSubject   = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Catalog item name is required.");
        EnsureDurationPositive(durationMinutes);
        EnsurePriceNonNegative(price);
        EnsureCommissionInRange(commissionPercent);

        return new SvcCatalogItem(tenantId)
        {
            Name              = name.Trim(),
            Description       = description?.Trim(),
            Category          = category?.Trim(),
            DurationMinutes   = durationMinutes,
            Price             = price,
            CommissionPercent = commissionPercent,
            RequiresSubject   = requiresSubject,
            IsActive          = true,
        };
    }

    public void UpdateDetails(
        string  name,
        string? description,
        string? category,
        int     durationMinutes,
        bool    requiresSubject)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Catalog item name is required.");
        EnsureDurationPositive(durationMinutes);

        Name            = name.Trim();
        Description     = description?.Trim();
        Category        = category?.Trim();
        DurationMinutes = durationMinutes;
        RequiresSubject = requiresSubject;
        SetUpdatedAt();
    }

    public void UpdatePrice(decimal price)
    {
        EnsurePriceNonNegative(price);
        Price = price;
        SetUpdatedAt();
    }

    public void UpdateCommission(decimal? commissionPercent)
    {
        EnsureCommissionInRange(commissionPercent);
        CommissionPercent = commissionPercent;
        SetUpdatedAt();
    }

    public void Activate()   { IsActive = true;  SetUpdatedAt(); }
    public void Deactivate() { IsActive = false; SetUpdatedAt(); }

    private static void EnsureDurationPositive(int minutes)
    {
        if (minutes <= 0)
            throw new DomainException("Duration must be greater than zero minutes.");
    }

    private static void EnsurePriceNonNegative(decimal price)
    {
        if (price < 0m)
            throw new DomainException("Price cannot be negative.");
    }

    private static void EnsureCommissionInRange(decimal? pct)
    {
        if (pct is < 0m or > 100m)
            throw new DomainException("Commission percent must be between 0 and 100.");
    }
}
