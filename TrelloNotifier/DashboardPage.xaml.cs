using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TrelloNotifier.Models;

namespace TrelloNotifier;

public sealed partial class DashboardPage : Page
{
    private readonly ObservableCollection<TrelloCardViewModel> _cards = new();
    private MonitorSnapshot? _snapshot;
    private bool _updatingBoardFilter;

    public DashboardPage()
    {
        InitializeComponent();
        CardsList.ItemsSource = _cards;
        BoardFilter.ItemsSource = new[] { new TrelloBoard { Name = "Todos los tableros" } };
        BoardFilter.SelectedIndex = 0;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        AppServices.Monitor.Updated += OnMonitorUpdated;
        await RefreshCardsAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        AppServices.Monitor.Updated -= OnMonitorUpdated;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshCardsAsync();
    }

    private async Task RefreshCardsAsync()
    {
        RefreshButton.IsEnabled = false;
        try
        {
            await AppServices.Monitor.CheckNowAsync();
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void OpenCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } &&
            Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }

    private void OnMonitorUpdated(MonitorSnapshot snapshot)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _snapshot = snapshot;
            MonitorInfoBar.Message = snapshot.Message;
            MonitorInfoBar.Severity = snapshot.HasError ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
            MonitorInfoBar.IsOpen = snapshot.HasError || !snapshot.IsRunning;
            MonitorInfoBar.Visibility = MonitorInfoBar.IsOpen ? Visibility.Visible : Visibility.Collapsed;

            if (snapshot.IsRunning && !snapshot.HasError)
            {
                UpdateBoardFilter(snapshot);
                LastCheckText.Text = snapshot.LastCheck is null
                    ? "Sin comprobaciones todavía"
                    : $"Última actualización: {snapshot.LastCheck.Value.ToLocalTime():dd/MM/yyyy HH:mm}";
                DueSoonHelpText.Text = $"En los próximos {snapshot.NotifyBeforeMinutes} minutos";
            }

            ApplyFilter();
        });
    }

    private void StatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void BoardFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingBoardFilter) ApplyFilter();
    }

    private void CardSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void CardRow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyCardState((Control)sender);
    }

    private void CardRow_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        // ListView recicla filas: el color debe seguir al nuevo elemento, no al anterior.
        ApplyCardState((Control)sender);
    }

    private static void ApplyCardState(Control row)
    {
        if (row.DataContext is TrelloCardViewModel card)
        {
            VisualStateManager.GoToState(row, card.Status.ToString(), false);
        }
    }

    private void UpdateBoardFilter(MonitorSnapshot snapshot)
    {
        string? selectedId = BoardFilter.SelectedValue as string;
        List<TrelloBoard> boards = snapshot.GetBoards();
        boards.Insert(0, new TrelloBoard { Name = "Todos los tableros" });
        _updatingBoardFilter = true;
        try
        {
            BoardFilter.ItemsSource = boards;
            BoardFilter.SelectedItem = boards.FirstOrDefault(board => board.Id == selectedId) ?? boards[0];
        }
        finally
        {
            _updatingBoardFilter = false;
        }
    }

    private void ApplyFilter()
    {
        if (_snapshot is null) return;

        string? tag = (StatusFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        bool filtered = Enum.TryParse(tag, out TrelloCardStatus status);
        DateTimeOffset now = DateTimeOffset.Now;
        IEnumerable<TrelloCard> cards = _snapshot.GetFilteredCards(
            BoardFilter.SelectedValue as string, filtered ? status : null, now, CardSearch.Text);

        _cards.Clear();
        foreach (TrelloCard card in cards)
        {
            _cards.Add(new TrelloCardViewModel(card, now, _snapshot.NotifyBeforeMinutes));
        }

        bool available = _snapshot.IsRunning && !_snapshot.HasError;
        var counts = _snapshot.AssignedCards.GroupBy(card => card.GetStatus(now, _snapshot.NotifyBeforeMinutes))
            .ToDictionary(group => group.Key, group => group.Count());
        int overdue = counts.GetValueOrDefault(TrelloCardStatus.Overdue);
        int dueSoon = counts.GetValueOrDefault(TrelloCardStatus.DueSoon);
        OpenCountText.Text = available ? _snapshot.AssignedCards.Count.ToString() : "—";
        OverdueCountText.Text = available ? overdue.ToString() : "—";
        DueSoonCountText.Text = available ? dueSoon.ToString() : "—";
        HealthyCountText.Text = available ? (_snapshot.AssignedCards.Count - overdue - dueSoon).ToString() : "—";
        CardCountText.Text = !available ? "Mis tarjetas"
            : _cards.Count == _snapshot.AssignedCards.Count
                ? $"{_cards.Count} {(_cards.Count == 1 ? "tarjeta" : "tarjetas")}" : $"{_cards.Count} de {_snapshot.AssignedCards.Count} tarjetas";
        EmptyMessage.Text = _snapshot.HasError
            ? "No se pudieron consultar las tarjetas. Revisa la conexión y pulsa Comprobar ahora."
            : !_snapshot.IsRunning
                ? "Configura la conexión para consultar tus tarjetas."
                : _snapshot.AssignedCards.Count == 0
                    ? "No hay tarjetas abiertas asignadas a tu cuenta."
                    : "No hay tarjetas que coincidan con la búsqueda y los filtros seleccionados.";
        EmptyMessage.Visibility = _cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
