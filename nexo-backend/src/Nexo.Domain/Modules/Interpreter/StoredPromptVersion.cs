namespace Nexo.Domain.Modules.Interpreter;

/// <summary>
/// Versioned prompt stored in the DB. One active version per promptType at a time.
/// Platform-global — no tenant isolation.
/// </summary>
public class StoredPromptVersion
{
    public Guid     Id          { get; private set; }
    public string   PromptType  { get; private set; } = string.Empty; // extraction | interpretation | memory
    public string   Version     { get; private set; } = string.Empty; // semver e.g. "1.0.0"
    public string   Hash        { get; private set; } = string.Empty; // SHA-256 of content (first 8 chars for display)
    public bool     IsActive    { get; private set; }
    public string   Content     { get; private set; } = string.Empty;
    public string   Description { get; private set; } = string.Empty;
    public string   CreatedBy   { get; private set; } = string.Empty;
    public DateTime CreatedAt   { get; private set; }

    private StoredPromptVersion() { }

    public static StoredPromptVersion Create(
        string promptType,
        string version,
        string hash,
        string content,
        string description,
        string createdBy,
        bool   isActive = false)
        => new()
        {
            Id          = Guid.NewGuid(),
            PromptType  = promptType,
            Version     = version,
            Hash        = hash,
            Content     = content,
            Description = description,
            CreatedBy   = createdBy,
            IsActive    = isActive,
            CreatedAt   = DateTime.UtcNow,
        };

    public void Activate()   => IsActive = true;
    public void Deactivate() => IsActive = false;
}
