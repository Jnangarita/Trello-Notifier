using System.Diagnostics;
using System.Net.Http.Json;
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
            $"cards?filter={filter}&fields=id,name,idBoard,idList,due,dueComplete,closed,url", cancellationToken);
        if (cards.Any(card => !string.IsNullOrWhiteSpace(card.BoardId)))
        {
            List<TrelloBoard> boards = await GetListAsync<TrelloBoard>(settings,
                "boards?filter=all&fields=name&lists=all", cancellationToken);
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

    private async Task<List<T>> GetListAsync<T>(
        AppSettings settings, string resource, CancellationToken cancellationToken)
    {
        string url = $"{settings.ApiBaseUrl.Trim().TrimEnd('/')}/1/members/me/{resource}";

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            url += $"&key={Uri.EscapeDataString(settings.ApiKey.Trim())}";
        }

        if (!string.IsNullOrWhiteSpace(settings.Token))
        {
            url += $"&token={Uri.EscapeDataString(settings.Token.Trim())}";
        }

        var stopwatch = Stopwatch.StartNew();
        int? statusCode = null;
        bool succeeded = false;
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
            statusCode = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"El servidor respondió {(int)response.StatusCode} ({response.ReasonPhrase}). Verifica la URL y las credenciales.");
            }

            List<T> result = await response.Content.ReadFromJsonAsync<List<T>>(cancellationToken: cancellationToken)
                ?? new List<T>();
            succeeded = true;
            return result;
        }
        finally
        {
            // El tipo identifica el recurso sin registrar URL, query, host ni respuesta.
            Log.ForContext<TrelloApiClient>().Information("Consulta HTTP {ResourceType}: estado {StatusCode}; éxito {Succeeded}; cancelada {Cancelled}; duración {DurationMs} ms",
                typeof(T).Name, statusCode, succeeded, cancellationToken.IsCancellationRequested, stopwatch.ElapsedMilliseconds);
        }
    }
}
