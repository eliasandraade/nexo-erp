namespace Nexo.Domain.Modules.Service;

/// <summary>
/// Lifecycle of a <see cref="SvcOrder"/> (ordem de serviço). Stored as a string. Transitions
/// enforced by the entity; Completed/Cancelled are terminal.
/// </summary>
public enum SvcOrderStatus
{
    Draft,
    Open,
    InProgress,
    Completed,
    Cancelled,
}
