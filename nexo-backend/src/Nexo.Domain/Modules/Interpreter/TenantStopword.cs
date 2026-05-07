using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Interpreter;

// Tenant-specific stopwords extend the PT-BR base list used by IDescriptionNormalizer.
// Stored normalized (lowercase, no accents) to match the normalizer pipeline output.
public class TenantStopword : TenantEntity
{
    private TenantStopword() { }
    private TenantStopword(Guid tenantId) : base(tenantId) { }

    public string Word { get; private set; } = string.Empty;

    public static TenantStopword Create(Guid tenantId, string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            throw new DomainException("Stopword cannot be empty.");

        // Stored normalized: lowercase, trimmed — matches normalizer pipeline output.
        var normalized = word.Trim().ToLowerInvariant();

        return new TenantStopword(tenantId) { Word = normalized };
    }
}
