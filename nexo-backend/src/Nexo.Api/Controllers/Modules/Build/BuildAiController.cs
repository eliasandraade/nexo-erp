using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Modules.Build;

namespace Nexo.Api.Controllers.Modules.Build;

/// <summary>
/// Orken Build — Wave 5 AI (company side: Management, module build). Each generation creates a Pending suggestion with its
/// provenance; nothing changes until a person accepts it (whole or selected items). Generation needs the company's BuildAI
/// flag (403 build_ai_disabled) and, to send photos or files, BuildVision (403 build_vision_disabled). Risk analysis (GET) is
/// deterministic and always available. Project documents (also shown in the client portal when marked visible).
/// See docs/build/ai.md and docs/build/internal-api.md (# Wave 5).
/// </summary>
[ApiController]
[Route("api/v1/build")]
[Authorize(Roles = AppRoles.Management)]
[RequireModule("build")]
[ValidateBody]
[BuildFileErrors]
public class BuildAiController : ControllerBase
{
    private readonly BuildAiService _ai;
    private readonly BuildRiskService _risk;
    private readonly BuildDocumentService _documents;

    public BuildAiController(BuildAiService ai, BuildRiskService risk, BuildDocumentService documents)
    {
        _ai = ai;
        _risk = risk;
        _documents = documents;
    }

    // ── Generation ────────────────────────────────────────────────────────────

    /// <summary>Daily log copilot: free text (+ optional photo ids, vision) → structured draft of the day's log.</summary>
    [HttpPost("projects/{projectId:guid}/ai/daily-log-copilot")]
    public async Task<ActionResult<BuildAiSuggestionDto>> DailyLogCopilot(Guid projectId, [FromBody] BuildDailyLogCopilotRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await _ai.DailyLogCopilotAsync(projectId, request, ct));

    /// <summary>Progress interpreter: recent diary (+ optional photos, vision) → suggested % per stage. Needs a provider (no fallback).</summary>
    [HttpPost("projects/{projectId:guid}/ai/progress-interpreter")]
    public async Task<ActionResult<BuildAiSuggestionDto>> ProgressInterpreter(Guid projectId, [FromBody] BuildProgressInterpreterRequest request,
        CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await _ai.ProgressInterpreterAsync(projectId, request, ct));

    /// <summary>Model mapper: categories/zones of a model version → proposed stage bindings (rules without a provider).</summary>
    [HttpPost("projects/{projectId:guid}/ai/model-mapper")]
    public async Task<ActionResult<BuildAiSuggestionDto>> ModelMapper(Guid projectId, [FromBody] BuildModelMapperRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await _ai.ModelMapperAsync(projectId, request, ct));

    /// <summary>Client update from published/approved facts of a period (templated text without a provider).</summary>
    [HttpPost("projects/{projectId:guid}/ai/client-update")]
    public async Task<ActionResult<BuildAiSuggestionDto>> ClientUpdate(Guid projectId, [FromBody] BuildClientUpdateAiRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await _ai.ClientUpdateAsync(projectId, request, ct));

    /// <summary>Weekly client digest (the last 7 days).</summary>
    [HttpPost("projects/{projectId:guid}/ai/weekly-digest")]
    public async Task<ActionResult<BuildAiSuggestionDto>> WeeklyDigest(Guid projectId, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await _ai.WeeklyDigestAsync(projectId, ct));

    // ── Risk ──────────────────────────────────────────────────────────────────

    /// <summary>Deterministic schedule risk (SPI-like, delayed stages, forecast vs contract, late materials, pending change orders, NCs).</summary>
    [HttpGet("projects/{projectId:guid}/risk/delay")]
    public async Task<ActionResult<BuildRiskAnalysisDto>> DelayRisk(Guid projectId, CancellationToken ct) => Ok(await _risk.DelayAsync(projectId, ct));

    /// <summary>Deterministic cost risk (earned value, CPI-like, EAC, overruns, change orders, unlinked materials).</summary>
    [HttpGet("projects/{projectId:guid}/risk/cost")]
    public async Task<ActionResult<BuildRiskAnalysisDto>> CostRisk(Guid projectId, CancellationToken ct) => Ok(await _risk.CostAsync(projectId, ct));

    /// <summary>The LLM explains the deterministic analysis ({ kind: delay | cost }) — stored as a suggestion.</summary>
    [HttpPost("projects/{projectId:guid}/risk/explain")]
    public async Task<ActionResult<BuildAiSuggestionDto>> ExplainRisk(Guid projectId, [FromBody] BuildRiskExplainRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await _ai.ExplainRiskAsync(projectId, request, ct));

