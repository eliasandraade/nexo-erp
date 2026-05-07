using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Application.Modules.Interpreter;

// Triggered asynchronously after movement confirmation (never inline in UX flow).
// Can also be triggered manually via API (admin/background job entry point).
// Rebuilds the compact summary JSONB used to seed interpretation prompts.
public class RebuildMovementMemoryProfileUseCase
{
    private readonly IMovementMemoryService _memoryService;

    public RebuildMovementMemoryProfileUseCase(IMovementMemoryService memoryService)
    {
        _memoryService = memoryService;
    }

    public async Task ExecuteAsync(
        RebuildMovementMemoryProfileCommand command,
        CancellationToken                   ct = default)
    {
        await _memoryService.RebuildProfileAsync(command.TenantId, command.UserId, ct);
    }
}
