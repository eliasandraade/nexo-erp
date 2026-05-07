namespace Nexo.Domain.Modules.Interpreter;

/// <summary>
/// One row per AI engine invocation.
/// NOT a TenantEntity — no Global Query Filter. Platform admin reads across all tenants.
/// TenantId is stored for filtering/aggregation by the admin controller.
/// </summary>
public class InterpreterTelemetry
{
    public Guid     Id                    { get; private set; }
    public Guid     TenantId              { get; private set; }
    public Guid?    UserId                { get; private set; }
    public Guid?    MovementId            { get; private set; }
    public string   OperationType         { get; private set; } = string.Empty; // Analyze | Reprocess | TestConsole
    public string   Provider              { get; private set; } = string.Empty;
    public string   PromptType            { get; private set; } = string.Empty;
    public string   PromptVersion         { get; private set; } = string.Empty;
    public string   PromptHash            { get; private set; } = string.Empty;
    public int      InputTokens           { get; private set; }
    public int      OutputTokens          { get; private set; }
    public long     EstimatedCostMicros   { get; private set; }
    public int      DurationMs            { get; private set; }
    public bool     Success               { get; private set; }
    public string?  ErrorMessage          { get; private set; }
    public bool     FallbackUsed          { get; private set; }
    public string?  FallbackFromProvider  { get; private set; }
    public string   AnalyzerChainJson     { get; private set; } = "[]"; // JSON array
    public int      RequiresInputCount    { get; private set; }
    public double   AmountConfidence      { get; private set; }
    public double   DateConfidence        { get; private set; }
    public string?  RawPrompt             { get; private set; }
    public string?  RawResponse           { get; private set; }
    public DateTime CreatedAt             { get; private set; }

    private InterpreterTelemetry() { }

    public static InterpreterTelemetry Create(
        Guid    tenantId,
        Guid?   userId,
        Guid?   movementId,
        string  operationType,
        string  provider,
        string  promptType,
        string  promptVersion,
        string  promptHash,
        int     inputTokens,
        int     outputTokens,
        long    estimatedCostMicros,
        int     durationMs,
        bool    success,
        string? errorMessage,
        bool    fallbackUsed,
        string? fallbackFromProvider,
        string  analyzerChainJson,
        int     requiresInputCount,
        double  amountConfidence,
        double  dateConfidence,
        string? rawPrompt    = null,
        string? rawResponse  = null)
    {
        return new InterpreterTelemetry
        {
            Id                   = Guid.NewGuid(),
            TenantId             = tenantId,
            UserId               = userId,
            MovementId           = movementId,
            OperationType        = operationType,
            Provider             = provider,
            PromptType           = promptType,
            PromptVersion        = promptVersion,
            PromptHash           = promptHash,
            InputTokens          = inputTokens,
            OutputTokens         = outputTokens,
            EstimatedCostMicros  = estimatedCostMicros,
            DurationMs           = durationMs,
            Success              = success,
            ErrorMessage         = errorMessage,
            FallbackUsed         = fallbackUsed,
            FallbackFromProvider = fallbackFromProvider,
            AnalyzerChainJson    = analyzerChainJson,
            RequiresInputCount   = requiresInputCount,
            AmountConfidence     = amountConfidence,
            DateConfidence       = dateConfidence,
            RawPrompt            = rawPrompt,
            RawResponse          = rawResponse,
            CreatedAt            = DateTime.UtcNow,
        };
    }
}
