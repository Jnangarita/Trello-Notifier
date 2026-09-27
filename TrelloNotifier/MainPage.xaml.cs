using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
            ShowMessage(ex.Message, InfoBarSeverity.Error);
        }
    }

    private void Navigation_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            ContentFrame.Navigate(typeof(SettingsPage));
            Navigation.Header = "Configuración";
            return;
        }

        NavigateToDashboard();
    }

    private void NavigateToDashboard()
    {
        if (ContentFrame.CurrentSourcePageType != typeof(DashboardPage))
        {
            ContentFrame.Navigate(typeof(DashboardPage));
        }

        Navigation.Header = "Trello Notifier";
    }
}
