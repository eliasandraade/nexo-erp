using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Audit;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Audit;

public class AuditQueryService
{
    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _currentTenant;

    public AuditQueryService(NexoDbContext db, ICurrentTenant currentTenant)
    {
        _db            = db;
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<AuditRecordDto>> GetAsync(
        string? actionType,
        string? severity,
        string? actor,
        string? from,
        string? to,
        CancellationToken ct = default)
    {
        var tenantId = _currentTenant.Id;

        var query = _db.AuditRecords
            .Where(r => r.TenantId == tenantId)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(actionType))
            query = query.Where(r => r.ActionType == actionType);

        if (!string.IsNullOrWhiteSpace(severity))
            query = query.Where(r => r.Severity == severity);

        if (!string.IsNullOrWhiteSpace(actor))
            query = query.Where(r =>
                r.ActorName != null &&
                EF.Functions.ILike(r.ActorName, $"%{actor}%"));

        if (!string.IsNullOrWhiteSpace(from) && DateOnly.TryParse(from, out var fromDate))
        {
            var fromUtc = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(r => r.CreatedAt >= fromUtc);
        }

        if (!string.IsNullOrWhiteSpace(to) && DateOnly.TryParse(to, out var toDate))
        {
            var toUtc = toDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(r => r.CreatedAt <= toUtc);
        }

        return await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(500)
            .Select(r => new AuditRecordDto(
                r.Id.ToString(),
                r.CreatedAt.ToString("o"),
                r.ActionType,
                r.Severity,
                r.ActorName,
                r.ActorType,
                r.EntityType,
                r.EntityId,
                r.Description,
                r.MetadataJson,
                r.IpAddress))
            .ToListAsync(ct);
    }

    public async Task<AuditStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var tenantId = _currentTenant.Id;
        var stats = await _db.AuditRecords
            .Where(r => r.TenantId == tenantId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total    = g.Count(),
                Critical = g.Count(r => r.Severity == "critical"),
                Warning  = g.Count(r => r.Severity == "warning"),
                Info     = g.Count(r => r.Severity == "info"),
            })
            .FirstOrDefaultAsync(ct);

        return new AuditStatsDto(
            stats?.Total    ?? 0,
            stats?.Critical ?? 0,
            stats?.Warning  ?? 0,
            stats?.Info     ?? 0);
    }
}
