using Nexo.Application.Features.Financial;
using System.Text.Json;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Application.Modules.Interpreter;

public class VoidMovementUseCase
{
    private readonly IFinancialMovementRepository _movementRepo;
    private readonly IMovementAuditLogRepository  _auditLogRepo;
    private readonly IUnitOfWork                  _uow;
    private readonly ICurrentTenant               _currentTenant;
    private readonly ICurrentUser                 _currentUser;
    private readonly VerticalLedgerPostingService _ledger;

    public VoidMovementUseCase(
        IFinancialMovementRepository movementRepo,
        IMovementAuditLogRepository  auditLogRepo,
        IUnitOfWork                  uow,
        ICurrentTenant               currentTenant,
        ICurrentUser                 currentUser,
        VerticalLedgerPostingService ledger)
    {
        _ledger        = ledger;
        _movementRepo  = movementRepo;
        _auditLogRepo  = auditLogRepo;
        _uow           = uow;
        _currentTenant = currentTenant;
        _currentUser   = currentUser;
    }

    public async Task<VoidMovementResponse> ExecuteAsync(
        VoidMovementCommand command,
        CancellationToken   ct = default)
    {
        var tenantId = _currentTenant.Id;
        var userId   = _currentUser.UserId;

        var movement = await _movementRepo.GetByIdAsync(command.MovementId, ct)
            ?? throw new NotFoundException("Movement", command.MovementId);

        var previousState = JsonSerializer.Serialize(new
        {
            movement.Status, movement.Amount, movement.Direction, movement.Nature
        });

        var wasConfirmed = movement.Status == MovementStatus.Confirmed;
        movement.Void();

        var auditLog = MovementAuditLog.Record(
            tenantId:          tenantId,
            movementId:        movement.Id,
            action:            "Voided",
            changedBy:         userId,
            previousStateJson: previousState,
            newStateJson:      JsonSerializer.Serialize(new { movement.Status }));

        await _uow.ExecuteInTransactionAsync(async innerCt =>
        {
            _movementRepo.Update(movement);
            await _auditLogRepo.AddAsync(auditLog, innerCt);
            if (wasConfirmed) await _ledger.RetireMovementAsync(movement, innerCt);   // reversed in the livro
            await _movementRepo.SaveChangesAsync(innerCt);
        }, ct);

        return new VoidMovementResponse(movement.Id, movement.Status.ToString(), movement.UpdatedAt);
    }
}
