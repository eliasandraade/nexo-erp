using Nexo.Domain.Common;

namespace Nexo.Domain.Entities;

public class Category : TenantEntity
{
    private Category() { }
    private Category(Guid tenantId) : base(tenantId) { }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid? ParentCategoryId { get; private set; }
    public bool IsActive { get; private set; }

    // Navigation
    public Category? Parent { get; private set; }
    public ICollection<Category> Children { get; private set; } = [];
    public ICollection<Product> Products { get; private set; } = [];

    public static Category Create(
        Guid tenantId,
        string name,
        string? description = null,
        Guid? parentCategoryId = null)
    {
        return new Category(tenantId)
        {
            Name             = name.Trim(),
            Description      = description?.Trim(),
            ParentCategoryId = parentCategoryId,
            IsActive         = true,
        };
    }

    public void Update(string name, string? description, Guid? parentCategoryId)
    {
        Name             = name.Trim();
        Description      = description?.Trim();
        ParentCategoryId = parentCategoryId;
        SetUpdatedAt();
    }

    public void Deactivate() { IsActive = false; SetUpdatedAt(); }
    public void Activate()   { IsActive = true;  SetUpdatedAt(); }
}
