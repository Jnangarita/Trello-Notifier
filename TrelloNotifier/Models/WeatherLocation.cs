namespace TrelloNotifier.Models;

public sealed record WeatherLocation(double Latitude, double Longitude, string Name)
{
    public static WeatherLocation Default { get; } = new(10.9685, -74.7813, "Barranquilla, CO");

    public bool IsValid => double.IsFinite(Latitude) && Latitude is >= -90 and <= 90 &&
        double.IsFinite(Longitude) && Longitude is >= -180 and <= 180 &&
        !string.IsNullOrWhiteSpace(Name) && Name.Length <= 100 && !Name.Any(char.IsControl);
}
