using Nexo.Domain.Common;

namespace Nexo.Domain.Entities;

public class Category : TenantEntity
{
    private Category() { }
    private Category(Guid tenantId) : base(tenantId) { }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid? ParentCategoryId { get; private set; }
    public int SortOrder { get; private set; }                      // ordem de exibição no cardápio
    public bool IsActive { get; private set; }

    // Navigation
    public Category? Parent { get; private set; }
    public ICollection<Category> Children { get; private set; } = [];
    public ICollection<Product> Products { get; private set; } = [];

    public static Category Create(
        Guid tenantId,
        string name,
        string? description = null,
        Guid? parentCategoryId = null,
        int sortOrder = 0)
    {
        return new Category(tenantId)
        {
            Name             = name.Trim(),
            Description      = description?.Trim(),
            ParentCategoryId = parentCategoryId,
            SortOrder        = sortOrder,
            IsActive         = true,
        };
    }

    public void Update(string name, string? description, Guid? parentCategoryId, int sortOrder = 0)
    {
        Name             = name.Trim();
        Description      = description?.Trim();
        ParentCategoryId = parentCategoryId;
        SortOrder        = sortOrder;
        SetUpdatedAt();
    }

    public void Deactivate() { IsActive = false; SetUpdatedAt(); }
    public void Activate()   { IsActive = true;  SetUpdatedAt(); }
}
