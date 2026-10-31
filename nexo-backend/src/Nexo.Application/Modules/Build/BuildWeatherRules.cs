using System.Globalization;
using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

/// <summary>One forecast day at the site (Open-Meteo daily values; any of them may be missing).</summary>
public sealed record BuildWeatherDay(
    DateOnly Date,
    double? TemperatureMax,
    double? TemperatureMin,
    double? PrecipitationMm,
    int? PrecipitationProbability,
    double? WindSpeedMaxKmh,
    string? Description);

/// <summary>A stage as the weather rules see it: whether it is sensitive and the days it is (forecast to be) in the field.</summary>
public sealed record BuildWeatherStage(Guid StageId, string Name, bool Sensitive, DateOnly? ActiveFrom, DateOnly? ActiveTo);

/// <summary>An adverse condition of a day (rain, strong wind) — before matching stages.</summary>
public sealed record BuildWeatherCondition(string Kind, string Level, string Label);

/// <summary>
/// Weather per activity (Wave 6) — pure. A stage is <b>sensitive</b> when set explicitly (<see cref="BuildStage.WeatherSensitive"/>)
/// or, when not set, when its name contains one of the keywords (accent/case-insensitive; configurable in
/// <c>Build:Weather:SensitiveKeywords</c>). Thresholds (forecast, never certainty):
/// <list type="bullet">
/// <item>heavy rain — precipitation ≥ 10 mm <b>and</b> probability ≥ 70% → high;</item>
/// <item>rain — precipitation ≥ 10 mm with lower/unknown probability, or ≥ 2 mm with probability ≥ 60% → medium;</item>
/// <item>strong wind — max wind ≥ 50 km/h → high; ≥ 40 km/h → medium.</item>
/// </list>
/// A risk is raised for each sensitive stage active on the day (in progress, or forecast/planned to be in the field).
/// </summary>
public static class BuildWeatherRules
{
    public const double HeavyRainMm = 10;
    public const int HeavyRainProbability = 70;
    public const double RainMm = 2;
    public const int RainProbability = 60;
    public const double WindMediumKmh = 40;
    public const double WindHighKmh = 50;

    public const string High = "high";
    public const string Medium = "medium";
    public const string None = "none";

    public static readonly IReadOnlyList<string> DefaultKeywords =
    [
        "concretagem", "concreto", "fundação", "cobertura", "telhado", "impermeabilização", "pintura externa", "fachada",
        "terraplenagem",
    ];

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Explicit setting wins; otherwise the first keyword found in the name (null when none).</summary>
    public static (bool Sensitive, string Basis, string? Keyword) Sensitivity(string stageName, bool? explicitSetting,
        IReadOnlyList<string>? keywords = null)
    {
        if (explicitSetting is { } set) return (set, "explicit", null);
        var name = Fold(stageName);
        foreach (var k in keywords ?? DefaultKeywords)
        {
            var key = Fold(k);
            if (key.Length > 0 && name.Contains(key, StringComparison.Ordinal)) return (true, "keyword", k);
        }
        return (false, "none", null);
    }

    public static IReadOnlyList<BuildWeatherCondition> Conditions(BuildWeatherDay d)
    {
        var list = new List<BuildWeatherCondition>();
        var mm = d.PrecipitationMm ?? 0;
        var prob = d.PrecipitationProbability;
        if (mm >= HeavyRainMm && prob is >= HeavyRainProbability)
            list.Add(new("heavy_rain", High, string.Create(PtBr, $"Chuva forte prevista ({mm:0.#} mm, probabilidade {prob}%)")));
        else if (mm >= HeavyRainMm || (mm >= RainMm && prob is >= RainProbability))
            list.Add(new("rain", Medium, prob is { } p
                ? string.Create(PtBr, $"Chuva prevista ({mm:0.#} mm, probabilidade {p}%)")
                : string.Create(PtBr, $"Chuva prevista ({mm:0.#} mm)")));
        if (d.WindSpeedMaxKmh is { } wind && wind >= WindMediumKmh)
            list.Add(new("strong_wind", wind >= WindHighKmh ? High : Medium, string.Create(PtBr, $"Vento forte previsto (até {wind:0} km/h)")));
        return list;
    }

    /// <summary>Risks of the day for the sensitive stages active on it ("… — etapa X. Previsão, pode mudar.").</summary>
    public static IReadOnlyList<(BuildWeatherStage Stage, BuildWeatherCondition Condition, string Message)> Risks(
        BuildWeatherDay day, IEnumerable<BuildWeatherStage> stages)
    {
        var conditions = Conditions(day);
        if (conditions.Count == 0) return [];
        var active = stages.Where(s => s.Sensitive && IsActive(s, day.Date)).ToList();
        return active.SelectMany(s => conditions.Select(c => (s, c, $"{c.Label} — etapa {s.Name}. Previsão, pode mudar.")))
            .ToList();
    }

    public static bool IsActive(BuildWeatherStage s, DateOnly date)
        => s.ActiveFrom is { } from && from <= date && (s.ActiveTo is not { } to || date <= to);

    /// <summary>Worst level among the conditions of a day: high, medium or none.</summary>
    public static string Level(IEnumerable<BuildWeatherCondition> conditions)
    {
        var list = conditions.ToList();
        return list.Any(c => c.Level == High) ? High : list.Count > 0 ? Medium : None;
    }

    private static string Fold(string? value) => BuildTwinParse.Fold(value).ToLowerInvariant();
}
