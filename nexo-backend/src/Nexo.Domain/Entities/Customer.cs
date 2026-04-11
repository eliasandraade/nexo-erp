using Nexo.Domain.Common;
using Nexo.Domain.Enums;

namespace Nexo.Domain.Entities;

public class Customer : TenantEntity
{
    private Customer() { }
    private Customer(Guid tenantId) : base(tenantId) { }

    public PersonType PersonType { get; private set; }
    public string Name { get; private set; } = string.Empty;       // razão social ou nome completo
    public string? TradeName { get; private set; }                 // nome fantasia
    public DocumentType DocumentType { get; private set; }
    public string DocumentNumber { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? WhatsApp { get; private set; }
    public string? AddressJson { get; private set; }               // {street,number,complement,neighborhood,city,state,zipCode}
    public decimal? CreditLimit { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>
    /// Optional store affiliation. Null = customer is shared across all stores in the tenant.
    /// Set to scope a customer to a specific store (e.g. loyalty program per filial).
    /// </summary>
    public Guid? StoreId { get; private set; }

    // Navigation
    public ICollection<Sale> Sales { get; private set; } = [];

    public static Customer Create(
        Guid tenantId,
        PersonType personType,
        string name,
        DocumentType documentType,
        string documentNumber,
        string? tradeName = null,
        string? email = null,
        string? phone = null,
        string? whatsApp = null,
        string? addressJson = null,
        decimal? creditLimit = null,
        string? notes = null)
    {
        return new Customer(tenantId)
        {
            PersonType     = personType,
            Name           = name.Trim(),
            TradeName      = tradeName?.Trim(),
            DocumentType   = documentType,
            DocumentNumber = documentNumber.Trim(),
            Email          = email?.Trim().ToLowerInvariant(),
            Phone          = phone?.Trim(),
            WhatsApp       = whatsApp?.Trim(),
            AddressJson    = addressJson,
            CreditLimit    = creditLimit,
            Notes          = notes?.Trim(),
            IsActive       = true,
        };
    }

    public void Update(
        string name,
        string? tradeName,
        string? email,
        string? phone,
        string? whatsApp,
        string? addressJson,
        decimal? creditLimit,
        string? notes)
    {
        Name        = name.Trim();
        TradeName   = tradeName?.Trim();
        Email       = email?.Trim().ToLowerInvariant();
        Phone       = phone?.Trim();
        WhatsApp    = whatsApp?.Trim();
        AddressJson = addressJson;
        CreditLimit = creditLimit;
        Notes       = notes?.Trim();
        SetUpdatedAt();
    }

    public void Deactivate() { IsActive = false; SetUpdatedAt(); }
    public void Activate()   { IsActive = true;  SetUpdatedAt(); }
}
