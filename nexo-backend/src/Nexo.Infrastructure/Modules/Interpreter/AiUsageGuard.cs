using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Interpreter;

/// <summary>
/// The limits of paid AI calls, checked and reserved in one transaction holding PostgreSQL advisory locks (the company's,
/// then the provider's — always in that order): two concurrent calls never both see room that only one of them fits in.
/// Runs in its own scope so a reservation commits whatever the caller's unit of work does. A reservation older than
/// <see cref="StaleAfter"/> that was never settled (process died mid-call) stops counting.
/// </summary>
public sealed class AiUsageGuard : IAiUsageGuard
{
    public const long MicrosPerCent = 10_000;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AiUsageGuard> _logger;

    public AiUsageGuard(IServiceScopeFactory scopes, ILogger<AiUsageGuard> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public async Task<Guid> ReserveAsync(Guid tenantId, AnalyzerConfig config, long estimatedMicros, long estimatedTokens, CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var provider = config.Provider.ToString();
        var now = DateTime.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"ai-usage:tenant:" + tenantId}))", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"ai-usage:provider:" + provider}))", ct);

        var usage = await TenantUsageAsync(db, tenantId, now, ct);
        if (usage.HardLimitMicros is { } hard && usage.SpentMicros + usage.ReservedMicros + estimatedMicros > hard)
        {
            _logger.LogWarning("AI hard limit reached for tenant {TenantId}: paid call refused.", tenantId);
            throw new AiProviderCallException(config.Provider, AiCallFailure.LimitReached,
                "A empresa atingiu o limite mensal de gasto com IA definido na plataforma.");
        }
        var monthlyTokens = await db.AiProviders.AsNoTracking().Where(p => p.Provider == provider).Select(p => p.MonthlyTokenLimit).FirstOrDefaultAsync(ct);
        if (monthlyTokens is { } tokenLimit && await ProviderTokensAsync(db, provider, now, ct) + estimatedTokens > tokenLimit)
        {
            _logger.LogWarning("AI provider {Provider} monthly token limit reached: paid call refused.", provider);
            throw new AiProviderCallException(config.Provider, AiCallFailure.LimitReached,
                $"O provedor {provider} atingiu o limite mensal de tokens definido na plataforma.");
        }

        var reservation = AiUsageReservation.Reserve(tenantId, provider, config.ModelId, estimatedMicros, estimatedTokens, now);
        db.AiUsageReservations.Add(reservation);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (usage.SoftLimitMicros is { } soft && usage.SpentMicros + usage.ReservedMicros < soft && usage.SpentMicros + usage.ReservedMicros + estimatedMicros >= soft)
            _logger.LogWarning("AI soft limit crossed for tenant {TenantId}.", tenantId);
        return reservation.Id;
    }

    public async Task SettleAsync(Guid reservationId, long actualMicros, int inputTokens, int outputTokens, CancellationToken ct = default)
        => await UpdateAsync(reservationId, r => r.Settle(actualMicros, inputTokens, outputTokens, DateTime.UtcNow), ct);

    public async Task ReleaseAsync(Guid reservationId, CancellationToken ct = default)
        => await UpdateAsync(reservationId, r => r.Release(DateTime.UtcNow), ct);

    private async Task UpdateAsync(Guid id, Action<AiUsageReservation> change, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var reservation = await db.AiUsageReservations.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (reservation is null || reservation.Status != AiUsageStatus.Reserved) return;
        change(reservation);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The company's month: settled cost plus live reservations, against its limits.</summary>
    public static async Task<AiTenantUsage> TenantUsageAsync(NexoDbContext db, Guid tenantId, DateTime nowUtc, CancellationToken ct)
    {
        var month = AiUsageReservation.MonthOf(nowUtc);
        var fresh = nowUtc - StaleAfter;
        var rows = db.AiUsageReservations.AsNoTracking().Where(r => r.TenantId == tenantId && r.MonthStart == month);
        var spent = await rows.Where(r => r.Status == AiUsageStatus.Settled).SumAsync(r => (long?)r.ActualMicros, ct) ?? 0;
        var reserved = await rows.Where(r => r.Status == AiUsageStatus.Reserved && r.CreatedAt > fresh).SumAsync(r => (long?)r.ReservedMicros, ct) ?? 0;
        var limit = await db.TenantAiLimits.AsNoTracking().FirstOrDefaultAsync(l => l.TenantId == tenantId, ct);
        return new AiTenantUsage(tenantId, spent, reserved, limit?.SoftLimitCents * MicrosPerCent, limit?.HardLimitCents * MicrosPerCent);
    }

    private static async Task<long> ProviderTokensAsync(NexoDbContext db, string provider, DateTime nowUtc, CancellationToken ct)
    {
        var month = AiUsageReservation.MonthOf(nowUtc);
        var fresh = nowUtc - StaleAfter;
        var rows = db.AiUsageReservations.AsNoTracking().Where(r => r.Provider == provider && r.MonthStart == month);
        var used = await rows.Where(r => r.Status == AiUsageStatus.Settled).SumAsync(r => (long?)(r.InputTokens + r.OutputTokens), ct) ?? 0;
        var reserved = await rows.Where(r => r.Status == AiUsageStatus.Reserved && r.CreatedAt > fresh).SumAsync(r => (long?)r.ReservedTokens, ct) ?? 0;
        return used + reserved;
    }
}
