using System.Diagnostics;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using TrelloNotifier.Models;

namespace TrelloNotifier.Services;

internal sealed class DesktopNotificationService : IDisposable
{
    private bool _isRegistered;

    public void Initialize()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
            _isRegistered = true;
        }
        catch
        {
            _isRegistered = false;
        }
    }

    public bool ShowDueCards(IReadOnlyList<TrelloCard> cards, bool playSound, DateTimeOffset now)
    {
        if (!_isRegistered || cards.Count == 0)
        {
            return false;
        }

        AppNotificationBuilder builder = new AppNotificationBuilder()
            .SetAppLogoOverride(GetLogoUri(), AppNotificationImageCrop.Default);
        if (cards.Count == 1)
        {
            TrelloCard card = cards[0];
            bool overdue = card.Due <= now;
            string due = card.Due!.Value.ToLocalTime().ToString("dd/MM/yyyy 'a las' HH:mm");
            builder.AddArgument("url", card.Url)
                .AddText(overdue ? "Tarjeta vencida pendiente" : "Tarjeta próxima a vencer")
                .AddText(card.Name)
                .SetAttributionText($"{(overdue ? "Venció" : "Vence")} el {due}");
        }
        else
        {
            int overdueCount = cards.Count(card => card.Due <= now);
            string names = string.Join(" · ", cards.Take(3).Select(card => card.Name));
            if (names.Length > 180)
            {
                names = names[..177] + "…";
            }
            if (cards.Count > 3)
            {
                names += $" · y {cards.Count - 3} más";
            }
            builder.AddArgument("url", "https://trello.com/")
                .AddText($"Recordatorio: {cards.Count} tarjetas pendientes")
                .AddText($"{cards.Count - overdueCount} próximas a vencer · {overdueCount} vencidas\n{names}")
                .SetAttributionText("Abre Trello para revisar tus tarjetas");
        }

        if (!playSound)
        {
            builder.MuteAudio();
        }

        AppNotification notification = builder.BuildNotification();
        AppNotificationManager.Default.Show(notification);
        return notification.Id != 0;
    }

    public bool ShowTest(bool playSound)
    {
        if (!_isRegistered)
        {
            return false;
        }

        AppNotificationBuilder builder = new AppNotificationBuilder()
            .SetAppLogoOverride(GetLogoUri(), AppNotificationImageCrop.Default)
            .AddText("Notificación de prueba")
            .AddText("Trello Notifier puede mostrar avisos en este equipo.")
            .SetAttributionText("No se realizó ninguna conexión con Trello");

        if (!playSound)
        {
            builder.MuteAudio();
        }

        AppNotification notification = builder.BuildNotification();
        AppNotificationManager.Default.Show(notification);
        return notification.Id != 0;
    }

    public void ProcessActivation(AppNotificationActivatedEventArgs args)
    {
        OpenTrelloUrl(args);
    }

    public void Dispose()
    {
        if (!_isRegistered)
        {
            return;
        }

        AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
        AppNotificationManager.Default.Unregister();
        _isRegistered = false;
    }

    private static void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        OpenTrelloUrl(args);
    }

    private static void OpenTrelloUrl(AppNotificationActivatedEventArgs args)
    {
        if (!args.Arguments.ContainsKey("url") ||
            !Uri.TryCreate(args.Arguments["url"], UriKind.Absolute, out Uri? uri) ||
            !(uri.Host.Equals("trello.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".trello.com", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private static Uri GetLogoUri()
    {
        return new Uri(App.GetAssetPath("trello-notifier.png"));
    }
}
