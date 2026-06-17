namespace Nexo.Domain.Modules.Service;

/// <summary>
/// What a <see cref="SvcRecordEntry"/> is attached to. Only <see cref="Customer"/> and
/// <see cref="Subject"/> are accepted in v1; the remaining values are reserved for later PRs
/// (agenda/OS/pacotes) and are rejected at the validation layer until then.
/// </summary>
public enum SvcRecordContextType
{
    Customer,
    Subject,
    Appointment, // reserved — PR3
    Order,       // reserved — PR4
    Package,     // reserved — PR5
}
