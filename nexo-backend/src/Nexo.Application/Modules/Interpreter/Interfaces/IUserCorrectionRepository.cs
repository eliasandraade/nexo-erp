using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Application.Modules.Interpreter.Interfaces;

public interface IUserCorrectionRepository
{
    Task<IReadOnlyList<UserCorrection>> GetByMovementIdAsync(Guid movementId, CancellationToken ct = default);

    Task<IReadOnlyList<UserCorrection>> GetByTenantAsync(
        Guid              tenantId,
        DateOnly?         from     = null,
        DateOnly?         to       = null,
        CorrectionType?   type     = null,
        CancellationToken ct       = default);

    Task AddRangeAsync(IEnumerable<UserCorrection> corrections, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
