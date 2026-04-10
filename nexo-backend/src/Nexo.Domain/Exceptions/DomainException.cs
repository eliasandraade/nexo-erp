namespace Nexo.Domain.Exceptions;

/// <summary>
/// Base class for all domain-level exceptions.
/// Thrown when a business rule is violated.
/// The global exception middleware maps these to 422 Unprocessable Entity.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception inner) : base(message, inner) { }
}
