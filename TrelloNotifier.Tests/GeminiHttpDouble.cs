using System.Net;
using System.Text;
using System.Text.Json;

namespace TrelloNotifier.Tests;

internal sealed class GeminiHttpDouble : HttpMessageHandler
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
        (_, _) => Task.FromResult(Json(Envelope(Proposal())));
    public int Calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        return Respond(request, cancellationToken);
    }

    public static Dictionary<string, object?> Proposal(string? due = null, string issue = "") => new()
    {
        ["title"] = "Revisar pedido",
        ["description"] = "**Verificar** cantidades",
        ["due"] = due,
        ["dateIssue"] = issue,
        ["checklists"] = Array.Empty<object>()
    };

    public static string Envelope(params object[] cards) => JsonSerializer.Serialize(new
    {
        candidates = new[]
        {
            new { finishReason = "STOP", content = new { parts = new[] { new { text = JsonSerializer.Serialize(new { cards }) } } } }
        }
    });

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    internal sealed class NonSeekableStream : MemoryStream
    {
        public NonSeekableStream(byte[] bytes) : base(bytes) { }
        public override bool CanSeek => false;
    }
}
