namespace Nexo.Domain.Exceptions;

/// <summary>
/// Thrown when a requested entity does not exist.
/// The global exception middleware maps this to 404 Not Found.
/// </summary>
public class NotFoundException : DomainException
{
    public NotFoundException(string entityName, object key)
        : base($"{entityName} with identifier '{key}' was not found.") { }

    public NotFoundException(string message) : base(message) { }
}
