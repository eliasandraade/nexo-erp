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
            req.IsRequired, req.MaxSelections, req.SortOrder);
        await _repo.AddGroupAsync(group, ct);
        await _repo.SaveChangesAsync(ct);
        return Map(group);
    }

    public async Task<ModifierGroupDto> UpdateGroupAsync(Guid groupId, UpdateModifierGroupRequest req, CancellationToken ct = default)
    {
        var group = await _repo.GetByIdWithModifiersAsync(groupId, ct)
            ?? throw new NotFoundException("ModifierGroup", groupId);
        group.Update(req.Name, req.IsRequired, req.MaxSelections, req.SortOrder);
        await _repo.SaveChangesAsync(ct);
        return Map(group);
    }

    public async Task<ModifierGroupDto> AddModifierAsync(Guid groupId, CreateModifierRequest req, CancellationToken ct = default)
    {
        var group = await _repo.GetByIdWithModifiersAsync(groupId, ct)
            ?? throw new NotFoundException("ModifierGroup", groupId);
        var modifier = ProductModifier.Create(
            _currentTenant.Id, groupId, req.Name, req.PriceAdjustment, req.SortOrder);
        _repo.TrackModifier(modifier);
        await _repo.SaveChangesAsync(ct);
        // Re-fetch to get updated modifier list
        var updated = await _repo.GetByIdWithModifiersAsync(groupId, ct)
            ?? throw new NotFoundException("ModifierGroup", groupId);
        return Map(updated);
    }

    public async Task<ModifierGroupDto> UpdateModifierAsync(Guid groupId, Guid modifierId, UpdateModifierRequest req, CancellationToken ct = default)
    {
        var modifier = await _repo.GetModifierByIdAsync(modifierId, ct)
            ?? throw new NotFoundException("Modifier", modifierId);
        if (modifier.GroupId != groupId)
            throw new DomainException("Modifier does not belong to this group.");
        modifier.Update(req.Name, req.PriceAdjustment, req.SortOrder);
        await _repo.SaveChangesAsync(ct);
        var group = await _repo.GetByIdWithModifiersAsync(groupId, ct)
            ?? throw new NotFoundException("ModifierGroup", groupId);
        return Map(group);
    }

    public async Task DeleteModifierAsync(Guid groupId, Guid modifierId, CancellationToken ct = default)
    {
        var modifier = await _repo.GetModifierByIdAsync(modifierId, ct)
            ?? throw new NotFoundException("Modifier", modifierId);
        if (modifier.GroupId != groupId)
            throw new DomainException("Modifier does not belong to this group.");
        modifier.Deactivate();
        await _repo.SaveChangesAsync(ct);
    }

    private static ModifierGroupDto Map(ProductModifierGroup g) => new(
        g.Id, g.ProductId, g.Name, g.IsRequired, g.MaxSelections, g.SortOrder, g.IsActive,
        g.Modifiers.Select(m => new ModifierDto(m.Id, m.Name, m.PriceAdjustment, m.SortOrder, m.IsActive)).ToList());
}
