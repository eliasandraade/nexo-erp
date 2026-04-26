namespace Nexo.Application.Features.Categories;

// ── Requests ────────────────────────────────────────────────────────────────

public record CreateCategoryRequest(
    string Name,
    string? Description = null,
    Guid? ParentCategoryId = null,
    int SortOrder = 0);

public record UpdateCategoryRequest(
    string Name,
    string? Description,
    Guid? ParentCategoryId,
    int SortOrder = 0);

// ── Responses ───────────────────────────────────────────────────────────────

public record CategoryDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? ParentCategoryId,
    int SortOrder,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);
