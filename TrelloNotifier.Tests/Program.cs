using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Serilog;
using Serilog.Context;
using TrelloNotifier.Models;
using TrelloNotifier.Services;
using TrelloNotifier.Tests;

DateTimeOffset now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
int passed = 0;

await Run("Clima: consulta independiente sin claves, caché de una hora y renovación", async () =>
{
    DateTimeOffset clock = now;
    using var handler = new GeminiHttpDouble
    {
        Respond = (request, _) =>
        {
            Check(request.Method == HttpMethod.Get && request.RequestUri!.Host == "api.open-meteo.com" &&
                request.RequestUri.AbsolutePath == "/v1/forecast", "Consultar únicamente Open-Meteo por HTTPS.");
            string query = request.RequestUri!.Query;
            Check(query.Contains("latitude=10.9685") && query.Contains("longitude=-74.7813") &&
                query.Contains("timeformat=unixtime") && query.Contains("temperature_unit=celsius") &&
                query.Contains("wind_speed_unit=kmh") && query.Contains("apparent_temperature,weather_code,is_day") &&
                !query.Contains("key") && !request.Headers.Any(),
                "Usar coordenadas invariantes, unidades explícitas y ninguna credencial de Trello.");
            return Task.FromResult(GeminiHttpDouble.Json("{\"current\":{\"temperature_2m\":17.5,\"apparent_temperature\":16.2,\"weather_code\":2,\"is_day\":1,\"wind_speed_10m\":8.2,\"time\":1790000000}}"));
        }
    };
    using var client = new WeatherClient(handler, WeatherLocation.Default, () => clock);
    WeatherReport? report = await client.GetCurrentAsync(CancellationToken.None);
    Check(report is { TemperatureCelsius: 17.5, WindSpeedKmh: 8.2 } &&
        report.ObservedAt == DateTimeOffset.FromUnixTimeSeconds(1790000000), "Interpretar valores y hora UTC del proveedor.");
    Check(report is { ApparentTemperatureCelsius: 16.2, IsDay: true, Condition: WeatherCondition.PartlyCloudy } &&
        report.ConditionText == "Parcialmente nublado", "La tarjeta muestra sensación y condiciones reales de la API.");
    clock = now.AddMinutes(59);
    Check(await client.GetCurrentAsync(CancellationToken.None) == report && handler.Calls == 1, "Abrir o volver al Dashboard reutiliza caché.");
    clock = now.AddHours(1);
    await client.GetCurrentAsync(CancellationToken.None);
    Check(handler.Calls == 2, "Renovar al alcanzar exactamente una hora.");
});

await Run("Clima: refresco manual consulta con caché vigente y renueva el dato y su vigencia", async () =>
{
    DateTimeOffset clock = now;
    int temperature = 22;
    using var handler = new GeminiHttpDouble
    {
        Respond = (_, _) => Task.FromResult(GeminiHttpDouble.Json(JsonSerializer.Serialize(
            new { current = new { temperature_2m = temperature, wind_speed_10m = 1, time = 1790000000 } })))
    };
    using var client = new WeatherClient(handler, WeatherLocation.Default, () => clock);
    WeatherReport? original = await client.GetCurrentAsync(CancellationToken.None);
    temperature = 30;
    clock = now.AddMinutes(10);
    Check(await client.GetCurrentAsync(CancellationToken.None) == original && handler.Calls == 1,
        "La apertura normal mantiene el dato en caché aunque el servidor tenga otro.");
    WeatherReport? refreshed = await client.GetCurrentAsync(CancellationToken.None, forceRefresh: true);
    Check(refreshed?.TemperatureCelsius == 30 && handler.Calls == 2,
        "La acción manual consulta y publica el dato nuevo sin esperar una hora.");
    temperature = 31;
    clock = now.AddMinutes(65);
    Check(await client.GetCurrentAsync(CancellationToken.None) == refreshed && handler.Calls == 2,
        "Volver al Dashboard usa la caché renovada durante una hora desde el refresco manual.");
    clock = now.AddMinutes(70);
    Check((await client.GetCurrentAsync(CancellationToken.None))?.TemperatureCelsius == 31 && handler.Calls == 3,
        "La caché renovada también expira al alcanzar exactamente una hora.");
});

await Run("Clima: refresco manual permite recuperación inmediata tras fallo y conserva el fallback", async () =>
{
    bool fail = true;
    using var handler = new GeminiHttpDouble
    {
        Respond = (_, _) => Task.FromResult(GeminiHttpDouble.Json(
            "{\"current\":{\"temperature_2m\":30,\"wind_speed_10m\":1,\"time\":1790000000}}",
            fail ? System.Net.HttpStatusCode.ServiceUnavailable : System.Net.HttpStatusCode.OK))
    };
    using var client = new WeatherClient(handler, WeatherLocation.Default, () => now);
    Check(await client.GetCurrentAsync(CancellationToken.None) is null, "Representar el fallo inicial.");
    fail = false;
    Check(await client.GetCurrentAsync(CancellationToken.None) is null && handler.Calls == 1,
        "Las aperturas normales respetan la espera tras fallo.");
    Check(await client.GetCurrentAsync(CancellationToken.None, forceRefresh: true) is not null && handler.Calls == 2,
        "Pulsar refrescar reintenta sin esperar cinco minutos.");
    fail = true;
    Check(await client.GetCurrentAsync(CancellationToken.None, forceRefresh: true) is null && handler.Calls == 3,
        "Un refresco manual fallido devuelve No disponible sin lanzar excepciones a UI.");
    fail = false;
    Check(await client.GetCurrentAsync(CancellationToken.None) is null && handler.Calls == 3,
        "El fallo manual establece la misma espera para aperturas normales.");
    Check(await client.GetCurrentAsync(CancellationToken.None, forceRefresh: true) is not null && handler.Calls == 4,
        "Un nuevo refresco manual recupera el clima inmediatamente.");
    Check(await client.GetCurrentAsync(CancellationToken.None) is not null && handler.Calls == 4,
        "Compartir el resultado recuperado con las aperturas siguientes.");
});

await Run("Clima: cancelar el refresco manual conserva la caché válida y descarta respuestas tardías", async () =>
{
    using var handler = new GeminiHttpDouble
    {
        Respond = (_, _) => Task.FromResult(GeminiHttpDouble.Json("{\"current\":{\"temperature_2m\":22,\"wind_speed_10m\":1,\"time\":1790000000}}"))
    };
    using var client = new WeatherClient(handler, WeatherLocation.Default, () => now);
    WeatherReport? original = await client.GetCurrentAsync(CancellationToken.None);
    var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    handler.Respond = async (_, _) =>
    {
        started.SetResult(true);
        await release.Task;
        return GeminiHttpDouble.Json("{\"current\":{\"temperature_2m\":30,\"wind_speed_10m\":1,\"time\":1790000000}}");
    };
    using var cancellation = new CancellationTokenSource();
    Task<WeatherReport?> refreshing = client.GetCurrentAsync(cancellation.Token, forceRefresh: true);
    await started.Task;
    cancellation.Cancel();
    release.SetResult(true);
    bool canceled = false;
    try { await refreshing; }
    catch (OperationCanceledException) { canceled = true; }
    Check(canceled && handler.Calls == 2, "Cancelar la consulta forzada al navegar.");
    Check(await client.GetCurrentAsync(CancellationToken.None) == original && handler.Calls == 2,
        "No reemplazar la caché válida con un resultado cancelado ni forzar otra solicitud al volver.");
});

await Run("Clima: fallos HTTP, red, timeout y JSON dan fallback temporal y permiten recuperación", async () =>
{
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] failures =
    {
        (_, _) => Task.FromResult(GeminiHttpDouble.Json("private-weather-body", System.Net.HttpStatusCode.ServiceUnavailable)),
        (_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("private-weather-error")),
        (_, _) => Task.FromException<HttpResponseMessage>(new TaskCanceledException("private-weather-timeout")),
        (_, _) => Task.FromResult(GeminiHttpDouble.Json("no-json")),
        (_, _) => Task.FromResult(GeminiHttpDouble.Json("{}")),
        (_, _) => Task.FromResult(GeminiHttpDouble.Json("{\"current\":{\"temperature_2m\":null,\"wind_speed_10m\":0,\"time\":0}}")),
        (_, _) => Task.FromResult(GeminiHttpDouble.Json("{\"current\":{\"temperature_2m\":10,\"wind_speed_10m\":-1,\"time\":0}}")),
        (_, _) => Task.FromResult(GeminiHttpDouble.Json("{\"current\":{\"temperature_2m\":10,\"wind_speed_10m\":1,\"time\":253402300800}}")),
        (_, _) => Task.FromResult(GeminiHttpDouble.Json("{\"current\":{\"temperature_2m\":10,\"wind_speed_10m\":1,\"is_day\":2,\"time\":1790000000}}")),
        (_, _) => Task.FromResult(GeminiHttpDouble.Json(new string('x', 65537)))
    };
    var sink = new RecordingLogSink();
    Log.Logger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();
    try
    {
        foreach (var failure in failures)
        {
            DateTimeOffset clock = now;
            using var handler = new GeminiHttpDouble { Respond = failure };
            using var client = new WeatherClient(handler, WeatherLocation.Default, () => clock);
            Check(await client.GetCurrentAsync(CancellationToken.None) is null, "Un fallo devuelve No disponible sin excepción en UI.");
            clock = now.AddMinutes(4);
            Check(await client.GetCurrentAsync(CancellationToken.None) is null && handler.Calls == 1,
                "Limitar también los intentos fallidos al abrir repetidamente.");
            handler.Respond = (_, _) => Task.FromResult(GeminiHttpDouble.Json("{\"current\":{\"temperature_2m\":18,\"wind_speed_10m\":0,\"time\":1790000000}}"));
            clock = now.AddMinutes(5);
            Check(await client.GetCurrentAsync(CancellationToken.None) is not null && handler.Calls == 2,
                "Reintentar tras cinco minutos y recuperar el clima.");
        }
    }
    finally { Log.CloseAndFlush(); }
    var warnings = sink.Events.Where(item => item.Level == Serilog.Events.LogEventLevel.Warning && item.Properties.ContainsKey("Operation")).ToArray();
    Check(warnings.Length == failures.Length && warnings.All(item => item.Properties.ContainsKey("WeatherRequestId")),
        "Registrar cada fallo una vez y con LogContext.");
    Check(sink.Events.Any(item => item.Level == Serilog.Events.LogEventLevel.Information) &&
        sink.Events.All(item => !item.RenderMessage().Contains("private-weather") &&
            !(item.Exception?.ToString().Contains("private-weather") ?? false)), "Registrar resultados INFO y fallos saneados.");
});

await Run("Clima: logs HTTP incluyen GET, ruta segura, estado y duración sin simular llamadas de caché", async () =>
{
    var sink = new RecordingLogSink();
    Log.Logger = new LoggerConfiguration().MinimumLevel.Debug().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();
    using var handler = new GeminiHttpDouble
    {
        Respond = (_, _) => Task.FromResult(GeminiHttpDouble.Json("{\"current\":{\"temperature_2m\":30,\"wind_speed_10m\":1,\"time\":1790000000}}"))
    };
    using var client = new WeatherClient(handler, new WeatherLocation(12.34567, -45.67891, "private-weather-location"), () => now);
    try
    {
        await client.GetCurrentAsync(CancellationToken.None);
        int afterSuccess = sink.Events.Count;
        await client.GetCurrentAsync(CancellationToken.None);
        Check(sink.Events.Count == afterSuccess && handler.Calls == 1, "La caché no registra una petición HTTP inexistente.");

        handler.Respond = (_, _) => Task.FromResult(GeminiHttpDouble.Json("private-weather-body", System.Net.HttpStatusCode.ServiceUnavailable));
        await client.GetCurrentAsync(CancellationToken.None, forceRefresh: true);
        int afterFailure = sink.Events.Count;
        await client.GetCurrentAsync(CancellationToken.None);
        Check(sink.Events.Count == afterFailure && handler.Calls == 2, "La espera tras fallo tampoco registra nuevas peticiones.");

        handler.Respond = (_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("private-weather-network"));
        await client.GetCurrentAsync(CancellationToken.None, forceRefresh: true);
        handler.Respond = (_, _) => Task.FromException<HttpResponseMessage>(new TaskCanceledException("private-weather-timeout"));
        await client.GetCurrentAsync(CancellationToken.None, forceRefresh: true);
        handler.Respond = (_, _) => Task.FromResult(GeminiHttpDouble.Json("private-weather-json"));
        await client.GetCurrentAsync(CancellationToken.None, forceRefresh: true);

        using var cancellation = new CancellationTokenSource();
        handler.Respond = (_, _) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(cancellation.Token);
        };
        bool canceled = false;
        try { await client.GetCurrentAsync(cancellation.Token, forceRefresh: true); }
        catch (OperationCanceledException) { canceled = true; }
        Check(canceled, "Propagar la cancelación de navegación.");
        Log.Information("Fuera de consulta de clima");
    }
    finally { Log.CloseAndFlush(); }

    static object? Value(Serilog.Events.LogEvent item, string key) =>
        item.Properties.TryGetValue(key, out var property) && property is Serilog.Events.ScalarValue scalar ? scalar.Value : null;

    static string Render(Serilog.Events.LogEvent item)
    {
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var formatter = new Serilog.Formatting.Display.MessageTemplateTextFormatter("{Message:lj}", System.Globalization.CultureInfo.InvariantCulture);
        formatter.Format(item, writer);
        return writer.ToString();
    }

    var requests = sink.Events.Where(item => item.Properties.ContainsKey("DurationMs")).ToArray();
    Check(requests.Length == 6 && handler.Calls == 6 && requests.All(item =>
        Value(item, "Method") as string == "GET" && Value(item, "Resource") as string == "/v1/forecast" &&
        Value(item, "DurationMs") is long duration && duration >= 0 &&
        Value(item, "SourceContext") as string == "TrelloNotifier.Services.WeatherClient"),
        "Usar el mismo esquema de método, ruta, estado y duración del resto de clientes.");
    Check(requests[0].Level == Serilog.Events.LogEventLevel.Information && Value(requests[0], "StatusCode") is 200 &&
        Render(requests[0]).Contains("GET /v1/forecast -> 200 ("), "El GET correcto debe verse a nivel INFO.");
    Check(requests.Skip(1).Take(4).All(item => item.Level == Serilog.Events.LogEventLevel.Warning) &&
        Value(requests[1], "StatusCode") is 503 && Render(requests[1]).Contains("GET /v1/forecast -> 503 ("),
        "Conservar y mostrar el código HTTP al fallar.");
    Check(Value(requests[2], "StatusCode") is null && Value(requests[2], "HttpResult") as string == "sin respuesta" &&
        Value(requests[3], "Outcome") as string == "tiempo de espera agotado" &&
        Value(requests[4], "StatusCode") is 200 && Value(requests[4], "Outcome") as string == "respuesta JSON inválida",
        "Distinguir fallos de red, timeout y una respuesta HTTP correcta con JSON inválido.");
    Check(requests[5].Level == Serilog.Events.LogEventLevel.Debug &&
        Value(requests[5], "Outcome") as string == "cancelada por solicitud", "La navegación cancelada no genera una falsa advertencia.");
    Check(requests.Select(item => Value(item, "WeatherRequestId")).Distinct().Count() == 6 &&
        !sink.Events.Last().Properties.ContainsKey("WeatherRequestId"), "Correlacionar cada petición y liberar su LogContext.");
    Check(sink.Events.Any(item => Value(item, "ForceRefresh") is true) && sink.Events.All(item =>
        !item.RenderMessage().Contains("private-weather") && !item.RenderMessage().Contains("latitude") &&
        !item.RenderMessage().Contains("12.34567") && !item.RenderMessage().Contains("45.67891") &&
        !(item.Exception?.ToString().Contains("private-weather") ?? false)),
        "No exponer ubicación, query, payload ni mensajes originales de excepciones.");
});

await Run("Clima: solicitudes simultáneas comparten caché y cancelar no publica resultados tardíos", async () =>
{
    var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var handler = new GeminiHttpDouble
    {
        Respond = async (_, _) =>
        {
            started.TrySetResult(true);
            await release.Task;
            return GeminiHttpDouble.Json("{\"current\":{\"temperature_2m\":18,\"wind_speed_10m\":0,\"time\":1790000000}}");
        }
    };
    using var client = new WeatherClient(handler, WeatherLocation.Default, () => now);
    using var cancellation = new CancellationTokenSource();
    Task<WeatherReport?> canceled = client.GetCurrentAsync(cancellation.Token);
    await started.Task;
    Task<WeatherReport?> next = client.GetCurrentAsync(CancellationToken.None);
    Task<WeatherReport?> concurrent = client.GetCurrentAsync(CancellationToken.None);
    cancellation.Cancel();
    release.SetResult(true);
    bool observedCancellation = false;
    try { await canceled; }
    catch (OperationCanceledException) { observedCancellation = true; }
    Check(observedCancellation, "Respetar navegación incluso si el transporte responde tarde.");
    WeatherReport?[] reports = await Task.WhenAll(next, concurrent);
    Check(reports.All(report => report is not null) && handler.Calls == 2,
        "No guardar la respuesta cancelada; las dos cargas siguientes hacen una sola petición.");
    using var alreadyCanceled = new CancellationTokenSource();
    alreadyCanceled.Cancel();
    try { await client.GetCurrentAsync(alreadyCanceled.Token); Check(false, "Cancelar también al tener caché."); }
    catch (OperationCanceledException) { }
});

