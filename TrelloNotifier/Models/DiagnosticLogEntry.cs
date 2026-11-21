namespace TrelloNotifier.Models;

public sealed record DiagnosticLogEntry
{
    public string RawLine { get; init; } = string.Empty;
    public DateTimeOffset? Timestamp { get; init; }
    public string Level { get; init; } = "OTRO";
    public string Source { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public string TimestampText => Timestamp?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss.fff") ?? "Sin fecha";
    public string Severity => Level switch
    {
        "INFO" => "Info",
        "WARN" => "Warning",
        "ERROR" or "FATAL" => "Error",
        _ => "Other"
    };
}
