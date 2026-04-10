namespace Nexo.Domain.Exceptions;

/// <summary>
/// Thrown when an operation would create a duplicate or violate a uniqueness constraint.
/// The global exception middleware maps this to 409 Conflict.
/// </summary>
public class ConflictException : DomainException
{
    public ConflictException(string message) : base(message) { }
}