await Run("Clima: coordenadas configurables válidas y valores ausentes o inválidos seguros", () =>
{
    Check(WeatherClient.ResolveLocation(null, null, null) == WeatherLocation.Default, "Usar Barranquilla sin configuración.");
    Check(WeatherClient.ResolveLocation("-34.6", "-58.38", " Buenos Aires ") == new WeatherLocation(-34.6, -58.38, "Buenos Aires"),
        "Aceptar coordenadas negativas y nombre opcional.");
    Check(WeatherClient.ResolveLocation("0", "0", null).Name == "Ubicación configurada", "No atribuir a Barranquilla coordenadas personalizadas.");
    foreach (var pair in new[] { ("91", "0"), ("0", "181"), ("NaN", "0"), ("Infinity", "0"), ("4,7", "0"), ("4.7", "") })
        Check(WeatherClient.ResolveLocation(pair.Item1, pair.Item2, null) == WeatherLocation.Default, "Validar rangos, finitud y ambas coordenadas.");
    Check(WeatherClient.ResolveLocation("0", "0", "ciudad\ninválida") == WeatherLocation.Default, "Validar también la etiqueta visible.");
    return Task.CompletedTask;
});

await Run("Clima: condiciones WMO, noche y campos opcionales tienen presentación coherente", async () =>
{
    foreach (var sample in new[]
    {
        (Code: 0, Condition: WeatherCondition.Clear, Text: "Noche despejada"),
        (Code: 2, Condition: WeatherCondition.PartlyCloudy, Text: "Parcialmente nublado"),
        (Code: 3, Condition: WeatherCondition.Cloudy, Text: "Nublado"),
        (Code: 45, Condition: WeatherCondition.Fog, Text: "Niebla"),
        (Code: 63, Condition: WeatherCondition.Rain, Text: "Lluvia"),
        (Code: 85, Condition: WeatherCondition.Snow, Text: "Nieve"),
        (Code: 95, Condition: WeatherCondition.Thunderstorm, Text: "Tormenta"),
        (Code: 1000, Condition: WeatherCondition.Unknown, Text: "Condiciones no disponibles")
    })
    {
        using var handler = new GeminiHttpDouble
        {
            Respond = (_, _) => Task.FromResult(GeminiHttpDouble.Json(
                JsonSerializer.Serialize(new { current = new { temperature_2m = 22, wind_speed_10m = 1, time = 1790000000, weather_code = sample.Code, is_day = 0 } })))
        };
        using var client = new WeatherClient(handler, WeatherLocation.Default, () => now);
        WeatherReport? report = await client.GetCurrentAsync(CancellationToken.None);
        Check(report is { IsDay: false, ApparentTemperatureCelsius: null } &&
            report.Condition == sample.Condition && report.ConditionText == sample.Text,
            "Interpretar la noche y las familias meteorológicas; no inventar sensación ni condiciones desconocidas.");
    }
    var missing = new WeatherReport(22, 1, now);
    Check(missing.Condition == WeatherCondition.Unknown && missing.ApparentTemperatureCelsius is null,
        "Los campos ausentes no se presentan como un cielo despejado ni como cero grados.");
});

await Run("Gemini solicita JSON, aísla credenciales y devuelve propuestas sin destino", async () =>
{
    using var handler = new GeminiHttpDouble();
    handler.Respond = async (request, token) =>
    {
        Check(request.Method == HttpMethod.Post && request.RequestUri!.Host == "generativelanguage.googleapis.com" &&
            request.RequestUri.AbsolutePath == "/v1beta/models/gemini-3.5-flash-lite:generateContent" &&
            request.RequestUri.Query == "", "Usar el mismo modelo y endpoint, sin secretos en query.");
        Check(request.Headers.GetValues("x-goog-api-key").Single() == "dummy.gemini-key", "Admitir claves opacas con punto y autenticar por cabecera.");
        Check(request.Content!.Headers.ContentType?.MediaType == "application/json", "Enviar Content-Type application/json.");
        string payload = await request.Content!.ReadAsStringAsync(token);
        Check(!payload.Contains("dummy-trello") && !payload.Contains("dummy.gemini-key"), "No incluir credenciales en el prompt.");
        using JsonDocument body = JsonDocument.Parse(payload);
        JsonElement configuration = body.RootElement.GetProperty("generationConfig");
        Check(configuration.GetProperty("response_mime_type").GetString() == "application/json",
            "Solicitar una respuesta estructurada.");
        JsonElement cardSchema = configuration.GetProperty("response_schema").GetProperty("properties").GetProperty("cards");
        string schemaText = cardSchema.GetRawText();
        Check(!schemaText.Contains("maxLength") && !schemaText.Contains("maxItems") && !schemaText.Contains("minItems"),
            "Mantener el esquema de transporte simple; aplicar límites en la validación local.");
        Check(cardSchema.GetProperty("type").GetString() == "ARRAY" &&
            cardSchema.GetProperty("items").GetProperty("properties").GetProperty("due").GetProperty("nullable").GetBoolean(),
            "Usar response_schema con tipos OpenAPI y fechas realmente anulables.");
        string instruction = body.RootElement.GetProperty("system_instruction").GetProperty("parts")[0].GetProperty("text").GetString()!;
        Check(instruction.Contains(now.ToString("O")) && instruction.Contains(TimeZoneInfo.Utc.Id), "Enviar referencia temporal explícita.");
        var card = GeminiHttpDouble.Proposal("2026-09-22T09:00:00Z");
        card["checklists"] = new[] { new { name = "Revisión", items = new[] { "Contar", "Confirmar" } } };
        return GeminiHttpDouble.Json(GeminiHttpDouble.Envelope(card));
    };
    using var client = new GeminiCardClient(handler);
    var settings = new AppSettings { GeminiApiKey = "dummy.gemini-key", ApiKey = "dummy-trello-key", Token = "dummy-trello-token" };
    List<CardImportRow> rows = await client.GenerateAsync(settings, "Revisar pedido mañana a las 09:00", true, now, TimeZoneInfo.Utc, CancellationToken.None);
    TrelloCardDraft draft = rows.Single().Draft;
    Check(draft.Name == "Revisar pedido" && draft.Due == new DateTimeOffset(2026, 9, 22, 9, 0, 0, TimeSpan.Zero), "Convertir texto y fecha sin inventar un destino.");
    Check(draft.ListId == "" && draft.MemberIds.Count == 0 && draft.LabelIds.Count == 0, "Las asignaciones pertenecen a la app.");
    Check(draft.Checklists.Single().GetItems().SequenceEqual(new[] { "Contar", "Confirmar" }), "Conservar checklist individual.");
    Check(handler.Calls == 1, "Generar no crea tarjetas ni hace solicitudes adicionales.");
});

await Run("Gemini limita entradas antes de enviar y mantiene opcional la configuración", async () =>
{
    using var handler = new GeminiHttpDouble();
    using var client = new GeminiCardClient(handler);
    foreach (var input in new[]
    {
        (Settings: new AppSettings(), Text: "Texto"),
        (Settings: new AppSettings { GeminiApiKey = "dummy", GeminiModel = "https://otro.invalid" }, Text: "Texto"),
        (Settings: new AppSettings { GeminiApiKey = "dummy\r\nheader" }, Text: "Texto"),
        (Settings: new AppSettings { GeminiApiKey = "dummy key" }, Text: "Texto"),
        (Settings: new AppSettings { GeminiApiKey = "dummy\u0000key" }, Text: "Texto"),
        (Settings: new AppSettings { GeminiApiKey = "dummy" }, Text: " "),
        (Settings: new AppSettings { GeminiApiKey = "dummy" }, Text: new string('x', GeminiCardClient.MaxInputCharacters + 1))
    })
    {
        bool rejected = false;
        try { await client.GenerateAsync(input.Settings, input.Text, false, now, TimeZoneInfo.Utc, CancellationToken.None); }
        catch (FormatException) { rejected = true; }
        Check(rejected, "Rechazar configuración o texto inválido antes de HTTP.");
    }
    Check(handler.Calls == 0, "No hacer solicitudes inválidas.");
    Check(new AppSettings { ApiBaseUrl = "http://localhost" }.IsConfigured, "El flujo Trello sigue sin requerir Gemini.");
});

await Run("Gemini rechaza respuestas bloqueadas, truncadas, vacías y contratos inventados", async () =>
{
    var extra = GeminiHttpDouble.Proposal();
    extra["idList"] = "inventado";
    var emptyTitle = GeminiHttpDouble.Proposal();
    emptyTitle["title"] = " ";
    var largeTitle = GeminiHttpDouble.Proposal();
    largeTitle["title"] = new string('a', 201);
    var checklist = GeminiHttpDouble.Proposal();
    checklist["checklists"] = new[] { new { name = "No permitido en lote", items = new[] { "A" } } };
    string[] invalid =
    {
        "{}", "[]", "no-json", "{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}",
        GeminiHttpDouble.Envelope().Replace("STOP", "MAX_TOKENS"), GeminiHttpDouble.Envelope(),
        GeminiHttpDouble.Envelope(extra), GeminiHttpDouble.Envelope(emptyTitle), GeminiHttpDouble.Envelope(largeTitle),
        GeminiHttpDouble.Envelope(checklist), GeminiHttpDouble.Envelope(GeminiHttpDouble.Proposal("2026-09-22T09:00:00")),
        GeminiHttpDouble.Envelope(Enumerable.Range(0, 51).Select(_ => (object)GeminiHttpDouble.Proposal()).ToArray())
    };
    foreach (string json in invalid)
    {
        using var handler = new GeminiHttpDouble { Respond = (_, _) => Task.FromResult(GeminiHttpDouble.Json(json)) };
        using var client = new GeminiCardClient(handler);
        bool rejected = false;
        try { await client.GenerateAsync(new AppSettings { GeminiApiKey = "dummy" }, "Pendientes", false, now, TimeZoneInfo.Utc, CancellationToken.None); }
        catch (Exception ex) when (ex is FormatException or JsonException) { rejected = true; }
        Check(rejected && handler.Calls == 1, "No publicar propuestas parciales ni reintentar respuestas inválidas.");
    }
    using var multiple = new GeminiHttpDouble { Respond = (_, _) => Task.FromResult(GeminiHttpDouble.Json(GeminiHttpDouble.Envelope(GeminiHttpDouble.Proposal(), GeminiHttpDouble.Proposal()))) };
    using var singleClient = new GeminiCardClient(multiple);
    bool rejectedSingle = false;
    try { await singleClient.GenerateAsync(new AppSettings { GeminiApiKey = "dummy" }, "Una", true, now, TimeZoneInfo.Utc, CancellationToken.None); }
    catch (FormatException) { rejectedSingle = true; }
    Check(rejectedSingle, "No tomar silenciosamente la primera propuesta en modo individual.");
});

await Run("Gemini conserva fechas ausentes y señala ambigüedad o zona incorrecta", async () =>
{
    using var handler = new GeminiHttpDouble
    {
        Respond = (_, _) => Task.FromResult(GeminiHttpDouble.Json(GeminiHttpDouble.Envelope(
            GeminiHttpDouble.Proposal(), GeminiHttpDouble.Proposal(null, "Falta la hora del viernes"),
            GeminiHttpDouble.Proposal("2026-09-22T09:00:00-05:00"),
            GeminiHttpDouble.Proposal("2026-09-22T09:00:00Z", "Fecha ambigua"))))
    };
    using var client = new GeminiCardClient(handler);
    List<CardImportRow> rows = await client.GenerateAsync(new AppSettings { GeminiApiKey = "dummy" }, "Pendientes", false, now, TimeZoneInfo.Utc, CancellationToken.None);
    Check(rows.All(row => row.Draft.Due is null), "No asignar fechas ambiguas o inconsistentes.");
    Check(rows[0].ReviewNote == "" && rows.Skip(1).All(row => row.ReviewNote.Length > 0), "Distinguir ausencia de fecha de ambigüedad.");
    rows = CardBatchPreparer.Prepare(rows, "lista-real", Array.Empty<TrelloMember>(), Array.Empty<string>());
    Check(rows.All(row => row.CanSubmit && row.Draft.ListId == "lista-real") && rows[1].ReviewNote.Contains("hora"), "Preparar sin CSV y conservar las observaciones.");
});

await Run("Gemini limita respuestas, no reintenta errores y no muestra el cuerpo privado", async () =>
{
    foreach (System.Net.HttpStatusCode status in new[]
    {
        System.Net.HttpStatusCode.BadRequest, System.Net.HttpStatusCode.Unauthorized, System.Net.HttpStatusCode.Forbidden,
        System.Net.HttpStatusCode.NotFound, System.Net.HttpStatusCode.TooManyRequests, System.Net.HttpStatusCode.InternalServerError
    })
    {
        using var handler = new GeminiHttpDouble { Respond = (_, _) => Task.FromResult(GeminiHttpDouble.Json("private-response-secret", status)) };
        using var client = new GeminiCardClient(handler);
        bool rejected = false;
        try { await client.GenerateAsync(new AppSettings { GeminiApiKey = "dummy" }, "Pendientes", false, now, TimeZoneInfo.Utc, CancellationToken.None); }
        catch (HttpRequestException ex)
        {
            rejected = true;
            Check(ex.StatusCode == status && !ex.Message.Contains("private-response-secret") &&
                !GeminiCardClient.GetErrorMessage(ex).Contains("private-response-secret"), "Conservar status sin revelar el cuerpo.");
        }
        Check(rejected && handler.Calls == 1, "Sin reintentos automáticos, incluso ante 429.");
    }
    foreach (bool knownLength in new[] { true, false })
    {
        using var handler = new GeminiHttpDouble
        {
            Respond = (_, _) =>
            {
                var content = new StreamContent(new GeminiHttpDouble.NonSeekableStream(new byte[GeminiCardClient.MaxResponseBytes + 1]));
                if (knownLength) content.Headers.ContentLength = GeminiCardClient.MaxResponseBytes + 1;
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
            }
        };
        using var client = new GeminiCardClient(handler);
        bool rejected = false;
        try { await client.GenerateAsync(new AppSettings { GeminiApiKey = "dummy" }, "Pendientes", false, now, TimeZoneInfo.Utc, CancellationToken.None); }
        catch (FormatException) { rejected = true; }
        Check(rejected, "Limitar la respuesta recibida.");
    }
});

await Run("Gemini distingue errores del proveedor sin exponer mensajes ni metadatos privados", async () =>
{
    const string secret = "private-body-dummy-key-and-prompt";
    var cases = new[]
    {
        (Body: JsonSerializer.Serialize(new { error = new { message = secret, details = new[] { new { reason = "API_KEY_INVALID", metadata = new { key = secret } } } } }), Expected: "API key no es válida"),
        (Body: JsonSerializer.Serialize(new { error = new { message = secret, details = new[] { new { reason = "API_KEY_SERVICE_BLOCKED" } } } }), Expected: "restricciones"),
        (Body: JsonSerializer.Serialize(new { error = new { message = secret, details = new[] { new { reason = "SERVICE_DISABLED" } } } }), Expected: "deshabilitada"),
        (Body: JsonSerializer.Serialize(new { error = new { message = secret, details = new[] { new { fieldViolations = new[] { new { field = "generation_config.response_schema.properties[private]", description = secret } } } } } }), Expected: "response_schema"),
        (Body: JsonSerializer.Serialize(new { error = new { message = "Invalid generation_config.response_schema: " + secret } }), Expected: "response_schema"),
        (Body: JsonSerializer.Serialize(new { error = new { message = "Invalid maxOutputTokens: " + secret } }), Expected: "maxOutputTokens"),
        (Body: JsonSerializer.Serialize(new { error = new { message = secret, status = "FAILED_PRECONDITION" } }), Expected: "condición de uso"),
        (Body: JsonSerializer.Serialize(new { error = new { message = secret, status = "UNKNOWN", details = new[] { new { reason = secret } } } }), Expected: "sin un detalle reconocible")
    };
    foreach (var item in cases)
    {
        using var handler = new GeminiHttpDouble { Respond = (_, _) => Task.FromResult(GeminiHttpDouble.Json(item.Body, System.Net.HttpStatusCode.BadRequest)) };
        using var client = new GeminiCardClient(handler);
        bool rejected = false;
        try { await client.GenerateAsync(new AppSettings { GeminiApiKey = "dummy" }, "Pendientes", false, now, TimeZoneInfo.Utc, CancellationToken.None); }
        catch (HttpRequestException ex)
        {
            rejected = true;
            string message = GeminiCardClient.GetErrorMessage(ex);
            Check(ex.StatusCode == System.Net.HttpStatusCode.BadRequest && message.Contains(item.Expected), "Clasificar la evidencia del servidor y conservar HTTP 400.");
            Check(!message.Contains(secret) && !ex.ToString().Contains(secret) && ex.Data.Count == 0, "Nunca propagar texto, metadata ni claves del error.");
        }
        Check(rejected && handler.Calls == 1, "Informar el rechazo sin reintentar.");
    }
});

