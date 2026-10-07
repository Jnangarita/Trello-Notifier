using TrelloNotifier.Models;

namespace TrelloNotifier.Services;

// Estos dobles aíslan el monitor real de la red y de la interfaz de Windows.
internal sealed class TrelloApiClient
{
    public IReadOnlyList<TrelloCard> Cards { get; set; } = Array.Empty<TrelloCard>();
    public bool Fail { get; set; }
    public Action? BeforeResponse { get; set; }

    public async Task<IReadOnlyList<TrelloCard>> GetOpenCardsAsync(
        AppSettings settings, CancellationToken cancellationToken)
    {
        await Task.Yield();
        BeforeResponse?.Invoke();
        if (Fail)
        {
            throw new HttpRequestException("Fallo de conexión simulado");
        }
        return Cards;
    }
}

internal sealed class DesktopNotificationService
{
    public bool Succeed { get; set; } = true;
    public List<IReadOnlyList<TrelloCard>> Batches { get; } = new();

    public bool ShowDueCards(IReadOnlyList<TrelloCard> cards, bool playSound, DateTimeOffset now)
    {
        Batches.Add(cards.ToArray());
        return Succeed;
    }
}
