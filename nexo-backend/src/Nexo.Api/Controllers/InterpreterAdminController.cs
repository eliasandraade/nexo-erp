using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Api.Controllers;

/// <summary>
/// Platform-admin endpoints for the AI Operations cockpit.
/// All routes require a platform JWT (type: "platform").
/// Queries bypass tenant Global Query Filters intentionally.
/// </summary>
[ApiController]
[Route("api/platform/interpreter")]
[Authorize(Policy = "Platform")]
public class InterpreterAdminController : ControllerBase
{
    private readonly NexoDbContext          _db;
    private readonly AiProviderAdminService _providers;

    public InterpreterAdminController(NexoDbContext db, AiProviderAdminService providers)
    {
        _db        = db;
        _providers = providers;
    }

    private PlatformActor Actor() => new(GetPlatformUserId(), User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email"),
        Request.Headers.UserAgent.ToString(), HttpContext.TraceIdentifier);

    private Guid? GetPlatformUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DASHBOARD
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Aggregated AI operations stats for the dashboard widgets.</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(CancellationToken ct)
    {

        var now     = DateTime.UtcNow;
        var today   = now.Date;
        var week    = today.AddDays(-6);
        var month   = today.AddDays(-29);

        var allRows = await _db.InterpreterTelemetry
            .Where(t => t.CreatedAt >= month)
            .ToListAsync(ct);

        var todayRows  = allRows.Where(t => t.CreatedAt.Date == today).ToList();
        var weekRows   = allRows.Where(t => t.CreatedAt.Date >= week).ToList();

        // Provider split (last 7 days)
        var providerSplit = weekRows
            .GroupBy(t => t.Provider)
            .Select(g => new { provider = g.Key, count = g.Count() })
            .ToList();

        // Daily chart (last 7 days)
        var dailyChart = Enumerable.Range(0, 7)
            .Select(i => today.AddDays(-6 + i))
            .Select(date => new
            {
                date  = date.ToString("yyyy-MM-dd"),
                total = weekRows.Count(t => t.CreatedAt.Date == date),
                llm   = weekRows.Count(t => t.CreatedAt.Date == date && t.Provider != "RuleBased"),
            })
            .ToList();

        // Top tenants (last 7 days, by count)
        var topTenantIds = weekRows
            .GroupBy(t => t.TenantId)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => g.Key)
            .ToList();

