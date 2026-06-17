namespace Nexo.Domain.Modules.Service;

/// <summary>
/// Single source of truth for the Orken Service module family.
///
/// Decision D1: the "service" family is a set of billable vertical keys that all unlock the
/// SAME engine. <see cref="IsServiceFamilyKey"/> backs the family-aware module gate;
/// <see cref="Resolve"/> picks the active preset.
///
/// Q5: when a tenant holds more than one family key, resolution is deterministic — the
/// lowest <see cref="ServicePreset.Priority"/> wins. A per-store SvcSettings override is
/// out of scope for PR0 and comes with the engine (see the v1 plan).
/// </summary>
public static class ServicePresetRegistry
{
    /// <summary>Logical engine key. Individual verticals are the billable keys in <see cref="All"/>.</summary>
    public const string Family = "service";

    public static IReadOnlyList<ServicePreset> All { get; } = BuildAll();

    /// <summary>The billable vertical keys that belong to the service family.</summary>
    public static IReadOnlyList<string> FamilyKeys { get; } =
        All.Select(p => p.Key).ToArray();

    /// <summary>True when <paramref name="moduleKey"/> is one of the service-family verticals.</summary>
    public static bool IsServiceFamilyKey(string? moduleKey) =>
        !string.IsNullOrWhiteSpace(moduleKey)
        && All.Any(p => string.Equals(p.Key, moduleKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Resolves the active preset from a tenant's active module keys. Non-family keys are
    /// ignored. Returns null when no service-family key is active.
    /// </summary>
    public static ServicePreset? Resolve(IEnumerable<string>? activeModuleKeys)
    {
        if (activeModuleKeys is null) return null;

        return activeModuleKeys
            .Select(k => All.FirstOrDefault(p => string.Equals(p.Key, k, StringComparison.OrdinalIgnoreCase)))
            .Where(p => p is not null)
            .OrderBy(p => p!.Priority)
            .FirstOrDefault();
    }

    // Priority follows the v1 segment order (spec §1). Lower number = wins resolution ties.
    private static IReadOnlyList<ServicePreset> BuildAll()
    {
        // Baseline: every surface off. Each preset enables only what it uses via `with`, so the
        // capability sets stay declarative and free of repeated full constructors.
        // Declared as a local (not a static field) to avoid static-init ordering with `All`.
        var off = new ServiceCapabilities(
            Appointments: false, Orders: false, Quotes: false, Parts: false, Packages: false,
            SimpleRecord: false, Commissions: false, Recurrence: false, SubjectKind: null);

        return new List<ServicePreset>
        {
            new("clinica-medica", "Clínicas Médicas e Odontológicas", 0,
                new ServiceLabels("Paciente", "Profissional", "Procedimento", "Consulta", "Ordem de serviço", "Registro"),
                off with { Appointments = true, SimpleRecord = true }),

            new("personal-trainer", "Personal Trainers", 1,
                new ServiceLabels("Aluno", "Personal", "Sessão", "Sessão", "Ordem", "Avaliação"),
                off with { Appointments = true, Packages = true, SimpleRecord = true }),

            new("nutricionista", "Nutricionistas", 2,
                new ServiceLabels("Paciente", "Nutricionista", "Consulta", "Consulta", "Ordem", "Avaliação"),
                off with { Appointments = true, SimpleRecord = true }),

            new("oficina-mecanica", "Oficinas Mecânicas", 3,
                new ServiceLabels("Cliente", "Mecânico", "Serviço", "Agendamento", "Ordem de serviço", "Veículo"),
                off with { Orders = true, Quotes = true, Parts = true, SubjectKind = ServiceSubjectKind.Vehicle }),

            new("programador-autonomo", "Programadores Autônomos", 4,
                new ServiceLabels("Cliente", "Profissional", "Serviço", "Agendamento", "Projeto", "Item"),
                off with { Orders = true, Quotes = true }),

            new("autoescola", "Autoescolas", 5,
                new ServiceLabels("Aluno", "Instrutor", "Aula", "Aula", "Ordem", "Registro"),
                off with { Appointments = true, Packages = true, SimpleRecord = true }),

            new("pet-shop", "Pet Shops + Clínicas Veterinárias", 6,
                new ServiceLabels("Tutor", "Profissional", "Serviço", "Agendamento", "Ordem de serviço", "Pet"),
                off with { Appointments = true, Packages = true, SimpleRecord = true, SubjectKind = ServiceSubjectKind.Pet }),

            new("salao-beleza", "Salões de Beleza", 7,
                new ServiceLabels("Cliente", "Profissional", "Serviço", "Agendamento", "Comanda", "Registro"),
                off with { Appointments = true, Packages = true, Commissions = true }),

            new("escola-idiomas", "Escolas de Idiomas", 8,
                new ServiceLabels("Aluno", "Professor", "Aula", "Aula", "Matrícula", "Registro"),
                off with { Appointments = true, Packages = true, SimpleRecord = true, Recurrence = true }),
        };
    }
}
