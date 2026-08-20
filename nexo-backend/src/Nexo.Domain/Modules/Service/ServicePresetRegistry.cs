namespace Nexo.Domain.Modules.Service;

/// <summary>
/// Single source of truth for the Orken Service verticals.
///
/// Orken Service is ONE commercial module — the key <see cref="Family"/> ("service"). The
/// verticals in <see cref="All"/> are INTERNAL presets (the "ramo"), chosen per store via
/// SvcSettings during onboarding. They are descriptors (labels + capabilities), never module
/// keys, and never entitlements.
///
/// The per-vertical SKUs sold before that model are listed in <see cref="LegacyVerticalKeys"/>
/// and no longer grant access: the ConvertLegacyServiceSubscriptions migration rewrites them
/// to "service" while preserving each store's vertical in SvcSettings.
/// </summary>
public static class ServicePresetRegistry
{
    /// <summary>The single commercial module key. Verticals are internal presets, not SKUs.</summary>
    public const string Family = "service";

    public static IReadOnlyList<ServicePreset> All { get; } = BuildAll();

    /// <summary>
    /// Module keys sold per vertical before the single-module model. Kept as an explicit list
    /// (not derived from <see cref="All"/>) because the two sets have diverged: "barbearia" is
    /// a preset that was never a SKU, and a retired SKU must stay recognizable here even if its
    /// preset is later renamed or dropped.
    /// </summary>
    public static IReadOnlyList<string> LegacyVerticalKeys { get; } = new[]
    {
        "clinica-medica", "salao-beleza", "pet-shop", "oficina-mecanica", "nutricionista",
        "personal-trainer", "autoescola", "escola-idiomas", "programador-autonomo",
    };

    /// <summary>True when <paramref name="moduleKey"/> is a retired per-vertical SKU.</summary>
    public static bool IsLegacyVerticalKey(string? moduleKey) =>
        !string.IsNullOrWhiteSpace(moduleKey)
        && LegacyVerticalKeys.Any(k => string.Equals(k, moduleKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when the module key entitles the tenant to the Service engine. Only the single
    /// commercial module qualifies — the internal preset is configured separately (SvcSettings)
    /// and never derived from the module key.
    /// </summary>
    public static bool IsServiceEntitlement(string? moduleKey) =>
        string.Equals(moduleKey, Family, StringComparison.OrdinalIgnoreCase);

    /// <summary>Resolves a single preset by its exact key (the chosen vertical). Null when unknown.</summary>
    public static ServicePreset? GetByKey(string? presetKey) =>
        string.IsNullOrWhiteSpace(presetKey)
            ? null
            : All.FirstOrDefault(p => string.Equals(p.Key, presetKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when <paramref name="presetKey"/> is a valid internal preset.</summary>
    public static bool IsValidPresetKey(string? presetKey) => GetByKey(presetKey) is not null;

    // Priority follows the v1 segment order (spec §1). Lower number = wins resolution ties.
    private static IReadOnlyList<ServicePreset> BuildAll()
    {
        // Baseline: every surface off. Each preset enables only what it uses via `with`, so the
        // capability sets stay declarative and free of repeated full constructors.
        // Declared as a local (not a static field) to avoid static-init ordering with `All`.
        var off = new ServiceCapabilities(
            Appointments: false, Orders: false, Packages: false,
            Commissions: false, SubjectKind: null);

        return new List<ServicePreset>
        {
            new("clinica-medica", "Clínicas Médicas e Odontológicas", 0,
                new ServiceLabels("Paciente", "Profissional", "Procedimento", "Consulta", "Ordem de serviço", "Registro"),
                off with { Appointments = true }),

            new("personal-trainer", "Personal Trainers", 1,
                new ServiceLabels("Aluno", "Personal", "Sessão", "Sessão", "Ordem", "Avaliação"),
                off with { Appointments = true, Packages = true }),

            new("nutricionista", "Nutricionistas", 2,
                new ServiceLabels("Paciente", "Nutricionista", "Consulta", "Consulta", "Ordem", "Avaliação"),
                off with { Appointments = true }),

            new("oficina-mecanica", "Oficinas Mecânicas", 3,
                new ServiceLabels("Cliente", "Mecânico", "Serviço", "Agendamento", "Ordem de serviço", "Veículo"),
                off with { Orders = true, SubjectKind = ServiceSubjectKind.Vehicle }),

            new("programador-autonomo", "Programadores Autônomos", 4,
                new ServiceLabels("Cliente", "Profissional", "Serviço", "Agendamento", "Projeto", "Item"),
                off with { Orders = true }),

            new("autoescola", "Autoescolas", 5,
                new ServiceLabels("Aluno", "Instrutor", "Aula", "Aula", "Ordem", "Registro"),
                off with { Appointments = true, Packages = true }),

            new("pet-shop", "Pet Shops + Clínicas Veterinárias", 6,
                new ServiceLabels("Tutor", "Profissional", "Serviço", "Agendamento", "Ordem de serviço", "Pet"),
                off with { Appointments = true, Packages = true, SubjectKind = ServiceSubjectKind.Pet }),

            new("barbearia", "Barbearias", 7,
                new ServiceLabels("Cliente", "Barbeiro", "Serviço", "Agendamento", "Comanda", "Registro"),
                off with { Appointments = true, Orders = true, Packages = true, Commissions = true }),

            new("salao-beleza", "Salões de Beleza", 8,
                new ServiceLabels("Cliente", "Profissional", "Serviço", "Agendamento", "Comanda", "Registro"),
                off with { Appointments = true, Orders = true, Packages = true, Commissions = true }),

            new("escola-idiomas", "Escolas de Idiomas", 9,
                new ServiceLabels("Aluno", "Professor", "Aula", "Aula", "Matrícula", "Registro"),
                off with { Appointments = true, Packages = true }),
        };
    }
}
