using Nexo.Domain.Common;
using Nexo.Domain.Enums;

namespace Nexo.Domain.Entities;

/// <summary>
/// Who issues the nota fiscal of a store: each branch has its own CNPJ and state registration in
/// Brazil, so the profile is per store (one row per store). It is the data the future emitter
/// needs; nothing here computes a tax. Formats and check digits are validated by the service.
/// </summary>
public class FiscalProfile : StoreEntity
{
    private FiscalProfile() { }                          // EF Core
    private FiscalProfile(Guid tenantId) : base(tenantId) { }

    public string LegalName { get; private set; } = string.Empty;   // razão social
    public string? TradeName { get; private set; }                  // nome fantasia
    public string Cnpj { get; private set; } = string.Empty;        // 14 digits, no mask
    public string StateRegistration { get; private set; } = string.Empty;  // IE (digits) or "ISENTO"
    public string? MunicipalRegistration { get; private set; }      // IM — needed for services (NFS-e)
    public TaxRegime TaxRegime { get; private set; }

    public string Street { get; private set; } = string.Empty;
    public string Number { get; private set; } = string.Empty;
    public string? Complement { get; private set; }
    public string District { get; private set; } = string.Empty;
    public string CityName { get; private set; } = string.Empty;
    public string CityIbgeCode { get; private set; } = string.Empty;   // 7 digits
    public string State { get; private set; } = string.Empty;          // UF, 2 letters
    public string ZipCode { get; private set; } = string.Empty;        // CEP, 8 digits

    /// <summary>
    /// Código de Regime Tributário as the NF-e layout writes it (1 Simples, 2 Simples acima do
    /// sublimite, 3 regime normal, 4 MEI) — a mapping of the layout, not a tax decision.
    /// </summary>
    public int Crt => TaxRegime switch
    {
        TaxRegime.SimplesNacional => 1,
        TaxRegime.SimplesNacionalExcessoSublimite => 2,
        TaxRegime.Mei => 4,
        _ => 3,
    };

    public static FiscalProfile Create(Guid tenantId) => new(tenantId);

    public void Update(
        string legalName, string? tradeName, string cnpj, string stateRegistration, string? municipalRegistration,
        TaxRegime taxRegime, string street, string number, string? complement, string district,
        string cityName, string cityIbgeCode, string state, string zipCode)
    {
        LegalName = legalName.Trim();
        TradeName = Blank(tradeName);
        Cnpj = cnpj;
        StateRegistration = stateRegistration;
        MunicipalRegistration = Blank(municipalRegistration);
        TaxRegime = taxRegime;
        Street = street.Trim();
        Number = number.Trim();
        Complement = Blank(complement);
        District = district.Trim();
        CityName = cityName.Trim();
        CityIbgeCode = cityIbgeCode;
        State = state;
        ZipCode = zipCode;
        SetUpdatedAt();
    }

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
