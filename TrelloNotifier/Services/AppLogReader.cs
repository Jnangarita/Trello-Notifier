using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TrelloNotifier.Models;

namespace TrelloNotifier.Services;

internal sealed class AppLogReader
{
    public const int MaxEntries = 1000;
    public const int MaxReadBytes = 2 * 1024 * 1024;
    private const string HeaderSeparator = " --- [TrelloNotifier] ";
    private readonly string _directoryPath;

    public AppLogReader(string directoryPath) => _directoryPath = directoryPath;

    public Task<IReadOnlyList<string>> GetFilesAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlyList<string>>(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return new DirectoryInfo(_directoryPath).EnumerateFiles(AppLog.FileNamePrefix + "*.log")
                .Where(file => IsLogFileName(file.Name))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Select(file =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return file.Name;
                }).ToArray();
        }
        catch (DirectoryNotFoundException)
        {
            return Array.Empty<string>();
        }
    }, cancellationToken);

    public Task<DiagnosticLogSnapshot> ReadRecentAsync(string fileName, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsLogFileName(fileName)) throw new ArgumentException("Selecciona un archivo de log de la aplicación.", nameof(fileName));

        // Compartir lectura, escritura y borrado permite consultar el archivo activo durante su rotación.
        using var stream = new FileStream(Path.Combine(_directoryPath, fileName), FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        long length = stream.Length;
        long start = Math.Max(0, length - MaxReadBytes);
        stream.Seek(start, SeekOrigin.Begin);
        byte[] bytes = new byte[(int)(length - start)];
        int read = 0;
        while (read < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = stream.Read(bytes, read, Math.Min(64 * 1024, bytes.Length - read));
            if (count == 0) break;
            read += count;
        }

        string text = Encoding.UTF8.GetString(bytes, 0, read).TrimStart('\uFEFF');
        int first = start > 0 ? text.IndexOf('\n') + 1 : 0;
        int last = text.LastIndexOf('\n');
        bool truncated = start > 0;
        if (last < first || (start > 0 && first == 0)) return new DiagnosticLogSnapshot { IsTruncated = truncated };

        // Ignorar los fragmentos de los extremos: una escritura puede no haber terminado aún.
        using var reader = new StringReader(text[first..(last + 1)]);
        var entries = new Queue<DiagnosticLogEntry>();
        while (reader.ReadLine() is string line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (entries.Count == MaxEntries)
            {
                entries.Dequeue();
                truncated = true;
            }
            entries.Enqueue(ParseLine(line));
        }
        return new DiagnosticLogSnapshot { Entries = entries.Reverse().ToArray(), IsTruncated = truncated };
    }, cancellationToken);

    internal static DiagnosticLogEntry ParseLine(string line)
    {
        var unrecognized = new DiagnosticLogEntry { Message = line };
        int headerEnd = line.IndexOf(HeaderSeparator, StringComparison.Ordinal);
        if (headerEnd < 0) return unrecognized;
        string[] header = line[..headerEnd].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (header.Length != 3 || !int.TryParse(header[2], NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
            !DateTimeOffset.TryParseExact(header[0], "yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTimeOffset timestamp)) return unrecognized;

        int sourceStart = headerEnd + HeaderSeparator.Length;
        int messageStart = line.IndexOf(" : ", sourceStart, StringComparison.Ordinal);
        if (messageStart < 0) return unrecognized;
        return new DiagnosticLogEntry
        {
            Timestamp = timestamp,
            Level = header[1] switch { "INFOR" => "INFO", "WARNI" => "WARN", _ => header[1] },
            Source = line[sourceStart..messageStart].Trim(),
            Message = line[(messageStart + 3)..]
        };
    }

    private static bool IsLogFileName(string fileName) => Regex.IsMatch(fileName,
        "^" + Regex.Escape(AppLog.FileNamePrefix) + @"[0-9]{8}(?:_[0-9]{3,})?\.log\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}
