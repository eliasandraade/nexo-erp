using System.Text.Json;

namespace Nexo.Application.Modules.Interpreter.Interfaces;

/// <summary>
/// One structured call to the platform's language model (the provider the SuperAdmin configured): the active prompt of
/// <see cref="AiStructuredRequest.PromptType"/> as instructions, untrusted business content inside delimited data blocks,
/// the answer constrained to a JSON schema and validated against it before anything sees it. Usage is reserved against the
/// monthly limits before the call and settled (or released) after; every call is recorded in telemetry.
/// Failures: no usable provider → <c>AiProviderUnavailableException</c> (422 ai_provider_unavailable); provider error, limit
/// reached or an answer that does not satisfy the schema → <c>AiProviderCallException</c> (503 ai_provider_failed, reason
/// e.g. <c>invalid_output</c>, <c>limit_reached</c>).
/// </summary>
public interface IAiStructuredCompletion
{
    Task<AiStructuredResult> CompleteAsync(AiStructuredRequest request, CancellationToken ct = default);
}

/// <summary>An image or PDF sent with the request (vision). Only when the caller explicitly includes it.</summary>
public sealed record AiAttachment(string MediaType, byte[] Bytes, string? Label = null)
{
    public const string Pdf = "application/pdf";
    public bool IsPdf => MediaType == Pdf;
}

/// <param name="OperationType">Telemetry operation (e.g. <c>build-daily-log-copilot</c>), ≤ 50 characters.</param>
/// <param name="SystemContext">Trusted context written by Orken itself (never user text): appended to the instructions.</param>
/// <param name="UserContent">Untrusted content (business data, user text): always sent inside a data block.</param>
public sealed record AiStructuredRequest(
    string OperationType,
    string PromptType,
    Guid TenantId,
    Guid? UserId,
    string? SystemContext,
    string UserContent,
    IReadOnlyDictionary<string, JsonElement> JsonSchema,
    int MaxOutputTokens = 2048,
    IReadOnlyList<AiAttachment>? Attachments = null);

public sealed record AiStructuredResult(
    JsonElement Output,
    string Provider,
    string Model,
    Guid? PromptVersionId,
    string PromptVersion,
    string PromptHash,
    int InputTokens,
    int OutputTokens,
    long CostMicros,
    int LatencyMs);
