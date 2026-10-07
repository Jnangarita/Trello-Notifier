using System.Diagnostics;
using Serilog;
using Serilog.Context;
using TrelloNotifier.Models;

namespace TrelloNotifier.Services;

internal sealed class DueCardMonitor : IDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly TrelloApiClient _trelloApi;
    private readonly DesktopNotificationService _notifications;
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _monitorTask;

    public DueCardMonitor(
        SettingsStore settingsStore,
        TrelloApiClient trelloApi,
        DesktopNotificationService notifications)
    {
        _settingsStore = settingsStore;
        _trelloApi = trelloApi;
        _notifications = notifications;
    }

    public event Action<MonitorSnapshot>? Updated;

    public void Start()
    {
        Stop();
        _cancellationTokenSource = new CancellationTokenSource();
        CancellationToken token = _cancellationTokenSource.Token;
        _monitorTask = Task.Run(() => RunAsync(token), token);
        Log.ForContext<DueCardMonitor>().Information("Monitor iniciado");
    }

    public async Task CheckNowAsync(CancellationToken cancellationToken = default)
    {
        // SQLite tiene IO síncrono; también las comprobaciones manuales salen del hilo UI.
        await Task.Run(() => CheckAsync(cancellationToken), cancellationToken);
    }

    public void Restart()
    {
        Start();
    }

    public void Dispose()
    {
        Stop();
        _checkLock.Dispose();
    }

    private void Stop()
    {
        if (_cancellationTokenSource is not null) Log.ForContext<DueCardMonitor>().Information("Parada del monitor solicitada");
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        _monitorTask = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            int pollIntervalMinutes = new AppSettings().PollIntervalMinutes;
            try
            {
                AppSettings settings = _settingsStore.Load();
                pollIntervalMinutes = settings.PollIntervalMinutes;
                if (!settings.IsConfigured)
                {
                    Log.ForContext<DueCardMonitor>().Information("Monitor en espera de configuración");
                    Updated?.Invoke(new MonitorSnapshot(
                        false,
                        "Configura la conexión con Trello o con un servidor simulado para iniciar el monitor.",
                        null,
                        Array.Empty<TrelloCard>()));
                    return;
                }

                await CheckAsync(cancellationToken);
            }
            catch (IOException ex)
            {
                AppLog.WriteFailure("LoadMonitorSettings", ex);
                // Un bloqueo o una importación fallida debe ser visible y permitir reintento.
                Updated?.Invoke(new MonitorSnapshot(
                    true,
                    ex.Message,
                    null,
                    Array.Empty<TrelloCard>(),
                    true));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(pollIntervalMinutes), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        await _checkLock.WaitAsync(cancellationToken);
        using IDisposable checkContext = LogContext.PushProperty("CheckId", Guid.NewGuid().ToString("N"));
        var stopwatch = Stopwatch.StartNew();
        string operation = "LoadSettings";
        string outcome = "Failed";
        Log.ForContext<DueCardMonitor>().Debug("Comprobación iniciada");
        try
        {
            AppSettings settings = _settingsStore.Load();
            if (!settings.IsConfigured)
            {
                outcome = "NotConfigured";
                Updated?.Invoke(new MonitorSnapshot(
                    false,
                    "Falta configurar la conexión con Trello.",
                    null,
                    Array.Empty<TrelloCard>()));
                return;
            }

            operation = "GetOpenCards";
            IReadOnlyList<TrelloCard> cards = await _trelloApi.GetOpenCardsAsync(settings, cancellationToken);
            DateTimeOffset now = DateTimeOffset.Now;
            List<TrelloCard> eligibleCards = ReminderSchedule.GetEligibleCards(cards, settings, now);

            operation = "LoadNotificationHistory";
            Dictionary<string, DateTimeOffset> notifiedCards = _settingsStore.LoadNotifiedCards();
            HashSet<string> currentCardKeys = cards
                .Where(card => card.Due is not null && !card.DueComplete && !card.Closed)
                .Select(ReminderSchedule.GetKey)
                .ToHashSet(StringComparer.Ordinal);
            List<string> obsoleteKeys = notifiedCards.Keys.Where(key => !currentCardKeys.Contains(key)).ToList();
            foreach (string key in obsoleteKeys)
            {
                notifiedCards.Remove(key);
            }
            bool notificationStateChanged = obsoleteKeys.Count > 0;

            // La preferencia puede cambiar mientras una consulta manual espera a la API.
            operation = "LoadNotificationSettings";
            settings.NotificationsEnabled = _settingsStore.Load().NotificationsEnabled;
            cancellationToken.ThrowIfCancellationRequested();
            List<TrelloCard> reminders = eligibleCards
                .Where(card => ReminderSchedule.ShouldNotify(card, settings, notifiedCards, now))
                .ToList();
            operation = "SendDueNotifications";
            if (reminders.Count > 0 && _notifications.ShowDueCards(reminders, settings.PlaySound, now))
            {
                foreach (TrelloCard card in reminders)
                {
                    notifiedCards[ReminderSchedule.GetKey(card)] = now;
                }
                notificationStateChanged = true;
            }

            if (notificationStateChanged)
            {
                operation = "SaveNotificationHistory";
                _settingsStore.SaveNotifiedCards(notifiedCards);
            }

            int overdueCount = eligibleCards.Count(card => card.Due <= now);
            string reminderMessage = !settings.NotificationsEnabled
                ? "Notificaciones de escritorio desactivadas."
                : eligibleCards.Count == 0
                ? "No hay tarjetas para avisar."
                : $"Para avisos: {eligibleCards.Count - overdueCount} tarjeta(s) próxima(s) a vencer" +
                  $" y {overdueCount} vencida(s) pendiente(s).";
            string message = $"Monitor activo. {cards.Count} tarjeta(s) abierta(s) asignada(s). {reminderMessage}";
            Log.ForContext<DueCardMonitor>().Information("Tarjetas evaluadas: {CardCount}; candidatas: {EligibleCount}; avisos solicitados: {ReminderCount}",
                cards.Count, eligibleCards.Count, reminders.Count);
            operation = "PublishSnapshot";
            Updated?.Invoke(new MonitorSnapshot(true, message, now, eligibleCards)
            {
                AssignedCards = cards,
                NotifyBeforeMinutes = settings.NotifyBeforeMinutes
            });
            outcome = "Succeeded";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "Cancelled";
        }
        catch (Exception ex)
        {
            AppLog.WriteFailure(operation, ex);
            Updated?.Invoke(new MonitorSnapshot(
                true,
                ex.Message,
                DateTimeOffset.Now,
                Array.Empty<TrelloCard>(),
                true));
        }
        finally
        {
            Log.ForContext<DueCardMonitor>().Information("Comprobación finalizada: {Outcome}; duración {DurationMs} ms", outcome, stopwatch.ElapsedMilliseconds);
            _checkLock.Release();
        }
    }

}
