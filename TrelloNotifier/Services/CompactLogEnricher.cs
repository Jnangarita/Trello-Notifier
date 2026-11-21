using Serilog.Core;
using Serilog.Events;

namespace TrelloNotifier.Services;

internal sealed class CompactLogEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        string source = GetString(logEvent, "SourceContext");
        int separator = Math.Max(source.LastIndexOf('.'), source.LastIndexOf('+'));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ShortSourceContext", source[(separator + 1)..]));

        string session = GetString(logEvent, "SessionId");
        string check = GetString(logEvent, "CheckId");
        // Solo la representación de texto se abrevia; las propiedades originales permanecen intactas.
        string correlation = "s:" + session[..Math.Min(8, session.Length)];
        if (check.Length > 0) correlation += " c:" + check[..Math.Min(8, check.Length)];
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Correlation", correlation));
    }

    private static string GetString(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out LogEventPropertyValue? property) && property is ScalarValue { Value: string value }
            ? value : string.Empty;
}
