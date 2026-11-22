using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Serilog;
using Serilog.Debugging;
using Serilog.Events;

namespace TrelloNotifier.Services;

internal static class AppLog
{
    public const string FileName = "trello-notifier.log";
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
                    Path.Combine(DirectoryPath, FileName),
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{LevelLabel:l}] [{Correlation:l}] {ShortSourceContext:l}: {Message:lj}{NewLine}{Exception}",
                    formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                    rollingInterval: RollingInterval.Infinite,
                    fileSizeLimitBytes: 5 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 3,
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
        string source = Path.GetFileNameWithoutExtension(sourcePath);
        // App.xaml.cs identifica App, no el sufijo xaml que acortaría el enriquecedor.
        if (source.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)) source = source[..^5];
        if (string.IsNullOrWhiteSpace(source)) source = "UnhandledException";
        Log.ForContext("SourceContext", source)
            .Write(level ?? (exception is OperationCanceledException ? LogEventLevel.Warning : LogEventLevel.Error),
                SanitizeException(exception),
                "Falló la operación {Operation}; tipo={ErrorType}; código={ErrorCode}",
                operation, exception.GetType().FullName, exception.HResult);
    }

    private static Exception SanitizeException(Exception exception, int depth = 0)
    {
        // No retener la excepción original: también sus interiores y Data pueden contener secretos.
        if (depth >= 16) return new Exception("Se omitieron excepciones internas adicionales.");
        string description = $"{exception.GetType().FullName}; código={exception.HResult}. Mensaje omitido.";
        Exception safe = exception is AggregateException aggregate
            ? new AggregateException(description, aggregate.InnerExceptions.Select(inner => SanitizeException(inner, depth + 1)))
            : new Exception(description, exception.InnerException is { } inner ? SanitizeException(inner, depth + 1) : null);
        // El runtime formatea los frames (incluido async), sin rutas ni números de línea del PDB.
        // Serilog recibe una Exception real y usa su formato nativo para la cadena de interiores.
        string stack = new StackTrace(exception, false).ToString();
        if (stack.Length > 0) ExceptionDispatchInfo.SetRemoteStackTrace(safe, stack);
        return safe;
    }

    private static void ReportWriteFailure()
    {
        if (Interlocked.Exchange(ref _hasWriteFailure, 1) == 0)
        {
            Trace.TraceWarning("No se pudieron escribir todos los logs de Trello Notifier. Revisa permisos y espacio disponible.");
        }
    }

}
