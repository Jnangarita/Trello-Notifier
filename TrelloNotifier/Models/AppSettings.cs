namespace TrelloNotifier.Models;

public sealed class AppSettings
{
    public const string DefaultGeminiModel = "gemini-3.5-flash-lite";
    public string GeminiApiKey { get; set; } = string.Empty;
    public string GeminiModel { get; set; } = DefaultGeminiModel;
    public string ApiBaseUrl { get; set; } = "https://api.trello.com";
    public string ApiKey { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public int NotifyBeforeMinutes { get; set; } = 60;
    public int PollIntervalMinutes { get; set; } = 5;
    public int RepeatReminderMinutes { get; set; } = 30;
    public bool IncludeOverdueCards { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
    public bool PlaySound { get; set; } = true;
    public string Theme { get; set; } = "Default";

    public bool IsOfficialTrelloApi =>
        Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out Uri? uri) &&
        uri.Host.Equals("api.trello.com", StringComparison.OrdinalIgnoreCase);

    public bool IsConfigured =>
        Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out Uri? uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        (!IsOfficialTrelloApi ||
         (!string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Token)));
}
