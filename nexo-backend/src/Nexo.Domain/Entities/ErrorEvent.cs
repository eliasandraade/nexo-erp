using Nexo.Domain.Common;

namespace Nexo.Domain.Entities;

/// <summary>
/// An unexpected error (HTTP 500), grouped by fingerprint (exception type + route + top frames of
/// our code): one row per kind of failure, with how many times and when it happened last. The
/// platform team's error tracking, kept in our own database — no data leaves for a third party.
/// Rows hold no request body and no personal data: messages are masked before they are stored.
/// Written with an upsert by the tracker (never through the change tracker), read by the platform.
/// </summary>
public class ErrorEvent : BaseEntity
{
    private ErrorEvent() { }   // EF Core

    public string Fingerprint { get; private set; } = string.Empty;
    public string ExceptionType { get; private set; } = string.Empty;
    public string Summary { get; private set; } = string.Empty;
    public string Route { get; private set; } = string.Empty;
    public string Method { get; private set; } = string.Empty;
    public DateTime FirstSeenAt { get; private set; }
    public DateTime LastSeenAt { get; private set; }
    public int Count { get; private set; }
    public string? LastRequestId { get; private set; }
    public Guid? LastTenantId { get; private set; }
    public string StackTop { get; private set; } = string.Empty;
}
