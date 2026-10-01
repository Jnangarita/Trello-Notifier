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
        try
        {
            AppSettings settings = _settingsStore.Load();
            if (!settings.IsConfigured)
            {
                Updated?.Invoke(new MonitorSnapshot(
                    false,
                    "Falta configurar la conexión con Trello.",
                    null,
                    Array.Empty<TrelloCard>()));
                return;
            }

            IReadOnlyList<TrelloCard> cards = await _trelloApi.GetOpenCardsAsync(settings, cancellationToken);
            DateTimeOffset now = DateTimeOffset.Now;
            List<TrelloCard> eligibleCards = ReminderSchedule.GetEligibleCards(cards, settings, now);

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

            List<TrelloCard> reminders = eligibleCards
                .Where(card => ReminderSchedule.ShouldNotify(card, settings, notifiedCards, now))
                .ToList();
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
                _settingsStore.SaveNotifiedCards(notifiedCards);
            }

            int overdueCount = eligibleCards.Count(card => card.Due <= now);
            string reminderMessage = eligibleCards.Count == 0
                ? "No hay tarjetas para avisar."
                : $"Para avisos: {eligibleCards.Count - overdueCount} tarjeta(s) próxima(s) a vencer" +
                  $" y {overdueCount} vencida(s) pendiente(s).";
            string message = $"Monitor activo. {cards.Count} tarjeta(s) abierta(s) asignada(s). {reminderMessage}";
            Updated?.Invoke(new MonitorSnapshot(true, message, now, eligibleCards)
            {
                AssignedCards = cards,
                NotifyBeforeMinutes = settings.NotifyBeforeMinutes
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Updated?.Invoke(new MonitorSnapshot(
                true,
                ex.Message,
                DateTimeOffset.Now,
                Array.Empty<TrelloCard>(),
                true));
        }
        finally
        {
            _checkLock.Release();
        }
    }

}
