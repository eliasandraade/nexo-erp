using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Interpreter;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Api.Controllers.Modules.Interpreter;

/// <summary>
/// Operational Interpretation Engine — movement lifecycle.
///
/// Flow:
///   POST /interpreter/analyze          → always creates Draft
///   POST /movements/{id}/confirm       → Draft → Confirmed (only path)
///   POST /movements/{id}/reprocess     → re-run pipeline → Draft
///   POST /movements/{id}/void          → → Voided
///   GET  /movements                    → paginated list with filters
///   GET  /movements/{id}               → detail with extraction + suggestion + audit
/// </summary>
[ApiController]
[Authorize]
public class MovementsController : ControllerBase
{
    private readonly AnalyzeMovementUseCase           _analyzeUseCase;
    private readonly ConfirmMovementUseCase           _confirmUseCase;
    private readonly ReprocessMovementUseCase         _reprocessUseCase;
    private readonly VoidMovementUseCase              _voidUseCase;
    private readonly IFinancialMovementRepository     _movementRepo;
    private readonly IExtractionResultRepository      _extractionRepo;
    private readonly IInterpretationSuggestionRepository _suggestionRepo;
    private readonly IMovementAttachmentRepository    _attachmentRepo;
    private readonly IMovementAuditLogRepository      _auditLogRepo;
    private readonly ICurrentTenant                   _currentTenant;
    private readonly ICurrentUser                     _currentUser;

    public MovementsController(
        AnalyzeMovementUseCase               analyzeUseCase,
        ConfirmMovementUseCase               confirmUseCase,
        ReprocessMovementUseCase             reprocessUseCase,
        VoidMovementUseCase                  voidUseCase,
        IFinancialMovementRepository         movementRepo,
        IExtractionResultRepository          extractionRepo,
        IInterpretationSuggestionRepository  suggestionRepo,
        IMovementAttachmentRepository        attachmentRepo,
        IMovementAuditLogRepository          auditLogRepo,
        ICurrentTenant                       currentTenant,
        ICurrentUser                         currentUser)
    {
        _analyzeUseCase   = analyzeUseCase;
        _confirmUseCase   = confirmUseCase;
        _reprocessUseCase = reprocessUseCase;
        _voidUseCase      = voidUseCase;
        _movementRepo     = movementRepo;
        _extractionRepo   = extractionRepo;
        _suggestionRepo   = suggestionRepo;
        _attachmentRepo   = attachmentRepo;
        _auditLogRepo     = auditLogRepo;
        _currentTenant    = currentTenant;
        _currentUser      = currentUser;
    }

