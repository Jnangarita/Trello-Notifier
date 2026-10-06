using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Serilog;
using Serilog.Events;
using TrelloNotifier.Services;

namespace TrelloNotifier;

public partial class App : Application
{
    private Window? _mainWindow;

    public App()
    {
        AppLog.Initialize(debug: string.Equals(
            Environment.GetEnvironmentVariable("TRELLO_NOTIFIER_LOG_LEVEL"), "Debug", StringComparison.OrdinalIgnoreCase));
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        Log.ForContext<App>().Information(@"Aplicación iniciada.

  _____          _ _
 |_   _| __ ___ | | | ___
   | || '__/ _ \| | |/ _ \
   | || | |  __/| | | (_) |
   |_||_|  \___||_|_|\___/

 :: Trello Notifier :: (v{AppVersion})

", typeof(App).Assembly.GetName().Version?.ToString(3));
        InitializeComponent();
        AppServices.Notifications.Initialize();
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
        if (AppLog.HasWriteFailure)
        {
            MainPage.Current?.ShowMessage("No se pudieron iniciar los logs. Revisa los permisos y el espacio disponible.",
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
        }
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        try
        {
            AppServices.Monitor.Dispose();
            AppServices.Notifications.Dispose();
            Log.ForContext<App>().Information("Aplicación cerrada");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        AppLog.WriteFailure("UnhandledUI", e.Exception, LogEventLevel.Fatal);
        Log.CloseAndFlush();
    }

    private static void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            AppLog.WriteFailure("UnhandledProcess", exception, LogEventLevel.Fatal);
        }
        Log.CloseAndFlush();
    }
}
