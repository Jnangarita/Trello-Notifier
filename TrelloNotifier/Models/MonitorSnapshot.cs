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

    public IEnumerable<TrelloCard> GetCards(TrelloCardScope scope) => scope switch
    {
        TrelloCardScope.Pending => AssignedCards.Where(card => !card.DueComplete && !card.Closed),
        TrelloCardScope.History => AssignedCards.Where(card => card.DueComplete || card.Closed),
        TrelloCardScope.Completed => AssignedCards.Where(card => card.DueComplete),
        TrelloCardScope.Archived => AssignedCards.Where(card => card.Closed),
        _ => AssignedCards
    };

    public List<TrelloBoard> GetBoards(TrelloCardScope scope = TrelloCardScope.All) => GetCards(scope)
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
        string? boardId, TrelloCardStatus? status, DateTimeOffset now, string? searchText = null,
        TrelloCardScope scope = TrelloCardScope.All)
    {
        string search = searchText?.Trim() ?? string.Empty;
        return GetCards(scope)
            .Where(card => (string.IsNullOrEmpty(boardId) || string.Equals(card.BoardId, boardId, StringComparison.Ordinal)) &&
                (status is null || card.GetStatus(now, NotifyBeforeMinutes) == status) &&
                (search.Length == 0 || card.Name?.Contains(search, StringComparison.OrdinalIgnoreCase) == true))
            .OrderBy(card => card.Due ?? DateTimeOffset.MaxValue)
            .ThenBy(card => card.Name)
            .ThenBy(card => card.Id, StringComparer.Ordinal);
    }

    public (IReadOnlyList<TrelloCard> Cards, int TotalCount, int PageNumber, int PageCount) GetCardPage(
        int pageNumber, int pageSize, string? boardId, TrelloCardStatus? status, DateTimeOffset now,
        string? searchText = null, TrelloCardScope scope = TrelloCardScope.All)
    {
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize));

        List<TrelloCard> filteredCards = GetFilteredCards(boardId, status, now, searchText, scope).ToList();
        int totalCount = filteredCards.Count;
        int pageCount = totalCount == 0 ? 0 : (totalCount - 1) / pageSize + 1;
        int currentPage = Math.Clamp(pageNumber, 1, Math.Max(1, pageCount));
        List<TrelloCard> page = filteredCards.Skip((currentPage - 1) * pageSize).Take(pageSize).ToList();
        return (page, totalCount, currentPage, pageCount);
    }
}
