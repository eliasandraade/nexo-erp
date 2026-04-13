using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante;

public class ModifierGroupService
{
    private readonly IModifierGroupRepository _repo;
    private readonly ICurrentTenant           _currentTenant;

    public ModifierGroupService(IModifierGroupRepository repo, ICurrentTenant currentTenant)
    {
        _repo          = repo;
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<ModifierGroupDto>> GetByProductAsync(Guid productId, CancellationToken ct = default)
    {
        var groups = await _repo.GetByProductIdAsync(productId, ct);
        return groups.Select(Map).ToList();
    }

    public async Task<ModifierGroupDto> CreateGroupAsync(CreateModifierGroupRequest req, CancellationToken ct = default)
    {
        var group = ProductModifierGroup.Create(
            _currentTenant.Id, req.ProductId, req.Name,
            req.IsRequired,
            ToShort(req.MinSelections, nameof(req.MinSelections)),
            ToShort(req.MaxSelections, nameof(req.MaxSelections)),
            ToShort(req.SortOrder,     nameof(req.SortOrder)));
        await _repo.AddGroupAsync(group, ct);
        await _repo.SaveChangesAsync(ct);
        return Map(group);
    }

    public async Task<ModifierGroupDto> UpdateGroupAsync(Guid groupId, UpdateModifierGroupRequest req, CancellationToken ct = default)
    {
        var group = await _repo.GetByIdWithModifiersAsync(groupId, ct)
            ?? throw new NotFoundException("ModifierGroup", groupId);
        group.Update(req.Name, req.IsRequired,
            ToShort(req.MinSelections, nameof(req.MinSelections)),
            ToShort(req.MaxSelections, nameof(req.MaxSelections)),
            ToShort(req.SortOrder,     nameof(req.SortOrder)));
        await _repo.SaveChangesAsync(ct);
        return Map(group);
    }

    public async Task<ModifierGroupDto> AddModifierAsync(Guid groupId, CreateModifierRequest req, CancellationToken ct = default)
    {
        var group = await _repo.GetByIdWithModifiersAsync(groupId, ct)
            ?? throw new NotFoundException("ModifierGroup", groupId);
        var modifier = ProductModifier.Create(
            _currentTenant.Id, groupId, req.Name, req.PriceAdjustment,
            ToShort(req.SortOrder, nameof(req.SortOrder)));
        group.AddModifier(modifier);
        _repo.TrackModifier(modifier);
        await _repo.SaveChangesAsync(ct);
        return Map(group);
    }

    public async Task<ModifierDto> UpdateModifierAsync(Guid modifierId, UpdateModifierRequest req, CancellationToken ct = default)
    {
        var modifier = await _repo.GetModifierByIdAsync(modifierId, ct)
            ?? throw new NotFoundException("Modifier", modifierId);
        modifier.Update(req.Name, req.PriceAdjustment, ToShort(req.SortOrder, nameof(req.SortOrder)));
        await _repo.SaveChangesAsync(ct);
        return Map(modifier);
    }

    public async Task DeleteModifierAsync(Guid modifierId, CancellationToken ct = default)
    {
        var modifier = await _repo.GetModifierByIdAsync(modifierId, ct)
            ?? throw new NotFoundException("Modifier", modifierId);
        modifier.Deactivate();
        await _repo.SaveChangesAsync(ct);
    }

    private static short ToShort(int value, string fieldName)
    {
        if (value is < 0 or > 32767)
            throw new ArgumentOutOfRangeException(fieldName, value, $"{fieldName} must be between 0 and 32767.");
        return (short)value;
    }

    private static ModifierGroupDto Map(ProductModifierGroup g) => new(
        g.Id, g.ProductId, g.Name, g.IsRequired,
        g.MinSelections,
        g.MaxSelections, g.SortOrder, g.IsActive,
        g.Modifiers.Select(Map).ToList());

    private static ModifierDto Map(ProductModifier m) => new(
        m.Id, m.Name, m.PriceAdjustment, m.SortOrder, m.IsActive);
}
