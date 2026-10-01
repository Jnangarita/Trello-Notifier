namespace TrelloNotifier.Models;

public sealed class TrelloCardViewModel
{
    public TrelloCardViewModel(TrelloCard card, DateTimeOffset now, int? notifyBeforeMinutes = null)
    {
        Name = card.Name;
        BoardName = !string.IsNullOrWhiteSpace(card.BoardName)
            ? card.BoardName
            : !string.IsNullOrWhiteSpace(card.BoardId)
                ? card.BoardId
                : "Tablero no disponible";
        BoardText = string.IsNullOrWhiteSpace(card.BoardName) && string.IsNullOrWhiteSpace(card.BoardId)
            ? BoardName : $"Tablero: {BoardName}";
        Url = card.Url;
        IsArchived = card.Closed;
        DueText = card.Due?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "Sin fecha de vencimiento";
        Status = card.GetStatus(now, notifyBeforeMinutes ?? new AppSettings().NotifyBeforeMinutes);
        StatusText = card.Closed ? "Archivada" : Status switch
        {
            TrelloCardStatus.Overdue => "Vencida",
            TrelloCardStatus.DueSoon => "Próxima",
            TrelloCardStatus.Upcoming => "Al día",
            TrelloCardStatus.Completed => "Completada",
            _ => "Sin fecha"
        };

        if (card.Closed)
        {
            RemainingText = card.DueComplete ? "Completada y archivada" : "Archivada sin completar";
            return;
        }

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
    public string BoardName { get; }
    public string BoardText { get; }
    public string Url { get; }
    public bool IsArchived { get; }
    public string DueText { get; }
    public string RemainingText { get; }
    public TrelloCardStatus Status { get; }
    public string StatusText { get; }
}
