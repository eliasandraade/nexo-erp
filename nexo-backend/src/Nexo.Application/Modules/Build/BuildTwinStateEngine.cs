using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

/// <summary>
/// The 4D state of model elements on a date — pure and deterministic (no I/O, no clock).
///
/// State of an element at date d:
///   1. its latest explicit event (<see cref="BuildElementProgressEvent"/>) with EffectiveDate ≤ d (ties: the latest recorded);
///   2. otherwise derived from the stage it is bound to — precedence Element &gt; Zone (its zone or an ancestor, nearest first)
///      &gt; Category. When several stages bind it at the same level, the stage with the latest planned end wins (a known
///      planned end beats none; then the stage id, for determinism);
///   3. unbound and without events → null: never shown as progress.
/// Stage at d: percent = its last progress event with EffectiveDate ≤ d (no history at all: the current percent from the
/// day it is known, i.e. d ≥ today; before that 0). 100 → Completed; blocked → Blocked; planned end &lt; d → Delayed;
/// percent &gt; 0 → InProgress; else NotStarted.
/// </summary>
public static class BuildTwinStateEngine
{
    public sealed record StageInput(
        Guid StageId,
        DateOnly? PlannedEnd,
        /// <summary>Whether the stage counts as blocked on the computed date (the caller only knows the current status).</summary>
        bool Blocked,
        IReadOnlyList<(DateOnly EffectiveDate, DateTime RecordedAt, int Percent)> ProgressEvents,
        int CurrentPercent);

    public sealed record ElementInput(string ExternalId, string Category, Guid? ZoneId, Guid VersionId);

    public sealed record BindingInput(Guid StageId, BuildBindingScope Scope, string? ExternalId, Guid? ZoneId, string? Category,
        Guid? VersionId);

    public sealed record EventInput(string ExternalId, BuildElementState State, DateOnly EffectiveDate, DateTime RecordedAt);

    public sealed record ElementState(string ExternalId, BuildElementState? State, Guid? StageId, bool Explicit);

    public sealed record ZoneState(Guid ZoneId, BuildElementState? State, Guid? StageId);

    public sealed record Result(
        DateOnly Date,
        IReadOnlyList<ElementState> Elements,
        IReadOnlyList<ZoneState> Zones,
        IReadOnlyDictionary<Guid, BuildElementState> Stages)
    {
        public int Unbound => Elements.Count(e => e.State is null);

        public IReadOnlyDictionary<string, int> Counts => Enum.GetValues<BuildElementState>()
            .ToDictionary(s => s.ToString(), s => Elements.Count(e => e.State == s));

        /// <summary>State name → external ids (only states that occur).</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> States => Elements.Where(e => e.State is not null)
            .GroupBy(e => e.State!.Value)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key.ToString(), g => (IReadOnlyList<string>)g.Select(e => e.ExternalId).OrderBy(x => x, StringComparer.Ordinal).ToList());

        public IReadOnlyDictionary<string, IReadOnlyList<Guid>> ZoneStates => Zones.Where(z => z.State is not null)
            .GroupBy(z => z.State!.Value)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key.ToString(), g => (IReadOnlyList<Guid>)g.Select(z => z.ZoneId).OrderBy(x => x).ToList());
    }

    public static int StagePercentAt(StageInput stage, DateOnly date, DateOnly today)
    {
        if (stage.ProgressEvents.Count == 0) return date >= today ? Math.Clamp(stage.CurrentPercent, 0, 100) : 0;
        var last = stage.ProgressEvents
            .Where(e => e.EffectiveDate <= date)
            .OrderBy(e => e.EffectiveDate).ThenBy(e => e.RecordedAt)
            .Select(e => (int?)e.Percent)
            .LastOrDefault();
        return Math.Clamp(last ?? 0, 0, 100);
    }

    public static BuildElementState StageStateAt(StageInput stage, DateOnly date, DateOnly today)
    {
        var percent = StagePercentAt(stage, date, today);
        if (percent >= 100) return BuildElementState.Completed;
        if (stage.Blocked) return BuildElementState.Blocked;
        if (stage.PlannedEnd is { } end && end < date) return BuildElementState.Delayed;
        return percent > 0 ? BuildElementState.InProgress : BuildElementState.NotStarted;
    }

    public static Result Compute(
        DateOnly date,
        DateOnly today,
        IReadOnlyList<ElementInput> elements,
        IReadOnlyDictionary<Guid, Guid?> zoneParents,
        IReadOnlyList<StageInput> stages,
        IReadOnlyList<BindingInput> bindings,
        IReadOnlyList<EventInput> events)
    {
        var stageById = stages.ToDictionary(s => s.StageId);
        var stageStates = stages.ToDictionary(s => s.StageId, s => StageStateAt(s, date, today));
        // only bindings to known stages count (a deleted stage's bindings are gone with it anyway)
        var valid = bindings.Where(b => stageById.ContainsKey(b.StageId)).ToList();

        var byElement = valid.Where(b => b.Scope == BuildBindingScope.Element && b.ExternalId is not null)
            .GroupBy(b => b.ExternalId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(b => b.StageId).ToList(), StringComparer.Ordinal);
        var byZone = valid.Where(b => b.Scope == BuildBindingScope.Zone && b.ZoneId is not null)
            .GroupBy(b => b.ZoneId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(b => b.StageId).ToList());
        var byCategory = valid.Where(b => b.Scope == BuildBindingScope.Category && b.Category is not null).ToList();

        var latestEvent = events.Where(e => e.EffectiveDate <= date)
            .GroupBy(e => e.ExternalId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.EffectiveDate).ThenBy(e => e.RecordedAt).Last().State, StringComparer.Ordinal);

        Guid? Pick(IEnumerable<Guid> candidates)
        {
            var list = candidates.Distinct().ToList();
            if (list.Count == 0) return null;
            return list
                .OrderByDescending(id => stageById[id].PlannedEnd.HasValue)
                .ThenByDescending(id => stageById[id].PlannedEnd ?? DateOnly.MinValue)
                .ThenBy(id => id)
                .First();
        }

        Guid? ZoneStage(Guid? zoneId)
        {
            var current = zoneId;
            for (var guard = 0; current is { } z && guard < 64; guard++)
            {
                if (byZone.TryGetValue(z, out var ids) && Pick(ids) is { } picked) return picked;
                current = zoneParents.TryGetValue(z, out var p) ? p : null;
            }
            return null;
        }

        var result = new List<ElementState>(elements.Count);
        foreach (var e in elements)
        {
            Guid? stage = byElement.TryGetValue(e.ExternalId, out var direct) ? Pick(direct) : null;
            stage ??= ZoneStage(e.ZoneId);
            stage ??= Pick(byCategory
                .Where(b => string.Equals(b.Category, e.Category, StringComparison.OrdinalIgnoreCase)
                         && (b.VersionId is null || b.VersionId == e.VersionId))
                .Select(b => b.StageId));

            if (latestEvent.TryGetValue(e.ExternalId, out var explicitState))
                result.Add(new ElementState(e.ExternalId, explicitState, stage, Explicit: true));
            else
                result.Add(new ElementState(e.ExternalId, stage is { } s ? stageStates[s] : null, stage, Explicit: false));
        }

        var zones = zoneParents.Keys
            .Select(z => ZoneStage(z) is { } s ? new ZoneState(z, stageStates[s], s) : new ZoneState(z, null, null))
            .ToList();

        return new Result(date, result, zones, stageStates);
    }
}
