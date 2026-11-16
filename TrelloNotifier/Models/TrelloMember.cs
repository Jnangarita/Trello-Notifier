using System.Text.Json.Serialization;

namespace TrelloNotifier.Models;

public sealed class TrelloMember
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(FullName)
        ? (string.IsNullOrWhiteSpace(Username) ? Id : $"@{Username}")
        : (string.IsNullOrWhiteSpace(Username) ? FullName : $"{FullName} (@{Username})");
}
