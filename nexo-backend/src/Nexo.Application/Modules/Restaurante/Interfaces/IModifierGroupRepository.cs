using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante.Interfaces;

public interface IModifierGroupRepository
{
    Task<IReadOnlyList<ProductModifierGroup>> GetByProductIdAsync(Guid productId, CancellationToken ct = default);

    /// <summary>
    /// Bypasses query filters. Used by the public portal where no tenant context is set.
    /// Returns all active groups (with their modifiers) for the given product and tenant.
    /// </summary>
    Task<IReadOnlyList<ProductModifierGroup>> GetByProductIdAsync(Guid productId, Guid tenantId, CancellationToken ct = default);

    Task<ProductModifierGroup?> GetByIdWithModifiersAsync(Guid id, CancellationToken ct = default);
    Task<ProductModifier?> GetModifierByIdAsync(Guid modifierId, CancellationToken ct = default);

    /// <summary>
    /// Bypasses query filters. Used by the public portal where no tenant context is set.
    /// Returns only active modifiers belonging to the given tenant.
    /// </summary>
    Task<ProductModifier?> GetActiveModifierAsync(Guid modifierId, Guid tenantId, CancellationToken ct = default);
    Task AddGroupAsync(ProductModifierGroup group, CancellationToken ct = default);
    void TrackModifier(ProductModifier modifier);
    Task SaveChangesAsync(CancellationToken ct = default);
}
