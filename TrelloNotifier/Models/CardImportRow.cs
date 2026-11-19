namespace TrelloNotifier.Models;

public sealed record CardImportRow
{
    public int RowNumber { get; init; }
    public TrelloCardDraft Draft { get; init; } = new();
    public string RequestedMembers { get; init; } = string.Empty;
    public string MemberSummary { get; init; } = "Sin asignar";
    public string? ParseError { get; init; }
    public string Message { get; init; } = string.Empty;
    public string ReviewNote { get; init; } = string.Empty;
    public CardImportState State { get; init; } = CardImportState.Ready;
    public TrelloCard? CreatedCard { get; init; }

    public bool CanSubmit => State is CardImportState.Ready or CardImportState.Rejected;
    public string Name => string.IsNullOrWhiteSpace(Draft.Name) ? "(Sin título)" : Draft.Name;
    public bool CanOpen => CreatedCard?.TrelloUri is not null;
    public string DueText => Draft.Due?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "Sin vencimiento";
    public string StatusText => State switch
    {
        CardImportState.Ready => "Lista para crear",
        CardImportState.Invalid => "Revisar datos",
        CardImportState.Excluded => "Excluida",
        CardImportState.Creating => "Creando...",
        CardImportState.Created => "Creada",
        CardImportState.Rejected => "No creada · Reintentable",
        _ => "Resultado incierto · Revisar Trello"
    };
}
