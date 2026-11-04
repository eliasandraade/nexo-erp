using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Application.Modules.Interpreter;

// Rule: analyze ALWAYS creates a Draft. Never confirms automatically.
// Frontend receives FieldStatus per field and renders accordingly.
public class AnalyzeMovementUseCase
{
    private readonly IAnalysisRunner                _runner;
    private readonly IInterpretationService         _interpretationService;
    private readonly IMovementMemoryService         _memoryService;
    private readonly IDescriptionNormalizer         _normalizer;
    private readonly IFinancialMovementRepository   _movementRepo;
    private readonly IExtractionResultRepository    _extractionRepo;
    private readonly IInterpretationSuggestionRepository _suggestionRepo;
    private readonly IMovementAttachmentRepository  _attachmentRepo;
    private readonly IUnitOfWork                    _uow;
    private readonly ICurrentTenant                 _currentTenant;
    private readonly ICurrentUser                   _currentUser;
    private readonly ILogger<AnalyzeMovementUseCase> _logger;
    private readonly ITelemetryWriter? _telemetry;

    public AnalyzeMovementUseCase(
        IAnalysisRunner                      runner,
        IInterpretationService               interpretationService,
        IMovementMemoryService               memoryService,
        IDescriptionNormalizer               normalizer,
        IFinancialMovementRepository         movementRepo,
        IExtractionResultRepository          extractionRepo,
        IInterpretationSuggestionRepository  suggestionRepo,
        IMovementAttachmentRepository        attachmentRepo,
        IUnitOfWork                          uow,
        ICurrentTenant                       currentTenant,
        ICurrentUser                         currentUser,
        ILogger<AnalyzeMovementUseCase>      logger,
        ITelemetryWriter?                    telemetry = null)
    {
        _runner                = runner;
        _interpretationService = interpretationService;
        _memoryService         = memoryService;
        _normalizer            = normalizer;
        _movementRepo          = movementRepo;
        _extractionRepo        = extractionRepo;
        _suggestionRepo        = suggestionRepo;
        _attachmentRepo        = attachmentRepo;
        _uow                   = uow;
        _currentTenant         = currentTenant;
        _currentUser           = currentUser;
        _logger                = logger;
        _telemetry             = telemetry;
    }

