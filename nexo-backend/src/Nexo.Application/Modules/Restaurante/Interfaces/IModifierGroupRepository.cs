using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante.Interfaces;

public interface IModifierGroupRepository
{
    Task<IReadOnlyList<ProductModifierGroup>> GetByProductIdAsync(Guid productId, CancellationToken ct = default);
    Task<ProductModifierGroup?> GetByIdWithModifiersAsync(Guid id, CancellationToken ct = default);
    Task<ProductModifier?> GetModifierByIdAsync(Guid modifierId, CancellationToken ct = default);
    Task AddGroupAsync(ProductModifierGroup group, CancellationToken ct = default);
    void TrackModifier(ProductModifier modifier);
    Task SaveChangesAsync(CancellationToken ct = default);
}
