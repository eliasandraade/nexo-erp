using Nexo.Domain.Common;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Área física do restaurante (Salão, Varanda, Deck, etc.).
/// Agrupa mesas para organização operacional.
/// </summary>
public class RestArea : TenantEntity
{
    private RestArea() { }
    private RestArea(Guid tenantId) : base(tenantId) { }

    public string  Name        { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool    IsActive    { get; private set; }

    // Navigation
    public ICollection<RestTable> Tables { get; private set; } = [];

    public static RestArea Create(Guid tenantId, string name, string? description = null)
        => new RestArea(tenantId)
        {
            Name        = name.Trim(),
            Description = description?.Trim(),
            IsActive    = true,
        };

    public void Update(string name, string? description)
    {
        Name        = name.Trim();
        Description = description?.Trim();
        SetUpdatedAt();
    }

    public void Activate()   { IsActive = true;  SetUpdatedAt(); }
    public void Deactivate() { IsActive = false; SetUpdatedAt(); }
}
