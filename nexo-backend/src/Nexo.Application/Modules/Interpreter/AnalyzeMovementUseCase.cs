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
    private readonly IAnalyzerSelector              _analyzerSelector;
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
        IAnalyzerSelector                    analyzerSelector,
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
        _analyzerSelector      = analyzerSelector;
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

        // 2. Select analyzer: RuleBased first for text-only (zero cost),
        //    LLM for files or when rule extraction is insufficient.
        var analyzer = _analyzerSelector.Select(command.InputSource, tenantId);

        _logger.LogInformation(
            "Interpreter.Analyze started. Source={Source} Analyzer={Analyzer} TenantId={TenantId}",
            command.InputSource, analyzer.Provider, tenantId);

        var analysisInput = new AnalysisInput(
            Source:     command.InputSource,
            RawText:    command.Text,
            StorageKey: storageKey,
            TenantId:   tenantId,
            UserId:     userId);

        // 3. Extract raw data from document/text.
        var analysisOutput = await analyzer.AnalyzeAsync(analysisInput, ct);

        var requiresInputCount = new[] {
            analysisOutput.Amount.Status, analysisOutput.Date.Status,
            analysisOutput.Payee.Status, analysisOutput.Account.Status
        }.Count(s => s == FieldStatus.RequiresInput);

        _logger.LogInformation(
            "Interpreter.Extraction done. Provider={Provider} Amount={Amount} AmountConf={AmountConf:F2} " +
            "DateConf={DateConf:F2} PayeeConf={PayeeConf:F2} RequiresInput={RequiresInput}",
            analyzer.Provider,
            analysisOutput.Amount.Value,
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
                Provider:             analyzer.Provider.ToString(),
                PromptType:           analysisOutput.PromptMetadata.PromptType,
                PromptVersion:        analysisOutput.PromptMetadata.PromptVersion,
                PromptHash:           analysisOutput.PromptMetadata.PromptHash,
                InputTokens:          analysisOutput.InputTokens,
                OutputTokens:         analysisOutput.OutputTokens,
                EstimatedCostMicros:  analysisOutput.EstimatedCostMicros,
                DurationMs:           elapsedMs,
                Success:              true,
                ErrorMessage:         null,
                FallbackUsed:         false,
                FallbackFromProvider: null,
                AnalyzerChain:        [analyzer.Provider.ToString()],
                RequiresInputCount:   requiresInputCount,
                AmountConfidence:     analysisOutput.Amount.Confidence,
                DateConfidence:       analysisOutput.Date.Confidence));
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
            DraftId: draftId,
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
