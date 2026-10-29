using Nexo.Domain.Common;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.TaxIntelligence;

/// <summary>
/// The tax profile of an establishment (store) — one per store. The identity data of the issuer
/// (razão social, CNPJ, address) stays in <see cref="Entities.FiscalProfile"/>; this aggregate only
/// keeps, versioned in time, the facts that change how taxes are computed.
/// </summary>
public class TaxProfile : StoreEntity
{
    private readonly List<TaxProfileVersion> _versions = [];

    private TaxProfile() { }   // EF Core
    private TaxProfile(Guid tenantId) : base(tenantId) { }

    public IReadOnlyCollection<TaxProfileVersion> Versions => _versions.AsReadOnly();

    public static TaxProfile Create(Guid tenantId) => new(tenantId);

    /// <summary>
    /// Adds a version. Nothing is ever edited or removed: a change of regime from a date is a new
    /// version from that date; a correction of the same date is a new version with a higher number,
    /// which wins for calculations made after it. Past calculations keep pointing at the version
    /// they used.
    /// </summary>
    public TaxProfileVersion AddVersion(
        DateOnly validFrom, TaxRegime regime, string state, string cityIbgeCode, string primaryCnae,
        IReadOnlyList<string> secondaryCnaes, IReadOnlyList<string> specialRegimes, IReadOnlyList<string> benefits,
        string reason, Guid createdById, string createdByName)
    {
        var number = _versions.Count == 0 ? 1 : _versions.Max(v => v.VersionNumber) + 1;
        var version = new TaxProfileVersion(
            TenantId, Id, number, validFrom, regime, state, cityIbgeCode, primaryCnae, secondaryCnaes,
            specialRegimes, benefits, reason, createdById, createdByName);
        _versions.Add(version);
        SetUpdatedAt();
        return version;
    }
}

/// <summary>
/// A snapshot of the tax facts of an establishment from <see cref="ValidFrom"/> on. Immutable.
/// The version in force on a date is the one with the latest <see cref="ValidFrom"/> not after the
/// date, and among those the highest <see cref="VersionNumber"/>.
/// </summary>
public class TaxProfileVersion : StoreEntity
{
    private TaxProfileVersion() { }   // EF Core

    internal TaxProfileVersion(
        Guid tenantId, Guid profileId, int versionNumber, DateOnly validFrom, TaxRegime regime, string state,
        string cityIbgeCode, string primaryCnae, IReadOnlyList<string> secondaryCnaes, IReadOnlyList<string> specialRegimes,
        IReadOnlyList<string> benefits, string reason, Guid createdById, string createdByName)
        : base(tenantId)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Informe o motivo da nova versão do perfil.");
        if (state.Length != 2) throw new DomainException("UF inválida.");
        if (cityIbgeCode.Length != 7 || !cityIbgeCode.All(char.IsAsciiDigit)) throw new DomainException("Código IBGE do município inválido.");
        if (primaryCnae.Length != 7 || !primaryCnae.All(char.IsAsciiDigit)) throw new DomainException("CNAE principal inválido (7 dígitos).");

        ProfileId = profileId;
        VersionNumber = versionNumber;
        ValidFrom = validFrom;
        Regime = regime;
        State = state.ToUpperInvariant();
        CityIbgeCode = cityIbgeCode;
        PrimaryCnae = primaryCnae;
        SecondaryCnaes = secondaryCnaes.Distinct().ToList();
        SpecialRegimes = specialRegimes.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();
        Benefits = benefits.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();
        Reason = reason.Trim();
        CreatedById = createdById;
        CreatedByName = createdByName;
    }

    public Guid ProfileId { get; private set; }
    public int VersionNumber { get; private set; }
    public DateOnly ValidFrom { get; private set; }
    public TaxRegime Regime { get; private set; }
    public string State { get; private set; } = string.Empty;
    public string CityIbgeCode { get; private set; } = string.Empty;
    public string PrimaryCnae { get; private set; } = string.Empty;
    public List<string> SecondaryCnaes { get; private set; } = [];
    /// <summary>Regimes diferenciados/específicos the establishment is under, as declared (free codes).</summary>
    public List<string> SpecialRegimes { get; private set; } = [];
    /// <summary>Tax benefits the establishment declares, as free text with their legal basis.</summary>
    public List<string> Benefits { get; private set; } = [];
    // Ancillary obligations are not a timeless fact of the profile: see TaxComplianceAssessment
    // (per period and scope, historical).
    public string Reason { get; private set; } = string.Empty;
    public Guid CreatedById { get; private set; }
    public string CreatedByName { get; private set; } = string.Empty;

    public bool IsSimplesNacional => Regime is TaxRegime.SimplesNacional or TaxRegime.SimplesNacionalExcessoSublimite or TaxRegime.Mei;
}
