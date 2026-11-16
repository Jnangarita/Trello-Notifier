using System.Text.Json.Serialization;

namespace TrelloNotifier.Models;

public sealed class TrelloLabel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? (string.IsNullOrWhiteSpace(Color) ? "Etiqueta sin nombre" : $"Etiqueta {Color}") : Name;
}