await Run("Gemini acota el detalle de errores y conserva HTTP ante cuerpos inválidos", async () =>
{
    foreach (string body in new[] { "<html>Error privado</html>", "{", "[]", "{\"error\":{\"details\":[null]}}", new string('x', 32769) })
    {
        using var handler = new GeminiHttpDouble
        {
            Respond = (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
            {
                Content = new StreamContent(new GeminiHttpDouble.NonSeekableStream(System.Text.Encoding.UTF8.GetBytes(body)))
            })
        };
        using var client = new GeminiCardClient(handler);
        bool rejected = false;
        try { await client.GenerateAsync(new AppSettings { GeminiApiKey = "dummy" }, "Pendientes", false, now, TimeZoneInfo.Utc, CancellationToken.None); }
        catch (HttpRequestException ex)
        {
            rejected = true;
            Check(ex.StatusCode == System.Net.HttpStatusCode.BadRequest && GeminiCardClient.GetErrorMessage(ex).Contains("sin un detalle reconocible"),
                "Un cuerpo inválido o excesivo mantiene el rechazo HTTP original.");
        }
        Check(rejected, "Rechazar sin publicar datos del cuerpo.");
    }
});

await Run("Gemini propaga cancelación y descarta una respuesta tardía", async () =>
{
    using var cancellation = new CancellationTokenSource();
    using var handler = new GeminiHttpDouble
    {
        Respond = (_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(GeminiHttpDouble.Json(GeminiHttpDouble.Envelope(GeminiHttpDouble.Proposal())));
        }
    };
    using var client = new GeminiCardClient(handler);
    bool cancelled = false;
    try { await client.GenerateAsync(new AppSettings { GeminiApiKey = "dummy" }, "Pendientes", false, now, TimeZoneInfo.Utc, cancellation.Token); }
    catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled && handler.Calls == 1, "No publicar una respuesta después de cancelar.");
});

await Run("La revalidación del lote conserva excluidas y resultados sin habilitar reenvíos", () =>
{
    CardImportRow[] source = Enum.GetValues<CardImportState>().Select((state, index) => new CardImportRow
    {
        RowNumber = index + 1,
        State = state,
        Draft = new TrelloCardDraft { Name = "Revisada" }
    }).ToArray();
    List<CardImportRow> prepared = CardBatchPreparer.Prepare(source, "lista", Array.Empty<TrelloMember>(), Array.Empty<string>());
    foreach (CardImportRow row in prepared)
    {
        CardImportState previous = source[row.RowNumber - 1].State;
        Check(row.State == (previous == CardImportState.Invalid ? CardImportState.Ready : previous), "Solo revalidar borradores previos al envío.");
    }
    return Task.CompletedTask;
});

await Run("SQLite v2 migra Gemini opcional sin perder preferencias ni historial", async () =>
{
    using var fixture = new Fixture(false);
    using (SqliteConnection connection = fixture.OpenDatabase())
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE AppSettings (
                Id INTEGER NOT NULL PRIMARY KEY, ApiBaseUrl TEXT NOT NULL, ApiKey TEXT NOT NULL, Token TEXT NOT NULL,
                NotifyBeforeMinutes INTEGER NOT NULL, PollIntervalMinutes INTEGER NOT NULL, RepeatReminderMinutes INTEGER NOT NULL,
                IncludeOverdueCards INTEGER NOT NULL, PlaySound INTEGER NOT NULL, Theme TEXT NOT NULL, NotificationsEnabled INTEGER NOT NULL);
            CREATE TABLE NotificationHistory (CardKey TEXT NOT NULL PRIMARY KEY, LastNotifiedAt TEXT NOT NULL);
            INSERT INTO AppSettings VALUES (1, 'http://localhost', 'dummy-key', 'dummy-token', 120, 7, 0, 1, 0, 'Dark', 0);
            INSERT INTO NotificationHistory VALUES ('previa', $last);
            PRAGMA user_version = 2;";
        command.Parameters.AddWithValue("$last", now.ToString("O"));
        command.ExecuteNonQuery();
    }
    AppSettings[] loaded = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(fixture.Store.Load)));
    Check(loaded.All(settings => settings.GeminiApiKey == "" && settings.GeminiModel == AppSettings.DefaultGeminiModel &&
        !settings.NotificationsEnabled && settings.Theme == "Dark" && settings.NotifyBeforeMinutes == 120 && settings.Token == "dummy-token"),
        "Migración concurrente con valores predeterminados y preferencias conservadas.");
    Check(fixture.Store.LoadNotifiedCards()["previa"].EqualsExact(now), "Conservar historial.");
    loaded[0].GeminiApiKey = "dummy-gemini-key";
    fixture.Store.Save(loaded[0]);
    Check(new SettingsStore(fixture.DirectoryPath).Load().GeminiApiKey == "dummy-gemini-key", "Conservar Gemini tras reiniciar.");
});

await Run("CSV admite BOM, comillas escapadas, Markdown y descripciones multilínea", () =>
{
    string csv = "\uFEFFtitulo,descripcion,vencimiento,responsables\r\n\"Revisar, pedido\",\"**Importante**: \"\"urgente\"\"\r\nlínea 2\",2026-12-05T09:00:00-05:00,ana;@carlos\r\n";
    List<CardImportRow> rows = CardCsvImporter.Parse(csv, CancellationToken.None);
    Check(rows.Count == 1 && rows[0].RowNumber == 1 && rows[0].Draft.Name == "Revisar, pedido", "Una fila lógica por tarjeta.");
    Check(rows[0].Draft.Description.Replace("\r\n", "\n") == "**Importante**: \"urgente\"\nlínea 2", "Conservar contenido y formato.");
    Check(rows[0].Draft.Due == new DateTimeOffset(2026, 12, 5, 9, 0, 0, TimeSpan.FromHours(-5)), "Conservar el instante con offset.");
    Check(rows[0].RequestedMembers == "ana;@carlos", "Conservar responsables por fila.");
    rows = CardCsvImporter.Parse("titulo;descripcion\nEntrega;\"Revisar; enviar\"\n", CancellationToken.None);
    Check(rows[0].Draft.Description == "Revisar; enviar" && rows[0].Draft.Due is null, "Separador de Excel y campos opcionales.");
    return Task.CompletedTask;
});

await Run("CSV rechaza estructura incorrecta y limita tamaño y cantidad", () =>
{
    string[] invalid =
    {
        "", "titulo\n", "descripcion\nTexto", "titulo,titulo\nA,B", "titulo,extra\nA,B",
        "titulo\n\"sin cierre", new string('a', CardCsvImporter.MaxFileBytes + 1),
        "titulo\n" + string.Concat(Enumerable.Repeat("Tarjeta\n", CardCsvImporter.MaxRows + 1))
    };
    foreach (string csv in invalid)
    {
        bool rejected = false;
        try { CardCsvImporter.Parse(csv, CancellationToken.None); }
        catch (FormatException) { rejected = true; }
        Check(rejected, "Rechazar archivos inválidos sin crear filas parciales.");
    }
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    bool cancelled = false;
    try { CardCsvImporter.Parse("titulo\nTarjeta", cancellation.Token); }
    catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled, "Permitir cancelar el procesamiento.");
    return Task.CompletedTask;
});

await Run("CSV valida vencimientos con zona y errores de columnas por fila", () =>
{
    List<CardImportRow> rows = CardCsvImporter.Parse(
        "titulo,vencimiento\nSin fecha,\nUTC,2026-12-05T14:00:00Z\nLocal,2026-12-05T14:00:00\nInválida,2026-02-30T09:00:00-05:00\nExtra,,dato", CancellationToken.None);
    Check(rows[0].Draft.Due is null && rows[0].ParseError is null, "Admitir fecha vacía.");
    Check(rows[1].Draft.Due == new DateTimeOffset(2026, 12, 5, 14, 0, 0, TimeSpan.Zero), "Admitir UTC explícito.");
    Check(rows.Skip(2).All(row => row.ParseError is not null), "Rechazar fechas ambiguas, inexistentes y exceso de columnas.");
    return Task.CompletedTask;
});

await Run("Importación combina responsables por fila y predeterminados sin asignaciones silenciosas", () =>
{
    var members = new[]
    {
        new TrelloMember { Id = "a", Username = "ana", FullName = "Ana" },
        new TrelloMember { Id = "b", Username = "carlos", FullName = "Carlos" }
    };
    List<CardImportRow> source = CardCsvImporter.Parse(
        "titulo,responsables\nComún,\nPropios,@ANA;carlos;ana\nDesconocido,nadie\nSolo símbolo,@\n ,ana", CancellationToken.None);
    List<CardImportRow> rows = CardBatchPreparer.Prepare(source, "lista", members, new[] { "b" });
    Check(rows[0].Draft.MemberIds.SequenceEqual(new[] { "b" }), "Aplicar predeterminados a celdas vacías.");
    Check(rows[1].Draft.MemberIds.SequenceEqual(new[] { "a", "b" }), "Los responsables de la fila prevalecen y no se duplican.");
    Check(rows.Take(2).All(row => row.State == CardImportState.Ready && row.Draft.ListId == "lista"), "Preparar válidas para el destino elegido.");
    Check(rows.Skip(2).All(row => row.State == CardImportState.Invalid), "Exigir usuarios reconocidos y título útil.");
    Check(source[0].Draft.ListId == "" && source[0].Draft.MemberIds.Count == 0, "Revisar no debe mutar el archivo de origen.");
    Check(CardBatchPreparer.Prepare(source.Take(1), "lista", members, new[] { "ausente" })[0].State == CardImportState.Invalid,
        "No omitir silenciosamente un responsable predeterminado ausente.");
    Check(CardBatchPreparer.Prepare(source.Take(1), "", members, Array.Empty<string>())[0].State == CardImportState.Invalid,
        "Reutilizar la validación de destino del borrador.");
    return Task.CompletedTask;
});

await Run("Resultados de importación evitan reenvíos y exportan texto seguro", () =>
{
    foreach (CardImportState state in Enum.GetValues<CardImportState>())
    {
        Check(new CardImportRow { State = state }.CanSubmit == (state is CardImportState.Ready or CardImportState.Rejected),
            "Solo reenviar pendientes o rechazos confirmados.");
    }
    var row = new CardImportRow
    {
        RowNumber = 1, Draft = new TrelloCardDraft { Name = " =HYPERLINK(\"ejemplo\")" }, State = CardImportState.Created,
        CreatedCard = new TrelloCard { Id = "creada", Url = "https://trello.com/c/ejemplo" }, Message = "Creada, correctamente"
    };
    string csv = CardCsvImporter.ExportResults(new[] { row });
    Check(csv.Contains("\"' =HYPERLINK(\"\"ejemplo\"\")\"") && csv.Contains("\"Creada, correctamente\""),
        "Neutralizar fórmulas y escapar comillas y separadores.");
    Check(csv.Contains("https://trello.com/c/ejemplo") && row.CanOpen, "Conservar ID y enlace seguro de la tarjeta.");
    foreach (string url in new[] { "http://trello.com/c/x", "https://trello.com.ejemplo.invalid/c/x", "file:///C:/dato", "javascript:alert(1)" })
        Check(new TrelloCard { Url = url }.TrelloUri is null, "No abrir esquemas ni hosts ajenos a Trello.");
    return Task.CompletedTask;
});

await Run("Creación manual exige título y lista y admite campos opcionales vacíos", () =>
{
    var draft = new TrelloCardDraft();
    Check(draft.GetValidationError() is not null, "Rechazar un formulario vacío.");
    draft.Name = "Nueva tarjeta";
    Check(draft.GetValidationError() is not null, "Exigir lista de destino.");
    draft.ListId = "lista-ejemplo";
    Check(draft.GetValidationError() is null, "Permitir tarjeta sin fecha, responsables, etiquetas ni checklists.");
    draft.Name = " \t ";
    Check(draft.GetValidationError() is not null, "No aceptar un título de espacios.");
    return Task.CompletedTask;
});

await Run("Checklists manuales conservan orden y omiten líneas vacías", () =>
{
    var checklist = new TrelloChecklistDraft
    {
        Name = "Antes de enviar",
        ItemsText = "  Revisar productos  \r\n\r\n Confirmar dirección\n \t \nAdjuntar factura\rÚltima revisión"
    };
    Check(checklist.GetItems().SequenceEqual(new[] { "Revisar productos", "Confirmar dirección", "Adjuntar factura", "Última revisión" }),
        "Respetar el orden y admitir saltos de línea de Windows y otros sistemas.");
    var draft = new TrelloCardDraft { Name = "Entrega", ListId = "lista", Checklists = new() { checklist } };
    Check(draft.GetValidationError() is null, "Aceptar el checklist completo.");
    checklist.Name = " ";
    Check(draft.GetValidationError() is not null, "Exigir nombre del checklist.");
    checklist.Name = "Revisión";
    checklist.ItemsText = "\r\n \t ";
    Check(draft.GetValidationError() is not null, "Rechazar un checklist sin elementos útiles.");
    return Task.CompletedTask;
});

await Run("Miembros y etiquetas de Trello muestran nombres y alternativas", () =>
{
    TrelloMember member = JsonSerializer.Deserialize<TrelloMember>("{\"id\":\"miembro\",\"fullName\":\"Ana\",\"username\":\"ana.ejemplo\"}")!;
    Check(member.DisplayName == "Ana (@ana.ejemplo)", "Distinguir miembros con nombres iguales por usuario.");
    member.FullName = "";
    Check(member.DisplayName == "@ana.ejemplo", "Usar el usuario cuando falta el nombre.");
    member.Username = "";
    Check(member.DisplayName == "miembro", "Usar el ID si falta información de presentación.");
    TrelloLabel label = JsonSerializer.Deserialize<TrelloLabel>("{\"id\":\"etiqueta\",\"name\":\"Urgente\",\"color\":\"red\"}")!;
    Check(label.DisplayName == "Urgente", "Mostrar el nombre de la etiqueta.");
    label.Name = "";
    Check(label.DisplayName == "Etiqueta red", "Las etiquetas sin nombre se distinguen por color.");
    label.Color = null;
    Check(label.DisplayName == "Etiqueta sin nombre", "Admitir etiquetas sin color ni nombre.");
    return Task.CompletedTask;
});

await Run("Ventana de aviso, límites y tarjetas completadas", () =>
{
    TrelloCard[] cards =
    {
        Card("sin-fecha", null), Card("completada", now.AddMinutes(10), true),
        Card("vencida", now.AddMinutes(-1)), Card("vence-ahora", now),
        Card("proxima", now.AddSeconds(1)), Card("limite", now.AddMinutes(60)),
        Card("futura", now.AddMinutes(60).AddSeconds(1))
    };
    var settings = new AppSettings();
    Check(ReminderSchedule.GetEligibleCards(cards, settings, now).Select(c => c.Id)
        .SequenceEqual(new[] { "proxima", "limite" }), "Solo próximas pendientes dentro del límite.");
    settings.IncludeOverdueCards = true;
    Check(ReminderSchedule.GetEligibleCards(cards, settings, now).Count == 4, "Incluir vencidas y vencimiento exacto.");
    Check(new TrelloCardViewModel(cards[2], now).RemainingText == "Vencida pendiente", "Texto para vencidas.");
    return Task.CompletedTask;
});

