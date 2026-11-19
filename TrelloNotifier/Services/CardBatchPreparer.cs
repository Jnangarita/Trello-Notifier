using System.Globalization;
using System.Text.RegularExpressions;
using TrelloNotifier.Models;

namespace TrelloNotifier.Services;

internal static class CardBatchPreparer
{
    public static DateTimeOffset? ParseDue(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return null;
        if (Regex.IsMatch(text,
            @"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}(:[0-9]{2}(\.[0-9]{1,7})?)?(Z|[+-][0-9]{2}:[0-9]{2})$",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset due)) return due;
        throw new FormatException("Vencimiento no válido. Usa ISO 8601 con zona horaria, por ejemplo 2026-12-05T09:00:00-05:00.");
    }

    public static List<CardImportRow> Prepare(IEnumerable<CardImportRow> source, string listId,
        IReadOnlyList<TrelloMember> members, IReadOnlyList<string> defaultMemberIds)
    {
        return source.Select(row =>
        {
            // Una revisión nunca convierte resultados de envío en tarjetas pendientes.
            if (row.State is not (CardImportState.Ready or CardImportState.Invalid)) return row;
            string? error = row.ParseError;
            var selected = new List<TrelloMember>();
            if (string.IsNullOrWhiteSpace(row.RequestedMembers))
            {
                foreach (string id in defaultMemberIds.Distinct())
                {
                    TrelloMember? member = members.FirstOrDefault(candidate => candidate.Id == id);
                    if (member is null) error ??= "Un responsable predeterminado ya no está disponible en el tablero.";
                    else selected.Add(member);
                }
            }
            else
            {
                string[] names = row.RequestedMembers.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (names.Length == 0) error ??= "Indica un usuario válido en responsables.";
                foreach (string name in names)
                {
                    string username = name.TrimStart('@');
                    if (string.IsNullOrWhiteSpace(username))
                    {
                        error ??= "Indica un nombre de usuario después de @.";
                        continue;
                    }
                    TrelloMember[] matches = members.Where(member => string.Equals(member.Username, username, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (matches.Length != 1) error ??= "Hay un responsable desconocido o ambiguo. Usa nombres de usuario del tablero, separados por punto y coma.";
                    else if (selected.All(member => member.Id != matches[0].Id)) selected.Add(matches[0]);
                }
            }
            var draft = new TrelloCardDraft
            {
                Name = row.Draft.Name,
                Description = row.Draft.Description,
                Due = row.Draft.Due,
                ListId = listId,
                MemberIds = selected.Select(member => member.Id).ToList()
            };
            error ??= draft.GetValidationError();
            return row with
            {
                Draft = draft,
                MemberSummary = selected.Count == 0 ? "Sin asignar" : string.Join(", ", selected.Select(member => member.DisplayName)),
                State = error is null ? CardImportState.Ready : CardImportState.Invalid,
                Message = error ?? string.Empty
            };
        }).ToList();
    }
}
