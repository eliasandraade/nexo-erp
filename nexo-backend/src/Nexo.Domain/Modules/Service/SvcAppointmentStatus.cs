namespace Nexo.Domain.Modules.Service;

/// <summary>
/// Lifecycle status of a <see cref="SvcAppointment"/>. Stored as a string. Transitions are
/// enforced by the entity's state machine; Completed/Cancelled/NoShow are terminal.
/// </summary>
public enum SvcAppointmentStatus
{
    Scheduled,
    Confirmed,
    InProgress,
    Completed,
    NoShow,
    Cancelled,
}