await Run("Estados de todas las tarjetas y límites de vencimiento", () =>
{
    (TrelloCard Card, TrelloCardStatus Status)[] cases =
    {
        (Card("sin-fecha", null), TrelloCardStatus.NoDueDate),
        (Card("completada-sin-fecha", null, true), TrelloCardStatus.Completed),
        (Card("completada-vencida", now.AddDays(-1), true), TrelloCardStatus.Completed),
        (Card("completada-proxima", now.AddMinutes(10), true), TrelloCardStatus.Completed),
        (Card("vencida", now.AddTicks(-1)), TrelloCardStatus.Overdue),
        (Card("vence-ahora", now.ToOffset(TimeSpan.FromHours(-5))), TrelloCardStatus.Overdue),
        (Card("proxima", now.AddTicks(1)), TrelloCardStatus.DueSoon),
        (Card("limite", now.AddMinutes(60).ToOffset(TimeSpan.FromHours(2))), TrelloCardStatus.DueSoon),
        (Card("futura", now.AddMinutes(60).AddTicks(1)), TrelloCardStatus.Upcoming)
    };
    foreach (var item in cases)
    {
        Check(item.Card.GetStatus(now, 60) == item.Status, $"Estado de {item.Card.Id}.");
    }

    TrelloCard changing = Card("cambiante", now.AddMinutes(90));
    Check(changing.GetStatus(now, 60) == TrelloCardStatus.Upcoming, "Fuera de la ventana actual.");
    Check(changing.GetStatus(now, 120) == TrelloCardStatus.DueSoon, "Respetar anticipación personalizada.");
    Check(changing.GetStatus(now.AddMinutes(30), 60) == TrelloCardStatus.DueSoon, "Entrar en ventana al avanzar el tiempo.");
    Check(changing.GetStatus(now.AddMinutes(90), 60) == TrelloCardStatus.Overdue, "Vencer en el instante exacto.");
    return Task.CompletedTask;
});

await Run("Presentación de tarjetas sin fecha y completadas", () =>
{
    var noDueDate = new TrelloCardViewModel(Card("sin-fecha", null), now);
    Check(noDueDate.RemainingText == "Sin fecha" && noDueDate.DueText == "Sin fecha de vencimiento", "Mostrar tarjeta sin fecha sin fallar.");
    foreach (DateTimeOffset? due in new DateTimeOffset?[] { null, now.AddDays(-1), now.AddHours(1) })
    {
        TrelloCard card = Card("completada", due, true);
        var completed = new TrelloCardViewModel(card, now);
        Check(completed.RemainingText == "Completada", "Una completada no se presenta como pendiente.");
        Check(completed.Name == card.Name && completed.Url == card.Url, "Conservar identidad y enlace.");
    }
    Check(new TrelloCardViewModel(Card("inminente", now.AddSeconds(30)), now).RemainingText ==
        "Vence en menos de un minuto", "Conservar detalle del vencimiento próximo.");
    return Task.CompletedTask;
});

await Run("Nombre del tablero visible también en tarjetas completadas y sin fecha", () =>
{
    foreach (TrelloCard card in new[] { Card("pendiente", now.AddMinutes(10)), Card("completada", null, true), Card("sin-fecha", null) })
    {
        card.BoardName = "Equipo de diseño";
        card.BoardId = "tablero-prueba";
        Check(new TrelloCardViewModel(card, now).BoardText == "Tablero: Equipo de diseño", "Mostrar el nombre en cualquier estado.");
        card.BoardName = " ";
        Check(new TrelloCardViewModel(card, now).BoardText == "Tablero: tablero-prueba", "Usar el identificador si falta el nombre.");
        card.BoardId = string.Empty;
        Check(new TrelloCardViewModel(card, now).BoardText == "Tablero no disponible", "Informar ausencia de datos sin una etiqueta vacía.");
    }
    return Task.CompletedTask;
});

await Run("Lista visible en tabla y vista compacta con alternativas para datos ausentes", () =>
{
    foreach (TrelloCard card in new[] { Card("pendiente", now.AddMinutes(10)), Card("completada", null, true), Card("sin-fecha", null) })
    {
        card.ListId = "lista-prueba";
        card.ListName = "En progreso";
        var viewModel = new TrelloCardViewModel(card, now);
        Check(viewModel.ListName == "En progreso" && viewModel.ListText == "Lista: En progreso", "Mostrar el nombre de lista en ambos diseños.");
        foreach (string? missingName in new string?[] { null, "", " " })
        {
            card.ListName = missingName;
            viewModel = new TrelloCardViewModel(card, now);
            Check(viewModel.ListName == "lista-prueba" && viewModel.ListText == "Lista: lista-prueba", "Usar ID cuando falta el nombre.");
        }
        card.ListId = string.Empty;
        viewModel = new TrelloCardViewModel(card, now);
        Check(viewModel.ListName == "Lista no disponible" && viewModel.ListText == "Lista no disponible", "Informar ausencia sin etiqueta vacía.");
    }
    return Task.CompletedTask;
});

await Run("Contrato de listas compatible con respuestas anteriores y valores nulos", () =>
{
    TrelloCard card = JsonSerializer.Deserialize<TrelloCard>("""
        { "id": "tarjeta", "idList": "lista-prueba" }
        """)!;
    TrelloBoard board = JsonSerializer.Deserialize<TrelloBoard>("""
        { "id": "tablero", "name": "Equipo", "lists": [{ "id": "lista-prueba", "name": "En progreso" }] }
        """)!;
    Check(board.Lists!.Single().Id == card.ListId && board.Lists.Single().Name == "En progreso", "Leer listas anidadas e identidad de la tarjeta.");
    card.ListName = board.Lists.Single().Name;
    string serialized = JsonSerializer.Serialize(card);
    Check(serialized.Contains("\"idList\":\"lista-prueba\"") && !serialized.Contains("ListName"), "Conservar contrato externo y excluir nombre enriquecido.");
    foreach (string json in new[] { "{}", "{\"idList\":null}", "{\"idList\":\" \"}" })
    {
        TrelloCard legacy = JsonSerializer.Deserialize<TrelloCard>(json)!;
        Check(new TrelloCardViewModel(legacy, now).ListName == "Lista no disponible", "Aceptar tarjetas sin lista.");
    }
    foreach (string json in new[] { "{}", "{\"lists\":null}", "{\"lists\":[]}" })
    {
        TrelloBoard legacy = JsonSerializer.Deserialize<TrelloBoard>(json)!;
        Check(legacy.Lists is null || legacy.Lists.Count == 0, "Aceptar tableros de mocks anteriores sin listas.");
    }
    return Task.CompletedTask;
});

await Run("Insignias del dashboard respetan la anticipación y los estados especiales", () =>
{
    (TrelloCard Card, TrelloCardStatus Status, string Text)[] cases =
    {
        (Card("vencida", now), TrelloCardStatus.Overdue, "Vencida"),
        (Card("limite", now.AddMinutes(90)), TrelloCardStatus.DueSoon, "Próxima"),
        (Card("futura", now.AddMinutes(90).AddTicks(1)), TrelloCardStatus.Upcoming, "Al día"),
        (Card("sin-fecha", null), TrelloCardStatus.NoDueDate, "Sin fecha"),
        (Card("completada", now.AddDays(-1), true), TrelloCardStatus.Completed, "Completada")
    };
    foreach (var item in cases)
    {
        var viewModel = new TrelloCardViewModel(item.Card, now, 90);
        Check(viewModel.Status == item.Status && viewModel.StatusText == item.Text,
            $"La insignia de {item.Card.Id} coincide con la ventana del monitor.");
    }

    TrelloCard changing = Card("cambiante", now.AddMinutes(90));
    Check(new TrelloCardViewModel(changing, now, 60).StatusText == "Al día", "Fuera de una ventana más corta.");
    Check(new TrelloCardViewModel(changing, now.AddMinutes(90), 60).StatusText == "Vencida", "Actualizar el estado al vencer.");
    changing.BoardName = "Equipo de diseño";
    Check(new TrelloCardViewModel(changing, now).BoardName == "Equipo de diseño", "Columna de tablero sin prefijo.");
    changing.BoardName = null;
    changing.BoardId = "tablero-prueba";
    Check(new TrelloCardViewModel(changing, now).BoardName == "tablero-prueba", "Usar ID cuando falta el nombre.");
    changing.BoardId = string.Empty;
    Check(new TrelloCardViewModel(changing, now).BoardName == "Tablero no disponible", "Datos ausentes en la tabla.");
    return Task.CompletedTask;
});

await Run("Búsqueda parcial por nombre combinada con tablero y estado", () =>
{
    TrelloCard first = Card("primera", now.AddMinutes(10));
    first.Name = "Preparar entrega";
    first.BoardId = "a";
    TrelloCard second = Card("segunda", now.AddMinutes(20));
    second.Name = "Revisar ENTREGA";
    second.BoardId = "b";
    TrelloCard completed = Card("completada", null, true);
    completed.Name = "Entrega final";
    completed.BoardId = "a";
    TrelloCard other = Card("otra", null);
    other.Name = "Reunión semanal";
    other.BoardName = "Entrega";
    TrelloCard missingName = JsonSerializer.Deserialize<TrelloCard>("{\"id\":\"sin-nombre\",\"name\":null}")!;
    var snapshot = new MonitorSnapshot(true, "", now, new[] { first, second })
    {
        AssignedCards = new[] { completed, second, other, first, missingName },
        NotifyBeforeMinutes = 60
    };
    Check(snapshot.GetFilteredCards(null, null, now, "  eNtReGa  ").Select(card => card.Id)
        .SequenceEqual(new[] { "primera", "segunda", "completada" }), "Buscar fragmentos sin distinguir mayúsculas y mantener el orden.");
    Check(snapshot.GetFilteredCards("a", TrelloCardStatus.DueSoon, now, "entrega").Single() == first,
        "Combinar simultáneamente texto, tablero y estado.");
    Check(snapshot.GetFilteredCards("a", TrelloCardStatus.Completed, now, "final").Single() == completed,
        "Buscar también en completadas sin fecha.");
    Check(!snapshot.GetFilteredCards("b", null, now, "preparar").Any(), "No incluir coincidencias de otro tablero.");
    Check(!snapshot.GetFilteredCards(null, null, now, "inexistente").Any(), "Búsqueda sin resultados.");
    foreach (string? search in new string?[] { null, "", "  " })
    {
        Check(snapshot.GetFilteredCards(null, null, now, search).Count() == 5, "Vaciar la búsqueda restaura todas las tarjetas, incluso sin nombre.");
    }
    Check(snapshot.GetFilteredCards("a", TrelloCardStatus.DueSoon, now, "").Single() == first,
        "Vaciar la búsqueda conserva los otros filtros.");
    var refreshed = snapshot with { AssignedCards = new[] { second } };
    Check(refreshed.GetFilteredCards(null, null, now, "entrega").Single() == second, "Aplicar la búsqueda a datos actualizados.");
    Check(snapshot.AssignedCards.Count == 5 && snapshot.DueSoonCards.Count == 2, "Buscar no modifica tarjetas ni candidatas a avisos.");
    return Task.CompletedTask;
});

await Run("Filtro por tablero combinado con todos los estados", () =>
{
    var cards = new List<TrelloCard>();
    foreach (string boardId in new[] { "tablero-a", "tablero-b" })
    {
        TrelloCard[] boardCards =
        {
            Card(boardId + "-vencida", now), Card(boardId + "-proxima", now.AddMinutes(60)),
            Card(boardId + "-futura", now.AddMinutes(61)), Card(boardId + "-sin-fecha", null),
            Card(boardId + "-completada", now.AddMinutes(5), true)
        };
        foreach (TrelloCard card in boardCards)
        {
            card.BoardId = boardId;
            card.BoardName = "Mismo nombre";
        }
        cards.AddRange(boardCards);
    }
    cards.Add(Card("sin-tablero", null));
    var snapshot = new MonitorSnapshot(true, "", now, Array.Empty<TrelloCard>())
    {
        AssignedCards = cards,
        NotifyBeforeMinutes = 60
    };
    Check(snapshot.GetFilteredCards(null, null, now).Count() == 11, "Todos incluye tarjetas sin datos de tablero.");
    Check(snapshot.GetFilteredCards("", null, now).Count() == 11, "La opción Todos los tableros no excluye tarjetas.");
    Check(snapshot.GetFilteredCards("tablero-a", null, now).Select(card => card.Id).SequenceEqual(new[]
    {
        "tablero-a-vencida", "tablero-a-completada", "tablero-a-proxima", "tablero-a-futura", "tablero-a-sin-fecha"
    }), "Filtrar por identificador y conservar el orden por vencimiento, con sin fecha al final.");
    foreach (TrelloCardStatus status in Enum.GetValues<TrelloCardStatus>())
    {
        TrelloCard selected = snapshot.GetFilteredCards("tablero-b", status, now).Single();
        Check(selected.BoardId == "tablero-b" && selected.GetStatus(now, 60) == status, "Combinar ambos filtros sin confundir nombres iguales.");
    }
    Check(!snapshot.GetFilteredCards("inexistente", null, now).Any(), "Un tablero sin tarjetas da resultado vacío.");
    Check(snapshot.GetFilteredCards("tablero-a", TrelloCardStatus.DueSoon, now.AddMinutes(60)).Single().Id == "tablero-a-futura",
        "Recalcular estados al filtrar usando la hora indicada.");
    Check(cards.Count == 11 && snapshot.AssignedCards.Count == 11 && snapshot.DueSoonCards.Count == 0,
        "Los filtros no modifican el snapshot ni las candidatas a avisos.");
    return Task.CompletedTask;
});

await Run("Paginación recorre todas las tarjetas sin omisiones ni duplicados y con orden estable", () =>
{
    foreach (int pageSize in new[] { 10, 25, 50 })
    {
        foreach (int total in new[] { 0, 1, 10, 25, 26, 50, 51 })
        {
            TrelloCard[] cards = Enumerable.Range(1, total).Select(index =>
            {
                TrelloCard card = Card($"tarjeta-{index:D3}", now.AddMinutes(30));
                card.Name = "Mismo nombre";
                return card;
            }).ToArray();
            var snapshot = new MonitorSnapshot(true, "", now, Array.Empty<TrelloCard>())
            {
                AssignedCards = cards.Reverse().ToArray()
            };
            var first = snapshot.GetCardPage(1, pageSize, null, null, now);
            Check(first.TotalCount == total && first.PageCount == (int)Math.Ceiling((double)total / pageSize),
                "Contar páginas completas, parciales y resultados vacíos.");
            var visited = new List<string>();
            for (int number = 1; number <= first.PageCount; number++)
            {
                var page = snapshot.GetCardPage(number, pageSize, null, null, now);
                Check(page.PageNumber == number && page.Cards.Count > 0 && page.Cards.Count <= pageSize,
                    "Cada página válida contiene como máximo el tamaño solicitado.");
                visited.AddRange(page.Cards.Select(card => card.Id));
            }
            Check(visited.SequenceEqual(cards.Select(card => card.Id)),
                "El desempate por ID permite recorrer todos los resultados aunque coincidan fecha y nombre.");
            Check(snapshot.GetCardPage(int.MinValue, pageSize, null, null, now).PageNumber == 1,
                "Ajustar páginas inferiores al inicio.");
            var last = snapshot.GetCardPage(int.MaxValue, pageSize, null, null, now);
            Check(last.PageNumber == Math.Max(1, first.PageCount), "Ajustar páginas superiores al final sin desbordar.");
            Check(total != 0 || last.Cards.Count == 0, "La página vacía no contiene tarjetas.");
        }
    }
    return Task.CompletedTask;
});

await Run("Paginación aplica primero tablero, búsqueda, estado y ámbitos de pendientes e historial", () =>
{
    var cards = new List<TrelloCard>();
    for (int index = 1; index <= 30; index++)
    {
        foreach (string kind in new[] { "pendiente", "completada", "archivada", "ambas" })
        {
            TrelloCard card = Card($"{kind}-{index:D2}", now.AddMinutes(index), kind is "completada" or "ambas");
            card.Closed = kind is "archivada" or "ambas";
            card.BoardId = index % 2 == 0 ? "a" : "b";
            card.Name = index % 3 == 0 ? $"Entrega {index:D2}" : $"Reunión {index:D2}";
            cards.Add(card);
        }
    }
    var snapshot = new MonitorSnapshot(true, "", now, new[] { cards[0] })
    {
        AssignedCards = cards,
        NotifyBeforeMinutes = 60
    };
    foreach (var item in new[]
    {
        (Scope: TrelloCardScope.Pending, Count: 5),
        (Scope: TrelloCardScope.History, Count: 15),
        (Scope: TrelloCardScope.Completed, Count: 10),
        (Scope: TrelloCardScope.Archived, Count: 10)
    })
    {
        var page = snapshot.GetCardPage(2, 2, "a", null, now, "  ENTREGA  ", item.Scope);
        Check(page.TotalCount == item.Count && page.PageNumber == 2 && page.Cards.Count == 2,
            "El total corresponde a los filtros y al ámbito, antes de paginar.");
        Check(page.Cards.SequenceEqual(snapshot.GetFilteredCards("a", null, now, "entrega", item.Scope).Skip(2).Take(2)),
            "Conservar los filtros y el orden de vencimiento en las páginas intermedias.");
    }
    var pending = snapshot.GetCardPage(2, 2, "a", TrelloCardStatus.DueSoon, now, "entrega", TrelloCardScope.Pending);
    Check(pending.Cards.Select(card => card.Id).SequenceEqual(new[] { "pendiente-18", "pendiente-24" }),
        "Combinar todos los filtros sin reincorporar tarjetas históricas.");
    var noMatches = snapshot.GetCardPage(3, 10, "a", TrelloCardStatus.NoDueDate, now, "entrega", TrelloCardScope.Pending);
    Check(noMatches is { TotalCount: 0, PageCount: 0, PageNumber: 1 } && noMatches.Cards.Count == 0,
        "Una combinación sin coincidencias vuelve a la página inicial vacía.");
    Check(snapshot.AssignedCards.Count == 120 && snapshot.DueSoonCards.Single() == cards[0],
        "Paginar no reduce los datos de indicadores ni las candidatas a notificación.");
    return Task.CompletedTask;
});

