using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Interpreter;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Api.Controllers.Modules.Interpreter;

/// <summary>
/// Tenant-level Interpreter settings: stopwords and memory profile.
///
/// Stopwords: extend the PT-BR base list for this tenant's description normalization.
/// Memory profile: compact JSONB summary of movement patterns used to seed LLM prompts.
/// </summary>
[ApiController]
[Route("api/v1/tenants")]
[Authorize]
public class TenantInterpreterController : ControllerBase
{
    private readonly ITenantStopwordRepository            _stopwordRepo;
    private readonly IMovementMemoryProfileRepository     _profileRepo;
    private readonly RebuildMovementMemoryProfileUseCase  _rebuildUseCase;
    private readonly ICurrentTenant                       _currentTenant;
    private readonly ICurrentUser                         _currentUser;

    public TenantInterpreterController(
        ITenantStopwordRepository           stopwordRepo,
        IMovementMemoryProfileRepository    profileRepo,
        RebuildMovementMemoryProfileUseCase rebuildUseCase,
        ICurrentTenant                      currentTenant,
        ICurrentUser                        currentUser)
    {
        _stopwordRepo   = stopwordRepo;
        _profileRepo    = profileRepo;
        _rebuildUseCase = rebuildUseCase;
        _currentTenant  = currentTenant;
        _currentUser    = currentUser;
    }

    // ── Stopwords ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Lists all custom stopwords for the current tenant.
    /// These extend the PT-BR base list used by the description normalizer.
    /// </summary>
    [HttpGet("stopwords")]
    public async Task<ActionResult<IReadOnlyList<StopwordDto>>> ListStopwords(
        CancellationToken ct)
    {
        var words = await _stopwordRepo.GetWordsByTenantAsync(_currentTenant.Id, ct);
        return Ok(words.Select(w => new StopwordDto(w)).ToList());
    }

    /// <summary>Adds a new custom stopword for the current tenant.</summary>
    [HttpPost("stopwords")]
    public async Task<ActionResult<StopwordDto>> AddStopword(
        [FromBody] AddStopwordRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Word))
            return BadRequest(new { error = "Word cannot be empty." });

        var tenantId = _currentTenant.Id;
        var word     = request.Word.Trim().ToLowerInvariant();

        var existing = await _stopwordRepo.GetByWordAsync(tenantId, word, ct);
        if (existing is not null)
            return Conflict(new { error = $"Stopword '{word}' already exists for this tenant." });

        try
        {
            var stopword = TenantStopword.Create(tenantId, word);
            await _stopwordRepo.AddAsync(stopword, ct);
            await _stopwordRepo.SaveChangesAsync(ct);

            return Ok(new StopwordDto(stopword.Word));
        }
        catch (DomainException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Removes a custom stopword by its normalized value.</summary>
    [HttpDelete("stopwords/{word}")]
    public async Task<IActionResult> RemoveStopword(string word, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(word))
            return BadRequest(new { error = "Word cannot be empty." });

        var normalized = word.Trim().ToLowerInvariant();
        var existing   = await _stopwordRepo.GetByWordAsync(_currentTenant.Id, normalized, ct);

        if (existing is null)
            return NotFound(new { error = $"Stopword '{normalized}' not found." });

        await _stopwordRepo.RemoveAsync(existing, ct);
        await _stopwordRepo.SaveChangesAsync(ct);

        return NoContent();
    }

    // ── Memory Profile ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current compact memory profile for the authenticated user.
    /// This profile is injected into LLM prompts to improve interpretation accuracy.
    /// </summary>
    [HttpGet("memory-profile")]
    public async Task<ActionResult<MemoryProfileDto>> GetMemoryProfile(CancellationToken ct)
    {
        var profile = await _profileRepo.GetAsync(_currentTenant.Id, _currentUser.UserId, ct);

        if (profile is null)
            return Ok(new MemoryProfileDto(
                ProfileVersion:      0,
                ProfileType:         "User",
                MovementsConsidered: 0,
                LastRebuildAt:       null,
                Summary:             "{}"));

        return Ok(new MemoryProfileDto(
            ProfileVersion:      profile.ProfileVersion,
            ProfileType:         profile.ProfileType.ToString(),
            MovementsConsidered: profile.MovementsConsidered,
            LastRebuildAt:       profile.LastRebuildAt,
            Summary:             profile.Summary));
    }

    /// <summary>
    /// Manually triggers a memory profile rebuild for the current user.
    /// Normally this is triggered automatically after each movement confirmation.
    /// Use this to force a fresh rebuild (e.g. after bulk imports or corrections).
    /// </summary>
    [HttpPost("memory-profile/rebuild")]
    public async Task<IActionResult> RebuildMemoryProfile(CancellationToken ct)
    {
        var command = new RebuildMovementMemoryProfileCommand(
            TenantId: _currentTenant.Id,
            UserId:   _currentUser.UserId);

        await _rebuildUseCase.ExecuteAsync(command, ct);
        return Ok(new { message = "Memory profile rebuild completed." });
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public record AddStopwordRequest(string Word);

public record StopwordDto(string Word);

public record MemoryProfileDto(
    int       ProfileVersion,
    string    ProfileType,
    int       MovementsConsidered,
    DateTime? LastRebuildAt,
    string    Summary);
