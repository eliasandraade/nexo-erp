namespace Nexo.Domain.Exceptions;

/// <summary>
/// Thrown when a user attempts an action they are not authorized to perform.
/// The global exception middleware maps this to 403 Forbidden.
/// </summary>
public class ForbiddenException : DomainException
{
    public ForbiddenException(string message = "You do not have permission to perform this action.")
        : base(message) { }
}
