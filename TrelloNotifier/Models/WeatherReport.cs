namespace TrelloNotifier.Models;

public sealed record WeatherReport(double TemperatureCelsius, double WindSpeedKmh, DateTimeOffset ObservedAt)
{
    public double? ApparentTemperatureCelsius { get; init; }
    public int? WeatherCode { get; init; }
    public bool IsDay { get; init; } = true;

    public WeatherCondition Condition => WeatherCode switch
    {
        0 => WeatherCondition.Clear,
        1 or 2 => WeatherCondition.PartlyCloudy,
        3 => WeatherCondition.Cloudy,
        45 or 48 => WeatherCondition.Fog,
        51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => WeatherCondition.Rain,
        71 or 73 or 75 or 77 or 85 or 86 => WeatherCondition.Snow,
        95 or 96 or 97 or 99 => WeatherCondition.Thunderstorm,
        _ => WeatherCondition.Unknown
    };

    public string ConditionText => Condition switch
    {
        WeatherCondition.Clear => IsDay ? "Despejado" : "Noche despejada",
        WeatherCondition.PartlyCloudy => "Parcialmente nublado",
        WeatherCondition.Cloudy => "Nublado",
        WeatherCondition.Fog => "Niebla",
        WeatherCondition.Rain => "Lluvia",
        WeatherCondition.Snow => "Nieve",
        WeatherCondition.Thunderstorm => "Tormenta",
        _ => "Condiciones no disponibles"
    };
}

public enum WeatherCondition
{
    Unknown,
    Clear,
    PartlyCloudy,
    Cloudy,
    Fog,
    Rain,
    Snow,
    Thunderstorm
}
