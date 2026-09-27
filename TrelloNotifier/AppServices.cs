using TrelloNotifier.Services;

namespace TrelloNotifier;

internal static class AppServices
{
    public static SettingsStore Settings { get; } = new();
    public static TrelloApiClient Trello { get; } = new();
    public static DesktopNotificationService Notifications { get; } = new();
    public static DueCardMonitor Monitor { get; } = new(Settings, Trello, Notifications);
}
