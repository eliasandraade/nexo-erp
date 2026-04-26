using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Features.Categories;

public class CategoryService
{
    private readonly ICategoryRepository _categories;
    private readonly ICurrentTenant _currentTenant;

    public CategoryService(ICategoryRepository categories, ICurrentTenant currentTenant)
    {
        _categories = categories;
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<CategoryDto>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var list = await _categories.GetAllAsync(includeInactive, ct);
        return list.Select(MapToDto).ToList();
    }

    public async Task<CategoryDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var category = await _categories.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Category", id);
        return MapToDto(category);
    }

    public async Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken ct = default)
    {
        if (request.ParentCategoryId.HasValue)
        {
            _ = await _categories.GetByIdAsync(request.ParentCategoryId.Value, ct)
                ?? throw new NotFoundException("Category", request.ParentCategoryId.Value);
        }

        var category = Category.Create(
            _currentTenant.Id,
            request.Name,
            request.Description,
            request.ParentCategoryId,
            request.SortOrder);

        await _categories.AddAsync(category, ct);
        await _categories.SaveChangesAsync(ct);
        return MapToDto(category);
    }

    public async Task<CategoryDto> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken ct = default)
    {
        var category = await _categories.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Category", id);

        if (request.ParentCategoryId.HasValue && request.ParentCategoryId.Value == id)
            throw new DomainException("A category cannot be its own parent.");

        category.Update(request.Name, request.Description, request.ParentCategoryId, request.SortOrder);
        await _categories.SaveChangesAsync(ct);
        return MapToDto(category);
    }

    public async Task ActivateAsync(Guid id, CancellationToken ct = default)
    {
        var category = await _categories.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Category", id);
        category.Activate();
        await _categories.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var category = await _categories.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Category", id);

        if (await _categories.HasChildrenAsync(id, ct))
            throw new ConflictException("Cannot deactivate a category that has active subcategories.");

        category.Deactivate();
        await _categories.SaveChangesAsync(ct);
    }

    private static CategoryDto MapToDto(Category c) => new(
        c.Id, c.Name, c.Description, c.ParentCategoryId, c.SortOrder, c.IsActive, c.CreatedAt, c.UpdatedAt);
}
