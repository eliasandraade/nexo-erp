namespace Nexo.Application.Integrations.Weather;

public sealed record WeatherResult(
    double  Latitude,
    double  Longitude,
    DateOnly Date,
    double  TemperatureMax,
    double  TemperatureMin,
    double  PrecipitationMm,
    int     WeatherCode,
    string  Description,       // mapped from WMO code, in Portuguese
    string  Summary            // formatted summary for WeatherSummary field: "28°C / 22°C · Parcialmente nublado · Chuva: 2mm"
);
