namespace Nexo.Infrastructure.Auth;

/// <summary>
/// Startup guard for the JWT signing secret. The repository ships placeholder secrets
/// (appsettings.json, appsettings.Development.json, the integration-test value); if any of them —
/// or anything equally guessable — ever signs tokens in a real environment, anyone who has read the
/// repo can forge access tokens, including platform super-admin ones.
///
/// Strict mode (every environment except Development and Testing) refuses to start when the secret
/// is missing, shorter than 32 characters (HS256 wants ≥ 256 bits), a known placeholder, contains a
/// placeholder marker, or has too little variety to be random. Messages never include the secret.
/// </summary>
public static class JwtSecretPolicy
{
    public const int MinimumLength = 32;
    private const int MinimumDistinctCharacters = 12;

    /// <summary>Secrets that are public because they are committed to this repository.</summary>
    private static readonly string[] KnownPublicSecrets =
    [
        "CHANGE_THIS_IN_PRODUCTION_USE_A_LONG_RANDOM_STRING_AT_LEAST_32_CHARS",
        "dev-secret-key-change-this-in-production-min-32-chars",
        "test-secret-key-minimum-32-characters-long!",
    ];

    /// <summary>
    /// Fragments only placeholder values contain. Deliberately narrow: a false positive would refuse
    /// a legitimate production secret and take the API down, so generic words are not listed.
    /// </summary>
    private static readonly string[] PlaceholderMarkers =
    [
        "change_this", "change-this", "changethis", "change_me", "change-me", "changeme", "placeholder",
    ];

    /// <summary>Strict everywhere except local development and the integration-test host.</summary>
    public static bool IsStrictEnvironment(string environmentName)
        => !string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns the reason the secret is unsafe, or null when it is acceptable.</summary>
    public static string? Problem(string? secret, bool strict)
    {
        if (string.IsNullOrWhiteSpace(secret))
            return "Jwt:Secret is not configured.";
        if (secret.Length < MinimumLength)
            return $"Jwt:Secret must be at least {MinimumLength} characters.";
        if (!strict)
            return null;

        if (KnownPublicSecrets.Any(k => string.Equals(k, secret, StringComparison.Ordinal)))
            return "Jwt:Secret is a value committed to the repository. Set a private random secret (env var Jwt__Secret).";
        var lower = secret.ToLowerInvariant();
        if (PlaceholderMarkers.Any(lower.Contains))
            return "Jwt:Secret looks like a placeholder. Set a private random secret (env var Jwt__Secret).";
        if (secret.Distinct().Count() < MinimumDistinctCharacters)
            return "Jwt:Secret has too little variety to be random. Use at least 32 random characters.";
        return null;
    }

    /// <summary>Throws (failing startup) when the secret is unsafe for the environment.</summary>
    public static void EnsureSafe(string? secret, string environmentName)
    {
        var problem = Problem(secret, IsStrictEnvironment(environmentName));
        if (problem is not null)
            throw new InvalidOperationException($"Refusing to start: {problem}");
    }
}
