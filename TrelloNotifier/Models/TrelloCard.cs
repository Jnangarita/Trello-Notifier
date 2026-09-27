using System.Text.Json.Serialization;

namespace TrelloNotifier.Models;

public sealed class TrelloCard
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("idBoard")]
    public string BoardId { get; set; } = string.Empty;

    [JsonIgnore]
    public string? BoardName { get; set; }

    [JsonPropertyName("due")]
    public DateTimeOffset? Due { get; set; }

    [JsonPropertyName("dueComplete")]
    public bool DueComplete { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    public TrelloCardStatus GetStatus(DateTimeOffset now, int notifyBeforeMinutes)
    {
        if (DueComplete) return TrelloCardStatus.Completed;
        if (Due is null) return TrelloCardStatus.NoDueDate;
        if (Due <= now) return TrelloCardStatus.Overdue;
        return Due <= now.AddMinutes(notifyBeforeMinutes)
            ? TrelloCardStatus.DueSoon
            : TrelloCardStatus.Upcoming;
    }
}