await Run("Paginación conserva o ajusta la página al actualizar y rechaza tamaños inválidos", () =>
{
    TrelloCard[] cards = Enumerable.Range(1, 60).Select(index => Card($"tarjeta-{index:D2}", index <= 50 ? now.AddMinutes(index) : null)).ToArray();
    var snapshot = new MonitorSnapshot(true, "", now, Array.Empty<TrelloCard>()) { AssignedCards = cards };
    var last = snapshot.GetCardPage(3, 25, null, null, now);
    Check(last.PageNumber == 3 && last.Cards.Count == 10 && last.Cards.All(card => card.Due is null),
        "Conservar página y colocar las tarjetas sin fecha al final.");
    var reduced = snapshot with { AssignedCards = cards.Take(27).ToArray() };
    var adjusted = reduced.GetCardPage(last.PageNumber, 25, null, null, now);
    Check(adjusted is { PageNumber: 2, PageCount: 2, TotalCount: 27 } && adjusted.Cards.Count == 2,
        "Si la página desaparece al actualizar, mostrar la última disponible.");
    var empty = snapshot with { AssignedCards = Array.Empty<TrelloCard>() };
    Check(empty.GetCardPage(last.PageNumber, 25, null, null, now) is { PageNumber: 1, PageCount: 0, TotalCount: 0 },
        "Una actualización vacía limpia la paginación.");
    foreach (int invalidSize in new[] { 0, -1 })
    {
        bool rejected = false;
        try
        {
            snapshot.GetCardPage(1, invalidSize, null, null, now);
        }
        catch (ArgumentOutOfRangeException)
        {
            rejected = true;
        }
        Check(rejected, "Rechazar tamaños que impidan calcular páginas válidas.");
    }
    return Task.CompletedTask;
});

await Run("Opciones de tablero únicas, nombres ausentes y cambios de consulta", () =>
{
    TrelloCard first = Card("primera", null);
    first.BoardId = "a";
    TrelloCard second = Card("segunda", null);
    second.BoardId = "a";
    second.BoardName = "Alfa";
    TrelloCard third = Card("tercera", null);
    third.BoardId = "b";
    third.BoardName = "Alfa";
    TrelloCard unnamed = Card("sin-nombre", null);
    unnamed.BoardId = "c";
    unnamed.BoardName = " ";
    var snapshot = new MonitorSnapshot(true, "", now, Array.Empty<TrelloCard>())
    {
        AssignedCards = new[] { unnamed, third, first, second, Card("sin-tablero", null) }
    };
    List<TrelloBoard> boards = snapshot.GetBoards();
    Check(boards.Select(board => board.Id).SequenceEqual(new[] { "a", "b", "c" }), "Una opción por ID, incluso con nombres repetidos.");
    Check(boards[0].Name == "Alfa" && boards[2].Name == "c", "Usar un nombre disponible o el ID como alternativa.");
    second.BoardName = "Renombrado";
    Check(snapshot.GetBoards().Single(board => board.Id == "a").Name == "Renombrado", "Actualizar nombres conservando la identidad.");
    var empty = snapshot with { AssignedCards = Array.Empty<TrelloCard>() };
    Check(empty.GetBoards().Count == 0 && !empty.GetFilteredCards(null, null, now).Any(), "Una consulta vacía retira tableros y tarjetas.");
    return Task.CompletedTask;
});

await Run("Campos de tablero compatibles con respuestas anteriores y valores nulos", () =>
{
    TrelloCard card = JsonSerializer.Deserialize<TrelloCard>("""
        { "id": "tarjeta", "name": "Prueba", "idBoard": "tablero-prueba", "due": null }
        """)!;
    TrelloBoard board = JsonSerializer.Deserialize<TrelloBoard>("""
        { "id": "tablero-prueba", "name": "Tablero de prueba" }
        """)!;
    Check(card.BoardId == board.Id && board.Name == "Tablero de prueba", "Leer identificadores y nombres de los contratos Trello.");
    foreach (string json in new[] { "{\"id\":\"anterior\"}", "{\"id\":\"nula\",\"idBoard\":null}" })
    {
        TrelloCard legacy = JsonSerializer.Deserialize<TrelloCard>(json)!;
        var snapshot = new MonitorSnapshot(true, "", now, Array.Empty<TrelloCard>()) { AssignedCards = new[] { legacy } };
        Check(snapshot.GetBoards().Count == 0 && snapshot.GetFilteredCards(null, null, now).Single() == legacy,
            "Mantener visibles tarjetas sin tablero en Todos sin crear opciones vacías.");
    }
    return Task.CompletedTask;
});

await Run("Mis tarjetas separa pendientes del historial y sus tableros", () =>
{
    TrelloCard overdue = Card("vencida", now);
    overdue.BoardId = "activo";
    TrelloCard soon = Card("proxima", now.AddMinutes(30));
    TrelloCard future = Card("futura", now.AddDays(1));
    TrelloCard noDueDate = Card("sin-fecha", null);
    TrelloCard completed = Card("completada", null, true);
    completed.BoardId = "terminado";
    TrelloCard archived = Card("archivada", now.AddMinutes(-1));
    archived.Closed = true;
    archived.BoardId = "archivo";
    TrelloCard both = Card("completada-archivada", now.AddMinutes(10), true);
    both.Closed = true;
    both.BoardId = "archivo";
    var snapshot = new MonitorSnapshot(true, "", now, Array.Empty<TrelloCard>())
    {
        AssignedCards = new[] { overdue, soon, future, noDueDate, completed, archived, both },
        NotifyBeforeMinutes = 60
    };

    Check(snapshot.GetCards(TrelloCardScope.Pending).Select(card => card.Id)
        .SequenceEqual(new[] { "vencida", "proxima", "futura", "sin-fecha" }), "Pendientes excluye completadas y archivadas.");
    Check(snapshot.GetCards(TrelloCardScope.History).Count() == 3, "Historial incluye la unión, sin duplicar una completada archivada.");
    Check(snapshot.GetCards(TrelloCardScope.Completed).SequenceEqual(new[] { completed, both }), "Completadas incluye las archivadas.");
    Check(snapshot.GetCards(TrelloCardScope.Archived).SequenceEqual(new[] { archived, both }), "Archivar no exige completar.");
    Check(snapshot.GetBoards(TrelloCardScope.Pending).Single().Id == "activo", "Tableros del dashboard solo con pendientes.");
    Check(snapshot.GetBoards(TrelloCardScope.History).Select(board => board.Id).OrderBy(id => id)
        .SequenceEqual(new[] { "archivo", "terminado" }), "Tableros del historial solo con completadas o archivadas.");
    Check(snapshot.GetFilteredCards("archivo", null, now, " COMPLETADA ", TrelloCardScope.History).Single() == both,
        "Combinar historial con tablero y búsqueda sin distinguir mayúsculas.");
    Check(!snapshot.GetFilteredCards("archivo", null, now, null, TrelloCardScope.Pending).Any(), "Un tablero solo histórico no aporta pendientes.");
    Check(snapshot.GetFilteredCards(null, TrelloCardStatus.DueSoon, now, null, TrelloCardScope.Pending).Single() == soon,
        "El filtro temporal no reincorpora completadas ni archivadas.");
    Check(snapshot.GetCards(TrelloCardScope.Pending).Count(card => card.GetStatus(now, 60) is
        TrelloCardStatus.Upcoming or TrelloCardStatus.NoDueDate) == 2, "Al día solo incluye futuras y sin fecha pendientes.");

    completed.DueComplete = false;
    archived.Closed = false;
    Check(snapshot.GetCards(TrelloCardScope.Pending).Count() == 6 && snapshot.GetCards(TrelloCardScope.History).Single() == both,
        "Reabrir y restaurar tarjetas las devuelve a pendientes según su estado actual.");
    var empty = snapshot with { AssignedCards = Array.Empty<TrelloCard>() };
    Check(!empty.GetCards(TrelloCardScope.History).Any() && empty.GetBoards(TrelloCardScope.History).Count == 0,
        "Un historial vacío no conserva tarjetas ni opciones anteriores.");
    return Task.CompletedTask;
});

await Run("Archivadas conservan el estado de completado y nunca generan avisos", () =>
{
    foreach (bool complete in new[] { false, true })
    {
        foreach (DateTimeOffset? due in new DateTimeOffset?[] { null, now.AddDays(-1), now.AddMinutes(5) })
        {
            TrelloCard card = Card("archivada", due, complete);
            card.Closed = true;
            var viewModel = new TrelloCardViewModel(card, now);
            Check(viewModel.IsArchived && viewModel.StatusText == "Archivada", "Identificar la tarjeta como archivada, sin urgencia temporal.");
            Check(viewModel.RemainingText == (complete ? "Completada y archivada" : "Archivada sin completar"),
                "Distinguir archivadas terminadas de archivadas sin completar.");
            Check(ReminderSchedule.GetEligibleCards(new[] { card }, new AppSettings { IncludeOverdueCards = true }, now).Count == 0,
                "Excluir archivadas incluso con avisos de vencidas activos.");
        }
    }
    TrelloCard legacy = JsonSerializer.Deserialize<TrelloCard>("{\"id\":\"anterior\"}")!;
    TrelloCard archived = JsonSerializer.Deserialize<TrelloCard>("{\"id\":\"archivada\",\"closed\":true,\"dueComplete\":false}")!;
    Check(!legacy.Closed && archived.Closed && !archived.DueComplete, "Leer closed de Trello y conservar compatibilidad si falta.");
    Check(JsonSerializer.Serialize(archived).Contains("\"closed\":true"), "Conservar el nombre externo del campo.");
    return Task.CompletedTask;
});

await Run("Primer aviso y repetición a los 15, 30 y 60 minutos", () =>
{
    TrelloCard card = Card("tarjeta", now.AddHours(4));
    var history = new Dictionary<string, DateTimeOffset>();
    var settings = new AppSettings();
    Check(ReminderSchedule.ShouldNotify(card, settings, history, now), "Primer aviso inmediato.");
    history[ReminderSchedule.GetKey(card)] = now;
    foreach (int interval in new[] { 15, 30, 60 })
    {
        settings.RepeatReminderMinutes = interval;
        Check(!ReminderSchedule.ShouldNotify(card, settings, history, now.AddMinutes(interval).AddTicks(-1)), "No adelantar el aviso.");
        Check(ReminderSchedule.ShouldNotify(card, settings, history, now.AddMinutes(interval)), "Avisar al cumplir el intervalo.");
    }
    card.Due = card.Due!.Value.AddMinutes(1);
    Check(ReminderSchedule.ShouldNotify(card, settings, history, now), "Un vencimiento nuevo permite un aviso nuevo.");
    return Task.CompletedTask;
});

await Run("Nunca desactiva repeticiones también para vencidas", () =>
{
    var settings = new AppSettings { RepeatReminderMinutes = 0, IncludeOverdueCards = true };
    TrelloCard card = Card("tarjeta", now.AddMinutes(10));
    var history = new Dictionary<string, DateTimeOffset>();
    Check(ReminderSchedule.ShouldNotify(card, settings, history, now), "Nunca permite el primer aviso.");
    history[ReminderSchedule.GetKey(card)] = now;
    Check(!ReminderSchedule.ShouldNotify(card, settings, history, now.AddMinutes(5)), "No repetir próximas.");
    Check(!ReminderSchedule.ShouldNotify(card, settings, history, now.AddDays(1)), "No repetir vencidas.");
    return Task.CompletedTask;
});

await Run("Al vencer se aplica el intervalo de 2 horas desde el último aviso", () =>
{
    var settings = new AppSettings { IncludeOverdueCards = true, RepeatReminderMinutes = 15 };
    TrelloCard card = Card("tarjeta", now.AddMinutes(10));
    var history = new Dictionary<string, DateTimeOffset> { [ReminderSchedule.GetKey(card)] = now };
    Check(!ReminderSchedule.ShouldNotify(card, settings, history, now.AddMinutes(119)), "No repetir vencidas antes de dos horas.");
    Check(ReminderSchedule.ShouldNotify(card, settings, history, now.AddHours(2)), "Repetir vencidas a las dos horas.");
    return Task.CompletedTask;
});

await Run("Persistencia de preferencias y compatibilidad con configuración anterior", () =>
{
    using var fixture = new Fixture(false);
    File.WriteAllText(Path.Combine(fixture.DirectoryPath, "settings.json"), "{\"PollIntervalMinutes\":7}");
    AppSettings settings = fixture.Store.Load();
    Check(settings.PollIntervalMinutes == 7 && settings.RepeatReminderMinutes == 30 && !settings.IncludeOverdueCards && settings.NotificationsEnabled,
        "Importar preferencias y aplicar valores predeterminados a propiedades ausentes.");
    settings.RepeatReminderMinutes = 0;
    settings.IncludeOverdueCards = true;
    fixture.Store.Save(settings);
    settings = new SettingsStore(fixture.DirectoryPath).Load();
    Check(settings.RepeatReminderMinutes == 0 && settings.IncludeOverdueCards, "Preferencias conservadas.");
    return Task.CompletedTask;
});

await Run("Migración del historial anterior y conservación tras reiniciar", () =>
{
    using var fixture = new Fixture(false);
    TrelloCard card = Card("anterior", now.AddHours(1));
    string key = ReminderSchedule.GetKey(card);
    File.WriteAllText(Path.Combine(fixture.DirectoryPath, "notified-cards.json"), JsonSerializer.Serialize(new[] { key }));
    DateTimeOffset before = DateTimeOffset.UtcNow;
    var history = fixture.Store.LoadNotifiedCards();
    Check(history[key] >= before && history[key] <= DateTimeOffset.UtcNow, "Asignar hora de migración.");
    Check(new SettingsStore(fixture.DirectoryPath).LoadNotifiedCards()[key] == history[key], "Migración persistida una sola vez.");
    var settings = new AppSettings { RepeatReminderMinutes = 0 };
    Check(!ReminderSchedule.ShouldNotify(card, settings, history, DateTimeOffset.UtcNow.AddDays(1)), "Nunca respeta el historial antiguo.");
    history[key] = now;
    fixture.Store.SaveNotifiedCards(history);
    Check(new SettingsStore(fixture.DirectoryPath).LoadNotifiedCards()[key] == now, "Conservar hora exacta del último aviso.");
    return Task.CompletedTask;
});

await Run("SQLite conserva todas las preferencias sin crear archivos JSON", () =>
{
    using var fixture = new Fixture(false);
    AppSettings defaults = fixture.Store.Load();
    Check(JsonSerializer.Serialize(defaults) == JsonSerializer.Serialize(new AppSettings()), "Defaults de una instalación nueva.");
    Check(fixture.Store.LoadNotifiedCards().Count == 0, "Historial inicial vacío.");
    var expected = new AppSettings
    {
        ApiBaseUrl = "https://mock.example.com/api",
        ApiKey = "dummy-key-'ñ",
        Token = "dummy-token-'); DROP TABLE AppSettings; --",
        GeminiApiKey = "dummy-gemini-'key",
        GeminiModel = "gemini-model-test",
        NotifyBeforeMinutes = 180,
        PollIntervalMinutes = 11,
        RepeatReminderMinutes = 60,
        IncludeOverdueCards = true,
        NotificationsEnabled = false,
        PlaySound = false,
        Theme = "Dark"
    };
    fixture.Store.Save(expected);
    AppSettings actual = new SettingsStore(fixture.DirectoryPath).Load();
    Check(JsonSerializer.Serialize(actual) == JsonSerializer.Serialize(expected), "Conservar todos los campos y parametrizar caracteres especiales.");
    Check(File.Exists(fixture.DatabasePath), "Crear la base SQLite.");
    Check(!File.Exists(Path.Combine(fixture.DirectoryPath, "settings.json")) &&
        !File.Exists(Path.Combine(fixture.DirectoryPath, "notified-cards.json")), "No crear JSON de persistencia.");
    return Task.CompletedTask;
});

