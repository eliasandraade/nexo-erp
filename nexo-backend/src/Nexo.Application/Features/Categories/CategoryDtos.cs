namespace Nexo.Application.Features.Categories;

// ── Requests ────────────────────────────────────────────────────────────────

public record CreateCategoryRequest(
    string Name,
    string? Description = null,
    Guid? ParentCategoryId = null);

public record UpdateCategoryRequest(
    string Name,
    string? Description,
    Guid? ParentCategoryId);

// ── Responses ───────────────────────────────────────────────────────────────

public record CategoryDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? ParentCategoryId,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);
