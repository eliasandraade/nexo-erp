namespace Nexo.Domain.Modules.Interpreter;

// Immutable value object capturing full prompt traceability.
// PromptVersion = semver for humans. PromptHash = SHA-256 for machines (rollback, regression).
public sealed record PromptMetadata(
    string PromptType,
    string PromptVersion,
    string PromptHash)
{
    public static PromptMetadata None =>
        new(string.Empty, string.Empty, string.Empty);
}
