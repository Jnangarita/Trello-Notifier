namespace TrelloNotifier.Models;

public sealed class TrelloCardViewModel
{
    public TrelloCardViewModel(TrelloCard card, DateTimeOffset now)
    {
        Name = card.Name;
        BoardText = !string.IsNullOrWhiteSpace(card.BoardName)
            ? $"Tablero: {card.BoardName}"
            : !string.IsNullOrWhiteSpace(card.BoardId)
                ? $"Tablero: {card.BoardId}"
                : "Tablero no disponible";
        Url = card.Url;
        DueText = card.Due?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "Sin fecha de vencimiento";

        if (card.DueComplete)
        {
            RemainingText = "Completada";
            return;
        }

        if (card.Due is null)
        {
            RemainingText = "Sin fecha";
            return;
        }

        TimeSpan remaining = card.Due.Value - now;
        RemainingText = remaining <= TimeSpan.Zero
            ? "Vencida pendiente"
            : remaining.TotalMinutes < 1
            ? "Vence en menos de un minuto"
            : remaining.TotalHours < 1
                ? $"Vence en {(int)Math.Ceiling(remaining.TotalMinutes)} min"
                : $"Vence en {Math.Ceiling(remaining.TotalHours):0} h";
    }

    public string Name { get; }
    public string BoardText { get; }
    public string Url { get; }
    public string DueText { get; }
    public string RemainingText { get; }
}
