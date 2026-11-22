using System.Diagnostics;
using Serilog;
using Serilog.Context;
using Serilog.Events;
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
    private volatile bool _stopping;
    private volatile bool _shutdownRequested;
    private HashSet<(string Id, DateTimeOffset? Due, bool Complete, bool Closed)>? _lastCards;
    private HashSet<string>? _lastCooldownKeys;
    private (int Evaluated, int Candidates, int Cooldown, int Deduplicated, int Disabled, int Rejected, bool Enabled)? _lastSummaryCounts;
    private bool _lastCheckFailed;

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
        if (_shutdownRequested) return;
        Stop();
        _stopping = false;
        _cancellationTokenSource = new CancellationTokenSource();
        CancellationToken token = _cancellationTokenSource.Token;
        Task next = Task.Run(() => RunAsync(token), token);
        _monitorTask = _monitorTask is { IsCompleted: false } previous ? Task.WhenAll(previous, next) : next;
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
        _shutdownRequested = true;
        Stop();
        // No destruir el semáforo mientras una comprobación cancelada aún debe liberarlo.
    }

    public async Task StopAsync()
    {
        _shutdownRequested = true;
        Stop();
        try
        {
            if (_monitorTask is Task task)
            {
                try { await task.ConfigureAwait(false); }
                catch (OperationCanceledException) { /* Parada solicitada antes de arrancar Task.Run. */ }
            }
        }
        finally
        {
            // Esperar también una comprobación manual que ya estaba en curso.
            await _checkLock.WaitAsync().ConfigureAwait(false);
            _checkLock.Release();
        }
    }

    private void Stop()
    {
        _stopping = true;
        if (_cancellationTokenSource is not null) Log.ForContext<DueCardMonitor>().Information("Parada del monitor solicitada");
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            int pollIntervalMinutes = new AppSettings().PollIntervalMinutes;
            try
            {
                AppSettings settings = _settingsStore.Load();
                cancellationToken.ThrowIfCancellationRequested();
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
                if (cancellationToken.IsCancellationRequested) return;
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
        if (_stopping) return;
        await _checkLock.WaitAsync(cancellationToken);
        if (_stopping)
        {
            _checkLock.Release();
            return;
        }
        using IDisposable checkContext = LogContext.PushProperty("CheckId", Guid.NewGuid().ToString("N"));
        var stopwatch = Stopwatch.StartNew();
        string operation = "LoadSettings";
        string outcome = "Failed";
        Log.ForContext<DueCardMonitor>().Debug("Comprobación iniciada");
        try
        {
            if (_stopping)
            {
                outcome = "Cancelled";
                return;
            }
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
            if (_stopping)
            {
                outcome = "Cancelled";
                return;
            }
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
            List<TrelloCard> uniqueCandidates = eligibleCards.DistinctBy(ReminderSchedule.GetKey).ToList();
            int deduplicated = eligibleCards.Count - uniqueCandidates.Count;
            var cooldownKeys = new HashSet<string>(StringComparer.Ordinal);
            int disabled = 0;
            var reminders = new List<TrelloCard>();
            foreach (TrelloCard card in uniqueCandidates)
            {
                if (ReminderSchedule.ShouldNotify(card, settings, notifiedCards, now)) reminders.Add(card);
                else if (!settings.NotificationsEnabled) disabled++;
                else if (settings.RepeatReminderMinutes == 0) deduplicated++;
                else cooldownKeys.Add(ReminderSchedule.GetKey(card));
            }
            int cooldown = cooldownKeys.Count;
            operation = "SendDueNotifications";
            bool sent = reminders.Count > 0 && _notifications.ShowDueCards(reminders, settings.PlaySound, now);
            if (sent)
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
            operation = "PublishSnapshot";
            Updated?.Invoke(new MonitorSnapshot(true, message, now, eligibleCards)
            {
                AssignedCards = cards,
                NotifyBeforeMinutes = settings.NotifyBeforeMinutes
            });
            var cardState = cards.Select(card => (card.Id, card.Due, card.DueComplete, card.Closed)).ToHashSet();
            int rejected = sent ? 0 : reminders.Count;
            var summaryCounts = (cards.Count, eligibleCards.Count, cooldown, deduplicated, disabled, rejected, settings.NotificationsEnabled);
            bool changed = _lastSummaryCounts != summaryCounts || _lastCards is null || !_lastCards.SetEquals(cardState) ||
                _lastCooldownKeys is null || !_lastCooldownKeys.SetEquals(cooldownKeys);
            LogEventLevel summaryLevel = rejected > 0 ? LogEventLevel.Warning
                : sent || changed || _lastCheckFailed ? LogEventLevel.Information : LogEventLevel.Debug;
            var details = new List<string>();
            if (sent) details.Add($"{reminders.Count} tarjetas notificadas");
            if (cooldown > 0) details.Add($"{cooldown} en cooldown");
            if (deduplicated > 0) details.Add($"{deduplicated} por deduplicación");
            if (disabled > 0) details.Add($"{disabled} con avisos desactivados");
            if (rejected > 0) details.Add($"{rejected} rechazadas por Windows");
            Log.ForContext<DueCardMonitor>()
                .ForContext("NotifiedCardCount", sent ? reminders.Count : 0)
                .ForContext("CooldownCount", cooldown)
                .ForContext("DeduplicatedCount", deduplicated)
                .ForContext("DisabledCount", disabled)
                .ForContext("RejectedCount", rejected)
                .Write(summaryLevel,
                    "Evaluadas: {EvaluatedCount} | Candidatas: {CandidateCount} | Avisos enviados: {NotificationCount}{SummaryDetail} ({DurationMs} ms)",
                    cards.Count, eligibleCards.Count, sent ? 1 : 0,
                    details.Count == 0 ? string.Empty : " (" + string.Join(", ", details) + ")", stopwatch.ElapsedMilliseconds);
            _lastCards = cardState;
            _lastSummaryCounts = summaryCounts;
            _lastCooldownKeys = cooldownKeys;
            _lastCheckFailed = false;
            outcome = "Succeeded";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "Cancelled";
        }
        catch (OperationCanceledException ex)
        {
            _lastCheckFailed = true;
            AppLog.WriteFailure(operation, ex);
            Updated?.Invoke(MonitorSnapshot.CreateTimeout(DateTimeOffset.Now));
        }
        catch (Exception ex)
        {
            _lastCheckFailed = true;
            AppLog.WriteFailure(operation, ex);
            // Un fallo de transporte no confirma falta de Internet y puede incluir la URL autenticada.
            string message = ex is HttpRequestException { StatusCode: null }
                ? "No se pudo conectar con Trello. Revisa tu conexión a Internet y vuelve a intentarlo."
                : ex.Message;
            Updated?.Invoke(new MonitorSnapshot(
                true,
                message,
                DateTimeOffset.Now,
                Array.Empty<TrelloCard>(),
                true));
        }
        finally
        {
            Log.ForContext<DueCardMonitor>().Debug("Comprobación finalizada: {Outcome}; duración {DurationMs} ms", outcome, stopwatch.ElapsedMilliseconds);
            _checkLock.Release();
        }
    }

}
