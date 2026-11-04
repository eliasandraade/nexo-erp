using Nexo.Application.Features.Financial;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Application.Modules.Interpreter;

// Reprocessing runs the full analysis pipeline again on an existing movement.
// Always resets to Draft — never auto-confirms.
// Creates new ExtractionResult + InterpretationSuggestion, appends a ReprocessLog.
// Original results are never overwritten (append-only for traceability).
public class ReprocessMovementUseCase
{
    private readonly IAnalysisRunner                   _runner;
    private readonly IInterpretationService              _interpretationService;
    private readonly IMovementMemoryService              _memoryService;
    private readonly IDescriptionNormalizer              _normalizer;
    private readonly IFinancialMovementRepository        _movementRepo;
    private readonly IExtractionResultRepository         _extractionRepo;
    private readonly IInterpretationSuggestionRepository _suggestionRepo;
    private readonly IMovementAttachmentRepository       _attachmentRepo;
    private readonly IReprocessLogRepository             _reprocessLogRepo;
    private readonly IUnitOfWork                         _uow;
    private readonly ICurrentTenant                      _currentTenant;
    private readonly ICurrentUser                        _currentUser;
    private readonly ILogger<ReprocessMovementUseCase>  _logger;
    private readonly ITelemetryWriter?                  _telemetry;
    private readonly VerticalLedgerPostingService        _ledger;

    public ReprocessMovementUseCase(
        IAnalysisRunner                    runner,
        IInterpretationService               interpretationService,
        IMovementMemoryService               memoryService,
        IDescriptionNormalizer               normalizer,
        IFinancialMovementRepository         movementRepo,
        IExtractionResultRepository          extractionRepo,
        IInterpretationSuggestionRepository  suggestionRepo,
        IMovementAttachmentRepository        attachmentRepo,
        IReprocessLogRepository              reprocessLogRepo,
        IUnitOfWork                          uow,
        ICurrentTenant                       currentTenant,
        ICurrentUser                         currentUser,
        ILogger<ReprocessMovementUseCase>    logger,
        VerticalLedgerPostingService         ledger,
        ITelemetryWriter?                    telemetry = null)
    {
        _ledger                = ledger;
        _runner                = runner;
        _interpretationService = interpretationService;
        _memoryService         = memoryService;
        _normalizer            = normalizer;
        _movementRepo          = movementRepo;
        _extractionRepo        = extractionRepo;
        _suggestionRepo        = suggestionRepo;
        _attachmentRepo        = attachmentRepo;
        _reprocessLogRepo      = reprocessLogRepo;
        _uow                   = uow;
        _currentTenant         = currentTenant;
        _currentUser           = currentUser;
        _logger                = logger;
        _telemetry             = telemetry;
    }

    public async Task<ReprocessMovementResponse> ExecuteAsync(
        ReprocessMovementCommand command,
        CancellationToken        ct = default)
    {
        var tenantId  = _currentTenant.Id;
        var userId    = _currentUser.UserId;
        var startedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "Interpreter.Reprocess started. MovementId={MovementId} Reason={Reason} ForceAnalyzer={ForceAnalyzer}",
            command.MovementId, command.Reason, command.ForceAnalyzer?.ToString() ?? "auto");

        var movement = await _movementRepo.GetByIdAsync(command.MovementId, ct)
            ?? throw new NotFoundException("Movement", command.MovementId);

        var previousExtraction = await _extractionRepo.GetLatestByMovementIdAsync(movement.Id, ct)
            ?? throw new InvalidOperationException($"No ExtractionResult found for movement {movement.Id}.");

        var previousSuggestion = await _suggestionRepo.GetLatestByMovementIdAsync(movement.Id, ct)
            ?? throw new InvalidOperationException($"No InterpretationSuggestion found for movement {movement.Id}.");

        // Snapshot previous state for diff computation.
        var previousExtractionSnapshot = SnapshotExtraction(previousExtraction);
        var previousSuggestionSnapshot = SnapshotSuggestion(previousSuggestion);

        // Reset to Draft — human must re-confirm (a confirmed one leaves the livro until then).
        var wasConfirmed = movement.Status == MovementStatus.Confirmed;
        movement.ResetToDraft();

        // Resolve attachment if the movement has one.
        string? storageKey = null;
        var attachments = await _attachmentRepo.GetByMovementIdAsync(movement.Id, ct);
        if (attachments.Count > 0)
            storageKey = attachments[0].StorageKey;

        var inputSource = storageKey is not null ? InputSourceType.File : InputSourceType.Text;
        var analysisInput = new AnalysisInput(
            Source:     inputSource,
            RawText:    movement.Description,
            StorageKey: storageKey,
            TenantId:   tenantId,
            UserId:     userId);

        // A forced provider runs alone (no silent substitute); otherwise the usable providers in order, with fallback.
        var run = await _runner.RunAsync(analysisInput, command.ForceAnalyzer, "Reprocess", ct);
        var analysisOutput = run.Output;

        // The log records the analyzer and prompt that actually ran.
        var reprocessLog = ReprocessLog.Start(
            tenantId:                  tenantId,
            movementId:                movement.Id,
            triggeredBy:               userId,
            reason:                    command.Reason,
            previousExtractionResultId: previousExtraction.Id,
            previousSuggestionId:      previousSuggestion.Id,
            analyzerProvider:          run.Provider,
            promptVersion:             string.IsNullOrEmpty(analysisOutput.Prompt.PromptVersion) ? "-" : analysisOutput.Prompt.PromptVersion);
        var memoryContext  = await _memoryService.GetCompactContextAsync(tenantId, userId, ct);
        var interpretation = await _interpretationService.SuggestAsync(
            analysisOutput, memoryContext, tenantId, userId, ct);

