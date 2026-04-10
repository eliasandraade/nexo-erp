using Nexo.Domain.Common;
using Nexo.Domain.Enums;

namespace Nexo.Domain.Entities;

/// <summary>
/// Represents a company/organization (tenant) in the Nexo ERP platform.
/// Each tenant is an isolated business unit with its own users, data, and module subscriptions.
/// Replaces the previous Store entity.
/// </summary>
public class Tenant : BaseEntity
{
    private Tenant() { } // EF Core constructor

    public string Slug { get; private set; } = string.Empty;
    public string CompanyName { get; private set; } = string.Empty;
    public string? TradeName { get; private set; }
    public string TaxId { get; private set; } = string.Empty;       // CNPJ/CPF
    public string Email { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public string? BusinessType { get; private set; }               // 'varejo', 'restaurante', etc.
    public string? StripeCustomerId { get; private set; }
    public TenantStatus Status { get; private set; }
    public DateTime? TrialEndsAt { get; private set; }

    public static Tenant Create(
        string companyName,
        string taxId,
        string email,
        string? tradeName = null,
        string? phone = null,
        string? businessType = null)
    {
        return new Tenant
        {
            Id = Guid.NewGuid(),
            Slug = GenerateSlug(companyName),
            CompanyName = companyName.Trim(),
            TradeName = tradeName?.Trim(),
            TaxId = taxId.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            Phone = phone?.Trim(),
            BusinessType = businessType?.Trim().ToLowerInvariant(),
            Status = TenantStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    public void Update(
        string companyName,
        string? tradeName,
        string taxId,
        string email,
        string? phone,
        string? businessType)
    {
        CompanyName = companyName.Trim();
        TradeName = tradeName?.Trim();
        TaxId = taxId.Trim();
        Email = email.Trim().ToLowerInvariant();
        Phone = phone?.Trim();
        BusinessType = businessType?.Trim().ToLowerInvariant();
        SetUpdatedAt();
    }

    public void SetStatus(TenantStatus status)
    {
        Status = status;
        SetUpdatedAt();
    }

    public void SetStripeCustomerId(string stripeCustomerId)
    {
        StripeCustomerId = stripeCustomerId;
        SetUpdatedAt();
    }

    public void SetTrialEnd(DateTime trialEndsAt)
    {
        TrialEndsAt = trialEndsAt;
        SetUpdatedAt();
    }

    private static string GenerateSlug(string companyName)
    {
        var slug = companyName
            .Trim()
            .ToLowerInvariant()
            .Replace(" ", "-")
            .Replace(".", "")
            .Replace("/", "")
            .Replace("&", "e");

        // Ensure uniqueness by appending short GUID segment
        return $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";
    }
}
