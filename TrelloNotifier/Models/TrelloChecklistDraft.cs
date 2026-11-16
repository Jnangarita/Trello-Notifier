namespace TrelloNotifier.Models;

public sealed class TrelloChecklistDraft
{
    public string Name { get; set; } = string.Empty;
    public string ItemsText { get; set; } = string.Empty;

    public string[] GetItems() => ItemsText.Split(new[] { '\r', '\n' },
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
