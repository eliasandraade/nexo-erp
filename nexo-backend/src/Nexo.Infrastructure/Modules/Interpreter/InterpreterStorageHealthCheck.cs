using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nexo.Application.Modules.Interpreter.Interfaces;

namespace Nexo.Infrastructure.Modules.Interpreter;

/// <summary>
/// Verifies that the attachment storage backend is reachable.
/// Writes and deletes a probe file during each check.
/// </summary>
public sealed class InterpreterStorageHealthCheck : IHealthCheck
{
    private readonly IAttachmentStorage _storage;

    public InterpreterStorageHealthCheck(IAttachmentStorage storage)
        => _storage = storage;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken  cancellationToken = default)
    {
        try
        {
            await _storage.PingAsync(cancellationToken);
            return HealthCheckResult.Healthy("Attachment storage is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Attachment storage is unreachable.", ex);
        }
    }
}
