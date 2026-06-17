namespace Nexo.Domain.Modules.Service;

/// <summary>
/// Per-preset display nouns. Pure presentation metadata consumed by the frontend so a
/// single set of screens reads "Paciente"/"Aluno"/"Tutor" etc. without nine UIs.
/// </summary>
public sealed record ServiceLabels(
    string Customer,
    string Professional,
    string CatalogItem,
    string Appointment,
    string Order,
    string Subject);