    // ── Suggestions ───────────────────────────────────────────────────────────

    [HttpGet("projects/{projectId:guid}/ai/suggestions")]
    public async Task<ActionResult<IReadOnlyList<BuildAiSuggestionDto>>> Suggestions(Guid projectId, [FromQuery] string? capability,
        [FromQuery] string? status, CancellationToken ct)
        => Ok(await _ai.ListAsync(projectId, capability, status, ct));

    [HttpGet("ai/suggestions/{id:guid}")]
    public async Task<ActionResult<BuildAiSuggestionDto>> Suggestion(Guid id, CancellationToken ct) => Ok(await _ai.GetAsync(id, ct));

    /// <summary>Applies the suggestion (or the items in itemIds). Expired or already decided → 409.</summary>
    [HttpPost("ai/suggestions/{id:guid}/accept")]
    public async Task<ActionResult<BuildAiSuggestionDto>> Accept(Guid id, [FromBody] AcceptBuildAiSuggestionRequest? request, CancellationToken ct)
        => Ok(await _ai.AcceptAsync(id, request ?? new AcceptBuildAiSuggestionRequest(), ct));

    [HttpPost("ai/suggestions/{id:guid}/reject")]
    public async Task<ActionResult<BuildAiSuggestionDto>> Reject(Guid id, [FromBody] RejectBuildAiSuggestionRequest? request, CancellationToken ct)
        => Ok(await _ai.RejectAsync(id, request ?? new RejectBuildAiSuggestionRequest(), ct));

    // ── Documents ─────────────────────────────────────────────────────────────

    [HttpGet("projects/{projectId:guid}/documents")]
    public async Task<ActionResult<IReadOnlyList<BuildDocumentDto>>> Documents(Guid projectId, [FromQuery] string? category, CancellationToken ct)
        => Ok(await _documents.ListAsync(projectId, category, ct));

    /// <summary>
    /// Multipart: <c>file</c> (PDF, JPEG, PNG or WebP by the first bytes; ≤ 20 MB), <c>category</c> (Contrato, Projeto, ArtRrt,
    /// Alvara, NotaFiscal, Laudo, Manual, Garantia, Foto, Outro), <c>title?</c>, <c>validUntil?</c>, <c>clientVisible?</c>.
    /// </summary>
    [HttpPost("projects/{projectId:guid}/documents")]
    [BuildUploadLimit(BuildUploadKind.Document)]
    public async Task<ActionResult<BuildDocumentDto>> UploadDocument(Guid projectId, IFormFile? file, [FromForm] string? category,
        [FromForm] string? title, [FromForm] DateOnly? validUntil, [FromForm] bool? clientVisible, CancellationToken ct)
    {
        await using var buffer = await BuildTwinController.BufferAsync(file, BuildDocument.MaxBytes, ct);
        return StatusCode(StatusCodes.Status201Created,
            await _documents.CreateAsync(projectId, category, title, validUntil, clientVisible ?? false, buffer, ct));
    }

    [HttpGet("documents/{id:guid}")]
    public async Task<ActionResult<BuildDocumentDto>> Document(Guid id, CancellationToken ct) => Ok(await _documents.GetAsync(id, ct));

    [HttpPut("documents/{id:guid}")]
    public async Task<ActionResult<BuildDocumentDto>> UpdateDocument(Guid id, [FromBody] UpdateBuildDocumentRequest request, CancellationToken ct)
        => Ok(await _documents.UpdateAsync(id, request, ct));

    [HttpPut("documents/{id:guid}/client-visibility")]
    public async Task<ActionResult<BuildDocumentDto>> SetDocumentVisibility(Guid id, [FromBody] SetBuildDocumentVisibilityRequest request,
        CancellationToken ct)
        => Ok(await _documents.SetVisibilityAsync(id, request.ClientVisible, ct));

    [HttpDelete("documents/{id:guid}")]
    public async Task<IActionResult> DeleteDocument(Guid id, CancellationToken ct)
    {
        await _documents.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Document intelligence (needs BuildAI and BuildVision): extracted fields as a Pending suggestion.</summary>
    [HttpPost("documents/{id:guid}/analyze")]
    public async Task<ActionResult<BuildAiSuggestionDto>> AnalyzeDocument(Guid id, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await _ai.AnalyzeDocumentAsync(id, ct));
}
