namespace TrelloNotifier.Models;

public sealed record DiagnosticLogSnapshot
{
    public IReadOnlyList<DiagnosticLogEntry> Entries { get; init; } = Array.Empty<DiagnosticLogEntry>();
    public bool IsTruncated { get; init; }
}
