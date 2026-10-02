using System.Text.Json.Serialization;

namespace TrelloNotifier.Models;

public sealed class TrelloList
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
