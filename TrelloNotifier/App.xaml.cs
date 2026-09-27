using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace TrelloNotifier;

public partial class App : Application
{
    private Window? _mainWindow;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        AppServices.Notifications.Initialize();
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public static string GetAssetPath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = new MainWindow();

        AppActivationArguments activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        if (activationArgs.Kind == ExtendedActivationKind.AppNotification &&
            activationArgs.Data is AppNotificationActivatedEventArgs notificationArgs)
        {
            AppServices.Notifications.ProcessActivation(notificationArgs);
        }

        _mainWindow.Activate();
        AppServices.Monitor.Start();
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        AppServices.Monitor.Dispose();
        AppServices.Notifications.Dispose();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        string logPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
        File.AppendAllText(logPath, $"{DateTimeOffset.Now:O}{Environment.NewLine}{e.Exception}{Environment.NewLine}");
    }
}
