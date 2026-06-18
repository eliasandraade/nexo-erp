namespace Nexo.Application.Modules.Service.Public;

/// <summary>A busy interval (an existing non-terminal appointment) in UTC.</summary>
public readonly record struct BusyInterval(DateTime StartUtc, DateTime EndUtc);

/// <summary>
/// Pure, side-effect-free availability engine for the public booking portal. Given a professional's
/// weekly working hours and their existing appointments, it enumerates the real free slots that fit
/// the chosen service duration — honouring the slot grid, minimum lead time, the future-days horizon,
/// and the store timezone. No working hours ⇒ no slots (never a fabricated time).
///
/// Everything here is deterministic given <paramref name="nowUtc"/>, which makes it unit-testable.
/// </summary>
public static class AvailabilityCalculator
{
    /// <summary>Hard cap on returned slots so a wide window can never produce an unbounded payload.</summary>
    public const int MaxSlots = 2000;

    public static IReadOnlyList<DateTime> GenerateSlots(
        ServiceWorkingHours hours,
        TimeZoneInfo tz,
        DateTime nowUtc,
        int minLeadMinutes,
        int bookingDaysAhead,
        int slotIntervalMinutes,
        int durationMinutes,
        IReadOnlyList<BusyInterval> busy,
        DateTime? fromUtc = null,
        DateTime? toUtc = null)
    {
        if (hours.IsEmpty || durationMinutes <= 0 || slotIntervalMinutes <= 0)
            return [];

        var earliestUtc = nowUtc.AddMinutes(Math.Max(0, minLeadMinutes));
        if (fromUtc is { } f && f > earliestUtc) earliestUtc = f;

        var horizonUtc = nowUtc.AddDays(Math.Max(1, bookingDaysAhead));
        if (toUtc is { } t && t < horizonUtc) horizonUtc = t;

        if (earliestUtc > horizonUtc) return [];

        var startDateLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(earliestUtc, tz));
        var endDateLocal   = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(horizonUtc, tz));

        var slots = new List<DateTime>();

        for (var date = startDateLocal; date <= endDateLocal; date = date.AddDays(1))
        {
            foreach (var window in hours.ForDay(date.DayOfWeek))
            {
                var winStart = window.Start.Hour * 60 + window.Start.Minute;
                var winEnd   = window.End.Hour * 60 + window.End.Minute;

                for (var m = winStart; m + durationMinutes <= winEnd; m += slotIntervalMinutes)
                {
                    var localStart = date.ToDateTime(new TimeOnly(m / 60, m % 60));
                    if (!TryConvertToUtc(localStart, tz, out var slotStartUtc)) continue;

                    var slotEndUtc = slotStartUtc.AddMinutes(durationMinutes);

                    if (slotStartUtc < earliestUtc || slotStartUtc > horizonUtc) continue;
                    if (Overlaps(busy, slotStartUtc, slotEndUtc)) continue;

                    slots.Add(slotStartUtc);
                    if (slots.Count >= MaxSlots)
                        return Finalize(slots);
                }
            }
        }

        return Finalize(slots);
    }

    /// <summary>
    /// True when [startUtc, endUtc) fits entirely inside one of the professional's working windows
    /// for that local day. Used at booking time to reject off-hours times the client may have crafted
    /// outside the published availability list.
    /// </summary>
    public static bool IsWithinWorkingHours(
        ServiceWorkingHours hours, TimeZoneInfo tz, DateTime startUtc, DateTime endUtc)
    {
        if (hours.IsEmpty || endUtc <= startUtc) return false;

        var localStart = TimeZoneInfo.ConvertTimeFromUtc(startUtc, tz);
        var localEnd   = TimeZoneInfo.ConvertTimeFromUtc(endUtc, tz);

        // A slot must not straddle midnight — windows are within a single day.
        if (DateOnly.FromDateTime(localStart) != DateOnly.FromDateTime(localEnd)
            && localEnd.TimeOfDay != TimeSpan.Zero)
            return false;

        var startTime = TimeOnly.FromDateTime(localStart);
        var endTime   = TimeOnly.FromDateTime(localEnd);

        foreach (var window in hours.ForDay(localStart.DayOfWeek))
        {
            if (startTime >= window.Start && endTime <= window.End)
                return true;
        }
        return false;
    }

    public static bool Overlaps(IReadOnlyList<BusyInterval> busy, DateTime startUtc, DateTime endUtc)
    {
        foreach (var b in busy)
            if (startUtc < b.EndUtc && endUtc > b.StartUtc)
                return true;
        return false;
    }

    private static IReadOnlyList<DateTime> Finalize(List<DateTime> slots)
        => slots.Distinct().OrderBy(s => s).ToList();

    private static bool TryConvertToUtc(DateTime localUnspecified, TimeZoneInfo tz, out DateTime utc)
    {
        try
        {
            utc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(localUnspecified, DateTimeKind.Unspecified), tz);
            return true;
        }
        catch (ArgumentException)
        {
            // Invalid local time (e.g. inside a spring-forward gap) — skip the slot.
            utc = default;
            return false;
        }
    }
}