        var tenantNames = await _db.Tenants
            .IgnoreQueryFilters()
            .Where(t => topTenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.CompanyName })
            .ToDictionaryAsync(t => t.Id, t => t.CompanyName, ct);

        var topTenants = topTenantIds.Select(id => new
        {
            tenantId    = id,
            tenantName  = tenantNames.GetValueOrDefault(id, "Unknown"),
            count       = weekRows.Count(t => t.TenantId == id),
        }).ToList();

        // Acceptance rate: movements that reached Confirmed status (last 7 days)
        var draftIds = weekRows.Where(t => t.MovementId.HasValue).Select(t => t.MovementId!.Value).Distinct().ToList();
        int confirmedCount = 0;
        if (draftIds.Count > 0)
        {
            confirmedCount = await _db.IntMovements
                .IgnoreQueryFilters()
                .Where(m => draftIds.Contains(m.Id) && m.Status == MovementStatus.Confirmed)
                .CountAsync(ct);
        }
        var acceptanceRate = draftIds.Count > 0
            ? Math.Round((double)confirmedCount / draftIds.Count * 100, 1)
            : 0;

        // Correction count (user corrections in last 7 days)
        var correctionCount = await _db.IntUserCorrections
            .IgnoreQueryFilters()
            .Where(c => c.CreatedAt >= week)
            .CountAsync(ct);

        var avgAmountConf = weekRows.Count > 0 ? weekRows.Average(t => t.AmountConfidence) : 0;
        var avgDateConf   = weekRows.Count > 0 ? weekRows.Average(t => t.DateConfidence)   : 0;

        var totalCostMicros30d = allRows.Sum(t => t.EstimatedCostMicros);

        return Ok(new
        {
            today = new
            {
                total   = todayRows.Count,
                llm     = todayRows.Count(t => t.Provider != "RuleBased"),
                errors  = todayRows.Count(t => !t.Success),
                costUsd = todayRows.Sum(t => t.EstimatedCostMicros) / 1_000_000.0,
            },
            week7d = new
            {
                total          = weekRows.Count,
                llm            = weekRows.Count(t => t.Provider != "RuleBased"),
                acceptanceRate,
                correctionCount,
                avgAmountConf  = Math.Round(avgAmountConf, 3),
                avgDateConf    = Math.Round(avgDateConf,   3),
                costUsd        = weekRows.Sum(t => t.EstimatedCostMicros) / 1_000_000.0,
            },
            month30d = new
            {
                total   = allRows.Count,
                costUsd = Math.Round(totalCostMicros30d / 1_000_000.0, 4),
            },
            providerSplit,
            dailyChart,
            topTenants,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TELEMETRY (paginated log)
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("telemetry")]
    public async Task<IActionResult> GetTelemetry(
        [FromQuery] int    page      = 1,
        [FromQuery] int    pageSize  = 50,
        [FromQuery] string? provider = null,
        [FromQuery] string? opType   = null,
        [FromQuery] bool?   success  = null,
        CancellationToken ct = default)
    {

        if (page < 1)     page     = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 200) pageSize = 200;

        var query = _db.InterpreterTelemetry.AsQueryable();

        if (!string.IsNullOrWhiteSpace(provider))
            query = query.Where(t => t.Provider == provider);
        if (!string.IsNullOrWhiteSpace(opType))
            query = query.Where(t => t.OperationType == opType);
        if (success.HasValue)
            query = query.Where(t => t.Success == success.Value);

        var total = await query.CountAsync(ct);
        var rows  = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Join tenant names
        var tenantIds = rows.Select(r => r.TenantId).Distinct().ToList();
        var tenantNames = await _db.Tenants
            .IgnoreQueryFilters()
            .Where(t => tenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.CompanyName })
            .ToDictionaryAsync(t => t.Id, t => t.CompanyName, ct);

        var items = rows.Select(t => new
        {
            t.Id,
            t.TenantId,
            tenantName         = tenantNames.GetValueOrDefault(t.TenantId, "Unknown"),
            t.UserId,
            t.MovementId,
            t.OperationType,
            t.Provider,
            t.PromptType,
            t.PromptVersion,
            t.PromptHash,
            t.InputTokens,
            t.OutputTokens,
            costUsd            = t.EstimatedCostMicros / 1_000_000.0,
            t.DurationMs,
            t.Success,
            t.ErrorMessage,
            t.FallbackUsed,
            t.FallbackFromProvider,
            t.RequiresInputCount,
            t.AmountConfidence,
            t.DateConfidence,
            createdAt          = t.CreatedAt,
        }).ToList();

        return Ok(new
        {
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize),
            items,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // COSTS (per-tenant aggregation)
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("costs")]
    public async Task<IActionResult> GetCosts(CancellationToken ct)
    {

        var since30d = DateTime.UtcNow.AddDays(-30);

        var costRows = await _db.InterpreterTelemetry
            .Where(t => t.CreatedAt >= since30d)
            .GroupBy(t => t.TenantId)
            .Select(g => new
            {
                TenantId    = g.Key,
                TotalMicros = g.Sum(t => t.EstimatedCostMicros),
                TotalCalls  = g.Count(),
                LlmCalls    = g.Count(t => t.Provider != "RuleBased"),
            })
            .OrderByDescending(x => x.TotalMicros)
            .ToListAsync(ct);

        var tenantIds = costRows.Select(r => r.TenantId).ToList();

        var tenantNames = await _db.Tenants
            .IgnoreQueryFilters()
            .Where(t => tenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.CompanyName })
            .ToDictionaryAsync(t => t.Id, t => t.CompanyName, ct);

        var limits = await _db.TenantAiLimits
            .Where(l => tenantIds.Contains(l.TenantId))
            .ToDictionaryAsync(l => l.TenantId, ct);

        var items = costRows.Select(r =>
        {
            limits.TryGetValue(r.TenantId, out var limit);
            var costUsd = r.TotalMicros / 1_000_000.0;
            return new
            {
                r.TenantId,
                tenantName     = tenantNames.GetValueOrDefault(r.TenantId, "Unknown"),
                costUsd        = Math.Round(costUsd, 4),
                r.TotalCalls,
                r.LlmCalls,
                softLimitUsd   = limit?.SoftLimitCents  != null ? (double?)limit.SoftLimitCents  / 100.0 : null,
                hardLimitUsd   = limit?.HardLimitCents  != null ? (double?)limit.HardLimitCents  / 100.0 : null,
                softLimitHit   = limit?.SoftLimitCents  != null && costUsd * 100 >= limit.SoftLimitCents,
                hardLimitHit   = limit?.HardLimitCents  != null && costUsd * 100 >= limit.HardLimitCents,
            };
        }).ToList();

        var totalUsd = costRows.Sum(r => r.TotalMicros) / 1_000_000.0;

        return Ok(new { totalUsd = Math.Round(totalUsd, 4), items });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AI PROVIDERS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every provider with its real status (available, disabled, missing_key, missing_model, implementation_missing,
    /// secret_unavailable, invalid_credentials, rate_limited, unhealthy) and the order the automatic mode uses. Never a key.
    /// </summary>
    [HttpGet("providers")]
    public async Task<IActionResult> GetProviders(CancellationToken ct)
    {
        var states = await _providers.ListAsync(ct);
        return Ok(new
        {
            providers = states.Select(ProviderView),
            secretStorageAvailable = _providers.SecretStorageAvailable,
            automaticOrder = states.Where(s => s.IsAvailable).Select(s => s.Provider.ToString()),
        });
    }

    [HttpPatch("providers/{id:guid}")]
    [Authorize(Policy = "PlatformAdmin")]
    public async Task<IActionResult> PatchProvider(Guid id, [FromBody] PatchProviderRequest body, CancellationToken ct)
        => Ok(ProviderView(await _providers.UpdateAsync(id, new AiProviderPatch(body.IsEnabled, body.IsDefault, body.ModelId, body.Priority,
            body.MonthlyTokenLimit, body.ClearMonthlyTokenLimit ?? false, body.InputUsdPerMillionTokens, body.OutputUsdPerMillionTokens), Actor(), ct)));

    public sealed record PatchProviderRequest(
        bool?    IsEnabled                 = null,
        bool?    IsDefault                 = null,
        string?  ModelId                   = null,
        int?     Priority                  = null,
        long?    MonthlyTokenLimit         = null,
        bool?    ClearMonthlyTokenLimit    = null,
        decimal? InputUsdPerMillionTokens  = null,
        decimal? OutputUsdPerMillionTokens = null);

    /// <summary>Stores a new API key, encrypted (AES-256-GCM, master key outside the database). Answers only the last four.</summary>
    [HttpPut("providers/{id:guid}/api-key")]
    [Authorize(Policy = "PlatformAdmin")]
    public async Task<IActionResult> SetApiKey(Guid id, [FromBody] SetApiKeyRequest body, CancellationToken ct)
        => Ok(ProviderView(await _providers.SetKeyAsync(id, body.ApiKey ?? "", Actor(), ct)));

    public sealed record SetApiKeyRequest(string? ApiKey);

    [HttpDelete("providers/{id:guid}/api-key")]
    [Authorize(Policy = "PlatformAdmin")]
    public async Task<IActionResult> ClearApiKey(Guid id, CancellationToken ct)
        => Ok(ProviderView(await _providers.ClearKeyAsync(id, Actor(), ct)));

    /// <summary>"Testar conexão": one minimal call with a short timeout; the sanitized result is recorded and returned.</summary>
    [HttpPost("providers/{id:guid}/test")]
    [Authorize(Policy = "PlatformAdmin")]
    public async Task<IActionResult> TestProvider(Guid id, CancellationToken ct)
        => Ok(await _providers.TestAsync(id, Actor(), ct));

    private static object ProviderView(AiProviderState s) => new
    {
        s.Id,
        s.Name,
        provider = s.Provider.ToString(),
        status = s.StatusName,
        s.StatusMessage,
        s.Implemented,
        s.RequiresKey,
        s.AllowedByDeployment,
        s.IsEnabled,
        s.IsDefault,
        s.Priority,
        s.ModelId,
        hasApiKey = s.HasApiKey,
        s.ApiKeyLastFour,
        s.ApiKeyUpdatedAt,
        s.MonthlyTokenLimit,
        inputUsdPerMillionTokens = s.CostPerMillionInputMicros / 1_000_000m,
        outputUsdPerMillionTokens = s.CostPerMillionOutputMicros / 1_000_000m,
        lastTest = s.LastTestAt is null ? null : new { status = s.LastTestStatus, message = s.LastTestMessage, at = s.LastTestAt },
    };

    // ─────────────────────────────────────────────────────────────────────────
    // PROMPT VERSIONS
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("prompts")]
    public async Task<IActionResult> GetPrompts(
        [FromQuery] string? type = null,
        CancellationToken ct = default)
    {

        var query = _db.StoredPromptVersions.AsQueryable();

        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(p => p.PromptType == type);

        var rawPrompts = await query
            .OrderBy(p => p.PromptType)
            .ThenByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                p.Id,
                p.PromptType,
                p.Version,
                p.Hash,
                p.IsActive,
                p.Description,
                p.CreatedBy,
                p.CreatedAt,
                p.Content,
            })
            .ToListAsync(ct);

        var prompts = rawPrompts.Select(p => new
        {
            p.Id,
            p.PromptType,
            p.Version,
            p.Hash,
            p.IsActive,
            p.Description,
            p.CreatedBy,
            p.CreatedAt,
            contentPreview = p.Content.Length > 200 ? p.Content[..200] + "…" : p.Content,
        }).ToList();

        return Ok(prompts);
    }

    [HttpPost("prompts/{id:guid}/activate")]
    public async Task<IActionResult> ActivatePrompt(Guid id, CancellationToken ct)
    {

        var target = await _db.StoredPromptVersions.FindAsync(new object[] { id }, ct);
        if (target is null) return NotFound();

        // Deactivate all others of the same type
        var siblings = await _db.StoredPromptVersions
            .Where(p => p.PromptType == target.PromptType && p.IsActive && p.Id != id)
            .ToListAsync(ct);

        foreach (var sibling in siblings)
            sibling.Deactivate();

        target.Activate();

        await _db.SaveChangesAsync(ct);
        return Ok(new { activated = id, promptType = target.PromptType, version = target.Version });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PLAYGROUND
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the same analysis the companies use. "auto" (or empty) follows the automatic order with fallback; a named
    /// provider runs alone: unavailable means 422 ai_provider_unavailable, failing means 503 ai_provider_failed, never a 500
    /// and never another provider in its place. Writes telemetry; never returns the provider's raw answer.
    /// </summary>
    [HttpPost("playground")]
    [Authorize(Policy = "PlatformAdmin")]
    public async Task<IActionResult> Playground([FromBody] PlaygroundRequest body, CancellationToken ct)
    {
        AnalyzerProvider? force = null;
        if (!string.IsNullOrWhiteSpace(body.ForceProvider) && !string.Equals(body.ForceProvider, "auto", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(body.ForceProvider, out _) || !Enum.TryParse<AnalyzerProvider>(body.ForceProvider, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                return BadRequest(new { error = "Provedor de IA desconhecido.", code = "ai_provider_invalid", requestId = HttpContext.TraceIdentifier });
            force = parsed;
        }

        var result = await _providers.PlaygroundAsync(new AiPlaygroundRequest(body.Text ?? "", force, body.TenantId), Actor(), ct);
        var run = result.Run;
        var output = run.Output;
        var interpretation = result.Interpretation;
        return Ok(new
        {
            result.TenantId,
            provider = run.Provider.ToString(),
            run.ModelId,
            run.Forced,
            run.FallbackUsed,
            attempts = run.Attempts.Select(x => new { provider = x.Provider.ToString(), x.ModelId, x.Succeeded, x.FailureReason, x.DurationMs }),
            elapsedMs = run.DurationMs,
            prompt = new { type = output.Prompt.PromptType, version = output.Prompt.PromptVersion, hash = output.Prompt.PromptHash },
            extraction = new
            {
                amount  = new { output.Amount.Value,  output.Amount.Confidence,  status = output.Amount.Status.ToString() },
                date    = new { output.Date.Value,    output.Date.Confidence,    status = output.Date.Status.ToString() },
                payee   = new { output.Payee.Value,   output.Payee.Confidence,   status = output.Payee.Status.ToString() },
                account = new { output.Account.Value, output.Account.Confidence, status = output.Account.Status.ToString() },
                output.InputTokens,
                output.OutputTokens,
                costUsd = output.EstimatedCostMicros / 1_000_000.0,
            },
            interpretation = new
            {
                direction    = interpretation.Direction.ToString(),
                dirSource    = interpretation.DirectionSource.ToString(),
                nature       = interpretation.Nature.ToString(),
                natureSource = interpretation.NatureSource.ToString(),
                interpretation.CategoryId,
                catSource    = interpretation.CategorySource.ToString(),
                contextType  = interpretation.ContextType?.ToString(),
                interpretation.ContextId,
                ctxSource    = interpretation.ContextSource.ToString(),
                interpretation.AccountId,
                accSource    = interpretation.AccountSource.ToString(),
            },
        });
    }

    public sealed record PlaygroundRequest(string? Text, string? ForceProvider = null, Guid? TenantId = null);
}
