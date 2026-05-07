using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Application.Modules.Interpreter;

// Rule: confirmation is the ONLY way a movement leaves Draft status.
// Any field that differs from the original suggestion is recorded as a UserCorrection
// and used as dataset for future interpretation improvement.
// Memory profile rebuild is triggered asynchronously AFTER commit — never blocks UX.
public class ConfirmMovementUseCase
{
    private readonly IFinancialMovementRepository        _movementRepo;
    private readonly IInterpretationSuggestionRepository _suggestionRepo;
    private readonly IUserCorrectionRepository           _correctionRepo;
    private readonly IMovementAuditLogRepository         _auditLogRepo;
    private readonly IDescriptionNormalizer              _normalizer;
    private readonly IMovementMemoryService              _memoryService;
    private readonly IUnitOfWork                         _uow;
    private readonly ICurrentTenant                      _currentTenant;
    private readonly ICurrentUser                        _currentUser;
    private readonly ILogger<ConfirmMovementUseCase>     _logger;

    public ConfirmMovementUseCase(
        IFinancialMovementRepository        movementRepo,
        IInterpretationSuggestionRepository suggestionRepo,
        IUserCorrectionRepository           correctionRepo,
        IMovementAuditLogRepository         auditLogRepo,
        IDescriptionNormalizer              normalizer,
        IMovementMemoryService              memoryService,
        IUnitOfWork                         uow,
        ICurrentTenant                      currentTenant,
        ICurrentUser                        currentUser,
        ILogger<ConfirmMovementUseCase>     logger)
    {
        _movementRepo   = movementRepo;
        _suggestionRepo = suggestionRepo;
        _correctionRepo = correctionRepo;
        _auditLogRepo   = auditLogRepo;
        _normalizer     = normalizer;
        _memoryService  = memoryService;
        _uow            = uow;
        _currentTenant  = currentTenant;
        _currentUser    = currentUser;
        _logger         = logger;
    }

    public async Task<ConfirmMovementResponse> ExecuteAsync(
        ConfirmMovementCommand command,
        CancellationToken      ct = default)
    {
        var tenantId  = _currentTenant.Id;
        var userId    = _currentUser.UserId;
        var startedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "Interpreter.Confirm started. DraftId={DraftId} TenantId={TenantId}",
            command.DraftId, tenantId);

        var movement = await _movementRepo.GetDraftByIdAsync(command.DraftId, ct)
            ?? throw new NotFoundException("Draft movement", command.DraftId);

        var suggestion = await _suggestionRepo.GetByIdAsync(command.OriginalSuggestionId, ct)
            ?? throw new NotFoundException("InterpretationSuggestion", command.OriginalSuggestionId);

        // Capture state before mutation for audit log.
        var previousState = SerializeMovement(movement);

        // Normalize description deterministically before confirming.
        var normalizedDescription = _normalizer.Normalize(command.Description, tenantId);

        movement.UpdateFields(
            direction:             command.Direction,
            nature:                command.Nature,
            amount:                command.Amount,
            date:                  command.Date,
            description:           command.Description,
            normalizedDescription: normalizedDescription,
            contextType:           command.ContextType,
            contextId:             command.ContextId,
            categoryId:            command.CategoryId,
            accountId:             command.AccountId);

        movement.Confirm();

        var newState = SerializeMovement(movement);

        // Compute corrections: every field the user changed from the original suggestion.
        var corrections = BuildCorrections(tenantId, userId, suggestion, command);

        if (corrections.Count > 0)
            suggestion.MarkRejected();
        else
            suggestion.MarkAccepted();

        var auditLog = MovementAuditLog.Record(
            tenantId:          tenantId,
            movementId:        movement.Id,
            action:            "Confirmed",
            changedBy:         userId,
            previousStateJson: previousState,
            newStateJson:      newState);

        await _uow.ExecuteInTransactionAsync(async innerCt =>
        {
            _movementRepo.Update(movement);
            _suggestionRepo.Update(suggestion);

            if (corrections.Count > 0)
                await _correctionRepo.AddRangeAsync(corrections, innerCt);

            await _auditLogRepo.AddAsync(auditLog, innerCt);
            await _movementRepo.SaveChangesAsync(innerCt);
        }, ct);

        var elapsedMs = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
        _logger.LogInformation(
            "Interpreter.Confirm completed. DraftId={DraftId} Accepted={Accepted} " +
            "Corrections={CorrectionCount} ElapsedMs={ElapsedMs:F0}",
            movement.Id, corrections.Count == 0, corrections.Count, elapsedMs);

        // Async rebuild: decoupled from UX — fires after commit, does not block response.
        // In production this should be dispatched via a background job / domain event.
        _ = _memoryService.RebuildProfileAsync(tenantId, userId);

        var correctionSummaries = corrections
            .Select(c => new CorrectionSummaryDto(
                c.CorrectionType.ToString(),
                c.OriginalValue,
                c.CorrectedValue))
            .ToList();

        return new ConfirmMovementResponse(
            Id:           movement.Id,
            Status:       movement.Status.ToString(),
            ConfirmedAt:  movement.UpdatedAt,
            Corrections:  correctionSummaries);
    }

    private static List<UserCorrection> BuildCorrections(
        Guid                     tenantId,
        Guid                     userId,
        InterpretationSuggestion suggestion,
        ConfirmMovementCommand   command)
    {
        var corrections = new List<UserCorrection>();

        void AddIfChanged(
            CorrectionType type,
            string?        original,
            string?        corrected)
        {
            if (original != corrected)
                corrections.Add(UserCorrection.Create(
                    tenantId:       tenantId,
                    suggestionId:   suggestion.Id,
                    movementId:     command.DraftId,
                    userId:         userId,
                    correctionType: type,
                    originalValue:  original ?? string.Empty,
                    correctedValue: corrected ?? string.Empty,
                    rawUserText:    null));
        }

        AddIfChanged(CorrectionType.Direction,
            suggestion.SuggestedDirection.ToString(),
            command.Direction.ToString());

        AddIfChanged(CorrectionType.Nature,
            suggestion.SuggestedNature.ToString(),
            command.Nature.ToString());

        AddIfChanged(CorrectionType.Category,
            suggestion.SuggestedCategoryId?.ToString(),
            command.CategoryId?.ToString());

        AddIfChanged(CorrectionType.ContextId,
            suggestion.SuggestedContextId?.ToString(),
            command.ContextId?.ToString());

        AddIfChanged(CorrectionType.Description,
            null,
            null); // descriptions are never pre-filled by suggestion, skip

        return corrections;
    }

    private static string SerializeMovement(FinancialMovement m) =>
        JsonSerializer.Serialize(new
        {
            m.Direction, m.Nature, m.Amount, m.Date,
            m.Description, m.CategoryId, m.ContextType,
            m.ContextId, m.AccountId, m.Status
        });
}
