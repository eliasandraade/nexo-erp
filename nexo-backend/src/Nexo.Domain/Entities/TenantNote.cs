using Nexo.Domain.Common;

namespace Nexo.Domain.Entities;

/// <summary>
/// Internal note written by a platform admin about a specific tenant.
/// Used as a lightweight CRM — track context, issues, and history.
/// </summary>
public class TenantNote : BaseEntity
{
    private TenantNote() { } // EF Core

    public Guid TenantId { get; private set; }
    public string Content { get; private set; } = string.Empty;

    /// <summary>Platform admin who wrote the note.</summary>
    public Guid? AuthorId { get; private set; }
    public string AuthorName { get; private set; } = string.Empty;

    public bool IsPinned { get; private set; }

    // Navigation
    public Tenant? Tenant { get; private set; }

    public static TenantNote Create(
        Guid tenantId,
        string content,
        Guid? authorId,
        string authorName,
        bool isPinned = false)
    {
        return new TenantNote
        {
            Id         = Guid.NewGuid(),
            TenantId   = tenantId,
            Content    = content.Trim(),
            AuthorId   = authorId,
            AuthorName = authorName,
            IsPinned   = isPinned,
            CreatedAt  = DateTime.UtcNow,
            UpdatedAt  = DateTime.UtcNow,
        };
    }

    public void TogglePin()
    {
        IsPinned = !IsPinned;
        SetUpdatedAt();
    }

    public void UpdateContent(string content)
    {
        Content = content.Trim();
        SetUpdatedAt();
    }
}
