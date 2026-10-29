namespace Nexo.Domain.Exceptions;

/// <summary>
/// Base class for all domain-level exceptions.
/// Thrown when a business rule is violated.
/// The global exception middleware maps these to 422 Unprocessable Entity.
/// </summary>
public class DomainException : Exception
{
    /// <summary>Machine-readable reason (e.g. "discount_requires_manager"), sent to the client as { code }.</summary>
    public string? Code { get; }

    /// <summary>Itemised reasons, sent to the client as { details } (e.g. why an operation is not supported).</summary>
    public IReadOnlyList<string> Details { get; } = [];

    public DomainException(string message) : base(message) { }
    public DomainException(string message, string code) : base(message) => Code = code;
    public DomainException(string message, string code, IReadOnlyList<string> details) : base(message)
    {
        Code = code;
        Details = details;
    }
    public DomainException(string message, Exception inner) : base(message, inner) { }
}