    public async Task<AnalyzeMovementResponse> ExecuteAsync(
        AnalyzeMovementCommand command,
        CancellationToken      ct = default)
    {
        if (command.Text is null && command.AttachmentId is null)
            throw new DomainException("Either Text or AttachmentId is required to analyze a movement.");

        var tenantId  = _currentTenant.Id;
        var userId    = _currentUser.UserId;
        var startedAt = DateTimeOffset.UtcNow;

        // 1. Resolve attachment storage key if provided.
        string? storageKey = null;
        if (command.AttachmentId.HasValue)
        {
            var attachment = await _attachmentRepo.GetByIdAsync(command.AttachmentId.Value, ct)
                ?? throw new NotFoundException("Attachment", command.AttachmentId.Value);
            storageKey = attachment.StorageKey;
        }

        _logger.LogInformation("Interpreter.Analyze started. Source={Source} TenantId={TenantId}", command.InputSource, tenantId);

        var analysisInput = new AnalysisInput(
            Source:     command.InputSource,
            RawText:    command.Text,
            StorageKey: storageKey,
            TenantId:   tenantId,
            UserId:     userId);

        // 2–3. The providers the SuperAdmin made usable, in order (default first, then priority); an expected failure
        //      of one moves on to the next. Nothing usable → ai_provider_unavailable (422), all failing → ai_provider_failed (503).
        var run = await _runner.RunAsync(analysisInput, null, "Analyze", ct);
        var analysisOutput = run.Output;

        var requiresInputCount = new[] {
            analysisOutput.Amount.Status, analysisOutput.Date.Status,
            analysisOutput.Payee.Status, analysisOutput.Account.Status
        }.Count(s => s == FieldStatus.RequiresInput);

        _logger.LogInformation(
            "Interpreter.Extraction done. Provider={Provider} AmountFound={AmountFound} AmountConf={AmountConf:F2} " +
            "DateConf={DateConf:F2} PayeeConf={PayeeConf:F2} RequiresInput={RequiresInput}",
            run.Provider,
            analysisOutput.Amount.Value is not null,
            analysisOutput.Amount.Confidence,
            analysisOutput.Date.Confidence,
            analysisOutput.Payee.Confidence,
            requiresInputCount);

        // 4. Get compact memory context for interpretation prompt (never blocks UX — cached).
        var memoryContext = await _memoryService.GetCompactContextAsync(tenantId, userId, ct);

        // 5. Interpret extracted data into business suggestions.
        var interpretation = await _interpretationService.SuggestAsync(
            analysisOutput, memoryContext, tenantId, userId, ct);

        // 6. Normalize description — deterministic, culture-invariant, never via LLM.
        var rawDescription = command.Text
            ?? analysisOutput.Payee.Value
            ?? string.Empty;
        var normalizedDescription = _normalizer.Normalize(rawDescription, tenantId);

        // 7. Build draft with best available data; unknown fields remain null for user to fill.
        var movement = FinancialMovement.CreateDraft(
            tenantId:             tenantId,
            createdBy:            userId,
            direction:            interpretation.Direction,
            nature:               interpretation.Nature,
            amount:               analysisOutput.Amount.Value ?? 0m,
            date:                 analysisOutput.Date.Value   ?? DateOnly.FromDateTime(DateTime.UtcNow),
            description:          rawDescription.Length > 0 ? rawDescription : "Sem descrição",
            normalizedDescription: normalizedDescription,
            contextType:          interpretation.ContextType ?? FinancialContextType.Obra,
            contextId:            interpretation.ContextId,
            categoryId:           interpretation.CategoryId,
            accountId:            interpretation.AccountId);

        var extractionResult = ExtractionResult.Create(tenantId, movement.Id, command.InputSource,
            command.Text, analysisOutput);

        var suggestion = InterpretationSuggestion.Create(
            tenantId:      tenantId,
            movementId:    movement.Id,
            direction:     interpretation.Direction,     directionSource:  interpretation.DirectionSource,
            nature:        interpretation.Nature,        natureSource:     interpretation.NatureSource,
            categoryId:    interpretation.CategoryId,    categorySource:   interpretation.CategorySource,
            contextType:   interpretation.ContextType,   contextId:        interpretation.ContextId,
            contextSource: interpretation.ContextSource,
            accountId:     interpretation.AccountId,     accountSource:    interpretation.AccountSource);

        // 8. Persist all in one transaction.
        await _uow.ExecuteInTransactionAsync(async innerCt =>
        {
            await _movementRepo.AddAsync(movement, innerCt);
            await _extractionRepo.AddAsync(extractionResult, innerCt);
            await _suggestionRepo.AddAsync(suggestion, innerCt);
            await _movementRepo.SaveChangesAsync(innerCt);
        }, ct);

        var elapsedMs = (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
        _logger.LogInformation(
            "Interpreter.Analyze completed. DraftId={DraftId} ElapsedMs={ElapsedMs} " +
            "Direction={Direction} DirectionSource={DirectionSource} CategoryId={CategoryId}",
            movement.Id, elapsedMs,
            interpretation.Direction, interpretation.DirectionSource, interpretation.CategoryId);

        if (_telemetry is not null)
        {
            _ = _telemetry.WriteAsync(new TelemetryEntry(
                TenantId:             tenantId,
                UserId:               userId,
                MovementId:           movement.Id,
                OperationType:        "Analyze",
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
                RequiresInputCount:   requiresInputCount,
                AmountConfidence:     analysisOutput.Amount.Confidence,
                DateConfidence:       analysisOutput.Date.Confidence,
                ModelId:              run.ModelId));
        }

        return BuildResponse(movement.Id, analysisOutput, extractionResult, interpretation, suggestion);
    }

    internal static AnalyzeMovementResponse BuildResponse(
        Guid                  draftId,
        AnalysisOutput        analysisOutput,
        ExtractionResult      extraction,
        InterpretationOutput  interpretation,
        InterpretationSuggestion suggestion)
    {
        return new AnalyzeMovementResponse(
            DraftId:      draftId,
            SuggestionId: suggestion.Id,
            Extraction: new ExtractionSummaryDto(
                Amount: new DecimalFieldDto(
                    extraction.DetectedAmount,
                    extraction.AmountConfidence,
                    extraction.AmountStatus.ToString(),
                    extraction.AnalyzerProvider.ToString()),
                Date: new DateFieldDto(
                    extraction.DetectedDate?.ToString("yyyy-MM-dd"),
                    extraction.DateConfidence,
                    extraction.DateStatus.ToString(),
                    extraction.AnalyzerProvider.ToString()),
                Payee: new StringFieldDto(
                    extraction.DetectedPayee,
                    extraction.PayeeConfidence,
                    extraction.PayeeStatus.ToString(),
                    extraction.AnalyzerProvider.ToString()),
                Account: new StringFieldDto(
                    extraction.DetectedAccount,
                    extraction.AccountConfidence,
                    extraction.AccountStatus.ToString(),
                    extraction.AnalyzerProvider.ToString()),
                AnalyzerUsed: extraction.AnalyzerProvider.ToString()),
            Suggestion: new MovementSuggestionDto(
                Direction: new FieldSuggestionDto(
                    suggestion.SuggestedDirection.ToString(), null,
                    suggestion.DirectionSource.ToString()),
                Nature: new FieldSuggestionDto(
                    suggestion.SuggestedNature.ToString(), null,
                    suggestion.NatureSource.ToString()),
                Category: new CategorySuggestionDto(
                    suggestion.SuggestedCategoryId, null,
                    suggestion.CategorySource.ToString()),
                Context: new ContextSuggestionDto(
                    suggestion.SuggestedContextType?.ToString(),
                    suggestion.SuggestedContextId, null,
                    suggestion.ContextSource.ToString()),
                Account: new AccountSuggestionDto(
                    suggestion.SuggestedAccountId, null,
                    suggestion.AccountSource.ToString())));
    }
}
