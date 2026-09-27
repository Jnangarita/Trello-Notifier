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

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        AppServices.Monitor.Updated += OnMonitorUpdated;
        _ = AppServices.Monitor.CheckNowAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        AppServices.Monitor.Updated -= OnMonitorUpdated;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await AppServices.Monitor.CheckNowAsync();
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
            StatusText.Text = snapshot.Message;
            StatusIcon.Glyph = snapshot.HasError ? "\uEA39" : snapshot.IsRunning ? "\uE73E" : "\uE895";
            LastCheckText.Text = snapshot.LastCheck is null
                ? "Sin comprobaciones todavía"
                : $"Última comprobación: {snapshot.LastCheck.Value.ToLocalTime():dd/MM/yyyy HH:mm:ss}";

            if (snapshot.IsRunning && !snapshot.HasError)
            {
                UpdateBoardFilter(snapshot);
                FilterHelpText.Text = $"Próximas a vencer: próximos {snapshot.NotifyBeforeMinutes} minutos. " +
                    "Completadas: vencimiento marcado como completado en Trello.";
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
            _cards.Add(new TrelloCardViewModel(card, now));
        }

        CardCountText.Text = $"{_cards.Count} de {_snapshot.AssignedCards.Count} tarjeta(s)";
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
