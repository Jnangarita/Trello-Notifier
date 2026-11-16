namespace TrelloNotifier.Models;

public sealed class TrelloCardDraft
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ListId { get; set; } = string.Empty;
    public DateTimeOffset? Due { get; set; }
    public List<string> MemberIds { get; set; } = new();
    public List<string> LabelIds { get; set; } = new();
    public List<TrelloChecklistDraft> Checklists { get; set; } = new();

    public string? GetValidationError()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "Escribe el título de la tarjeta.";
        if (string.IsNullOrWhiteSpace(ListId)) return "Selecciona una lista de destino.";
        if (Checklists.Any(checklist => string.IsNullOrWhiteSpace(checklist.Name)))
            return "Escribe un nombre para cada checklist o elimina los que no necesites.";
        if (Checklists.Any(checklist => checklist.GetItems().Length == 0))
            return "Añade al menos un elemento a cada checklist o elimina los que no necesites.";
        return null;
    }
}
