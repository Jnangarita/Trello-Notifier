using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;
using TrelloNotifier.Models;

namespace TrelloNotifier.Services;

internal static class CardCsvImporter
{
    public const int MaxFileBytes = 2 * 1024 * 1024;
    public const int MaxRows = 1000;
    public const string Template = "titulo,descripcion,vencimiento,responsables\r\n" +
        "Revisar inventario,**Importante:** validar existencias,,\r\n" +
        "Enviar informe,Resumen semanal,,\r\n";

    public static List<CardImportRow> Parse(string text, CancellationToken cancellationToken)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxFileBytes) throw new FormatException("El CSV no puede superar 2 MiB.");
        text = text.TrimStart('\uFEFF');
        using var headerReader = new StringReader(text);
        string? firstLine;
        do { firstLine = headerReader.ReadLine(); } while (firstLine is not null && string.IsNullOrWhiteSpace(firstLine));
        if (firstLine is null) throw new FormatException("El archivo CSV está vacío.");

        // TextFieldParser forma parte de .NET; respeta comillas, comillas escapadas y campos multilínea.
        using var parser = new TextFieldParser(new StringReader(text))
        {
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(firstLine.Contains(',') ? "," : ";");
        try
        {
            string[] headers = (parser.ReadFields() ?? Array.Empty<string>()).Select(value => value.Trim().ToLowerInvariant()).ToArray();
            string[] supported = { "titulo", "descripcion", "vencimiento", "responsables" };
            if (!headers.Contains("titulo") || headers.Distinct().Count() != headers.Length || headers.Except(supported).Any())
                throw new FormatException("Usa la cabecera titulo y, opcionalmente, descripcion, vencimiento y responsables, sin columnas repetidas ni adicionales.");

            var rows = new List<CardImportRow>();
            while (!parser.EndOfData)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (rows.Count == MaxRows) throw new FormatException("El CSV admite un máximo de 1000 tarjetas por lote.");
                string[] fields = parser.ReadFields() ?? Array.Empty<string>();
                string Field(string name)
                {
                    int index = Array.IndexOf(headers, name);
                    return index >= 0 && index < fields.Length ? fields[index] : string.Empty;
                }
                string? error = fields.Length == headers.Length ? null : "El número de columnas no coincide con la cabecera.";
                string dueText = Field("vencimiento").Trim();
                DateTimeOffset? due = null;
                if (dueText.Length > 0)
                {
                    bool explicitOffset = Regex.IsMatch(dueText,
                        @"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}(:[0-9]{2}(\.[0-9]{1,7})?)?(Z|[+-][0-9]{2}:[0-9]{2})$",
                        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                    if (explicitOffset && DateTimeOffset.TryParse(dueText, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed))
                        due = parsed;
                    else
                        error ??= "Vencimiento no válido. Usa ISO 8601 con zona horaria, por ejemplo 2026-12-05T09:00:00-05:00.";
                }
                rows.Add(new CardImportRow
                {
                    RowNumber = rows.Count + 1,
                    Draft = new TrelloCardDraft { Name = Field("titulo").Trim(), Description = Field("descripcion"), Due = due },
                    RequestedMembers = Field("responsables").Trim(),
                    ParseError = error
                });
            }
            if (rows.Count == 0) throw new FormatException("El CSV contiene la cabecera, pero no tiene tarjetas.");
            return rows;
        }
        catch (MalformedLineException)
        {
            throw new FormatException("El CSV tiene comillas o separadores incorrectos. Revisa el formato del archivo.");
        }
    }

    public static List<CardImportRow> Prepare(IEnumerable<CardImportRow> source, string listId,
        IReadOnlyList<TrelloMember> members, IReadOnlyList<string> defaultMemberIds)
    {
        return source.Select(row =>
        {
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

    public static string ExportResults(IEnumerable<CardImportRow> rows)
    {
        var text = new StringBuilder("\uFEFFfila,titulo,estado,id_tarjeta,url,detalle\r\n");
        foreach (CardImportRow row in rows)
        {
            string[] fields = { row.RowNumber.ToString(CultureInfo.InvariantCulture), row.Draft.Name, row.StatusText,
                row.CreatedCard?.Id ?? "", row.CreatedCard?.TrelloUri?.AbsoluteUri ?? "", row.Message };
            text.AppendLine(string.Join(",", fields.Select(EscapeCell)));
        }
        return text.ToString();
    }

    private static string EscapeCell(string value)
    {
        // Las comillas CSV no impiden que Excel interprete una celda como fórmula.
        string trimmed = value.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
