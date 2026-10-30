namespace Nexo.Application.Modules.Build;

/// <summary>
/// Working-day calendar of the forecast (Wave 6) — pure. Working weekdays are configurable (<c>Build:Forecast:WorkingDays</c>,
/// default Monday–Saturday: Sunday is never worked by default) and the Brazilian national holidays of
/// <see cref="BrazilNationalHolidays"/> (plus optional extra dates) are never working days.
/// </summary>
public sealed class BuildWorkCalendar
{
    /// <summary>Upper bound of working days any computation walks (≈ 8 years) — protects against absurd rates.</summary>
    public const int MaxWorkingDays = 2500;

    public static readonly IReadOnlyList<DayOfWeek> DefaultWorkingDays =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];

    private readonly HashSet<DayOfWeek> _days;
    private readonly HashSet<DateOnly> _holidays;

    public BuildWorkCalendar(IEnumerable<DayOfWeek>? workingDays, IEnumerable<DateOnly>? holidays)
    {
        _days = (workingDays ?? DefaultWorkingDays).ToHashSet();
        if (_days.Count == 0) _days = DefaultWorkingDays.ToHashSet();   // a calendar without working days would never finish
        _holidays = (holidays ?? []).ToHashSet();
    }

    /// <summary>Monday–Saturday with the national holidays of the static table.</summary>
    public static BuildWorkCalendar Default { get; } = new(DefaultWorkingDays, BrazilNationalHolidays.All());

    public IReadOnlyCollection<DayOfWeek> WorkingDays => _days;

    public bool IsHoliday(DateOnly date) => _holidays.Contains(date);

    public bool IsWorkingDay(DateOnly date) => _days.Contains(date.DayOfWeek) && !_holidays.Contains(date);

    /// <summary>Working days in [<paramref name="from"/>, <paramref name="to"/>] (both inclusive); 0 when to &lt; from.</summary>
    public int WorkingDaysBetween(DateOnly from, DateOnly to)
    {
        if (to < from) return 0;
        var span = to.DayNumber - from.DayNumber + 1;
        if (span > MaxWorkingDays * 2) span = MaxWorkingDays * 2;
        var count = 0;
        for (var i = 0; i < span; i++)
            if (IsWorkingDay(from.AddDays(i))) count++;
        return count;
    }

    /// <summary>The date itself when it is a working day, else the next working day.</summary>
    public DateOnly NextWorkingDay(DateOnly date)
    {
        var d = date;
        for (var guard = 0; !IsWorkingDay(d) && guard < 366; guard++) d = d.AddDays(1);
        return d;
    }

    /// <summary>
    /// The date reached after <paramref name="workingDays"/> working days counted strictly after <paramref name="from"/>
    /// (0 → <paramref name="from"/> itself). Capped at <see cref="MaxWorkingDays"/>.
    /// </summary>
    public DateOnly AddWorkingDays(DateOnly from, int workingDays)
    {
        var remaining = Math.Clamp(workingDays, 0, MaxWorkingDays);
        var d = from;
        while (remaining > 0)
        {
            d = d.AddDays(1);
            if (IsWorkingDay(d)) remaining--;
        }
        return d;
    }

    /// <summary>
    /// Parses "Mon,Tue,Wed" / "seg,ter" / "1,2,3" (0 = Sunday). Unknown tokens are ignored; nothing valid → the default.
    /// </summary>
    public static IReadOnlyList<DayOfWeek> ParseDays(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return DefaultWorkingDays;
        var days = new List<DayOfWeek>();
        foreach (var raw in csv.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var t = raw.ToLowerInvariant();
            DayOfWeek? d = t switch
            {
                "0" or "sun" or "sunday" or "dom" or "domingo" => DayOfWeek.Sunday,
                "1" or "mon" or "monday" or "seg" or "segunda" => DayOfWeek.Monday,
                "2" or "tue" or "tuesday" or "ter" or "terca" or "terça" => DayOfWeek.Tuesday,
                "3" or "wed" or "wednesday" or "qua" or "quarta" => DayOfWeek.Wednesday,
                "4" or "thu" or "thursday" or "qui" or "quinta" => DayOfWeek.Thursday,
                "5" or "fri" or "friday" or "sex" or "sexta" => DayOfWeek.Friday,
                "6" or "sat" or "saturday" or "sab" or "sáb" or "sabado" or "sábado" => DayOfWeek.Saturday,
                _ => null,
            };
            if (d is { } day && !days.Contains(day)) days.Add(day);
        }
        return days.Count == 0 ? DefaultWorkingDays : days.OrderBy(x => ((int)x + 6) % 7).ToList();
    }
}

/// <summary>
/// Brazilian national holidays (Lei 662/1949, Lei 6.802/1980, Lei 14.759/2023 and Good Friday) as a small static table — no
/// external API. Fixed dates hold for every year; Good Friday (movable) is listed for 2026–2028 only: extend
/// <see cref="GoodFriday"/> each year (or add dates in <c>Build:Forecast:ExtraHolidays</c>). Carnival and Corpus Christi are
/// optional days off (ponto facultativo), not national holidays — add them per company in the extra list when they stop work.
/// State and municipal holidays are not included.
/// </summary>
public static class BrazilNationalHolidays
{
    public const int FirstYear = 2026;
    public const int LastYear = 2028;

    private static readonly (int Month, int Day, string Name)[] Fixed =
    [
        (1, 1, "Confraternização Universal"),
        (4, 21, "Tiradentes"),
        (5, 1, "Dia do Trabalho"),
        (9, 7, "Independência do Brasil"),
        (10, 12, "Nossa Senhora Aparecida"),
        (11, 2, "Finados"),
        (11, 15, "Proclamação da República"),
        (11, 20, "Dia Nacional de Zumbi e da Consciência Negra"),
        (12, 25, "Natal"),
    ];

    private static readonly Dictionary<int, DateOnly> GoodFriday = new()
    {
        [2026] = new DateOnly(2026, 4, 3),
        [2027] = new DateOnly(2027, 3, 26),
        [2028] = new DateOnly(2028, 4, 14),
    };

    public static IReadOnlyList<(DateOnly Date, string Name)> For(int year)
    {
        var list = Fixed.Select(f => (new DateOnly(year, f.Month, f.Day), f.Name)).ToList();
        if (GoodFriday.TryGetValue(year, out var gf)) list.Add((gf, "Sexta-feira Santa"));
        return list.OrderBy(x => x.Item1).ToList();
    }

    /// <summary>Holidays from <see cref="FirstYear"/> − 1 to <see cref="LastYear"/> + 2 (fixed dates; Good Friday in the table).</summary>
    public static IReadOnlyList<DateOnly> All()
        => Enumerable.Range(FirstYear - 1, LastYear - FirstYear + 4).SelectMany(y => For(y).Select(h => h.Date)).ToList();
}
