using System.Diagnostics;
using System.Runtime.CompilerServices;
using Serilog;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;

namespace TrelloNotifier.Services;

internal static class AppLog
{
    private static int _hasWriteFailure;

    public static bool HasWriteFailure => Volatile.Read(ref _hasWriteFailure) != 0;

    public static void Initialize(string? directoryPath = null, bool debug = false)
    {
        Interlocked.Exchange(ref _hasWriteFailure, 0);
        // SelfLog puede contener rutas y excepciones: nunca copiar su texto.
        SelfLog.Enable(_ => ReportWriteFailure());
        try
        {
            directoryPath ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Trello Notifier", "Logs");
            Directory.CreateDirectory(directoryPath);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(debug ? LogEventLevel.Debug : LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.With(new LogLevelEnricher())
                .Enrich.WithProperty("SessionId", Guid.NewGuid().ToString("N"))
                .Enrich.WithProperty("ProcessId", Environment.ProcessId)
                .Enrich.WithProperty("SourceContext", "TrelloNotifier")
                .WriteTo.Async(sink => sink.File(
                    Path.Combine(directoryPath, "trello-notifier-.log"),
                    outputTemplate: "{Timestamp:yyyy-MM-ddTHH:mm:ss.fffzzz} {LevelLabel,5:l} {ProcessId} --- [TrelloNotifier] {SourceContext,-48:l} : {Message:l} [session={SessionId} check={CheckId}]{NewLine}",
                    formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 5 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 14,
                    shared: true), bufferSize: 1000, blockWhenFull: false)
                .CreateLogger();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // El diagnóstico no debe impedir el arranque si el disco no está disponible.
            ReportWriteFailure();
        }
    }

    public static void WriteFailure(string operation, Exception exception, LogEventLevel? level = null,
        [CallerFilePath] string sourcePath = "")
    {
        // Lista permitida: no serializar Exception, Message, Data, rutas ni excepciones internas.
        // Los métodos sin nombres de archivo permiten localizar el fallo sin revelar el perfil local.
        string[] frames = new StackTrace(exception, false).GetFrames()
            .Select(frame => frame.GetMethod())
            .Where(method => method?.DeclaringType?.Namespace?.StartsWith("TrelloNotifier", StringComparison.Ordinal) == true)
            .Take(12)
            .Select(method => $"{method!.DeclaringType!.FullName}.{method.Name}")
            .ToArray();
        // CallerFilePath se reduce al componente; nunca se escribe la ruta de compilación.
        Log.ForContext("SourceContext", Path.GetFileNameWithoutExtension(sourcePath))
            .Write(level ?? (exception is OperationCanceledException ? LogEventLevel.Warning : LogEventLevel.Error),
                "Falló la operación {Operation}; tipo={ErrorType}; código={ErrorCode}; métodos={Frames}",
                operation, exception.GetType().FullName, exception.HResult, string.Join(" -> ", frames));
    }

    private static void ReportWriteFailure()
    {
        if (Interlocked.Exchange(ref _hasWriteFailure, 1) == 0)
        {
            Trace.TraceWarning("No se pudieron escribir todos los logs de Trello Notifier. Revisa permisos y espacio disponible.");
        }
    }

    private sealed class LogLevelEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            // u5 de Serilog genera INFOR/WARNI; Spring Boot utiliza INFO/WARN.
            string label = logEvent.Level switch
            {
                LogEventLevel.Verbose => "TRACE",
                LogEventLevel.Debug => "DEBUG",
                LogEventLevel.Information => "INFO",
                LogEventLevel.Warning => "WARN",
                LogEventLevel.Error => "ERROR",
                LogEventLevel.Fatal => "FATAL",
                _ => logEvent.Level.ToString()
            };
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("LevelLabel", label));
        }
    }
}
