namespace Nexo.Domain.Modules.Interpreter;

public enum AiUsageStatus
{
    /// <summary>Taken before a paid call, for its worst case; counts against the limits until settled or released.</summary>
    Reserved = 1,
    /// <summary>The call happened: the actual tokens and cost replace the estimate.</summary>
    Settled = 2,
    /// <summary>The call didn't happen or failed before any usage: nothing counts.</summary>
    Released = 3,
}

/// <summary>
/// One paid AI call against the monthly limits (per company in money, per provider in tokens). Reserved for its worst case
/// before the call — under a lock, so concurrent calls can't all pass a hard limit — then settled with the real usage or
/// released. NOT a TenantEntity: the platform reads across companies; TenantId is explicit.
/// </summary>
public class AiUsageReservation
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string ModelId { get; private set; } = string.Empty;
    public DateOnly MonthStart { get; private set; }
    public AiUsageStatus Status { get; private set; }
    public long ReservedMicros { get; private set; }
    public long ReservedTokens { get; private set; }
    public long ActualMicros { get; private set; }
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? SettledAt { get; private set; }

    private AiUsageReservation() { }

    public static DateOnly MonthOf(DateTime utc) => new(utc.Year, utc.Month, 1);

    public static AiUsageReservation Reserve(Guid tenantId, string provider, string modelId, long micros, long tokens, DateTime nowUtc)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Provider = provider,
            ModelId = modelId.Length > 100 ? modelId[..100] : modelId,
            MonthStart = MonthOf(nowUtc),
            Status = AiUsageStatus.Reserved,
            ReservedMicros = Math.Max(0, micros),
            ReservedTokens = Math.Max(0, tokens),
            CreatedAt = nowUtc,
        };

    public void Settle(long actualMicros, int inputTokens, int outputTokens, DateTime nowUtc)
    {
        Status = AiUsageStatus.Settled;
        ActualMicros = Math.Max(0, actualMicros);
        InputTokens = Math.Max(0, inputTokens);
        OutputTokens = Math.Max(0, outputTokens);
        SettledAt = nowUtc;
    }

    public void Release(DateTime nowUtc)
    {
        Status = AiUsageStatus.Released;
        SettledAt = nowUtc;
    }
}
