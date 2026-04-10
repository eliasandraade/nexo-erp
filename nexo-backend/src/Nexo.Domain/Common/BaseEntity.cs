namespace Nexo.Domain.Common;

/// <summary>
/// Base class for all domain entities.
/// Provides Id, CreatedAt, UpdatedAt.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; protected set; }
    public DateTime CreatedAt { get; protected set; }
    public DateTime UpdatedAt { get; protected set; }

    protected BaseEntity()
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    protected void SetUpdatedAt() => UpdatedAt = DateTime.UtcNow;
}
