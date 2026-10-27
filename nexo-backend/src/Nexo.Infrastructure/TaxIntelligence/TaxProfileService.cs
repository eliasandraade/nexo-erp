using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.Persistence;
using Npgsql;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>
/// The establishment's tax profile. Versions are only ever added; the one in force on a date is the
/// latest start not after it (and, for the same start, the highest number). A retroactive correction
/// is a new version — calculations already made keep the version they used.
/// </summary>
public sealed class TaxProfileService
{
    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly TaxAuditTrail _audit;

    public TaxProfileService(NexoDbContext db, ICurrentTenant tenant, TaxAuditTrail audit)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
    }

    public async Task<TaxProfileDto> GetAsync(DateOnly today, CancellationToken ct)
    {
        var versions = await _db.TaxProfileVersions.AsNoTracking()
            .OrderByDescending(v => v.ValidFrom).ThenByDescending(v => v.VersionNumber)
            .ToListAsync(ct);
        var current = versions.FirstOrDefault(v => v.ValidFrom <= today);
        var fiscal = await _db.FiscalProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        var suggestion = fiscal is null ? null : new TaxProfileSuggestionDto(fiscal.TaxRegime, fiscal.State, fiscal.CityIbgeCode);
        return new TaxProfileDto(current is null ? null : Map(current), versions.Select(Map).ToList(), suggestion);
    }

    /// <summary>The version in force on the date, or a coded 422 telling the user to create one.</summary>
    public async Task<TaxProfileVersion> GetInForceAsync(DateOnly date, CancellationToken ct)
        => await _db.TaxProfileVersions.AsNoTracking()
               .Where(v => v.ValidFrom <= date)
               .OrderByDescending(v => v.ValidFrom).ThenByDescending(v => v.VersionNumber)
               .FirstOrDefaultAsync(ct)
           ?? throw new DomainException(
               $"Não há perfil tributário vigente em {date:dd/MM/yyyy}. Cadastre o perfil (regime, UF, município, CNAE) com início até essa data.",
               "tax_profile_missing");

    public async Task<TaxProfileVersionDto> AddVersionAsync(CreateTaxProfileVersionRequest r, CancellationToken ct)
    {
        try
        {
            var profile = await _db.TaxProfiles.Include(p => p.Versions).FirstOrDefaultAsync(ct);
            if (profile is null)
            {
                profile = TaxProfile.Create(_tenant.Id);
                _db.TaxProfiles.Add(profile);
            }
            var version = profile.AddVersion(
                r.ValidFrom, r.Regime, r.State.Trim(), r.CityIbgeCode.Trim(), r.PrimaryCnae.Trim(),
                r.SecondaryCnaes ?? [], r.SpecialRegimes ?? [], r.Benefits ?? [],
                r.AccessoryObligations ?? AccessoryObligationsStatus.Unknown, r.AccessoryObligationsEvidence,
                r.Reason, _audit.UserId, _audit.UserName);
            _db.TaxProfileVersions.Add(version);

            _audit.Record(TaxAuditActions.ProfileVersionCreated, "ProfileVersion", version.Id,
                $"Perfil tributário v{version.VersionNumber}: {r.Regime} a partir de {r.ValidFrom:dd/MM/yyyy}.",
                new { version.VersionNumber, validFrom = r.ValidFrom, regime = r.Regime.ToString(), r.State, r.CityIbgeCode, r.Reason,
                    accessoryObligations = version.AccessoryObligations.ToString(), version.AccessoryObligationsEvidence },
                AuditSeverity.Warning);
            await _db.SaveChangesAsync(ct);
            return Map(version);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("Outra versão do perfil foi criada ao mesmo tempo. Recarregue e tente de novo.");
        }
    }

    private static TaxProfileVersionDto Map(TaxProfileVersion v) => new(
        v.Id, v.VersionNumber, v.ValidFrom, v.Regime, v.State, v.CityIbgeCode, v.PrimaryCnae, v.SecondaryCnaes,
        v.SpecialRegimes, v.Benefits, v.AccessoryObligations, v.AccessoryObligationsEvidence, v.Reason, v.CreatedByName, v.CreatedAt);
}