    // ── Analyze ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts and interprets a movement from text or a previously uploaded attachment.
    /// Always creates a Draft — never confirms automatically.
    /// Returns extraction fields with FieldStatus per field for frontend rendering.
    /// </summary>
    [HttpPost("api/v1/interpreter/analyze")]
    public async Task<ActionResult<AnalyzeMovementResponse>> Analyze(
        [FromBody] AnalyzeMovementRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text) && request.AttachmentId is null)
            return BadRequest(new { error = "Either 'text' or 'attachmentId' is required." });

        if (!Enum.TryParse<InputSourceType>(request.InputSource, ignoreCase: true, out var source))
            return BadRequest(new { error = $"Invalid inputSource '{request.InputSource}'." });

        try
        {
            var command = new AnalyzeMovementCommand(
                TenantId:    _currentTenant.Id,
                UserId:      _currentUser.UserId,
                Text:        request.Text,
                AttachmentId: request.AttachmentId,
                InputSource: source);

            var response = await _analyzeUseCase.ExecuteAsync(command, ct);
            return Ok(response);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (DomainException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── Confirm ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Confirms a Draft movement with the final user-validated values.
    /// Records UserCorrections for any field that differs from the original suggestion.
    /// Creates an audit log entry. Triggers async memory profile rebuild.
    /// </summary>
    [HttpPost("api/v1/movements/{draftId:guid}/confirm")]
    public async Task<ActionResult<ConfirmMovementResponse>> Confirm(
        Guid draftId,
        [FromBody] ConfirmMovementRequest request,
        CancellationToken ct)
    {
        if (!Enum.TryParse<MovementDirection>(request.Direction, ignoreCase: true, out var direction))
            return BadRequest(new { error = $"Invalid direction '{request.Direction}'." });

        if (!Enum.TryParse<MovementNature>(request.Nature, ignoreCase: true, out var nature))
            return BadRequest(new { error = $"Invalid nature '{request.Nature}'." });

        if (!Enum.TryParse<FinancialContextType>(request.ContextType, ignoreCase: true, out var contextType))
            return BadRequest(new { error = $"Invalid contextType '{request.ContextType}'." });

        if (request.Amount <= 0)
            return BadRequest(new { error = "Amount must be greater than zero." });

        if (request.Date > DateOnly.FromDateTime(DateTime.UtcNow))
            return BadRequest(new { error = "Date cannot be in the future." });

        try
        {
            var command = new ConfirmMovementCommand(
                TenantId:            _currentTenant.Id,
                UserId:              _currentUser.UserId,
                DraftId:             draftId,
                Amount:              request.Amount,
                Date:                request.Date,
                Description:         request.Description,
                Direction:           direction,
                Nature:              nature,
                CategoryId:          request.CategoryId,
                ContextType:         contextType,
                ContextId:           request.ContextId,
                AccountId:           request.AccountId,
                OriginalSuggestionId: request.OriginalSuggestionId,
                SupplierId:          request.SupplierId);

            var response = await _confirmUseCase.ExecuteAsync(command, ct);
            return Ok(response);
        }
        catch (NotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (DomainException ex)   { return BadRequest(new { error = ex.Message }); }
    }

    // ── Reprocess ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-runs the full analysis pipeline on an existing movement.
    /// Creates a new ExtractionResult + InterpretationSuggestion, appends a ReprocessLog.
    /// Resets status to Draft — never auto-confirms.
    /// </summary>
    [HttpPost("api/v1/movements/{id:guid}/reprocess")]
    public async Task<ActionResult<ReprocessMovementResponse>> Reprocess(
        Guid id,
        [FromBody] ReprocessMovementRequest request,
        CancellationToken ct)
    {
        if (!Enum.TryParse<TriggerReason>(request.Reason, ignoreCase: true, out var reason))
            return BadRequest(new { error = $"Invalid reason '{request.Reason}'." });

        AnalyzerProvider? forceProvider = null;
        if (!string.IsNullOrWhiteSpace(request.ForceAnalyzer))
        {
            if (!Enum.TryParse<AnalyzerProvider>(request.ForceAnalyzer, ignoreCase: true, out var p))
                return BadRequest(new { error = $"Invalid forceAnalyzer '{request.ForceAnalyzer}'." });
            forceProvider = p;
        }

        try
        {
            var command = new ReprocessMovementCommand(
                TenantId:      _currentTenant.Id,
                UserId:        _currentUser.UserId,
                MovementId:    id,
                Reason:        reason,
                ForceAnalyzer: forceProvider,
                Notes:         request.Notes);

            var response = await _reprocessUseCase.ExecuteAsync(command, ct);
            return Ok(response);
        }
        catch (NotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (DomainException ex)   { return BadRequest(new { error = ex.Message }); }
    }

    // ── Void ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Voids a movement (Draft or Confirmed). Creates an audit log entry.
    /// Voided movements cannot be recovered — create a new movement to correct.
    /// </summary>
    [HttpPost("api/v1/movements/{id:guid}/void")]
    public async Task<ActionResult<VoidMovementResponse>> Void(
        Guid id,
        CancellationToken ct)
    {
        try
        {
            var command = new VoidMovementCommand(
                TenantId:   _currentTenant.Id,
                UserId:     _currentUser.UserId,
                MovementId: id);

            var response = await _voidUseCase.ExecuteAsync(command, ct);
            return Ok(response);
        }
        catch (NotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (DomainException ex)   { return BadRequest(new { error = ex.Message }); }
    }

    // ── List ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a paginated list of movements scoped to a context (contextType + contextId).
    /// Supports date range and status filters.
    /// </summary>
    [HttpGet("api/v1/movements")]
    public async Task<ActionResult<MovementListResponse>> List(
        [FromQuery] string  contextType,
        [FromQuery] Guid    contextId,
        [FromQuery] string? status    = null,
        [FromQuery] string? from      = null,
        [FromQuery] string? to        = null,
        [FromQuery] int     page      = 1,
        [FromQuery] int     pageSize  = 20,
        CancellationToken ct          = default)
    {
        if (!Enum.TryParse<FinancialContextType>(contextType, ignoreCase: true, out var ctxType))
            return BadRequest(new { error = $"Invalid contextType '{contextType}'." });

        MovementStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<MovementStatus>(status, ignoreCase: true, out var s))
                return BadRequest(new { error = $"Invalid status '{status}'." });
            statusFilter = s;
        }

        if (page < 1)    page     = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        var fromDate = ParseDate(from);
        var toDate   = ParseDate(to);

        var items = await _movementRepo.GetByContextAsync(
            ctxType, contextId, fromDate, toDate, statusFilter, page, pageSize, ct);

        var total = await _movementRepo.CountByContextAsync(
            ctxType, contextId, fromDate, toDate, statusFilter, ct);

        return Ok(new MovementListResponse(
            Items: items.Select(m => new MovementListItemResponse(
                Id:          m.Id,
                Direction:   m.Direction.ToString(),
                Nature:      m.Nature.ToString(),
                Amount:      m.Amount,
                Date:        m.Date,
                Description: m.Description,
                ContextType: m.ContextType.ToString(),
                ContextId:   m.ContextId,
                SupplierId:  m.SupplierId,
                Status:      m.Status.ToString(),
                CreatedAt:   m.CreatedAt)).ToList(),
            TotalCount: total,
            Page:       page,
            PageSize:   pageSize));
    }

    // ── Detail ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns full movement detail including the latest extraction,
    /// latest interpretation suggestion, attachments, and audit log.
    /// </summary>
    [HttpGet("api/v1/movements/{id:guid}")]
    public async Task<ActionResult<MovementDetailFullResponse>> GetById(
        Guid id,
        CancellationToken ct)
    {
        var movement = await _movementRepo.GetByIdAsync(id, ct);
        if (movement is null)
            return NotFound(new { error = "Movement not found." });

        var extraction  = await _extractionRepo.GetLatestByMovementIdAsync(id, ct);
        var suggestion  = await _suggestionRepo.GetLatestByMovementIdAsync(id, ct);
        var attachments = await _attachmentRepo.GetByMovementIdAsync(id, ct);
        var auditLogs   = await _auditLogRepo.GetByMovementIdAsync(id, ct);

        return Ok(new MovementDetailFullResponse(
            Movement: new MovementDetailsResponse(
                Id:                   movement.Id,
                Direction:            movement.Direction.ToString(),
                Nature:               movement.Nature.ToString(),
                Amount:               movement.Amount,
                Date:                 movement.Date,
                Description:          movement.Description,
                NormalizedDescription: movement.NormalizedDescription,
                CategoryId:           movement.CategoryId,
                ContextType:          movement.ContextType.ToString(),
                ContextId:            movement.ContextId,
                AccountId:            movement.AccountId,
                Status:               movement.Status.ToString(),
                CreatedAt:            movement.CreatedAt,
                UpdatedAt:            movement.UpdatedAt),
            Attachments: attachments.Select(a => new AttachmentDto(
                a.Id, a.FileName, a.ContentType, a.SizeBytes, a.CreatedAt)).ToList(),
            Extraction: extraction is null ? null : MapExtraction(extraction),
            Suggestion: suggestion is null ? null : MapSuggestion(suggestion),
            AuditLog: auditLogs.Select(a => new AuditLogDto(
                a.Id, a.Action, a.ChangedBy, a.CreatedAt)).ToList()));
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private static ExtractionSummaryDto MapExtraction(ExtractionResult e) =>
        new(
            Amount:      new DecimalFieldDto(e.DetectedAmount, e.AmountConfidence, e.AmountStatus.ToString(), e.AnalyzerProvider.ToString()),
            Date:        new DateFieldDto(e.DetectedDate?.ToString("yyyy-MM-dd"), e.DateConfidence, e.DateStatus.ToString(), e.AnalyzerProvider.ToString()),
            Payee:       new StringFieldDto(e.DetectedPayee, e.PayeeConfidence, e.PayeeStatus.ToString(), e.AnalyzerProvider.ToString()),
            Account:     new StringFieldDto(e.DetectedAccount, e.AccountConfidence, e.AccountStatus.ToString(), e.AnalyzerProvider.ToString()),
            AnalyzerUsed: e.AnalyzerProvider.ToString());

    private static MovementSuggestionDto MapSuggestion(InterpretationSuggestion s) =>
        new(
            Direction: new FieldSuggestionDto(s.SuggestedDirection.ToString(), null, s.DirectionSource.ToString()),
            Nature:    new FieldSuggestionDto(s.SuggestedNature.ToString(), null, s.NatureSource.ToString()),
            Category:  new CategorySuggestionDto(s.SuggestedCategoryId, null, s.CategorySource.ToString()),
            Context:   new ContextSuggestionDto(s.SuggestedContextType?.ToString(), s.SuggestedContextId, null, s.ContextSource.ToString()),
            Account:   new AccountSuggestionDto(s.SuggestedAccountId, null, s.AccountSource.ToString()));

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateOnly.TryParseExact(value, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d : null;
    }
}

// ── Response types unique to detail view ─────────────────────────────────────

public record MovementDetailFullResponse(
    MovementDetailsResponse         Movement,
    IReadOnlyList<AttachmentDto>    Attachments,
    ExtractionSummaryDto?           Extraction,
    MovementSuggestionDto?          Suggestion,
    IReadOnlyList<AuditLogDto>      AuditLog);

public record AttachmentDto(
    Guid     Id,
    string   FileName,
    string   ContentType,
    long     SizeBytes,
    DateTime CreatedAt);

public record AuditLogDto(
    Guid     Id,
    string   Action,
    Guid     ChangedBy,
    DateTime CreatedAt);
