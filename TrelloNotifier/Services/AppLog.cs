using System.Diagnostics;
using System.Runtime.CompilerServices;
using Serilog;
using Serilog.Debugging;
using Serilog.Events;

namespace TrelloNotifier.Services;

internal static class AppLog
{
    public const string FileNamePrefix = "trello-notifier-";
    private static readonly string DefaultDirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Trello Notifier", "Logs");
    private static int _hasWriteFailure;

    public static bool HasWriteFailure => Volatile.Read(ref _hasWriteFailure) != 0;
    public static string DirectoryPath { get; private set; } = DefaultDirectoryPath;
    public static string SessionId { get; private set; } = string.Empty;

    public static void Initialize(string? directoryPath = null, bool debug = false)
    {
        Interlocked.Exchange(ref _hasWriteFailure, 0);
        SessionId = Guid.NewGuid().ToString("N");
        // SelfLog puede contener rutas y excepciones: nunca copiar su texto.
        SelfLog.Enable(_ => ReportWriteFailure());
        try
        {
            DirectoryPath = directoryPath ?? DefaultDirectoryPath;
            Directory.CreateDirectory(DirectoryPath);
            // El valor global respalda callbacks nativos sin ExecutionContext de arranque.
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(debug ? LogEventLevel.Debug : LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("SessionId", SessionId)
                .Enrich.WithProperty("ProcessId", Environment.ProcessId)
                .Enrich.WithProperty("SourceContext", "TrelloNotifier")
                .Enrich.With(new CompactLogEnricher())
                .WriteTo.Async(sink => sink.File(
                    Path.Combine(DirectoryPath, FileNamePrefix + ".log"),
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{Correlation:l}] {ShortSourceContext:l}: {Message:lj}{NewLine}{Exception}",
                    formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 5 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 14,
                    flushToDiskInterval: TimeSpan.FromSeconds(1),
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

}
