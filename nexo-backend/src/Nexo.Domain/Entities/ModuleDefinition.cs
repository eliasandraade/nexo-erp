using Nexo.Domain.Common;

namespace Nexo.Domain.Entities;

/// <summary>
/// Catalog entry for a Nexo ERP module.
/// Stores metadata, Stripe product/price references, and reference pricing.
/// is_published = false means the module exists in code but is not yet for sale.
/// </summary>
public class ModuleDefinition : BaseEntity
{
    private ModuleDefinition() { } // EF Core constructor

    /// <summary>Unique kebab-case identifier. Examples: "varejo", "restaurante", "academia-musculacao"</summary>
    public string Key { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Version { get; private set; } = "1.0.0";
    public bool IsPublished { get; private set; }

    // Stripe references (null until configured in Stripe Dashboard)
    public string? StripeProductId { get; private set; }
    public string? StripePriceMonthly { get; private set; }
    public string? StripePriceQuarterly { get; private set; }
    public string? StripePriceSemiannual { get; private set; }
    public string? StripePriceAnnual { get; private set; }
    public string? StripePriceLifetime { get; private set; }

    // Reference prices (BRL)
    public decimal? PriceMonthly { get; private set; }
    public decimal? PriceQuarterly { get; private set; }
    public decimal? PriceSemiannual { get; private set; }
    public decimal? PriceAnnual { get; private set; }
    public decimal? PriceLifetime { get; private set; }

    public static ModuleDefinition Create(
        string key,
        string name,
        string? description = null,
        decimal? priceMonthly = null,
        decimal? priceAnnual = null,
        decimal? priceLifetime = null)
    {
        return new ModuleDefinition
        {
            Key = key.Trim().ToLowerInvariant(),
            Name = name.Trim(),
            Description = description?.Trim(),
            IsPublished = false,
            PriceMonthly = priceMonthly,
            PriceAnnual = priceAnnual,
            PriceLifetime = priceLifetime,
        };
    }

    /// <summary>
    /// Preço de tabela em valor mensal para um tipo de plano: mensal; trimestral / 3; semestral / 6; anual / 12. Vitalício,
    /// avaliação e cortesia valem zero. É a base do MRR estimado (painel da plataforma e contrato da Orken), nunca fatura.
    /// </summary>
    public decimal MonthlyReferencePrice(Nexo.Domain.Enums.PlanType planType) => planType switch
    {
        Nexo.Domain.Enums.PlanType.Monthly    => PriceMonthly ?? 0m,
        Nexo.Domain.Enums.PlanType.Quarterly  => (PriceQuarterly ?? 0m) / 3m,
        Nexo.Domain.Enums.PlanType.Semiannual => (PriceSemiannual ?? 0m) / 6m,
        Nexo.Domain.Enums.PlanType.Annual     => (PriceAnnual ?? 0m) / 12m,
        _                                     => 0m,
    };

    public void Publish() { IsPublished = true; SetUpdatedAt(); }
    public void Unpublish() { IsPublished = false; SetUpdatedAt(); }

    public void SetStripePrices(
        string productId,
        string? monthly,
        string? quarterly,
        string? semiannual,
        string? annual,
        string? lifetime)
    {
        StripeProductId = productId;
        StripePriceMonthly = monthly;
        StripePriceQuarterly = quarterly;
        StripePriceSemiannual = semiannual;
        StripePriceAnnual = annual;
        StripePriceLifetime = lifetime;
        SetUpdatedAt();
    }
}
