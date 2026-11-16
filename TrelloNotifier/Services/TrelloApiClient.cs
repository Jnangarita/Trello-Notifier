using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Serilog;
using TrelloNotifier.Models;

namespace TrelloNotifier.Services;

internal sealed class TrelloApiClient
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public Task<IReadOnlyList<TrelloCard>> GetOpenCardsAsync(
        AppSettings settings,
        CancellationToken cancellationToken) => GetCardsAsync(settings, "open", cancellationToken);

    public Task<IReadOnlyList<TrelloCard>> GetAllCardsAsync(
        AppSettings settings,
        CancellationToken cancellationToken) => GetCardsAsync(settings, "all", cancellationToken);

    private async Task<IReadOnlyList<TrelloCard>> GetCardsAsync(
        AppSettings settings, string filter, CancellationToken cancellationToken)
    {
        List<TrelloCard> cards = await GetListAsync<TrelloCard>(settings,
            $"members/me/cards?filter={filter}&fields=id,name,idBoard,idList,due,dueComplete,closed,url", cancellationToken);
        if (cards.Any(card => !string.IsNullOrWhiteSpace(card.BoardId)))
        {
            List<TrelloBoard> boards = await GetListAsync<TrelloBoard>(settings,
                "members/me/boards?filter=all&fields=name&lists=all", cancellationToken);
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            var listNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (TrelloBoard board in boards)
            {
                if (!string.IsNullOrWhiteSpace(board.Id) && !string.IsNullOrWhiteSpace(board.Name))
                {
                    names[board.Id] = board.Name;
                }
                if (board.Lists is not null)
                {
                    foreach (TrelloList list in board.Lists)
                    {
                        if (!string.IsNullOrWhiteSpace(list.Id) && !string.IsNullOrWhiteSpace(list.Name))
                        {
                            listNames[list.Id] = list.Name;
                        }
                    }
                }
            }
            foreach (TrelloCard card in cards)
            {
                if (!string.IsNullOrWhiteSpace(card.BoardId) && names.TryGetValue(card.BoardId, out string? name))
                {
                    card.BoardName = name;
                }
                if (!string.IsNullOrWhiteSpace(card.ListId) && listNames.TryGetValue(card.ListId, out string? listName))
                {
                    card.ListName = listName;
                }
            }
        }
        return cards;
    }

    public Task<List<TrelloBoard>> GetCreationBoardsAsync(AppSettings settings, CancellationToken cancellationToken) =>
        GetListAsync<TrelloBoard>(settings, "members/me/boards?filter=open&fields=name&lists=open", cancellationToken);

    public Task<List<TrelloMember>> GetBoardMembersAsync(AppSettings settings, string boardId, CancellationToken cancellationToken) =>
        GetListAsync<TrelloMember>(settings,
            $"boards/{Uri.EscapeDataString(boardId)}/members?fields=fullName,username", cancellationToken);

    public Task<List<TrelloLabel>> GetBoardLabelsAsync(AppSettings settings, string boardId, CancellationToken cancellationToken) =>
        GetListAsync<TrelloLabel>(settings,
            $"boards/{Uri.EscapeDataString(boardId)}/labels?fields=name,color&limit=1000", cancellationToken);

    public async Task<TrelloCard> CreateCardAsync(AppSettings settings, TrelloCardDraft draft, CancellationToken cancellationToken)
    {
        string? error = draft.GetValidationError();
        if (error is not null) throw new ArgumentException(error, nameof(draft));

        var fields = new Dictionary<string, string>
        {
            ["idList"] = draft.ListId,
            ["name"] = draft.Name.Trim(),
            ["desc"] = draft.Description
        };
        if (draft.MemberIds.Count > 0) fields["idMembers"] = string.Join(",", draft.MemberIds);
        if (draft.LabelIds.Count > 0) fields["idLabels"] = string.Join(",", draft.LabelIds);
        if (draft.Due is not null) fields["due"] = draft.Due.Value.ToString("O", CultureInfo.InvariantCulture);
        TrelloCard? card = await SendAsync<TrelloCard>(settings, HttpMethod.Post, "cards", fields, cancellationToken);
        if (card is null || string.IsNullOrWhiteSpace(card.Id))
            throw new JsonException("La respuesta no contiene el identificador de la tarjeta.");
        return card;
    }

    public static bool IsDefiniteCreationRejection(Exception error) => error is HttpRequestException
    {
        StatusCode: HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or
        HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.TooManyRequests
    };

    public async Task<string> CreateChecklistAsync(
        AppSettings settings, string cardId, string name, CancellationToken cancellationToken)
    {
        JsonElement result = await SendAsync<JsonElement>(settings, HttpMethod.Post, "checklists",
            new Dictionary<string, string> { ["idCard"] = cardId, ["name"] = name.Trim() }, cancellationToken);
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("id", out JsonElement id) ||
            id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()))
            throw new JsonException("La respuesta no contiene el identificador del checklist.");
        return id.GetString()!;
    }

    public async Task AddChecklistItemAsync(
        AppSettings settings, string checklistId, string name, CancellationToken cancellationToken)
    {
        await SendAsync<JsonElement>(settings, HttpMethod.Post,
            $"checklists/{Uri.EscapeDataString(checklistId)}/checkItems",
            new Dictionary<string, string> { ["name"] = name, ["checked"] = "false", ["pos"] = "bottom" }, cancellationToken);
    }

    private async Task<List<T>> GetListAsync<T>(AppSettings settings, string resource, CancellationToken cancellationToken) =>
        await SendAsync<List<T>>(settings, HttpMethod.Get, resource, null, cancellationToken) ?? new List<T>();

    private async Task<T?> SendAsync<T>(AppSettings settings, HttpMethod method, string resource,
        Dictionary<string, string>? fields, CancellationToken cancellationToken)
    {
        string url = $"{settings.ApiBaseUrl.Trim().TrimEnd('/')}/1/{resource}";
        string separator = resource.Contains('?') ? "&" : "?";

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            url += $"{separator}key={Uri.EscapeDataString(settings.ApiKey.Trim())}";
            separator = "&";
        }

        if (!string.IsNullOrWhiteSpace(settings.Token))
        {
            url += $"{separator}token={Uri.EscapeDataString(settings.Token.Trim())}";
        }

        var stopwatch = Stopwatch.StartNew();
        int? statusCode = null;
        bool succeeded = false;
        try
        {
            using var request = new HttpRequestMessage(method, url);
            if (fields is not null) request.Content = new FormUrlEncodedContent(fields);
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            statusCode = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException("Trello no pudo completar la solicitud. Revisa la conexión y los permisos.",
                    null, response.StatusCode);
            }

            T? result = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
            succeeded = true;
            return result;
        }
        finally
        {
            // El tipo identifica el recurso sin registrar URL, query, host ni respuesta.
            Log.ForContext<TrelloApiClient>().Information("Solicitud HTTP {Method} {ResourceType}: estado {StatusCode}; éxito {Succeeded}; cancelada {Cancelled}; duración {DurationMs} ms",
                method.Method, typeof(T).Name, statusCode, succeeded, cancellationToken.IsCancellationRequested, stopwatch.ElapsedMilliseconds);
        }
    }
}
