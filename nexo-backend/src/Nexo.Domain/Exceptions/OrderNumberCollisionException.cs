namespace Nexo.Domain.Exceptions;

/// <summary>
/// Thrown by the infrastructure layer when a MAX+1 order number collides with a
/// concurrent insert on the unique index. The service layer catches this and retries.
/// </summary>
public class OrderNumberCollisionException : Exception
{
    public OrderNumberCollisionException()
        : base("Order number collision — retrying.") { }
}
