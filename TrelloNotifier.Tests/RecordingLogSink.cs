using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace TrelloNotifier.Tests;

internal sealed class RecordingLogSink : ILogEventSink
{
    public ConcurrentQueue<LogEvent> Events { get; } = new();
    public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
}
