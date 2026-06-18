using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexo.Application.Modules.Service.Public;

/// <summary>A single open window on a weekday, in the store's local wall clock.</summary>
public sealed record WorkingTimeWindow(TimeOnly Start, TimeOnly End);

/// <summary>
/// Parsed per-professional weekly availability (see <see cref="Nexo.Domain.Modules.Service.SvcProfessional.WorkingHoursJson"/>).
///
/// The wire shape is an array of <c>{ "weekday": 0-6, "windows": [{ "start": "HH:mm", "end": "HH:mm" }] }</c>
/// where weekday follows <see cref="DayOfWeek"/> (0 = Sunday). Parsing is intentionally lenient: a
/// malformed document yields an EMPTY schedule (no availability) rather than throwing — the portal
/// then honestly shows "indisponível" instead of inventing slots or 500-ing.
/// </summary>
public sealed class ServiceWorkingHours
{
    private readonly IReadOnlyDictionary<DayOfWeek, IReadOnlyList<WorkingTimeWindow>> _byDay;

    private ServiceWorkingHours(IReadOnlyDictionary<DayOfWeek, IReadOnlyList<WorkingTimeWindow>> byDay)
        => _byDay = byDay;

    public static ServiceWorkingHours Empty { get; } =
        new(new Dictionary<DayOfWeek, IReadOnlyList<WorkingTimeWindow>>());

    /// <summary>True when no day has any valid window — the professional is not publicly bookable.</summary>
    public bool IsEmpty => _byDay.Count == 0;

    /// <summary>The valid windows for a weekday (possibly empty), sorted by start time.</summary>
    public IReadOnlyList<WorkingTimeWindow> ForDay(DayOfWeek day) =>
        _byDay.TryGetValue(day, out var windows) ? windows : [];

    public static ServiceWorkingHours Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;

        List<DayDto>? days;
        try
        {
            days = JsonSerializer.Deserialize<List<DayDto>>(json, JsonOpts);
        }
        catch (JsonException)
        {
            return Empty; // never trust the column to 500 availability
        }

        if (days is null || days.Count == 0) return Empty;

        var byDay = new Dictionary<DayOfWeek, IReadOnlyList<WorkingTimeWindow>>();
        foreach (var day in days)
        {
            if (day.Weekday is < 0 or > 6) continue;
            var weekday = (DayOfWeek)day.Weekday;

            var windows = new List<WorkingTimeWindow>();
            foreach (var w in day.Windows ?? [])
            {
                if (!TryParseTime(w.Start, out var start) || !TryParseTime(w.End, out var end)) continue;
                if (end <= start) continue;
                windows.Add(new WorkingTimeWindow(start, end));
            }

            if (windows.Count == 0) continue;
            windows.Sort((a, b) => a.Start.CompareTo(b.Start));

            // Merge same-weekday entries if the JSON repeats a day.
            byDay[weekday] = byDay.TryGetValue(weekday, out var existing)
                ? existing.Concat(windows).OrderBy(x => x.Start).ToList()
                : windows;
        }

        return byDay.Count == 0 ? Empty : new ServiceWorkingHours(byDay);
    }

    private static bool TryParseTime(string? value, out TimeOnly time)
    {
        time = default;
        return !string.IsNullOrWhiteSpace(value)
            && TimeOnly.TryParseExact(value.Trim(), "HH:mm", out time);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record DayDto(
        [property: JsonPropertyName("weekday")] int Weekday,
        [property: JsonPropertyName("windows")] List<WindowDto>? Windows);

    private sealed record WindowDto(
        [property: JsonPropertyName("start")] string? Start,
        [property: JsonPropertyName("end")] string? End);
}
