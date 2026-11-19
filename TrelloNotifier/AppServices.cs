using TrelloNotifier.Services;

namespace TrelloNotifier;

internal static class AppServices
{
    public static SettingsStore Settings { get; } = new();
    public static TrelloApiClient Trello { get; } = new();
    public static GeminiCardClient Gemini { get; } = new();
    public static AppLogReader Logs { get; } = new(AppLog.DirectoryPath);
    public static DesktopNotificationService Notifications { get; } = new();
    public static DueCardMonitor Monitor { get; } = new(Settings, Trello, Notifications);
}