        var normalizedDescription = _normalizer.Normalize(movement.Description, tenantId);

        movement.UpdateFields(
            direction:             interpretation.Direction,
            nature:                interpretation.Nature,
            amount:                analysisOutput.Amount.Value ?? movement.Amount,
            date:                  analysisOutput.Date.Value   ?? movement.Date,
            description:           movement.Description,
            normalizedDescription: normalizedDescription,
            contextType:           interpretation.ContextType ?? movement.ContextType,
            contextId:             interpretation.ContextId,
            categoryId:            interpretation.CategoryId,
            accountId:             interpretation.AccountId);

        var newExtraction = ExtractionResult.Create(tenantId, movement.Id, inputSource,
            movement.Description, analysisOutput);

        var newSuggestion = InterpretationSuggestion.Create(
            tenantId:      tenantId,
            movementId:    movement.Id,
            direction:     interpretation.Direction,     directionSource:  interpretation.DirectionSource,
            nature:        interpretation.Nature,        natureSource:     interpretation.NatureSource,
            categoryId:    interpretation.CategoryId,    categorySource:   interpretation.CategorySource,
            contextType:   interpretation.ContextType,   contextId:        interpretation.ContextId,
            contextSource: interpretation.ContextSource,
            accountId:     interpretation.AccountId,     accountSource:    interpretation.AccountSource);

        // Build diff between previous and new results for observability.
        var diffJson = BuildDiffJson(
            previousExtractionSnapshot, SnapshotExtraction(newExtraction),
            previousSuggestionSnapshot, SnapshotSuggestion(newSuggestion));

        reprocessLog.Complete(newExtraction.Id, newSuggestion.Id, diffJson, command.Notes);

        await _uow.ExecuteInTransactionAsync(async innerCt =>
        {
            _movementRepo.Update(movement);
            await _extractionRepo.AddAsync(newExtraction, innerCt);
            await _suggestionRepo.AddAsync(newSuggestion, innerCt);
            await _reprocessLogRepo.AddAsync(reprocessLog, innerCt);
            if (wasConfirmed) await _ledger.RetireMovementAsync(movement, correction: true, innerCt);
            await _movementRepo.SaveChangesAsync(innerCt);
        }, ct);

        var elapsedMs = (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
        _logger.LogInformation(
            "Interpreter.Reprocess completed. MovementId={MovementId} ReprocessLogId={LogId} " +
            "Provider={Provider} ElapsedMs={ElapsedMs:F0}",
            movement.Id, reprocessLog.Id, run.Provider, elapsedMs);

        if (_telemetry is not null)
        {
            _ = _telemetry.WriteAsync(new TelemetryEntry(
                TenantId:             tenantId,
                UserId:               userId,
                MovementId:           movement.Id,
                OperationType:        "Reprocess",
                Provider:             run.Provider.ToString(),
                PromptType:           analysisOutput.PromptMetadata.PromptType,
                PromptVersion:        analysisOutput.PromptMetadata.PromptVersion,
                PromptHash:           analysisOutput.PromptMetadata.PromptHash,
                InputTokens:          analysisOutput.InputTokens,
                OutputTokens:         analysisOutput.OutputTokens,
                EstimatedCostMicros:  analysisOutput.EstimatedCostMicros,
                DurationMs:           run.DurationMs,
                Success:              true,
                ErrorMessage:         null,
                FallbackUsed:         run.FallbackUsed,
                FallbackFromProvider: run.FallbackFrom?.ToString(),
                AnalyzerChain:        run.Chain,
                RequiresInputCount:   new[] { analysisOutput.Amount.Status, analysisOutput.Date.Status, analysisOutput.Payee.Status, analysisOutput.Account.Status }
                                          .Count(s => s == FieldStatus.RequiresInput),
                AmountConfidence:     analysisOutput.Amount.Confidence,
                DateConfidence:       analysisOutput.Date.Confidence,
                ModelId:              run.ModelId));
        }

        var analyzeResponse = AnalyzeMovementUseCase.BuildResponse(
            movement.Id, analysisOutput, newExtraction, interpretation, newSuggestion);

        return new ReprocessMovementResponse(
            ReprocessLogId: reprocessLog.Id,
            NewDraftId:     movement.Id,
            Extraction:     analyzeResponse.Extraction,
            Suggestion:     analyzeResponse.Suggestion);
    }

    private static string SnapshotExtraction(ExtractionResult e) =>
        JsonSerializer.Serialize(new
        {
            e.DetectedAmount, e.AmountConfidence,
            e.DetectedDate,   e.DateConfidence,
            e.DetectedPayee,  e.PayeeConfidence,
            e.AnalyzerProvider, e.PromptVersion
        });

    private static string SnapshotSuggestion(InterpretationSuggestion s) =>
        JsonSerializer.Serialize(new
        {
            s.SuggestedDirection, s.DirectionSource,
            s.SuggestedNature,    s.NatureSource,
            s.SuggestedCategoryId, s.CategorySource,
            s.SuggestedContextId,  s.ContextSource
        });

    private static string BuildDiffJson(
        string prevExtraction, string newExtraction,
        string prevSuggestion, string newSuggestion) =>
        JsonSerializer.Serialize(new
        {
            extraction = new { before = prevExtraction, after = newExtraction },
            suggestion = new { before = prevSuggestion, after = newSuggestion }
        });
}
