namespace Nexo.Application.Common;

/// <summary>
/// "Hoje" for business rules (vencimento, atraso): the operation's calendar day in Brazil, whatever
/// the server's zone. Instants stay UTC; only day boundaries come from here.
/// </summary>
public static class BusinessClock
{
    private static readonly TimeZoneInfo Brazil = Resolve();

    private static TimeZoneInfo Resolve()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT"); }
    }

    /// <summary>UTC instant at which today (Brazil) began.</summary>
    public static DateTime TodayStartUtc(DateTime? nowUtc = null)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc ?? DateTime.UtcNow, Brazil);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified), Brazil);
    }

    /// <summary>UTC instant at which a Brazil calendar day begins.</summary>
    public static DateTime DayStartUtc(DateOnly day)
        => TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Brazil);

    /// <summary>Noon of a Brazil calendar day, in UTC — a date-only value that never slips a day.</summary>
    public static DateTime NoonUtc(DateOnly day)
        => TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified), Brazil);

    /// <summary>A UTC instant as Brazil local time.</summary>
    public static DateTime ToBrazil(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Brazil);

    /// <summary>Whole days a título is late (0 when due today or later).</summary>
    public static int DaysOverdue(DateTime dueDateUtc, DateTime? nowUtc = null)
    {
        var dueDay   = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(dueDateUtc, DateTimeKind.Utc), Brazil).Date;
        var todayDay = TimeZoneInfo.ConvertTimeFromUtc(nowUtc ?? DateTime.UtcNow, Brazil).Date;
        return Math.Max(0, (todayDay - dueDay).Days);
    }
}
