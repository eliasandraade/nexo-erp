using Nexo.Domain.Common;
using Nexo.Domain.Enums;

namespace Nexo.Domain.Entities;

public class Supplier : TenantEntity
{
    private Supplier() { }
    private Supplier(Guid tenantId) : base(tenantId) { }

    public PersonType PersonType { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? TradeName { get; private set; }
    public DocumentType DocumentType { get; private set; }
    public string DocumentNumber { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? ContactName { get; private set; }
    public string? AddressJson { get; private set; }
    public int? PaymentTermsDays { get; private set; }             // prazo padrão de pagamento
    public string? BankInfoJson { get; private set; }              // {bank,agency,account,pixKey}
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; }

    public static Supplier Create(
        Guid tenantId,
        PersonType personType,
        string name,
        DocumentType documentType,
        string documentNumber,
        string? tradeName = null,
        string? email = null,
        string? phone = null,
        string? contactName = null,
        string? addressJson = null,
        int? paymentTermsDays = null,
        string? bankInfoJson = null,
        string? notes = null)
    {
        return new Supplier(tenantId)
        {
            PersonType       = personType,
            Name             = name.Trim(),
            TradeName        = tradeName?.Trim(),
            DocumentType     = documentType,
            DocumentNumber   = documentNumber.Trim(),
            Email            = email?.Trim().ToLowerInvariant(),
            Phone            = phone?.Trim(),
            ContactName      = contactName?.Trim(),
            AddressJson      = addressJson,
            PaymentTermsDays = paymentTermsDays,
            BankInfoJson     = bankInfoJson,
            Notes            = notes?.Trim(),
            IsActive         = true,
        };
    }

    public void Update(
        string name,
        string? tradeName,
        string? email,
        string? phone,
        string? contactName,
        string? addressJson,
        int? paymentTermsDays,
        string? bankInfoJson,
        string? notes)
    {
        Name             = name.Trim();
        TradeName        = tradeName?.Trim();
        Email            = email?.Trim().ToLowerInvariant();
        Phone            = phone?.Trim();
        ContactName      = contactName?.Trim();
        AddressJson      = addressJson;
        PaymentTermsDays = paymentTermsDays;
        BankInfoJson     = bankInfoJson;
        Notes            = notes?.Trim();
        SetUpdatedAt();
    }

    public void Deactivate() { IsActive = false; SetUpdatedAt(); }
    public void Activate()   { IsActive = true;  SetUpdatedAt(); }
}
