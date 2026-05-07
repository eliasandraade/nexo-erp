using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;
using System.Text.Json;

namespace Nexo.Infrastructure.Modules.Interpreter;

/// <summary>
/// Persists one InterpreterTelemetry row per AI invocation.
/// Uses a separate DbContext scope to ensure telemetry is always saved,
/// even if the parent operation rolls back.
/// Errors are swallowed and logged — telemetry must never break the main flow.
/// </summary>
public sealed class TelemetryWriterService : ITelemetryWriter
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<TelemetryWriterService> _logger;

    public TelemetryWriterService(IServiceProvider sp, ILogger<TelemetryWriterService> logger)
    {
        _sp     = sp;
        _logger = logger;
    }

    public async Task WriteAsync(TelemetryEntry e, CancellationToken ct = default)
    {
        try
        {
            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();

            var chain = JsonSerializer.Serialize(e.AnalyzerChain);

            var row = InterpreterTelemetry.Create(
                tenantId:             e.TenantId,
                userId:               e.UserId,
                movementId:           e.MovementId,
                operationType:        e.OperationType,
                provider:             e.Provider,
                promptType:           e.PromptType,
                promptVersion:        e.PromptVersion,
                promptHash:           e.PromptHash,
                inputTokens:          e.InputTokens,
                outputTokens:         e.OutputTokens,
                estimatedCostMicros:  e.EstimatedCostMicros,
                durationMs:           e.DurationMs,
                success:              e.Success,
                errorMessage:         e.ErrorMessage,
                fallbackUsed:         e.FallbackUsed,
                fallbackFromProvider: e.FallbackFromProvider,
                analyzerChainJson:    chain,
                requiresInputCount:   e.RequiresInputCount,
                amountConfidence:     e.AmountConfidence,
                dateConfidence:       e.DateConfidence,
                rawPrompt:            e.RawPrompt,
                rawResponse:          e.RawResponse);

            db.InterpreterTelemetry.Add(row);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TelemetryWriter: failed to persist telemetry row. Swallowing.");
        }
    }
}
