using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using TrelloNotifier.Models;
using TrelloNotifier.Services;

namespace TrelloNotifier;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            AppSettings settings = await Task.Run(AppServices.Settings.Load);
            LoadSettings(settings);
            SettingsPanel.IsEnabled = true;
        }
        catch (IOException ex)
        {
            AppLog.WriteFailure("LoadSettingsPage", ex);
            MainPage.Current?.ShowMessage(ex.Message, InfoBarSeverity.Error);
        }
    }

    private void LoadSettings(AppSettings settings)
    {
        ApiBaseUrlTextBox.Text = settings.ApiBaseUrl;
        ApiKeyPasswordBox.Password = settings.ApiKey;
        TokenPasswordBox.Password = settings.Token;
        GeminiApiKeyPasswordBox.Password = settings.GeminiApiKey;
        GeminiModelTextBox.Text = settings.GeminiModel;
        NotifyBeforeComboBox.SelectedItem = NotifyBeforeComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == settings.NotifyBeforeMinutes.ToString());
        PollIntervalNumberBox.Value = settings.PollIntervalMinutes;
        RepeatReminderComboBox.SelectedItem = RepeatReminderComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == settings.RepeatReminderMinutes.ToString())
            ?? RepeatReminderComboBox.Items[2];
        IncludeOverdueCheckBox.IsChecked = settings.IncludeOverdueCards;
        NotificationsToggleSwitch.IsOn = settings.NotificationsEnabled;
        SoundCheckBox.IsChecked = settings.PlaySound;

        ThemeComboBox.SelectedItem = ThemeComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), settings.Theme, StringComparison.Ordinal))
            ?? ThemeComboBox.Items[0];
    }

    private void RevealCredential_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button)
        {
            bool isApiKey = button == ApiKeyRevealButton;
            PasswordBox field = isApiKey ? ApiKeyPasswordBox : TokenPasswordBox;
            bool isVisible = button.IsChecked == true;
            field.PasswordRevealMode = isVisible ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;
            string action = isVisible ? "Ocultar" : "Mostrar";
            button.Content = action;
            AutomationProperties.SetName(button, $"{action} {(isApiKey ? "API key" : "token")}");
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildSettings(out AppSettings? settings))
        {
            return;
        }

        SettingsPanel.IsEnabled = false;
        try
        {
            await Task.Run(() => AppServices.Settings.Save(settings));
            MainPage.Current?.ApplyTheme(settings.Theme);
            AppServices.Monitor.Restart();
            MainPage.Current?.ShowMessage("Configuración guardada. El monitor se reinició.", InfoBarSeverity.Success);
        }
        catch (IOException ex)
        {
            AppLog.WriteFailure("SaveSettings", ex);
            MainPage.Current?.ShowMessage(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SettingsPanel.IsEnabled = true;
        }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildSettings(out AppSettings? settings))
        {
            return;
        }

        try
        {
            IReadOnlyList<TrelloCard> cards = await AppServices.Trello.GetOpenCardsAsync(settings, CancellationToken.None);
            MainPage.Current?.ShowMessage(
                $"Conexión correcta. El servidor devolvió {cards.Count} tarjeta(s) abierta(s).",
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            AppLog.WriteFailure("TestConnection", ex);
            MainPage.Current?.ShowMessage(ex.Message, InfoBarSeverity.Error);
        }
    }

    private void TestNotification_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool wasShown = AppServices.Notifications.ShowTest(SoundCheckBox.IsChecked == true);
            MainPage.Current?.ShowMessage(
                wasShown
                    ? "Notificación de prueba enviada."
                    : "Windows no permitió registrar la aplicación para mostrar notificaciones.",
                wasShown ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        }
        catch (Exception ex)
        {
            AppLog.WriteFailure("TestNotification", ex);
            MainPage.Current?.ShowMessage("No se pudo enviar la notificación de prueba a Windows.", InfoBarSeverity.Error);
        }
    }

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is ComboBoxItem item)
        {
            MainPage.Current?.ApplyTheme(item.Tag?.ToString() ?? "Default");
        }
    }

    private bool TryBuildSettings(out AppSettings settings)
    {
        settings = new AppSettings
        {
            ApiBaseUrl = ApiBaseUrlTextBox.Text.Trim(),
            ApiKey = ApiKeyPasswordBox.Password.Trim(),
            Token = TokenPasswordBox.Password.Trim(),
            GeminiApiKey = GeminiApiKeyPasswordBox.Password.Trim(),
            GeminiModel = GeminiModelTextBox.Text.Trim(),
            NotifyBeforeMinutes = int.TryParse(
                (NotifyBeforeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out int notifyBeforeMinutes)
                ? notifyBeforeMinutes : 0,
            PollIntervalMinutes = double.IsNaN(PollIntervalNumberBox.Value)
                ? 0
                : (int)PollIntervalNumberBox.Value,
            PlaySound = SoundCheckBox.IsChecked == true,
            NotificationsEnabled = NotificationsToggleSwitch.IsOn,
            RepeatReminderMinutes = int.TryParse(
                (RepeatReminderComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out int repeatMinutes)
                ? repeatMinutes : 30,
            IncludeOverdueCards = IncludeOverdueCheckBox.IsChecked == true,
            Theme = (ThemeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Default"
        };

        if (!string.IsNullOrEmpty(settings.GeminiApiKey) && GeminiCardClient.GetConfigurationError(settings) is string geminiError)
        {
            MainPage.Current?.ShowMessage(geminiError, InfoBarSeverity.Warning);
            return false;
        }

        if (!Uri.TryCreate(settings.ApiBaseUrl, UriKind.Absolute, out Uri? apiUri) ||
            (apiUri.Scheme != Uri.UriSchemeHttp && apiUri.Scheme != Uri.UriSchemeHttps))
        {
            MainPage.Current?.ShowMessage("No se pudo conectar con el servidor. Verifica la dirección.", InfoBarSeverity.Warning);
            return false;
        }

        if (settings.IsOfficialTrelloApi &&
            (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.Token)))
        {
            MainPage.Current?.ShowMessage(
                "La API key y el token son obligatorios para la API oficial de Trello.",
                InfoBarSeverity.Warning);
            return false;
        }

        if (NotifyBeforeComboBox.SelectedItem is null)
        {
            MainPage.Current?.ShowMessage("Selecciona la anticipación de los recordatorios.", InfoBarSeverity.Warning);
            return false;
        }

        if (settings.NotifyBeforeMinutes is < 1 or > 10080 || settings.PollIntervalMinutes is < 1 or > 1440 ||
            settings.RepeatReminderMinutes is not (0 or 15 or 30 or 60))
        {
            MainPage.Current?.ShowMessage("Revisa los intervalos indicados.", InfoBarSeverity.Warning);
            return false;
        }

        return true;
    }
}