await Run("Migración SQLite v1 conserva preferencias e historial y activa los avisos por defecto", async () =>
{
    using var fixture = new Fixture(false);
    using (SqliteConnection connection = fixture.OpenDatabase())
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE AppSettings (
                Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
                ApiBaseUrl TEXT NOT NULL, ApiKey TEXT NOT NULL, Token TEXT NOT NULL,
                NotifyBeforeMinutes INTEGER NOT NULL, PollIntervalMinutes INTEGER NOT NULL,
                RepeatReminderMinutes INTEGER NOT NULL,
                IncludeOverdueCards INTEGER NOT NULL CHECK (IncludeOverdueCards IN (0, 1)),
                PlaySound INTEGER NOT NULL CHECK (PlaySound IN (0, 1)), Theme TEXT NOT NULL);
            CREATE TABLE NotificationHistory (CardKey TEXT NOT NULL PRIMARY KEY, LastNotifiedAt TEXT NOT NULL);
            INSERT INTO AppSettings VALUES (1, 'https://mock.example.com', 'dummy-key', 'dummy-token', 90, 7, 0, 1, 0, 'Dark');
            INSERT INTO NotificationHistory VALUES ('anterior|123', $last);
            PRAGMA user_version = 1;";
        command.Parameters.AddWithValue("$last", now.ToString("O"));
        command.ExecuteNonQuery();
    }
    File.WriteAllText(Path.Combine(fixture.DirectoryPath, "settings.json"), "Respaldo que no debe reimportarse");
    AppSettings[] migrated = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        new SettingsStore(fixture.DirectoryPath).Load())));
    var expected = new AppSettings
    {
        ApiBaseUrl = "https://mock.example.com", ApiKey = "dummy-key", Token = "dummy-token",
        NotifyBeforeMinutes = 90, PollIntervalMinutes = 7, RepeatReminderMinutes = 0,
        IncludeOverdueCards = true, PlaySound = false, Theme = "Dark"
    };
    Check(migrated.All(settings => JsonSerializer.Serialize(settings) == JsonSerializer.Serialize(expected)),
        "Migración concurrente conserva todos los campos y habilita las notificaciones.");
    Check(fixture.Store.LoadNotifiedCards().Single().Value == now, "Conservar el historial sin reimportar JSON.");
    expected.NotificationsEnabled = false;
    fixture.Store.Save(expected);
    Check(!new SettingsStore(fixture.DirectoryPath).Load().NotificationsEnabled, "Conservar desactivación al reabrir la base migrada.");
    using SqliteConnection migratedConnection = fixture.OpenDatabase();
    using SqliteCommand versionCommand = migratedConnection.CreateCommand();
    versionCommand.CommandText = "PRAGMA user_version;";
    Check(Convert.ToInt32(versionCommand.ExecuteScalar()) == 3, "Confirmar la nueva versión del esquema.");
});

await Run("Migración de ambos JSON conserva precisión y no vuelve a importar respaldos", () =>
{
    using var fixture = new Fixture(false);
    string settingsPath = Path.Combine(fixture.DirectoryPath, "settings.json");
    string historyPath = Path.Combine(fixture.DirectoryPath, "notified-cards.json");
    var expected = new AppSettings { ApiBaseUrl = "https://mock.example.com", Token = "dummy-token", Theme = "Light" };
    DateTimeOffset last = now.AddTicks(1234567).ToOffset(TimeSpan.FromMinutes(-330));
    var original = new Dictionary<string, DateTimeOffset> { ["Card'|123"] = last, ["card'|123"] = now };
    string settingsJson = JsonSerializer.Serialize(expected);
    string historyJson = JsonSerializer.Serialize(original);
    File.WriteAllText(settingsPath, settingsJson);
    File.WriteAllText(historyPath, historyJson);

    // Cualquier primera operación debe migrar ambos archivos, incluso leer el historial.
    var imported = fixture.Store.LoadNotifiedCards();
    Check(imported.Count == 2 && imported["Card'|123"].EqualsExact(last), "Mantener ticks, offset y claves sensibles a mayúsculas.");
    Check(JsonSerializer.Serialize(fixture.Store.Load()) == settingsJson, "Importar todas las preferencias.");
    Check(File.ReadAllText(settingsPath) == settingsJson && File.ReadAllText(historyPath) == historyJson, "No modificar los originales.");

    expected.Theme = "Dark";
    fixture.Store.Save(expected);
    fixture.Store.SaveNotifiedCards(new());
    File.WriteAllText(settingsPath, "JSON inválido posterior a la migración");
    File.WriteAllText(historyPath, "JSON inválido posterior a la migración");
    var restarted = new SettingsStore(fixture.DirectoryPath);
    Check(restarted.Load().Theme == "Dark" && restarted.LoadNotifiedCards().Count == 0,
        "SQLite es la fuente definitiva aunque los respaldos cambien o el historial se vacíe.");
    return Task.CompletedTask;
});

await Run("Migración inválida revierte el esquema y permite reintento sin pérdida", () =>
{
    foreach (var invalid in new[]
    {
        (Settings: "{", History: "{}"),
        (Settings: "null", History: "{}"),
        (Settings: "{\"Token\":null}", History: "{}"),
        (Settings: "{\"Theme\":\"Dark\"}", History: "{"),
        (Settings: "{}", History: "null"),
        (Settings: "{}", History: "[null]")
    })
    {
        using var fixture = new Fixture(false);
        string settingsPath = Path.Combine(fixture.DirectoryPath, "settings.json");
        string historyPath = Path.Combine(fixture.DirectoryPath, "notified-cards.json");
        File.WriteAllText(settingsPath, invalid.Settings);
        File.WriteAllText(historyPath, invalid.History);
        ExpectStorageFailure(() => fixture.Store.Load());
        Check(File.ReadAllText(settingsPath) == invalid.Settings && File.ReadAllText(historyPath) == invalid.History,
            "Preservar ambos archivos si falla la importación.");
        using (SqliteConnection connection = fixture.OpenDatabase())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            Check(Convert.ToInt32(command.ExecuteScalar()) == 0, "No confirmar migración fallida.");
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table';";
            Check(Convert.ToInt32(command.ExecuteScalar()) == 0, "Revertir también las tablas creadas.");
        }
        File.WriteAllText(settingsPath, "{\"Theme\":\"Light\"}");
        File.WriteAllText(historyPath, JsonSerializer.Serialize(new Dictionary<string, DateTimeOffset> { ["recuperada|123"] = now }));
        Check(fixture.Store.Load().Theme == "Light" && fixture.Store.LoadNotifiedCards()["recuperada|123"] == now,
            "Reintentar la importación completa después de corregir los archivos.");
    }
    return Task.CompletedTask;
});

await Run("Escritura de historial es atómica y errores SQL no exponen datos", () =>
{
    using var fixture = new Fixture();
    fixture.Store.SaveNotifiedCards(new() { ["original|123"] = now });
    using (SqliteConnection connection = fixture.OpenDatabase())
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TRIGGER RejectHistory BEFORE INSERT ON NotificationHistory
            WHEN NEW.CardKey = 'rechazada'
            BEGIN SELECT RAISE(ABORT, 'dummy-sensitive-detail'); END;";
        command.ExecuteNonQuery();
    }
    IOException error = ExpectStorageFailure(() => fixture.Store.SaveNotifiedCards(new()
    {
        ["primera"] = now.AddMinutes(1),
        ["rechazada"] = now.AddMinutes(2)
    }));
    Check(!error.ToString().Contains("dummy-sensitive-detail", StringComparison.Ordinal), "No propagar detalles del motor.");
    var history = new SettingsStore(fixture.DirectoryPath).LoadNotifiedCards();
    Check(history.Count == 1 && history["original|123"] == now, "Deshacer borrado e inserciones parciales.");
    return Task.CompletedTask;
});

await Run("Base bloqueada informa error y permite guardar al liberar el bloqueo", () =>
{
    using var fixture = new Fixture();
    AppSettings settings = fixture.Store.Load();
    settings.Theme = "Dark";
    using (SqliteConnection connection = fixture.OpenDatabase())
    using (SqliteTransaction transaction = connection.BeginTransaction())
    {
        IOException error = ExpectStorageFailure(() => fixture.Store.Save(settings));
        Check(error.Message.Contains("ocupada", StringComparison.Ordinal), "Informar bloqueo con espera acotada.");
    }
    Check(fixture.Store.Load().Theme == "Default", "Una escritura bloqueada no modifica preferencias.");
    fixture.Store.Save(settings);
    Check(fixture.Store.Load().Theme == "Dark", "Reintentar tras liberar el bloqueo.");
    return Task.CompletedTask;
});

await Run("Versión futura y base corrupta se conservan sin reinicialización", () =>
{
    using var fixture = new Fixture();
    using (SqliteConnection connection = fixture.OpenDatabase())
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version = 4;";
        command.ExecuteNonQuery();
    }
    ExpectStorageFailure(() => fixture.Store.Save(new AppSettings()));
    using (SqliteConnection connection = fixture.OpenDatabase())
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ApiBaseUrl FROM AppSettings WHERE Id = 1;";
        Check((string?)command.ExecuteScalar() == "https://mock.example.com", "No sobrescribir datos de una versión futura.");
    }

    using var corrupt = new Fixture(false);
    const string invalidDatabase = "Esto no es una base SQLite";
    File.WriteAllText(corrupt.DatabasePath, invalidDatabase);
    ExpectStorageFailure(() => corrupt.Store.Load());
    Check(File.ReadAllText(corrupt.DatabasePath) == invalidDatabase, "No borrar una base corrupta.");
    return Task.CompletedTask;
});

await Run("Primera apertura concurrente migra una sola vez", async () =>
{
    using var fixture = new Fixture(false);
    File.WriteAllText(Path.Combine(fixture.DirectoryPath, "notified-cards.json"), "[\"anterior|123\"]");
    var histories = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        new SettingsStore(fixture.DirectoryPath).LoadNotifiedCards())));
    Check(histories.All(history => history.Count == 1 && history["anterior|123"] == histories[0]["anterior|123"]),
        "Todas las instancias observan una única migración confirmada.");
});

await Run("Fallo de almacenamiento es visible en monitor manual y automático", async () =>
{
    using var fixture = new Fixture(false);
    File.WriteAllText(Path.Combine(fixture.DirectoryPath, "settings.json"), "{");
    using var monitor = fixture.CreateMonitor();
    var reported = new TaskCompletionSource<MonitorSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
    monitor.Updated += snapshot => reported.TrySetResult(snapshot);
    await monitor.CheckNowAsync();
    Check((await reported.Task).HasError && fixture.Notifications.Batches.Count == 0, "Comprobación manual informa fallo sin avisar.");
    reported = new TaskCompletionSource<MonitorSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
    monitor.Start();
    Check((await reported.Task.WaitAsync(TimeSpan.FromSeconds(10))).HasError, "El monitor automático informa el fallo de carga.");
});

await Run("Monitor agrupa avisos y evita duplicados concurrentes y tras reiniciar", async () =>
{
    using var fixture = new Fixture();
    fixture.Api.Cards = new[] { Card("uno", DateTimeOffset.UtcNow.AddMinutes(50)), Card("dos", DateTimeOffset.UtcNow.AddMinutes(55)) };
    using var monitor = fixture.CreateMonitor();
    await Task.WhenAll(monitor.CheckNowAsync(), monitor.CheckNowAsync());
    Check(fixture.Notifications.Batches.Count == 1 && fixture.Notifications.Batches[0].Count == 2, "Un único lote para dos tarjetas.");
    using var restarted = fixture.CreateMonitor();
    await restarted.CheckNowAsync();
    Check(fixture.Notifications.Batches.Count == 1, "Reiniciar no duplica avisos.");
    var history = fixture.Store.LoadNotifiedCards();
    history[ReminderSchedule.GetKey(fixture.Api.Cards[0])] = DateTimeOffset.UtcNow.AddMinutes(-31);
    fixture.Store.SaveNotifiedCards(history);
    await restarted.CheckNowAsync();
    Check(fixture.Notifications.Batches.Count == 2 && fixture.Notifications.Batches[1].Single().Id == "uno", "Repetir solo la tarjeta cuyo intervalo transcurrió.");
});

await Run("Desactivar avisos mantiene las tarjetas y el historial y permite reanudarlos", async () =>
{
    foreach (int repeatMinutes in new[] { 0, 30 })
    {
        using var fixture = new Fixture();
        AppSettings settings = fixture.Store.Load();
        settings.NotificationsEnabled = false;
        settings.IncludeOverdueCards = true;
        settings.RepeatReminderMinutes = repeatMinutes;
        fixture.Store.Save(settings);
        DateTimeOffset checkTime = DateTimeOffset.UtcNow;
        TrelloCard previous = Card("avisada", checkTime.AddMinutes(40));
        fixture.Api.Cards = new[] { previous, Card("nueva", checkTime.AddMinutes(50)), Card("vencida", checkTime.AddDays(-1)) };
        string previousKey = ReminderSchedule.GetKey(previous);
        DateTimeOffset lastNotified = checkTime.AddHours(-3);
        fixture.Store.SaveNotifiedCards(new() { [previousKey] = lastNotified });
        using var monitor = fixture.CreateMonitor();
        MonitorSnapshot? snapshot = null;
        monitor.Updated += value => snapshot = value;
        await Task.WhenAll(monitor.CheckNowAsync(), monitor.CheckNowAsync());
        Check(fixture.Notifications.Batches.Count == 0, "Omitir primeros avisos, repeticiones y resúmenes con la opción desactivada.");
        Check(snapshot is { IsRunning: true, HasError: false, AssignedCards.Count: 3, DueSoonCards.Count: 3 },
            "Seguir consultando y publicando las tarjetas sin avisos.");
        Check(snapshot!.Message.Contains("Notificaciones de escritorio desactivadas", StringComparison.Ordinal), "Informar la pausa de avisos.");
        var history = fixture.Store.LoadNotifiedCards();
        Check(history.Count == 1 && history[previousKey] == lastNotified, "No registrar avisos omitidos ni alterar su última fecha.");

        using var restarted = fixture.CreateMonitor();
        await restarted.CheckNowAsync();
        Check(fixture.Notifications.Batches.Count == 0, "Reiniciar mantiene los avisos desactivados.");
        settings.NotificationsEnabled = true;
        fixture.Store.Save(settings);
        await restarted.CheckNowAsync();
        Check(fixture.Notifications.Batches.Single().Count == (repeatMinutes == 0 ? 2 : 3),
            "Reanudar avisos elegibles respetando Nunca y el historial anterior.");
        await restarted.CheckNowAsync();
        Check(fixture.Notifications.Batches.Count == 1, "Reactivar no produce duplicados en la siguiente comprobación.");
    }
});

await Run("Desactivar durante una consulta en curso impide enviar el aviso al recibir la respuesta", async () =>
{
    using var fixture = new Fixture();
    fixture.Api.Cards = new[] { Card("en-curso", DateTimeOffset.UtcNow.AddMinutes(50)) };
    fixture.Api.BeforeResponse = () =>
    {
        AppSettings settings = fixture.Store.Load();
        settings.NotificationsEnabled = false;
        fixture.Store.Save(settings);
    };
    using var monitor = fixture.CreateMonitor();
    MonitorSnapshot? snapshot = null;
    monitor.Updated += value => snapshot = value;
    await monitor.CheckNowAsync();
    Check(snapshot is { HasError: false, AssignedCards.Count: 1 }, "Completar la consulta en curso.");
    Check(fixture.Notifications.Batches.Count == 0 && fixture.Store.LoadNotifiedCards().Count == 0,
        "Respetar la preferencia recién guardada sin consumir el primer aviso.");
});

await Run("Un aviso rechazado no consume el intervalo de repetición", async () =>
{
    using var fixture = new Fixture();
    fixture.Api.Cards = new[] { Card("uno", DateTimeOffset.UtcNow.AddMinutes(50)) };
    fixture.Notifications.Succeed = false;
    using var monitor = fixture.CreateMonitor();
    await monitor.CheckNowAsync();
    Check(fixture.Store.LoadNotifiedCards().Count == 0, "No guardar un aviso fallido.");
    fixture.Notifications.Succeed = true;
    await monitor.CheckNowAsync();
    Check(fixture.Notifications.Batches.Count == 2 && fixture.Store.LoadNotifiedCards().Count == 1, "Reintentar en la siguiente comprobación.");
});

