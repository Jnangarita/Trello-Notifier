namespace TrelloNotifier.Models;

public sealed record MonitorSnapshot(
    bool IsRunning,
    string Message,
    DateTimeOffset? LastCheck,
    IReadOnlyList<TrelloCard> DueSoonCards,
    bool HasError = false)
{
    public IReadOnlyList<TrelloCard> AssignedCards { get; init; } = Array.Empty<TrelloCard>();
    public int NotifyBeforeMinutes { get; init; }

    public List<TrelloBoard> GetBoards() => AssignedCards
        .Where(card => !string.IsNullOrWhiteSpace(card.BoardId))
        .GroupBy(card => card.BoardId, StringComparer.Ordinal)
        .Select(group => new TrelloBoard
        {
            Id = group.Key,
            Name = group.Select(card => card.BoardName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
                ?? group.Key
        })
        .OrderBy(board => board.Name)
        .ThenBy(board => board.Id, StringComparer.Ordinal)
        .ToList();

    public IEnumerable<TrelloCard> GetFilteredCards(
        string? boardId, TrelloCardStatus? status, DateTimeOffset now, string? searchText = null)
    {
        string search = searchText?.Trim() ?? string.Empty;
        return AssignedCards
            .Where(card => (string.IsNullOrEmpty(boardId) || string.Equals(card.BoardId, boardId, StringComparison.Ordinal)) &&
                (status is null || card.GetStatus(now, NotifyBeforeMinutes) == status) &&
                (search.Length == 0 || card.Name?.Contains(search, StringComparison.OrdinalIgnoreCase) == true))
            .OrderBy(card => card.Due ?? DateTimeOffset.MaxValue)
            .ThenBy(card => card.Name);
    }
}
