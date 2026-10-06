using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TrelloNotifier.Models;
using TrelloNotifier.Services;

namespace TrelloNotifier;

public sealed partial class DashboardPage : Page
{
    private readonly ObservableCollection<TrelloCardViewModel> _cards = new();
    private MonitorSnapshot? _snapshot;
    private bool _updatingBoardFilter;
    private CancellationTokenSource? _pageCancellation;
    private bool _refreshing;

    internal TrelloCardScope Scope { get; private set; } = TrelloCardScope.Pending;

    public DashboardPage()
    {
        InitializeComponent();
        CardsList.ItemsSource = _cards;
        BoardFilter.ItemsSource = new[] { new TrelloBoard { Name = "Todos los tableros" } };
        BoardFilter.SelectedIndex = 0;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Scope = e.Parameter is TrelloCardScope.History ? TrelloCardScope.History : TrelloCardScope.Pending;
        if (Scope == TrelloCardScope.History)
        {
            PageTitle.Text = "Historial";
            PageDescription.Text = "Completadas y archivadas asignadas a ti. Estado actual en Trello.";
            SummaryPanel.Visibility = Visibility.Collapsed;
            RefreshText.Text = "Actualizar historial";
            AutomationProperties.SetName(RefreshButton, RefreshText.Text);
            LastCheckText.Text = "Sin consultas todavía";
            StatusFilter.Items.Clear();
            StatusFilter.Items.Add(new ComboBoxItem { Content = "Todas", Tag = nameof(TrelloCardScope.History) });
            StatusFilter.Items.Add(new ComboBoxItem { Content = "Completadas", Tag = nameof(TrelloCardScope.Completed) });
            StatusFilter.Items.Add(new ComboBoxItem { Content = "Archivadas", Tag = nameof(TrelloCardScope.Archived) });
            StatusFilter.SelectedIndex = 0;
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _pageCancellation = new CancellationTokenSource();
        if (Scope == TrelloCardScope.Pending) AppServices.Monitor.Updated += OnMonitorUpdated;
        await RefreshCardsAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        AppServices.Monitor.Updated -= OnMonitorUpdated;
        _pageCancellation?.Cancel();
        _pageCancellation?.Dispose();
        _pageCancellation = null;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshCardsAsync();
    }

    private async Task RefreshCardsAsync()
    {
        if (_refreshing || _pageCancellation is null) return;
        CancellationToken cancellationToken = _pageCancellation.Token;
        _refreshing = true;
        RefreshButton.IsEnabled = false;
        try
        {
            if (Scope == TrelloCardScope.History)
            {
                await RefreshHistoryAsync(cancellationToken);
            }
            else
            {
                await AppServices.Monitor.CheckNowAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // La navegación cancela la consulta y descarta su resultado.
        }
        finally
        {
            _refreshing = false;
            if (!cancellationToken.IsCancellationRequested) RefreshButton.IsEnabled = true;
        }
    }

    private async Task RefreshHistoryAsync(CancellationToken cancellationToken)
    {
        MonitorSnapshot snapshot;
        try
        {
            AppSettings settings = await Task.Run(AppServices.Settings.Load, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!settings.IsConfigured)
            {
                snapshot = new MonitorSnapshot(false, "Configura la conexión para consultar el historial.", null, Array.Empty<TrelloCard>());
            }
            else
            {
                IReadOnlyList<TrelloCard> cards = await AppServices.Trello.GetAllCardsAsync(settings, cancellationToken);
                snapshot = new MonitorSnapshot(true, "", DateTimeOffset.Now, Array.Empty<TrelloCard>())
                {
                    AssignedCards = cards,
                    NotifyBeforeMinutes = settings.NotifyBeforeMinutes
                };
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException ||
            (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Los errores de transporte pueden contener la URL autenticada.
            AppLog.WriteFailure("RefreshHistory", ex);
            snapshot = new MonitorSnapshot(true,
                "No se pudo consultar el historial. Revisa la conexión y la configuración e inténtalo de nuevo.",
                null, Array.Empty<TrelloCard>(), true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        DisplaySnapshot(snapshot);
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
            if (_pageCancellation is not null) DisplaySnapshot(snapshot);
        });
    }

    private void DisplaySnapshot(MonitorSnapshot snapshot)
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
                ? "Sin consultas todavía"
                : $"Última actualización: {snapshot.LastCheck.Value.ToLocalTime():dd/MM/yyyy HH:mm}";
            DueSoonHelpText.Text = $"En los próximos {snapshot.NotifyBeforeMinutes} minutos";
        }

        ApplyFilter();
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
            VisualStateManager.GoToState(row, card.IsArchived ? "Archived" : card.Status.ToString(), false);
        }
    }

    private void UpdateBoardFilter(MonitorSnapshot snapshot)
    {
        string? selectedId = BoardFilter.SelectedValue as string;
        List<TrelloBoard> boards = snapshot.GetBoards(Scope);
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
        bool filtered = Enum.TryParse(tag, out TrelloCardStatus status) && Scope == TrelloCardScope.Pending;
        TrelloCardScope selectedScope = Scope == TrelloCardScope.History && Enum.TryParse(tag, out TrelloCardScope historyScope)
            ? historyScope : Scope;
        DateTimeOffset now = DateTimeOffset.Now;
        IEnumerable<TrelloCard> cards = _snapshot.GetFilteredCards(
            BoardFilter.SelectedValue as string, filtered ? status : null, now, CardSearch.Text, selectedScope);

        _cards.Clear();
        foreach (TrelloCard card in cards)
        {
            _cards.Add(new TrelloCardViewModel(card, now, _snapshot.NotifyBeforeMinutes));
        }

        bool available = _snapshot.IsRunning && !_snapshot.HasError;
        List<TrelloCard> scopedCards = _snapshot.GetCards(Scope).ToList();
        var counts = scopedCards.GroupBy(card => card.GetStatus(now, _snapshot.NotifyBeforeMinutes))
            .ToDictionary(group => group.Key, group => group.Count());
        int overdue = counts.GetValueOrDefault(TrelloCardStatus.Overdue);
        int dueSoon = counts.GetValueOrDefault(TrelloCardStatus.DueSoon);
        OpenCountText.Text = available ? scopedCards.Count.ToString() : "—";
        OverdueCountText.Text = available ? overdue.ToString() : "—";
        DueSoonCountText.Text = available ? dueSoon.ToString() : "—";
        HealthyCountText.Text = available ? (scopedCards.Count - overdue - dueSoon).ToString() : "—";
        EmptyMessage.Text = _snapshot.HasError
            ? "No se pudieron consultar las tarjetas. Revisa la conexión y vuelve a actualizar."
            : !_snapshot.IsRunning
                ? "Configura la conexión para consultar tus tarjetas."
                : scopedCards.Count == 0
                    ? Scope == TrelloCardScope.History
                        ? "No hay tarjetas completadas ni archivadas asignadas a tu cuenta."
                        : "No hay tarjetas pendientes sin archivar asignadas a tu cuenta."
                    : "No hay tarjetas que coincidan con la búsqueda y los filtros seleccionados.";
        EmptyMessage.Visibility = _cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
