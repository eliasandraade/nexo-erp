namespace Nexo.Domain.Modules.Interpreter;

/// <summary>
/// Per-tenant AI spend limits (soft = warning, hard = block).
/// Managed by super_admin; values are in cents (USD × 100).
/// NOT a TenantEntity — no global query filter; admin reads directly.
/// </summary>
public class TenantAiLimit
{
    public Guid  Id               { get; private set; }
    public Guid  TenantId         { get; private set; }
    public int?  SoftLimitCents   { get; private set; }
    public int?  HardLimitCents   { get; private set; }
    public DateTime UpdatedAt     { get; private set; }

    private TenantAiLimit() { }

    public static TenantAiLimit Create(Guid tenantId, int? softLimitCents, int? hardLimitCents)
        => new()
        {
            Id             = Guid.NewGuid(),
            TenantId       = tenantId,
            SoftLimitCents = softLimitCents,
            HardLimitCents = hardLimitCents,
            UpdatedAt      = DateTime.UtcNow,
        };

    public void Update(int? softLimitCents, int? hardLimitCents)
    {
        SoftLimitCents = softLimitCents;
        HardLimitCents = hardLimitCents;
        UpdatedAt      = DateTime.UtcNow;
    }
}