await Run("Monitor publica todas las asignadas sin ampliar los avisos", async () =>
{
    using var fixture = new Fixture();
    AppSettings settings = fixture.Store.Load();
    settings.NotifyBeforeMinutes = 120;
    fixture.Store.Save(settings);
    DateTimeOffset checkTime = DateTimeOffset.UtcNow;
    fixture.Api.Cards = new[]
    {
        Card("vencida", checkTime.AddDays(-1)), Card("proxima", checkTime.AddMinutes(90)),
        Card("futura", checkTime.AddDays(1)), Card("sin-fecha", null),
        Card("completada", checkTime.AddMinutes(30), true)
    };
    using var monitor = fixture.CreateMonitor();
    MonitorSnapshot? snapshot = null;
    monitor.Updated += value => snapshot = value;
    await monitor.CheckNowAsync();
    Check(snapshot is { HasError: false, NotifyBeforeMinutes: 120, AssignedCards.Count: 5 }, "Publicar todas las tarjetas y la anticipación usada.");
    Check(snapshot!.AssignedCards.Select(c => c.Id).SequenceEqual(fixture.Api.Cards.Select(c => c.Id)), "El snapshot conserva todas las abiertas para filtrar localmente.");
    Check(snapshot.GetCards(TrelloCardScope.Pending).Count() == 4, "El dashboard excluye completadas del snapshot.");
    Check(snapshot.DueSoonCards.Single().Id == "proxima", "Separar candidatas de consulta completa.");
    Check(fixture.Notifications.Batches.Single().Single().Id == "proxima", "Solo avisar de la próxima pendiente.");

    fixture.Api.Fail = true;
    await monitor.CheckNowAsync();
    Check(snapshot is { HasError: true, AssignedCards.Count: 0 }, "No presentar resultados anteriores como actuales tras un fallo.");
    fixture.Api.Fail = false;
    fixture.Api.Cards = Array.Empty<TrelloCard>();
    await monitor.CheckNowAsync();
    Check(snapshot is { HasError: false, IsRunning: true, AssignedCards.Count: 0 }, "Una consulta vacía es válida y retira las tarjetas anteriores.");
    Check(fixture.Notifications.Batches.Count == 1, "Fallo y lista vacía no generan más avisos.");
});

await Run("Activar vencidas cambia avisos pero no las tarjetas consultables", async () =>
{
    using var fixture = new Fixture();
    TrelloCard card = Card("vencida", DateTimeOffset.UtcNow.AddDays(-1));
    fixture.Api.Cards = new[] { card };
    using var monitor = fixture.CreateMonitor();
    MonitorSnapshot? snapshot = null;
    monitor.Updated += value => snapshot = value;
    await monitor.CheckNowAsync();
    Check(fixture.Notifications.Batches.Count == 0 && snapshot?.DueSoonCards.Count == 0, "Vencidas desactivadas por defecto.");
    Check(snapshot?.AssignedCards.Single().Id == "vencida", "Vencida visible aunque sus avisos estén desactivados.");
    AppSettings settings = fixture.Store.Load();
    settings.IncludeOverdueCards = true;
    fixture.Store.Save(settings);
    await monitor.CheckNowAsync();
    Check(fixture.Notifications.Batches.Count == 1 && snapshot?.DueSoonCards.Single().Id == "vencida", "Primer aviso y presencia en la lista.");
    fixture.Store.SaveNotifiedCards(new() { [ReminderSchedule.GetKey(card)] = DateTimeOffset.UtcNow.AddMinutes(-121) });
    await monitor.CheckNowAsync();
    Check(fixture.Notifications.Batches.Count == 2, "Repetición de vencidas después de dos horas.");
    settings.IncludeOverdueCards = false;
    fixture.Store.Save(settings);
    await monitor.CheckNowAsync();
    Check(fixture.Notifications.Batches.Count == 2 && snapshot?.DueSoonCards.Count == 0, "Desactivar retira vencidas de las candidatas a avisos.");
    Check(snapshot?.AssignedCards.Single().Id == "vencida", "Desactivar avisos mantiene la vencida consultable.");
});

await Run("Fallo de API conserva historial y no emite notificaciones", async () =>
{
    using var fixture = new Fixture();
    fixture.Store.SaveNotifiedCards(new() { ["existente|123"] = now });
    fixture.Api.Fail = true;
    using var monitor = fixture.CreateMonitor();
    MonitorSnapshot? snapshot = null;
    monitor.Updated += value => snapshot = value;
    await monitor.CheckNowAsync();
    Check(snapshot?.HasError == true, "Informar fallo.");
    Check(snapshot?.Message == "No se pudo conectar con Trello. Revisa tu conexión a Internet y vuelve a intentarlo.",
        "Mostrar un mensaje de conexión accionable en lugar del detalle técnico del transporte.");
    Check(fixture.Store.LoadNotifiedCards()["existente|123"] == now && fixture.Notifications.Batches.Count == 0, "No alterar historial ante fallo.");
});

await Run("Timeout muestra un aviso comprensible y permite recuperarse sin alterar el historial", async () =>
{
    using var fixture = new Fixture();
    fixture.Store.SaveNotifiedCards(new() { ["existente|123"] = now });
    fixture.Api.BeforeResponse = () => throw new TaskCanceledException("Detalle técnico del timeout HTTP");
    using var monitor = fixture.CreateMonitor();
    MonitorSnapshot? snapshot = null;
    monitor.Updated += value => snapshot = value;

    await monitor.CheckNowAsync();

    Check(snapshot is { HasError: true, Title: "Tiempo de espera agotado", AssignedCards.Count: 0 },
        "El timeout muestra un título en español y no presenta tarjetas obsoletas.");
    Check(snapshot?.Message == "El servidor tardó demasiado en responder. Inténtalo nuevamente en unos momentos.",
        "Mostrar el mensaje solicitado sin detalles técnicos.");
    Check(fixture.Store.LoadNotifiedCards()["existente|123"] == now && fixture.Notifications.Batches.Count == 0,
        "El timeout no altera el historial ni envía avisos.");

    fixture.Api.BeforeResponse = null;
    await monitor.CheckNowAsync();
    Check(snapshot is { HasError: false, Title: "" }, "Una consulta correcta retira el título y el error de timeout.");
});

await Run("Completadas, archivadas o ausentes dejan de avisar y un nuevo vencimiento se reevalúa", async () =>
{
    using var fixture = new Fixture();
    TrelloCard completed = Card("completada", DateTimeOffset.UtcNow.AddMinutes(40));
    TrelloCard removed = Card("ausente", DateTimeOffset.UtcNow.AddMinutes(45));
    TrelloCard changed = Card("cambiada", DateTimeOffset.UtcNow.AddMinutes(50));
    TrelloCard archived = Card("archivada", DateTimeOffset.UtcNow.AddMinutes(35));
    fixture.Api.Cards = new[] { completed, removed, changed, archived };
    using var monitor = fixture.CreateMonitor();
    await monitor.CheckNowAsync();
    completed.DueComplete = true;
    archived.Closed = true;
    changed.Due = changed.Due!.Value.AddMinutes(1);
    fixture.Api.Cards = new[] { completed, changed, archived };
    await monitor.CheckNowAsync();
    Check(fixture.Notifications.Batches[1].Single().Id == "cambiada", "Solo avisar del nuevo vencimiento.");
    Check(fixture.Store.LoadNotifiedCards().Count == 1, "Retirar claves completadas, archivadas, ausentes y del vencimiento anterior.");
});

await Run("Logs: escribe el formato compacto y los niveles en el archivo", () =>
{
    using var fixture = new Fixture(initialize: false);
    string directory = Path.Combine(fixture.DirectoryPath, "Logs");
    AppLog.Initialize(directory, debug: true);
    try
    {
        Log.ForContext("SourceContext", "ComponenteFicticio").Debug("Detalle de prueba");
        Log.Information("POST /cards -> 200 (12 ms)");
        Log.Warning("Advertencia de prueba");
        Log.Error("Error de prueba");
        Log.Fatal("Error fatal de prueba");
    }
    finally
    {
        Log.CloseAndFlush();
    }
    string[] files = Directory.GetFiles(directory, "trello-notifier*.log");
    Check(files.Length == 1, "Escribir el archivo de log en el destino configurado.");
    string[] lines = File.ReadAllLines(files[0]);
    Check(lines.Length == 5 && new[] { "DEBUG", "INFO", "WARN", "ERROR", "FATAL" }.Select((level, index) => lines[index].Contains($"[{level}]")).All(found => found),
        "Escribir los nombres completos de los niveles y conservar el orden cronológico en el archivo.");
    Check(lines[0].Contains("ComponenteFicticio: Detalle de prueba"), "Conservar el componente del evento.");
    return Task.CompletedTask;
});

await Run("Logs: reutiliza el archivo entre arranques y rota por tamaño conservando tres", () =>
{
    using var fixture = new Fixture(initialize: false);
    string currentPath = Path.Combine(fixture.DirectoryPath, AppLog.FileName);
    WriteSession("Primer arranque ficticio");
    File.SetLastWriteTimeUtc(currentPath, DateTime.UtcNow.AddDays(-2));
    WriteSession("Segundo arranque ficticio");
    Check(Directory.GetFiles(fixture.DirectoryPath, "*.log").Length == 1 &&
        File.ReadAllText(currentPath).Contains("Primer arranque ficticio") && File.ReadAllText(currentPath).Contains("Segundo arranque ficticio"),
        "Reutilizar el archivo existente sin rotar al reiniciar ni por su fecha.");

    for (int sequence = 1; sequence <= 4; sequence++)
    {
        // Archivo ficticio lleno: comprobar el límite real de producción sin generar miles de eventos.
        using (var stream = new FileStream(currentPath, FileMode.Open, FileAccess.Write)) stream.SetLength(5 * 1024 * 1024);
        WriteSession($"Rotación ficticia {sequence}");
        currentPath = Path.Combine(fixture.DirectoryPath, $"trello-notifier_{sequence:000}.log");
        Check(File.Exists(currentPath) && File.ReadAllText(currentPath).Contains($"Rotación ficticia {sequence}"),
            "Escribir el siguiente evento en un archivo nuevo cuando el anterior llegó a 5 MiB.");
        string[] expected = Enumerable.Range(Math.Max(0, sequence - 2), Math.Min(sequence + 1, 3))
            .Select(index => index == 0 ? AppLog.FileName : $"trello-notifier_{index:000}.log").ToArray();
        Check(Directory.GetFiles(fixture.DirectoryPath, "*.log").Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(expected),
            "Conservar solo los tres archivos más recientes, incluido el activo, y eliminar el más antiguo.");
    }
    Check(!AppLog.HasWriteFailure, "Rotar sin errores ni pérdidas del evento que abre el siguiente archivo.");
    return Task.CompletedTask;

    void WriteSession(string message)
    {
        AppLog.Initialize(fixture.DirectoryPath);
        try { Log.Information("{TestMessage}", message); }
        finally { Log.CloseAndFlush(); }
    }
});

