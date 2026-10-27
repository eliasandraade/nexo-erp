using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.TaxIntelligence;

/// <summary>
/// A tax rule's stable identity (platform-wide). Its behaviour lives in <see cref="TaxRuleVersion"/>s:
/// a change of law or a correction is a new version, never an edit, so any past calculation can be
/// rebuilt with exactly what was in force.
/// </summary>
public class TaxRule : BaseEntity
{
    private readonly List<TaxRuleVersion> _versions = [];

    private TaxRule() { }   // EF Core

    /// <summary>Stable key, e.g. "CBS.RATE.2026-TEST". Unique; never reused for another rule.</summary>
    public string Key { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    public IReadOnlyCollection<TaxRuleVersion> Versions => _versions.AsReadOnly();

    public static TaxRule Create(string key, string name, string description)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new DomainException("Informe a chave da regra.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Informe o nome da regra.");
        return new TaxRule { Key = key.Trim().ToUpperInvariant(), Name = name.Trim(), Description = description.Trim() };
    }
}

/// <summary>
/// One version of a rule: validity, conditions and result, plus the provisions it rests on. The
/// content (conditions, result, validity, priority) is fixed at creation and fingerprinted in
/// <see cref="ContentHash"/>; only the status moves afterwards, and each move is audited.
/// Conditions and result are typed JSON produced and read by the engine (Application layer).
/// </summary>
public class TaxRuleVersion : BaseEntity
{
    private readonly List<TaxRuleVersionSource> _sources = [];

    private TaxRuleVersion() { }   // EF Core

    public Guid RuleId { get; private set; }
    public int Version { get; private set; }
    public TaxRuleStatus Status { get; private set; }
    public DateOnly ValidFrom { get; private set; }
    /// <summary>Last day the version applies; null = open-ended.</summary>
    public DateOnly? ValidTo { get; private set; }
    public int Priority { get; private set; }
    public string ConditionsJson { get; private set; } = "[]";
    public string ResultJson { get; private set; } = "{}";
    /// <summary>SHA-256 of the canonical content, computed by the engine when the version is built.</summary>
    public string ContentHash { get; private set; } = string.Empty;
    /// <summary>How the version entered the catalogue ("seed:…" in migrations, "platform" via the admin API).</summary>
    public string Origin { get; private set; } = string.Empty;
    public string CreatedBy { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public DateTime? ActivatedAt { get; private set; }
    public DateTime? StatusChangedAt { get; private set; }

    public IReadOnlyCollection<TaxRuleVersionSource> Sources => _sources.AsReadOnly();

    public bool Covers(DateOnly date) => ValidFrom <= date && (ValidTo is null || date <= ValidTo);

    public static TaxRuleVersion Create(
        Guid ruleId, int version, DateOnly validFrom, DateOnly? validTo, int priority,
        string conditionsJson, string resultJson, string contentHash, string origin, string createdBy,
        string? notes, IEnumerable<Guid> legalSourceIds)
    {
        if (version < 1) throw new DomainException("A versão começa em 1.");
        if (validTo is not null && validTo < validFrom) throw new DomainException("O fim da vigência é anterior ao início.");
        if (string.IsNullOrWhiteSpace(contentHash)) throw new DomainException("Versão sem impressão digital do conteúdo.");

        var v = new TaxRuleVersion
        {
            RuleId = ruleId,
            Version = version,
            Status = TaxRuleStatus.Draft,
            ValidFrom = validFrom,
            ValidTo = validTo,
            Priority = priority,
            ConditionsJson = conditionsJson,
            ResultJson = resultJson,
            ContentHash = contentHash,
            Origin = origin,
            CreatedBy = createdBy,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };
        foreach (var id in legalSourceIds.Distinct())
            v._sources.Add(new TaxRuleVersionSource(v.Id, id));
        return v;
    }

    /// <summary>
    /// Moves the status. Content never changes; an active version can only be superseded or revoked
    /// (it may already have produced calculations), and a finished version never comes back.
    /// </summary>
    public void ChangeStatus(TaxRuleStatus next, DateTime atUtc)
    {
        if (next == Status) return;
        var allowed = Status switch
        {
            TaxRuleStatus.Draft or TaxRuleStatus.PendingRegulation or TaxRuleStatus.Unverified =>
                next is TaxRuleStatus.PendingRegulation or TaxRuleStatus.Unverified or TaxRuleStatus.Active or TaxRuleStatus.Revoked,
            TaxRuleStatus.Active => next is TaxRuleStatus.Superseded or TaxRuleStatus.Revoked,
            _ => false,
        };
        if (!allowed)
            throw new DomainException($"Uma versão {Status} não pode passar para {next}.", "tax_rule_status_transition");

        Status = next;
        StatusChangedAt = atUtc;
        if (next == TaxRuleStatus.Active) ActivatedAt = atUtc;
        SetUpdatedAt();
    }
}

/// <summary>Join between a rule version and a legal source (tax_rule_version_sources).</summary>
public class TaxRuleVersionSource
{
    private TaxRuleVersionSource() { }   // EF Core

    public TaxRuleVersionSource(Guid ruleVersionId, Guid legalSourceId)
    {
        RuleVersionId = ruleVersionId;
        LegalSourceId = legalSourceId;
    }

    public Guid RuleVersionId { get; private set; }
    public Guid LegalSourceId { get; private set; }
    public TaxLegalSource? LegalSource { get; private set; }
}
