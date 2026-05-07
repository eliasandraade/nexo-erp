namespace Nexo.Domain.Modules.Interpreter;

// Provides compact, LLM-ready context built from confirmed movement history.
// Rebuild is async and decoupled from the confirmation flow — never blocks UX.
public interface IMovementMemoryService
{
    // Returns the current compact summary JSON for injection into prompts.
    Task<string> GetCompactContextAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    // Triggered asynchronously after confirmation (via event/job — never inline).
    Task RebuildProfileAsync(Guid tenantId, Guid? userId, CancellationToken ct = default);
}
