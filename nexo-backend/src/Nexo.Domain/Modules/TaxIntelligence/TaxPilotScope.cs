using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.TaxIntelligence;

/// <summary>
/// Who takes part in the controlled pilot, and until when (platform-wide, kept by the platform). No
/// tenant is in it by default: a tenant — or only some of its stores — is added explicitly, with who
/// activated the pilot, when and under which decision record. Removing a participant stops new work for
/// it; what it already calculated stays readable and reproducible. Every change is audited by the caller.
/// </summary>
public class TaxPilotScope : BaseEntity
{
    private readonly List<TaxPilotParticipant> _participants = [];

    private TaxPilotScope() { }   // EF Core

    public bool Enabled { get; private set; }
    public DateOnly ValidFrom { get; private set; }
    public DateOnly? ValidTo { get; private set; }
    public string DecisionReference { get; private set; } = string.Empty;
    public Guid ActivatedById { get; private set; }
    public string ActivatedByName { get; private set; } = string.Empty;
    public DateTime ActivatedAt { get; private set; }

    public IReadOnlyCollection<TaxPilotParticipant> Participants => _participants.AsReadOnly();

    public static TaxPilotScope Create() => new() { Enabled = false };

    /// <summary>Activates (or changes) the pilot window. Who and when come from the authenticated request.</summary>
    public void Configure(bool enabled, DateOnly validFrom, DateOnly? validTo, string decisionReference, Guid byId, string byName, DateTime atUtc)
    {
        if (validTo is not null && validTo < validFrom)
            throw new DomainException("O fim do piloto é anterior ao início.", "tax_pilot_scope_validity");
        if (string.IsNullOrWhiteSpace(decisionReference))
            throw new DomainException("Diga onde está registrada a decisão do piloto.", "tax_pilot_scope_decision");
        if (byId == Guid.Empty || string.IsNullOrWhiteSpace(byName))
            throw new DomainException("A ativação do piloto identifica quem a fez.", "tax_pilot_scope_actor");
        Enabled = enabled;
        ValidFrom = validFrom;
        ValidTo = validTo;
        DecisionReference = decisionReference.Trim();
        ActivatedById = byId;
        ActivatedByName = byName.Trim();
        ActivatedAt = atUtc;
        SetUpdatedAt();
    }

    /// <summary>Adds a tenant (all its stores when <paramref name="storeId"/> is null) to the pilot.</summary>
    public TaxPilotParticipant AddParticipant(Guid tenantId, Guid? storeId, Guid byId, string byName, DateTime atUtc)
    {
        if (tenantId == Guid.Empty) throw new DomainException("Informe o tenant.", "tax_pilot_participant");
        if (_participants.Any(p => p.IsActive && p.TenantId == tenantId && p.StoreId == storeId))
            throw new DomainException("Este participante já está no piloto.", "tax_pilot_participant_duplicate");
        var p = new TaxPilotParticipant(Id, tenantId, storeId, byId, byName, atUtc);
        _participants.Add(p);
        SetUpdatedAt();
        return p;
    }

    public TaxPilotParticipant RemoveParticipant(Guid participantId, string reason, Guid byId, string byName, DateTime atUtc)
    {
        var p = _participants.FirstOrDefault(x => x.Id == participantId && x.IsActive)
            ?? throw new DomainException("Participante não encontrado no piloto.", "tax_pilot_participant");
        p.Remove(reason, byId, byName, atUtc);
        SetUpdatedAt();
        return p;
    }

    /// <summary>
    /// Whether new work of a tenant/store is inside the pilot today. Null = admitted; otherwise the refusal
    /// (code, message). A store allowlist, when the tenant has one, admits only those stores.
    /// </summary>
    public (string Code, string Message)? Refusal(Guid tenantId, Guid storeId, DateOnly today)
    {
        if (!Enabled) return ("tax_pilot_scope_disabled", "O piloto controlado não está ativo.");
        if (today < ValidFrom || (ValidTo is not null && today > ValidTo))
            return ("tax_pilot_scope_expired", "O piloto controlado está fora da validade.");
        var mine = _participants.Where(p => p.IsActive && p.TenantId == tenantId).ToList();
        if (mine.Count == 0) return ("tax_pilot_scope_tenant", "Esta empresa não participa do piloto controlado.");
        if (mine.All(p => p.StoreId is not null) && mine.All(p => p.StoreId != storeId))
            return ("tax_pilot_scope_store", "Esta loja não participa do piloto controlado.");
        return null;
    }
}

/// <summary>A tenant (or one of its stores) in the pilot. Removal is recorded, never deleted.</summary>
public class TaxPilotParticipant : BaseEntity
{
    private TaxPilotParticipant() { }   // EF Core

    internal TaxPilotParticipant(Guid scopeId, Guid tenantId, Guid? storeId, Guid byId, string byName, DateTime atUtc)
    {
        ScopeId = scopeId;
        TenantId = tenantId;
        StoreId = storeId;
        AddedById = byId;
        AddedByName = byName.Trim();
        AddedAt = atUtc;
    }

    public Guid ScopeId { get; private set; }
    public Guid TenantId { get; private set; }
    /// <summary>Null = every store of the tenant.</summary>
    public Guid? StoreId { get; private set; }
    public Guid AddedById { get; private set; }
    public string AddedByName { get; private set; } = string.Empty;
    public DateTime AddedAt { get; private set; }
    public DateTime? RemovedAt { get; private set; }
    public Guid? RemovedById { get; private set; }
    public string? RemovedByName { get; private set; }
    public string? RemovalReason { get; private set; }

    public bool IsActive => RemovedAt is null;

    internal void Remove(string reason, Guid byId, string byName, DateTime atUtc)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Diga por que o participante sai do piloto.", "tax_pilot_participant_reason");
        RemovedAt = atUtc;
        RemovedById = byId;
        RemovedByName = byName.Trim();
        RemovalReason = reason.Trim();
        SetUpdatedAt();
    }
}
