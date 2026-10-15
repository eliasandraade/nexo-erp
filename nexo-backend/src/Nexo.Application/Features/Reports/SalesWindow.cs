using System.Globalization;
using Nexo.Application.Common;

namespace Nexo.Application.Features.Reports;

/// <summary>
/// A report period in Brazil calendar days, both ends included. A sale belongs to the day it was
/// confirmed (or created, for the rare sale never confirmed) — in Brazil time, so a sale at 22:00 on
/// the 31st is still the 31st and not the 1st, as it would be in UTC.
/// </summary>
public sealed record SalesWindow(DateOnly From, DateOnly To)
{
    public const int MaxDays = 366;

    public DateTime FromUtc => BusinessClock.DayStartUtc(From);
    public DateTime ToUtcExclusive => BusinessClock.DayStartUtc(To.AddDays(1));
    public int Days => To.DayNumber - From.DayNumber + 1;

    public static DateOnly TodayInBrazil(DateTime? nowUtc = null)
        => DateOnly.FromDateTime(BusinessClock.ToBrazil(nowUtc ?? DateTime.UtcNow));

    /// <summary>From the 1st of the current month (Brazil) to today.</summary>
    public static SalesWindow MonthToDate(DateTime? nowUtc = null)
    {
        var today = TodayInBrazil(nowUtc);
        return new SalesWindow(new DateOnly(today.Year, today.Month, 1), today);
    }

    /// <summary>The last <paramref name="days"/> days ending today (Brazil).</summary>
    public static SalesWindow LastDays(int days, DateTime? nowUtc = null)
    {
        var today = TodayInBrazil(nowUtc);
        return new SalesWindow(today.AddDays(1 - days), today);
    }

    /// <summary>
    /// Reads <c>from</c>/<c>to</c> as yyyy-MM-dd. Missing ends default to the current month to date.
    /// Returns an error message (in Portuguese, for the screen) instead of throwing.
    /// </summary>
    public static SalesWindow? TryParse(string? from, string? to, out string? error, DateTime? nowUtc = null)
    {
        error = null;
        var month = MonthToDate(nowUtc);
        DateOnly f = month.From, t = month.To;
        if (!string.IsNullOrWhiteSpace(from) && !DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out f))
        {
            error = "Data inicial inválida. Use o formato AAAA-MM-DD.";
            return null;
        }
        if (!string.IsNullOrWhiteSpace(to) && !DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
        {
            error = "Data final inválida. Use o formato AAAA-MM-DD.";
            return null;
        }
        if (t < f)
        {
            error = "A data final vem antes da inicial.";
            return null;
        }
        var window = new SalesWindow(f, t);
        if (window.Days > MaxDays)
        {
            error = $"Escolha um período de até {MaxDays} dias.";
            return null;
        }
        return window;
    }
}
