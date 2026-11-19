using System.Globalization;
using System.Text;
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
                    try { due = CardBatchPreparer.ParseDue(dueText); }
                    catch (FormatException ex) { error ??= ex.Message; }
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
