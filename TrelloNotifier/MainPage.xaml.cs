using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TrelloNotifier.Models;
using TrelloNotifier.Services;

namespace TrelloNotifier;

public sealed partial class MainPage : Page
{
    public static MainPage? Current { get; private set; }

    public MainPage()
    {
        InitializeComponent();
        Current = this;
    }

    public void ApplyTheme(string theme)
    {
        RootGrid.RequestedTheme = Enum.TryParse(theme, out ElementTheme selectedTheme)
            ? selectedTheme
            : ElementTheme.Default;
    }

    public void ShowMessage(string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }

    private async void Navigation_Loaded(object sender, RoutedEventArgs e)
    {
        Navigation.SelectedItem = Navigation.MenuItems[0];
        NavigateToDashboard();
        try
        {
            var settings = await Task.Run(AppServices.Settings.Load);
            ApplyTheme(settings.Theme);
        }
        catch (IOException ex)
        {
            AppLog.WriteFailure("LoadTheme", ex);
            ShowMessage(ex.Message, InfoBarSeverity.Error);
        }
    }

    private void Navigation_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            if (ContentFrame.Navigate(typeof(SettingsPage))) Navigation.Header = "Configuración";
            return;
        }

        string? tag = (args.InvokedItemContainer as NavigationViewItem)?.Tag as string;
        if (tag == "Dashboard")
        {
            NavigateToDashboard();
            return;
        }
        NavigateToCards(tag == "History"
            ? TrelloCardScope.History : TrelloCardScope.Pending);
    }

    private void NavigateToDashboard()
    {
        if (ContentFrame.Content is DashboardPage || ContentFrame.Navigate(typeof(DashboardPage)))
            Navigation.Header = "Dashboard";
    }

    internal void NavigateToManualCreation()
    {
        if (ContentFrame.Navigate(typeof(CreateCardPage))) Navigation.Header = "Crear tarjeta";
    }

    internal void NavigateToBulkCreation()
    {
        if (ContentFrame.Navigate(typeof(BulkCreateCardPage))) Navigation.Header = "Crear tarjetas en lote";
    }

    internal void NavigateToCards(TrelloCardScope scope)
    {
        if (ContentFrame.Content is not CardsPage page || page.Scope != scope)
        {
            if (!ContentFrame.Navigate(typeof(CardsPage), scope)) return;
        }

        Navigation.Header = scope == TrelloCardScope.History ? "Historial" : "Trello Notifier";
    }
}