await Run("Logs: errores sin secretos y vaciado de la cola al cerrar", () =>
{
    using var fixture = new Fixture(initialize: false);
    string logsPath = Path.Combine(fixture.DirectoryPath, "Logs");
    AppLog.Initialize(logsPath);
    try
    {
        using IDisposable context = LogContext.PushProperty("CheckId", "comprobacion-ficticia");
        try
        {
            var error = new HttpRequestException("https://mock.invalid/?key=CLAVE_FICTICIA&token=TOKEN_FICTICIO",
                new IOException("TARJETA_PRIVADA_FICTICIA"));
            error.Data["settings"] = "CONFIGURACION_PRIVADA_FICTICIA";
            throw error;
        }
        catch (HttpRequestException ex)
        {
            AppLog.WriteFailure("TestSafeFailure", ex);
        }
        Log.Debug("DETALLE_SOLO_DEBUG");
        for (int i = 0; i < 40; i++) Log.ForContext<SettingsStore>().Information("Evento de prueba {Sequence}", i);
    }
    finally
    {
        Log.CloseAndFlush();
    }
    string[] lines = Directory.GetFiles(logsPath, "*.log").SelectMany(File.ReadAllLines).ToArray();
    Check(lines.Count(line => line.Contains("[INFO]")) == 40 && lines.Count(line => line.Contains("[ERROR]")) == 1,
        "El cierre vacía los eventos pendientes y filtra Debug por defecto.");
    string text = string.Join('\n', lines);
    foreach (string secret in new[] { "CLAVE_FICTICIA", "TOKEN_FICTICIO", "TARJETA_PRIVADA_FICTICIA", "CONFIGURACION_PRIVADA_FICTICIA", "mock.invalid", "DETALLE_SOLO_DEBUG" })
    {
        Check(!text.Contains(secret, StringComparison.Ordinal), "Excluir mensajes, datos, URL e interiores de excepciones.");
    }
    Match header = Regex.Match(lines[0], @"^(?<timestamp>\d{4}-\d{2}-\d{2} \S+ [+-]\d{2}:\d{2}) \[ERROR\] \[s:[a-f0-9]{8} c:comproba\] Program: ",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    Check(header.Success && DateTimeOffset.TryParse(header.Groups["timestamp"].Value, out _), "Cabecera compacta con fecha, nivel y componente.");
    Check(lines[0].Contains($"tipo={typeof(HttpRequestException).FullName}"), "Conservar el tipo técnico legible.");
    Check(lines[0].Contains(" c:comproba]"), "Propagar correlación abreviada.");
    Check(Regex.IsMatch(lines[0], @"\[s:[a-f0-9]{8} c:", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) &&
        !AppLog.HasWriteFailure, "Identificar sesión sin fallos del logger.");
    Check(text.Contains("System.IO.IOException") && !text.Contains("métodos="), "Mostrar interiores saneados sin la lista manual de métodos.");
    string firstInfo = lines.First(line => line.Contains("[INFO]"));
    Check(!firstInfo.Contains("TrelloNotifier.Services.SettingsStore") &&
        firstInfo.Contains("SettingsStore: Evento de prueba 0"), "Mostrar nombre corto y mensaje sin JSON.");
    return Task.CompletedTask;
});

await Run("Logs: modo diagnóstico y timeout como advertencia", () =>
{
    using var fixture = new Fixture(initialize: false);
    AppLog.Initialize(fixture.DirectoryPath, debug: true);
    try
    {
        Log.Debug("Detalle de diagnóstico");
        AppLog.WriteFailure("TestTimeout", new TaskCanceledException("URL_PRIVADA_FICTICIA"));
    }
    finally
    {
        Log.CloseAndFlush();
    }
    string[] lines = Directory.GetFiles(fixture.DirectoryPath, "*.log").SelectMany(File.ReadAllLines).ToArray();
    Check(lines.Count(line => line.Contains("[DEBUG]")) == 1 && lines.Count(line => line.Contains("[WARN]")) == 1,
        "Debug habilitado explícitamente.");
    Check(lines[0].Contains("[DEBUG]"), "Conservar detalle diagnóstico.");
    Check(lines[1].Contains("[WARN]"), "Timeout recuperable como advertencia.");
    Check(lines.All(line => !line.Contains("URL_PRIVADA_FICTICIA")), "No filtrar datos en timeout.");
    return Task.CompletedTask;
});

await Run("Logs: excepciones nativas saneadas conservan trazas e interiores y corrigen contexto XAML", async () =>
{
    var sink = new RecordingLogSink();
    Log.Logger = new LoggerConfiguration().Enrich.With(new CompactLogEnricher()).WriteTo.Sink(sink).CreateLogger();
    Exception? original = null;
    try
    {
        try { await ThrowOuterAsync(); }
        catch (Exception ex)
        {
            original = ex;
            AppLog.WriteFailure("UnhandledUI", ex, Serilog.Events.LogEventLevel.Fatal, @"C:\PERFIL_PRIVADO\App.xaml.cs");
            AppLog.WriteFailure("UnhandledProcess", new AggregateException("AGREGADO_PRIVADO", ex, new IOException("OTRO_PRIVADO")),
                sourcePath: @"C:\PERFIL_PRIVADO\SettingsPage.xaml.cs");
            AppLog.WriteFailure("UnknownCaller", ex, sourcePath: "");
        }
    }
    finally { Log.CloseAndFlush(); }
    var events = sink.Events.ToArray();
    Check(events.Length == 3 && events[0].Level == Serilog.Events.LogEventLevel.Fatal, "Un evento por fallo con nivel original.");
    Check(((Serilog.Events.ScalarValue)events[0].Properties["ShortSourceContext"]).Value as string == "App" &&
        ((Serilog.Events.ScalarValue)events[1].Properties["ShortSourceContext"]).Value as string == "SettingsPage" &&
        ((Serilog.Events.ScalarValue)events[2].Properties["SourceContext"]).Value as string == "UnhandledException",
        "Identificar App, páginas XAML y el contexto desconocido sin mostrar xaml.");
    Exception safe = events[0].Exception!;
    Check(safe is not null && !ReferenceEquals(safe, original) && safe.InnerException is not null &&
        !ReferenceEquals(safe.InnerException, original!.InnerException), "Entregar a Serilog una cadena independiente de Exception, no texto preformateado.");
    string rendered = safe!.ToString();
    Check(rendered.Contains("System.InvalidOperationException") && rendered.Contains("System.IO.IOException") &&
        rendered.Contains("ThrowOuterAsync") && rendered.Contains("ThrowInnerAsync") &&
        rendered.Contains(new System.Diagnostics.StackTrace(original!, false).ToString()),
        "Conservar tipos y trazas async formateadas por el runtime.");
    Check(events[1].Exception is AggregateException { InnerExceptions.Count: 2 }, "Conservar todas las ramas de AggregateException.");
    foreach (var item in events)
    {
        string text = item.RenderMessage() + item.Exception;
        foreach (string secret in new[] { "MENSAJE_PRIVADO", "INTERIOR_PRIVADO", "DATO_PRIVADO", "AGREGADO_PRIVADO", "OTRO_PRIVADO", "PERFIL_PRIVADO", ".cs:line", "métodos=" })
            Check(!text.Contains(secret, StringComparison.Ordinal), "Excluir mensajes, datos, rutas y formateo manual.");
        Check(item.Exception!.Data.Count == 0, "No copiar Data a la excepción saneada.");
    }
    Check(original!.Message == "MENSAJE_PRIVADO" && original.Data.Count == 1, "No modificar la excepción usada por el flujo de la aplicación.");

    static async Task ThrowInnerAsync()
    {
        await Task.Yield();
        throw new IOException("INTERIOR_PRIVADO");
    }

    static async Task ThrowOuterAsync()
    {
        try { await ThrowInnerAsync(); }
        catch (IOException ex)
        {
            var outer = new InvalidOperationException("MENSAJE_PRIVADO", ex);
            outer.Data["private"] = "DATO_PRIVADO";
            throw outer;
        }
    }
});

await Run("Logs: destino inaccesible no impide operar y permite recuperación", () =>
{
    using var fixture = new Fixture(initialize: false);
    string blockedPath = Path.Combine(fixture.DirectoryPath, "archivo");
    File.WriteAllText(blockedPath, "bloqueo ficticio");
    AppLog.Initialize(Path.Combine(blockedPath, "Logs"));
    try
    {
        Check(AppLog.HasWriteFailure, "El fallo del destino queda observable.");
        fixture.Store.Save(new AppSettings { ApiBaseUrl = "https://mock.invalid" });
        Check(fixture.Store.Load().ApiBaseUrl == "https://mock.invalid", "Logging no interrumpe persistencia.");
    }
    finally
    {
        Log.CloseAndFlush();
    }
    AppLog.Initialize(Path.Combine(fixture.DirectoryPath, "Logs"));
    try
    {
        Log.Information("Destino recuperado");
    }
    finally
    {
        Log.CloseAndFlush();
    }
    Check(!AppLog.HasWriteFailure, "Un destino válido permite recuperar el diagnóstico.");
    return Task.CompletedTask;
});

await Run("Logs: monitor correlaciona fallo y recuperación sin datos de tarjetas", async () =>
{
    using var fixture = new Fixture();
    string logsPath = Path.Combine(fixture.DirectoryPath, "Logs");
    using var monitor = fixture.CreateMonitor();
    AppLog.Initialize(logsPath, debug: true);
    try
    {
        fixture.Api.Fail = true;
        await monitor.CheckNowAsync();
        fixture.Api.Fail = false;
        fixture.Api.Cards = new[] { Card("TARJETA_SECRETA_FICTICIA", DateTimeOffset.UtcNow.AddMinutes(10)) };
        await monitor.CheckNowAsync();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await monitor.CheckNowAsync(cancelled.Token);
        }
        catch (OperationCanceledException)
        {
            // La cancelación solicitada no produce un fallo de diagnóstico.
        }
    }
    finally
    {
        Log.CloseAndFlush();
    }
    string[] lines = Directory.GetFiles(logsPath, "*.log").SelectMany(File.ReadAllLines).ToArray();
    int errorCount = 0;
    var checks = new Dictionary<string, string>();
    string? failedCheckId = null;
    foreach (string line in lines)
    {
        Check(!line.Contains("TARJETA_SECRETA_FICTICIA") && !line.Contains("Fallo de conexión simulado"), "Excluir contenido privado del flujo real.");
        Match correlation = Regex.Match(line, @" c:(?<id>[a-f0-9]{8})\]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (line.Contains("[ERROR]"))
        {
            errorCount++;
            failedCheckId = correlation.Groups["id"].Value;
        }
        Match outcome = Regex.Match(line, @"Comprobación finalizada: (?<outcome>\w+);", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (outcome.Success)
        {
            Check(correlation.Success, "Conservar CheckId en la salida de texto.");
            checks.Add(correlation.Groups["id"].Value, outcome.Groups["outcome"].Value);
        }
    }
    Check(errorCount == 1 && checks.Count == 2, "Un registro por fallo, dos comprobaciones y ninguna falsa alarma por cancelación.");
    Check(checks[failedCheckId!] == "Failed" && checks.Values.Contains("Succeeded"), "Correlación y recuperación observables.");
});

await Run("Logs: contexto estructurado conserva valores completos y aísla comprobaciones paralelas", async () =>
{
    var sink = new RecordingLogSink();
    using var logger = new LoggerConfiguration().Enrich.FromLogContext().Enrich.With(new CompactLogEnricher())
        .WriteTo.Sink(sink).CreateLogger();
    const string session = "45120f6a0123456789abcdef0123456789";
    using (LogContext.PushProperty("SessionId", session))
    {
        logger.ForContext<DueCardMonitor>().Information("Monitor iniciado");
        await Task.WhenAll(new[] { "9b996914aaaaaaaaaaaaaaaaaaaaaaaa", "abcdef01bbbbbbbbbbbbbbbbbbbbbbbb" }.Select(id => Task.Run(async () =>
        {
            using (LogContext.PushProperty("CheckId", id))
            {
                await Task.Yield();
                logger.ForContext<SettingsStore>().Information("Consulta terminada");
            }
        })));
        logger.Information("Sin comprobación");
    }
    var events = sink.Events.ToArray();
    Check(events.All(item => ((Serilog.Events.ScalarValue)item.Properties["SessionId"]).Value as string == session), "Conservar el SessionId completo a través de await y Task.Run.");
    Check(((Serilog.Events.ScalarValue)events[0].Properties["SourceContext"]).Value as string == "TrelloNotifier.Services.DueCardMonitor" &&
        ((Serilog.Events.ScalarValue)events[0].Properties["ShortSourceContext"]).Value as string == "DueCardMonitor", "Acortar solo la representación, sin alterar SourceContext.");
    Check(!events[0].Properties.ContainsKey("CheckId") && !events[3].Properties.ContainsKey("CheckId") &&
        ((Serilog.Events.ScalarValue)events[0].Properties["Correlation"]).Value as string == "s:45120f6a", "Omitir c: fuera del ciclo y restaurar los scopes.");
    Check(events.Skip(1).Take(2).Select(item => ((Serilog.Events.ScalarValue)item.Properties["CheckId"]).Value).Distinct().Count() == 2,
        "No mezclar identificadores entre tareas paralelas.");
});

await Run("Logs: monitor cuenta avisos agrupados, cooldown, deduplicación, desactivación y rechazo", async () =>
{
    using var fixture = new Fixture();
    using var monitor = fixture.CreateMonitor();
    var sink = new RecordingLogSink();
    Log.Logger = new LoggerConfiguration().MinimumLevel.Debug().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();
    try
    {
        TrelloCard card = Card("privada-ficticia", DateTimeOffset.UtcNow.AddMinutes(10));
        fixture.Api.Cards = new[] { card, card, Card("otra-ficticia", card.Due!.Value) };
        await monitor.CheckNowAsync();
        await monitor.CheckNowAsync();
        AppSettings settings = fixture.Store.Load();
        settings.RepeatReminderMinutes = 0;
        fixture.Store.Save(settings);
        await monitor.CheckNowAsync();
        settings.NotificationsEnabled = false;
        fixture.Store.Save(settings);
        await monitor.CheckNowAsync();
        settings.NotificationsEnabled = true;
        fixture.Store.Save(settings);
        fixture.Store.SaveNotifiedCards(new());
        fixture.Notifications.Succeed = false;
        await monitor.CheckNowAsync();
    }
    finally { Log.CloseAndFlush(); }
    var summaries = sink.Events.Where(item => item.Properties.ContainsKey("EvaluatedCount")).ToArray();
    int Count(int index, string key) => (int)((Serilog.Events.ScalarValue)summaries[index].Properties[key]).Value!;
    Check(summaries.Length == 5 && Count(0, "NotificationCount") == 1 && Count(0, "NotifiedCardCount") == 2 && Count(0, "DeduplicatedCount") == 1,
        "Una notificación agrupada para dos tarjetas distintas, sin duplicar la fila repetida.");
    Check(Count(1, "NotificationCount") == 0 && Count(1, "CooldownCount") == 2, "No confundir candidatas con enviadas durante cooldown.");
    Check(Count(2, "DeduplicatedCount") == 3 && Count(2, "CooldownCount") == 0, "Nunca repetir es deduplicación, no cooldown temporal.");
    Check(Count(3, "DisabledCount") == 2 && Count(3, "NotificationCount") == 0, "Identificar preferencias desactivadas.");
    Check(Count(4, "RejectedCount") == 2 && Count(4, "NotificationCount") == 0 && summaries[4].Level == Serilog.Events.LogEventLevel.Warning,
        "Un rechazo no se cuenta como enviado ni consume historial.");
    Check(fixture.Store.LoadNotifiedCards().Count == 0, "Conservar el reintento ante rechazo.");
});

await Run("Logs: ciclos sin cambios quedan en Debug y los cambios y recuperación son Information", async () =>
{
    using var fixture = new Fixture();
    using var monitor = fixture.CreateMonitor();
    var sink = new RecordingLogSink();
    Log.Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
    try
    {
        await monitor.CheckNowAsync();
        await monitor.CheckNowAsync();
        fixture.Api.Cards = new[] { Card("futura", DateTimeOffset.UtcNow.AddDays(5)) };
        await monitor.CheckNowAsync();
        fixture.Api.Fail = true;
        await monitor.CheckNowAsync();
        fixture.Api.Fail = false;
        await monitor.CheckNowAsync();
    }
    finally { Log.CloseAndFlush(); }
    var levels = sink.Events.Where(item => item.Properties.ContainsKey("EvaluatedCount")).Select(item => item.Level).ToArray();
    Check(levels.SequenceEqual(new[] { Serilog.Events.LogEventLevel.Information, Serilog.Events.LogEventLevel.Debug,
        Serilog.Events.LogEventLevel.Information, Serilog.Events.LogEventLevel.Information }), "Informar primer estado, cambios y recuperación; reducir rutina.");
});

await Run("Logs: cooldown repetido queda en Debug y cambios de conjunto o conteos vuelven a Information", async () =>
{
    using var fixture = new Fixture();
    using var monitor = fixture.CreateMonitor();
    var sink = new RecordingLogSink();
    Log.Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
    try
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TrelloCard overdue = Card("vencida-privada", now.AddDays(-1));
        TrelloCard upcoming = Card("proxima-privada", now.AddMinutes(10));
        fixture.Api.Cards = new[] { overdue, upcoming };
        fixture.Store.SaveNotifiedCards(new()
        {
            [ReminderSchedule.GetKey(overdue)] = now,
            [ReminderSchedule.GetKey(upcoming)] = now
        });
        AppSettings settings = fixture.Store.Load();
        settings.IncludeOverdueCards = true;
        settings.NotifyBeforeMinutes = 5;
        fixture.Store.Save(settings);
        await monitor.CheckNowAsync(); // Primera candidata: vencida, en cooldown.
        await monitor.CheckNowAsync(); // Mismo estado.
        settings.IncludeOverdueCards = false;
        settings.NotifyBeforeMinutes = 15;
        fixture.Store.Save(settings);
        await monitor.CheckNowAsync(); // Mismos conteos, otra tarjeta en cooldown.
        await monitor.CheckNowAsync();
        fixture.Api.Cards = new[] { upcoming, overdue };
        await monitor.CheckNowAsync(); // Cambiar el orden no cambia el conjunto.
        fixture.Store.SaveNotifiedCards(new());
        await monitor.CheckNowAsync(); // Aviso enviado.
        await monitor.CheckNowAsync(); // Transición a cooldown.
        await monitor.CheckNowAsync(); // Repetición en cooldown.
        fixture.Api.Cards = new[] { upcoming, overdue, upcoming };
        await monitor.CheckNowAsync(); // Cambian conteos aunque no cambie el conjunto de tarjetas.
        await monitor.CheckNowAsync();
        settings.NotificationsEnabled = false;
        fixture.Store.Save(settings);
        await monitor.CheckNowAsync();
        await monitor.CheckNowAsync();
    }
    finally { Log.CloseAndFlush(); }
    var summaries = sink.Events.Where(item => item.Properties.ContainsKey("EvaluatedCount")).ToArray();
    Check(string.Join(",", summaries.Select(item => item.Level)) ==
        "Information,Debug,Information,Debug,Debug,Information,Information,Debug,Information,Debug,Information,Debug",
        "Informar envíos y transiciones; reducir repeticiones incluso con candidatas, cooldown o avisos desactivados.");
    Check(((Serilog.Events.ScalarValue)summaries[0].Properties["CandidateCount"]).Value!.Equals(1) &&
        ((Serilog.Events.ScalarValue)summaries[2].Properties["CandidateCount"]).Value!.Equals(1), "Detectar el cambio de conjunto aunque el conteo de candidatas sea idéntico.");
    Check(fixture.Notifications.Batches.Count == 1 && summaries.All(item => !item.RenderMessage().Contains("privada")),
        "La comparación no cambia los envíos ni escribe identificadores privados.");
});

await Run("Logs: parada espera la comprobación manual antes de vaciar el archivo", async () =>
{
    using var fixture = new Fixture();
    using var monitor = fixture.CreateMonitor();
    using var release = new ManualResetEventSlim();
    var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.Api.BeforeResponse = () => { entered.TrySetResult(true); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); };
    fixture.Api.Cards = new[] { Card("pendiente-ficticia", DateTimeOffset.UtcNow.AddMinutes(10)) };
    string directory = Path.Combine(fixture.DirectoryPath, "Logs");
    AppLog.Initialize(directory, debug: true);
    try
    {
        Task check = monitor.CheckNowAsync();
        await entered.Task;
        Task stop = monitor.StopAsync();
        Check(!stop.IsCompleted, "Esperar al productor antes de cerrar Serilog.");
        release.Set();
        await Task.WhenAll(check, stop);
        await monitor.CheckNowAsync();
        Check(fixture.Notifications.Batches.Count == 0, "No emitir nuevos avisos durante el cierre.");
        Log.Information("Cierre ficticio completado");
    }
    finally { release.Set(); Log.CloseAndFlush(); }
    string[] lines = Directory.GetFiles(directory, "*.log").SelectMany(File.ReadAllLines).ToArray();
    Check(lines.Last().EndsWith("Cierre ficticio completado", StringComparison.Ordinal), "El marcador de cierre queda después de los eventos del monitor.");
});

await Run("Arquitectura: modelos independientes de servicios, UI e IO", () =>
{
    ArchitectureTests.ModelsAreIndependent();
    return Task.CompletedTask;
});

await Run("Arquitectura: calendario depende solo de modelos y BCL permitida", () =>
{
    ArchitectureTests.ScheduleDependsOnlyOnModels();
    return Task.CompletedTask;
});

await Run("Arquitectura: guard rechaza dependencias prohibidas y aliases", () =>
{
    ArchitectureTests.GuardRejectsForbiddenDependencies();
    return Task.CompletedTask;
});

Console.WriteLine($"{passed} pruebas correctas.");

async Task Run(string name, Func<Task> test)
{
    await test();
    passed++;
    Console.WriteLine($"OK: {name}");
}

static TrelloCard Card(string id, DateTimeOffset? due, bool complete = false) =>
    new() { Id = id, Name = id, Due = due, DueComplete = complete, Url = "https://trello.com/" };

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static IOException ExpectStorageFailure(Action action)
{
    try
    {
        action();
    }
    catch (IOException ex)
    {
        return ex;
    }
    throw new InvalidOperationException("Se esperaba un error de almacenamiento observable.");
}

sealed class Fixture : IDisposable
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "trello-reminder-tests-" + Guid.NewGuid());
    public string DatabasePath => Path.Combine(DirectoryPath, "trello-notifier.db");
    public SettingsStore Store { get; }
    public TrelloApiClient Api { get; } = new();
    public DesktopNotificationService Notifications { get; } = new();

    public Fixture(bool initialize = true)
    {
        Directory.CreateDirectory(DirectoryPath);
        Store = new SettingsStore(DirectoryPath);
        if (initialize) Store.Save(new AppSettings { ApiBaseUrl = "https://mock.example.com" });
    }

    public SqliteConnection OpenDatabase()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    public DueCardMonitor CreateMonitor() => new(Store, Api, Notifications);
    public void Dispose() => Directory.Delete(DirectoryPath, true);
}
