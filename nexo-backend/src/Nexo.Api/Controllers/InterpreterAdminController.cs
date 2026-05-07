using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Api.Controllers;

/// <summary>
/// Platform-admin endpoints for the AI Operations cockpit.
/// All routes require a platform JWT (type: "platform").
/// Queries bypass tenant Global Query Filters intentionally.
/// </summary>
[ApiController]
[Route("api/platform/interpreter")]
[Authorize]
public class InterpreterAdminController : ControllerBase
{
    private readonly NexoDbContext          _db;
    private readonly IAnalyzerSelector      _analyzerSelector;
    private readonly IInterpretationService _interpretationService;
    private readonly IMovementMemoryService _memoryService;

    public InterpreterAdminController(
        NexoDbContext          db,
        IAnalyzerSelector      analyzerSelector,
        IInterpretationService interpretationService,
        IMovementMemoryService memoryService)
    {
        _db                    = db;
        _analyzerSelector      = analyzerSelector;
        _interpretationService = interpretationService;
        _memoryService         = memoryService;
    }

    private bool IsPlatformUser() =>
        User.FindFirstValue("type") == "platform";

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
        if (!IsPlatformUser()) return Forbid();

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
        if (!IsPlatformUser()) return Forbid();

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
        if (!IsPlatformUser()) return Forbid();

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

    [HttpGet("providers")]
    public async Task<IActionResult> GetProviders(CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var providers = await _db.AiProviders
            .OrderBy(p => p.Priority)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Provider,
                p.IsEnabled,
                p.IsDefault,
                p.ModelId,
                p.ApiKeyLastFour,
                hasApiKey          = p.ApiKeyEncrypted != null,
                p.MonthlyTokenLimit,
                costPerInputToken  = p.CostPerInputTokenMicros / 1_000_000.0,
                costPerOutputToken = p.CostPerOutputTokenMicros / 1_000_000.0,
                p.Priority,
                p.UpdatedAt,
            })
            .ToListAsync(ct);

        return Ok(providers);
    }

    [HttpPatch("providers/{id:guid}")]
    public async Task<IActionResult> PatchProvider(
        Guid id,
        [FromBody] PatchProviderRequest body,
        CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var provider = await _db.AiProviders.FindAsync(new object[] { id }, ct);
        if (provider is null) return NotFound();

        if (body.IsEnabled.HasValue)
            provider.SetEnabled(body.IsEnabled.Value);

        if (body.IsDefault.HasValue)
        {
            if (body.IsDefault.Value)
            {
                // Clear default on all others first
                var others = await _db.AiProviders.Where(p => p.Id != id && p.IsDefault).ToListAsync(ct);
                foreach (var other in others)
                    other.SetDefault(false);
            }
            provider.SetDefault(body.IsDefault.Value);
        }

        if (body.MonthlyTokenLimit is not null || body.CostPerInputTokenMicros is not null || body.CostPerOutputTokenMicros is not null)
        {
            provider.UpdateLimits(
                body.MonthlyTokenLimit ?? provider.MonthlyTokenLimit,
                body.CostPerInputTokenMicros ?? provider.CostPerInputTokenMicros,
                body.CostPerOutputTokenMicros ?? provider.CostPerOutputTokenMicros);
        }

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    public sealed record PatchProviderRequest(
        bool?  IsEnabled             = null,
        bool?  IsDefault             = null,
        long?  MonthlyTokenLimit     = null,
        long?  CostPerInputTokenMicros  = null,
        long?  CostPerOutputTokenMicros = null);

    [HttpPost("providers/{id:guid}/rotate-key")]
    public async Task<IActionResult> RotateKey(
        Guid id,
        [FromBody] RotateKeyRequest body,
        CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var provider = await _db.AiProviders.FindAsync(new object[] { id }, ct);
        if (provider is null) return NotFound();

        if (string.IsNullOrWhiteSpace(body.ApiKey))
        {
            provider.ClearApiKey();
            await _db.SaveChangesAsync(ct);
            return Ok(new { lastFour = (string?)null });
        }

        var lastFour  = body.ApiKey.Length >= 4
            ? body.ApiKey.Substring(body.ApiKey.Length - 4)
            : body.ApiKey;

        // TODO: replace with real AES-256 encryption via IEncryptionService
        var encrypted = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(body.ApiKey));
        provider.SetApiKey(encrypted, lastFour);

        await _db.SaveChangesAsync(ct);
        return Ok(new { lastFour });
    }

    public sealed record RotateKeyRequest(string? ApiKey);

    // ─────────────────────────────────────────────────────────────────────────
    // PROMPT VERSIONS
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("prompts")]
    public async Task<IActionResult> GetPrompts(
        [FromQuery] string? type = null,
        CancellationToken ct = default)
    {
        if (!IsPlatformUser()) return Forbid();

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
        if (!IsPlatformUser()) return Forbid();

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

    [HttpPost("playground")]
    public async Task<IActionResult> Playground(
        [FromBody] PlaygroundRequest body,
        CancellationToken ct)
    {
        if (!IsPlatformUser()) return Forbid();

        var platformUserId = GetPlatformUserId() ?? Guid.Empty;

        // Use the first real tenant if none provided (platform context has no tenant)
        var tenantId = body.TenantId ?? await _db.Tenants
            .IgnoreQueryFilters()
            .OrderBy(t => t.CompanyName)
            .Select(t => t.Id)
            .FirstOrDefaultAsync(ct);

        if (tenantId == Guid.Empty)
            return BadRequest(new { error = "No tenant available for playground context." });

        var source   = body.Source ?? InputSourceType.Text;
        var analyzer = _analyzerSelector.Select(source, tenantId, body.ForceProvider);

        var input = new AnalysisInput(
            Source:     source,
            RawText:    body.Text,
            StorageKey: null,
            TenantId:   tenantId,
            UserId:     platformUserId);

        var sw     = System.Diagnostics.Stopwatch.StartNew();
        var output = await analyzer.AnalyzeAsync(input, ct);
        sw.Stop();

        var memCtx         = await _memoryService.GetCompactContextAsync(tenantId, platformUserId, ct);
        var interpretation = await _interpretationService.SuggestAsync(output, memCtx, tenantId, platformUserId, ct);

        return Ok(new
        {
            provider      = analyzer.Provider.ToString(),
            elapsedMs     = sw.ElapsedMilliseconds,
            input = new
            {
                body.Text,
                source = source.ToString(),
            },
            extraction = new
            {
                amount  = new { output.Amount.Value,  output.Amount.Confidence,  output.Amount.Status },
                date    = new { output.Date.Value,    output.Date.Confidence,    output.Date.Status },
                payee   = new { output.Payee.Value,   output.Payee.Confidence,   output.Payee.Status },
                account = new { output.Account.Value, output.Account.Confidence, output.Account.Status },
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
            rawProviderResponse = output.RawProviderResponse,
        });
    }

    public sealed record PlaygroundRequest(
        string?          Text,
        InputSourceType? Source        = null,
        AnalyzerProvider? ForceProvider = null,
        Guid?            TenantId      = null);
}
