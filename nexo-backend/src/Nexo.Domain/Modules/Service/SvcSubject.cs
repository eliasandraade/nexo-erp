using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// The real subject of a service when it differs from the paying customer — a pet (tutor),
/// a vehicle (owner), a student/dependent (responsible party). Tenant-scoped (a
/// <see cref="TenantEntity"/>) because it belongs to the customer and may be served at more
/// than one store of the tenant. When a vertical does not need a distinct subject, records
/// reference the customer directly and no SvcSubject is created.
///
/// <see cref="MetadataJson"/> is free-form per-kind detail (species/breed, plate/model, level…)
/// stored as jsonb so the shape can evolve without migrations.
/// </summary>
public class SvcSubject : TenantEntity
{
    private SvcSubject() { }                                   // EF Core
    private SvcSubject(Guid tenantId) : base(tenantId) { }

    public Guid           CustomerId   { get; private set; }
    public SvcSubjectKind Kind         { get; private set; }
    public string         DisplayName  { get; private set; } = string.Empty;
    public string?        MetadataJson { get; private set; }
    public string?        Notes        { get; private set; }
    public bool           IsActive     { get; private set; }

    public static SvcSubject Create(
        Guid           tenantId,
        Guid           customerId,
        SvcSubjectKind kind,
        string         displayName,
        string?        metadataJson = null,
        string?        notes        = null)
    {
        if (customerId == Guid.Empty)
            throw new DomainException("Subject customer is required.");
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException("Subject display name is required.");

        return new SvcSubject(tenantId)
        {
            CustomerId   = customerId,
            Kind         = kind,
            DisplayName  = displayName.Trim(),
            MetadataJson = metadataJson,
            Notes        = notes?.Trim(),
            IsActive     = true,
        };
    }

    public void UpdateDetails(SvcSubjectKind kind, string displayName, string? notes)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException("Subject display name is required.");

        Kind        = kind;
        DisplayName = displayName.Trim();
        Notes       = notes?.Trim();
        SetUpdatedAt();
    }

    public void UpdateMetadata(string? metadataJson)
    {
        MetadataJson = metadataJson;
        SetUpdatedAt();
    }

    public void Activate()   { IsActive = true;  SetUpdatedAt(); }
    public void Deactivate() { IsActive = false; SetUpdatedAt(); }
}
