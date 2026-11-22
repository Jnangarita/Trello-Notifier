using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using Serilog.Context;
using Serilog.Events;
using TrelloNotifier.Models;

namespace TrelloNotifier.Services;

internal sealed class WeatherClient : IDisposable
{
    private const string ForecastResource = "/v1/forecast";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(5);
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<DateTimeOffset> _utcNow;
    private WeatherReport? _cached;
    private DateTimeOffset _expiresAt;

    public WeatherLocation Location { get; }

    public WeatherClient(HttpMessageHandler? handler = null, WeatherLocation? location = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        Location = location ?? ResolveLocation(
            Environment.GetEnvironmentVariable("TRELLO_NOTIFIER_WEATHER_LATITUDE"),
            Environment.GetEnvironmentVariable("TRELLO_NOTIFIER_WEATHER_LONGITUDE"),
            Environment.GetEnvironmentVariable("TRELLO_NOTIFIER_WEATHER_CITY"));
        if (!Location.IsValid) throw new ArgumentException("La ubicación de clima no es válida.", nameof(location));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseContentBufferSize = 64 * 1024
        };
    }

    internal static WeatherLocation ResolveLocation(string? latitude, string? longitude, string? city)
    {
        if (string.IsNullOrWhiteSpace(latitude) && string.IsNullOrWhiteSpace(longitude))
            return WeatherLocation.Default;

        if (double.TryParse(latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) &&
            double.TryParse(longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
        {
            var location = new WeatherLocation(lat, lon, string.IsNullOrWhiteSpace(city) ? "Ubicación configurada" : city.Trim());
            if (location.IsValid) return location;
        }

        Log.ForContext<WeatherClient>().Warning("Configuración de clima inválida; se usará la ubicación predeterminada");
        return WeatherLocation.Default;
    }

    public async Task<WeatherReport?> GetCurrentAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!forceRefresh && _utcNow() < _expiresAt) return _cached;

            using var context = LogContext.PushProperty("WeatherRequestId", Guid.NewGuid().ToString("N"));
            Log.ForContext<WeatherClient>().Information("{Method} {Resource}: consultando clima en Open-Meteo; actualización forzada={ForceRefresh}",
                HttpMethod.Get.Method, ForecastResource, forceRefresh);
            string url = FormattableString.Invariant(
                $"https://api.open-meteo.com{ForecastResource}?latitude={Location.Latitude}&longitude={Location.Longitude}") +
                "&current=temperature_2m,apparent_temperature,weather_code,is_day,wind_speed_10m&temperature_unit=celsius&wind_speed_unit=kmh&timeformat=unixtime&forecast_days=1";
            var stopwatch = Stopwatch.StartNew();
            int? statusCode = null;
            bool succeeded = false;
            bool canceled = false;
            string outcome = "fallo de transporte";
            try
            {
                // ResponseContentRead limita también el cuerpo (64 KiB) y lo incluye en el timeout.
                using HttpResponseMessage response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
                statusCode = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException("No se pudo consultar el clima.", null, response.StatusCode);
                ForecastResponse? forecast = await response.Content.ReadFromJsonAsync<ForecastResponse>(
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                CurrentWeather? current = forecast?.Current;
                if (current?.Temperature is not double temperature || !double.IsFinite(temperature) ||
                    current.WindSpeed is not double wind || !double.IsFinite(wind) || wind < 0 ||
                    (current.ApparentTemperature is double apparent && !double.IsFinite(apparent)) ||
                    (current.IsDay is int isDay && isDay is not (0 or 1)) ||
                    current.Time is not long time || time < -62135596800L || time > 253402300799L)
                    throw new JsonException("Respuesta de clima incompleta o inválida.");

                cancellationToken.ThrowIfCancellationRequested();
                _cached = new WeatherReport(temperature, wind, DateTimeOffset.FromUnixTimeSeconds(time))
                {
                    ApparentTemperatureCelsius = current.ApparentTemperature,
                    WeatherCode = current.WeatherCode,
                    IsDay = current.IsDay != 0
                };
                _expiresAt = _utcNow() + CacheDuration;
                succeeded = true;
                return _cached;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                canceled = true;
                outcome = "cancelada por solicitud";
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException ||
                (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                outcome = ex switch
                {
                    OperationCanceledException => "tiempo de espera agotado",
                    JsonException => "respuesta JSON inválida",
                    HttpRequestException { StatusCode: not null } => "solicitud rechazada",
                    _ => "fallo de transporte"
                };
                using var statusContext = LogContext.PushProperty("StatusCode", statusCode);
                AppLog.WriteFailure("GetCurrentWeather", ex, LogEventLevel.Warning);
                _cached = null;
                _expiresAt = _utcNow() + FailureCacheDuration;
                return null;
            }
            finally
            {
                if (succeeded)
                    Log.ForContext<WeatherClient>().Information(
                        "{Method} {Resource} -> {StatusCode} ({DurationMs} ms); caché válida durante {CacheMinutes} minutos",
                        HttpMethod.Get.Method, ForecastResource, statusCode, stopwatch.ElapsedMilliseconds, CacheDuration.TotalMinutes);
                else
                    Log.ForContext<WeatherClient>().ForContext("StatusCode", statusCode)
                        .Write(canceled ? LogEventLevel.Debug : LogEventLevel.Warning,
                            "{Method} {Resource} -> {HttpResult} ({DurationMs} ms): {Outcome}",
                            HttpMethod.Get.Method, ForecastResource, statusCode?.ToString(CultureInfo.InvariantCulture) ?? "sin respuesta",
                            stopwatch.ElapsedMilliseconds, outcome);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }

    private sealed class ForecastResponse
    {
        [JsonPropertyName("current")]
        public CurrentWeather? Current { get; set; }
    }

    private sealed class CurrentWeather
    {
        [JsonPropertyName("temperature_2m")]
        public double? Temperature { get; set; }

        [JsonPropertyName("wind_speed_10m")]
        public double? WindSpeed { get; set; }

        [JsonPropertyName("apparent_temperature")]
        public double? ApparentTemperature { get; set; }

        [JsonPropertyName("weather_code")]
        public int? WeatherCode { get; set; }

        [JsonPropertyName("is_day")]
        public int? IsDay { get; set; }

        [JsonPropertyName("time")]
        public long? Time { get; set; }
    }
}
