namespace Nexo.Domain.Modules.Service;

/// <summary>
/// Kind of "subject" of a service when it is distinct from the paying customer
/// (decision D4). Null on a preset means the vertical has no separate subject
/// (e.g. salão, programador) — the customer is the subject.
/// </summary>
public enum ServiceSubjectKind
{
    Pet     = 1,
    Vehicle = 2,
    Student = 3,
    Generic = 4
}
