using TrelloNotifier.Models;

namespace TrelloNotifier.Services;

internal static class ReminderSchedule
{
    public static List<TrelloCard> GetEligibleCards(
        IEnumerable<TrelloCard> cards, AppSettings settings, DateTimeOffset now)
    {
        return cards
            .Where(card =>
            {
                TrelloCardStatus status = card.GetStatus(now, settings.NotifyBeforeMinutes);
                return status == TrelloCardStatus.DueSoon ||
                    (status == TrelloCardStatus.Overdue && settings.IncludeOverdueCards);
            })
            .OrderBy(card => card.Due)
            .ToList();
    }

    public static bool ShouldNotify(TrelloCard card, AppSettings settings,
        IReadOnlyDictionary<string, DateTimeOffset> lastNotifications, DateTimeOffset now)
    {
        if (!lastNotifications.TryGetValue(GetKey(card), out DateTimeOffset lastNotified))
        {
            return true;
        }

        if (settings.RepeatReminderMinutes == 0)
        {
            return false;
        }

        int interval = card.Due <= now ? 120 : settings.RepeatReminderMinutes;
        return now - lastNotified >= TimeSpan.FromMinutes(interval);
    }

    public static string GetKey(TrelloCard card)
    {
        return $"{card.Id}|{card.Due!.Value.ToUnixTimeSeconds()}";
    }
}
