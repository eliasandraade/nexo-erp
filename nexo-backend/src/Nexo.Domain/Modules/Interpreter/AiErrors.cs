using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Interpreter;

/// <summary>Why a provider can or cannot be used right now. Wire names are snake_case (see <see cref="AiProviderStatusNames"/>).</summary>
public enum AiProviderStatus
{
    Available = 1,
    /// <summary>Turned off in the SuperAdmin.</summary>
    Disabled = 2,
    /// <summary>Blocked by the deployment's emergency switch (<c>Ai:Providers:{Provider}:Allowed=false</c>).</summary>
    BlockedByDeployment = 3,
    MissingKey = 4,
    MissingModel = 5,
    /// <summary>No analyzer for this provider exists in this version of Orken.</summary>
    ImplementationMissing = 6,
    /// <summary>The key can't be decrypted here: master key missing, rotated without the old one, or the envelope is corrupt.</summary>
    SecretUnavailable = 7,
    /// <summary>The last connection test was rejected for the credentials (401/403).</summary>
    InvalidCredentials = 8,
    /// <summary>The last connection test hit the provider's rate limit.</summary>
    RateLimited = 9,
    /// <summary>The last connection test failed for another reason (timeout, 5xx, model not found…).</summary>
    Unhealthy = 10,
}

public static class AiProviderStatusNames
{
    public static string Of(AiProviderStatus status) => status switch
    {
        AiProviderStatus.Available => "available",
        AiProviderStatus.Disabled => "disabled",
        AiProviderStatus.BlockedByDeployment => "blocked_by_deployment",
        AiProviderStatus.MissingKey => "missing_key",
        AiProviderStatus.MissingModel => "missing_model",
        AiProviderStatus.ImplementationMissing => "implementation_missing",
        AiProviderStatus.SecretUnavailable => "secret_unavailable",
        AiProviderStatus.InvalidCredentials => "invalid_credentials",
        AiProviderStatus.RateLimited => "rate_limited",
        _ => "unhealthy",
    };
}

/// <summary>
/// A provider was asked for (forced, or the only one left) but can't be used: an expected configuration state, never a
/// bug. Answered as 422 <c>ai_provider_unavailable</c> and never recorded as an unexpected error.
/// </summary>
public sealed class AiProviderUnavailableException : DomainException
{
    public const string ErrorCode = "ai_provider_unavailable";

    public AnalyzerProvider? Provider { get; }
    public AiProviderStatus? Status { get; }

    public AiProviderUnavailableException(string message, AnalyzerProvider? provider = null, AiProviderStatus? status = null)
        : base(message, ErrorCode) { Provider = provider; Status = status; }
}

/// <summary>What went wrong calling a provider, as far as the caller needs to know (never the provider's raw body).</summary>
public enum AiCallFailure
{
    InvalidCredentials = 1,
    RateLimited = 2,
    Timeout = 3,
    Network = 4,
    ProviderError = 5,
    ModelNotFound = 6,
    InvalidResponse = 7,
    Refused = 8,
    BadRequest = 9,
    UnsupportedInput = 10,
    LimitReached = 11,
    /// <summary>The answer is JSON but does not satisfy the requested schema (missing/typed/enum fields).</summary>
    InvalidOutput = 12,
}

/// <summary>
/// An expected operational failure of an AI provider call (credentials rejected, rate limit, timeout, provider down,
/// invalid output…). Carries only a sanitized message. The API answers 503 <c>ai_provider_failed</c> with the
/// <see cref="Failure"/> as reason; it is logged as a warning and never tracked as an unexpected error.
/// </summary>
public sealed class AiProviderCallException : Exception
{
    public const string ErrorCode = "ai_provider_failed";

    public AnalyzerProvider Provider { get; }
    public AiCallFailure Failure { get; }

    public AiProviderCallException(AnalyzerProvider provider, AiCallFailure failure, string safeMessage, Exception? inner = null)
        : base(safeMessage, inner)
    {
        Provider = provider;
        Failure = failure;
    }

    public string Reason => Failure switch
    {
        AiCallFailure.InvalidCredentials => "invalid_credentials",
        AiCallFailure.RateLimited => "rate_limited",
        AiCallFailure.Timeout => "timeout",
        AiCallFailure.Network => "network",
        AiCallFailure.ModelNotFound => "model_not_found",
        AiCallFailure.InvalidResponse => "invalid_response",
        AiCallFailure.Refused => "refused",
        AiCallFailure.BadRequest => "bad_request",
        AiCallFailure.UnsupportedInput => "unsupported_input",
        AiCallFailure.LimitReached => "limit_reached",
        AiCallFailure.InvalidOutput => "invalid_output",
        _ => "provider_error",
    };
}
