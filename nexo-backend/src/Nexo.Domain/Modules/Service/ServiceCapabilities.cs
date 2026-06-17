namespace Nexo.Domain.Modules.Service;

/// <summary>
/// Which engine surfaces a preset turns on (decision D2: the engine ships every surface;
/// the preset toggles which are active per vertical). These flags are the v1 baseline and
/// are validated/refined during P1 — see docs/superpowers/specs/2026-06-17-orken-service-v1.md §6.
/// </summary>
public sealed record ServiceCapabilities(
    bool Appointments,
    bool Orders,
    bool Quotes,
    bool Parts,
    bool Packages,
    bool SimpleRecord,
    bool Commissions,
    bool Recurrence,
    ServiceSubjectKind? SubjectKind);
