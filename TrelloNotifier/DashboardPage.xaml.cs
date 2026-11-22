using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TrelloNotifier.Models;

namespace TrelloNotifier;

public sealed partial class DashboardPage : Page
{
    private CancellationTokenSource? _pageCancellation;
    private CancellationTokenSource? _weatherCancellation;

    public DashboardPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_pageCancellation is not null) return;
        _pageCancellation = new CancellationTokenSource();
        LocationText.Text = AppServices.Weather.Location.Name;
        ToolTipService.SetToolTip(LocationText, AppServices.Weather.Location.Name);
        await RefreshWeatherAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _pageCancellation?.Cancel();
        _pageCancellation?.Dispose();
        _pageCancellation = null;
    }

    private async void RefreshWeather_Click(object sender, RoutedEventArgs e) => await RefreshWeatherAsync(forceRefresh: true);

    private async Task RefreshWeatherAsync(bool forceRefresh = false)
    {
        if (_pageCancellation is null || _weatherCancellation is { IsCancellationRequested: false }) return;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_pageCancellation.Token);
        _weatherCancellation = request;
        RefreshWeatherButton.IsEnabled = false;
        WeatherText.Text = "Cargando…";
        WeatherDetailsText.Text = "";
        WeatherConditionText.Text = "";
        DisplayWeatherIcon(null);
        try
        {
            WeatherReport? report = await AppServices.Weather.GetCurrentAsync(request.Token, forceRefresh);
            if (request.IsCancellationRequested || _weatherCancellation != request) return;
            WeatherText.FontSize = report is null ? 22 : 34;
            WeatherText.Text = report is null ? "No disponible" : $"{report.TemperatureCelsius:0}°C";
            WeatherDetailsText.Text = report is null
                ? "Inténtalo de nuevo en unos minutos."
                : report.ApparentTemperatureCelsius is double apparent ? $"Sensación {apparent:0}°C" : "Sensación no disponible";
            WeatherConditionText.Text = report?.ConditionText ?? "";
            DisplayWeatherIcon(report);
            ToolTipService.SetToolTip(WeatherCard, report is null ? "Datos: Open-Meteo"
                : $"Open-Meteo · {report.ObservedAt.ToLocalTime():dd/MM/yyyy HH:mm} (hora local del equipo) · Viento: {report.WindSpeedKmh:0.#} km/h");
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            // Navegar cancela la carga; la respuesta saliente no modifica la vista.
        }
        finally
        {
            if (_weatherCancellation == request)
            {
                _weatherCancellation = null;
                if (!request.IsCancellationRequested) RefreshWeatherButton.IsEnabled = true;
            }
        }
    }

    private void DisplayWeatherIcon(WeatherReport? report)
    {
        WeatherCondition condition = report?.Condition ?? WeatherCondition.Unknown;
        bool clearOrPartial = condition is WeatherCondition.Clear or WeatherCondition.PartlyCloudy;
        SunIllustration.Visibility = clearOrPartial && report?.IsDay == true ? Visibility.Visible : Visibility.Collapsed;
        MoonIllustration.Visibility = clearOrPartial && report?.IsDay == false ? Visibility.Visible : Visibility.Collapsed;
        CloudIllustration.Visibility = condition is not (WeatherCondition.Clear or WeatherCondition.Unknown) ? Visibility.Visible : Visibility.Collapsed;
        RainIllustration.Visibility = condition == WeatherCondition.Rain ? Visibility.Visible : Visibility.Collapsed;
        SnowIllustration.Visibility = condition == WeatherCondition.Snow ? Visibility.Visible : Visibility.Collapsed;
        ThunderIllustration.Visibility = condition == WeatherCondition.Thunderstorm ? Visibility.Visible : Visibility.Collapsed;
        FogIllustration.Visibility = condition == WeatherCondition.Fog ? Visibility.Visible : Visibility.Collapsed;
        UnknownIllustration.Visibility = condition == WeatherCondition.Unknown ? Visibility.Visible : Visibility.Collapsed;
    }
}
