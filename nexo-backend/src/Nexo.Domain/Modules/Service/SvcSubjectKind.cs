namespace Nexo.Domain.Modules.Service;

/// <summary>
/// The kind of subject a service is performed on, when it is not the paying customer
/// themselves. Stored as a string (HasConversion&lt;string&gt;). Per-type detail lives in
/// <see cref="SvcSubject.MetadataJson"/>.
/// </summary>
public enum SvcSubjectKind
{
    Pet,
    Vehicle,
    Student,
    Dependent,
    Other,
}
